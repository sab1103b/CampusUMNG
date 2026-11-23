using System;
using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Red de caminos del campus y cálculo de la ruta más corta (Dijkstra).
/// Ponlo en el objeto padre "RedCaminos" (Position 0,0,0 - Rotation 0,0,0 - Scale 1,1,1).
///
/// La ruta se calcula en LATITUD/LONGITUD y METROS, no en posiciones de Unity. Así no se
/// desajusta si Mapbox mueve, recentra o hace zoom al mapa.
///
/// Cómo se arma la ruta:
///  1. Desde tu ubicación se entra al camino por el PUNTO MÁS CERCANO de un tramo
///     (no por el nodo más cercano, que puede quedar detrás de ti).
///  2. Se recorre la red hasta la entrada más corta del lugar; si el lugar no tiene entradas,
///     hasta el punto del camino más cercano al lugar y de ahí al lugar.
///  3. Nunca se ignora la red: si estás lejos de los caminos, la ruta igual los usa y avisa
///     cuántos metros debes caminar para llegar al primero. Solo es línea recta si no hay red,
///     si la red no conecta ambos puntos o si el destino está a pocos metros.
///
/// Dos modos:
///  - "Cargar Desde Base Datos" apagado: usa los nodos que dibujaste en la escena (para armar la red inicial).
///  - "Cargar Desde Base Datos" encendido: crea los nodos desde la base de datos local al abrir la app,
///    así el administrador puede agregar o quitar puntos.
/// </summary>
public class RedCaminos : MonoBehaviour
{
    [Header("Cargar la red desde la base de datos local (para que el administrador pueda editarla)")]
    [FormerlySerializedAs("cargarDesdeApi")]
    [SerializeField] private bool cargarDesdeBaseDatos = false;
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private LugaresService servicio;

    /// <summary>Resultado de un cálculo de ruta. Los puntos están en latitud/longitud.</summary>
    public class Ruta
    {
        public readonly List<Vector2d> puntos = new List<Vector2d>();
        public bool porCaminos;          // false = línea recta
        public string motivoRecta;       // por qué no se usaron los caminos (si porCaminos = false)
        public double metrosTotal;
        public double metrosHastaCamino; // desde tu ubicación hasta el primer camino
        public bool terminaEnEntrada;    // true = llega a una entrada marcada del lugar
    }

    // ----- Datos de la red (en geo y en metros locales) -----
    private readonly List<NodoCamino> nodos = new List<NodoCamino>();
    private readonly List<Vector2d> geo = new List<Vector2d>();
    private readonly List<int> entradaDe = new List<int>();
    private readonly List<(int a, int b)> tramos = new List<(int a, int b)>();
    private List<(int vecino, double metros)>[] ady = new List<(int, double)>[0];
    private double[] dist = new double[0];
    private int[] previo = new int[0];

    // Proyección local: (lat, lon) -> metros planos alrededor del campus. Precisa a esta escala.
    private double lat0, lon0, metrosPorGradoLon;
    private const double MetrosPorGradoLat = 111320.0;

    private bool mapaListo;
    private bool hayDatosNuevos;

    public int Cantidad => nodos.Count;
    public bool Lista => geo.Count > 0;

    private void Awake()
    {
        if (mapa == null)
        {
            Debug.LogWarning("RedCaminos: asigna el Mapa.");
            return;
        }

        mapa.OnInitialized += AlIniciarMapa;

        // Si el mapa se mueve, los nodos visibles se reubican (la ruta no depende de esto)
        mapa.OnUpdated += ReubicarNodos;

        if (!cargarDesdeBaseDatos) return;

        if (servicio == null)
        {
            Debug.LogWarning("RedCaminos: asigna Servicio para cargar la red desde la base de datos.");
            return;
        }
        servicio.RedCargada += AlCargarRed;
    }

    private void OnDestroy()
    {
        if (mapa != null)
        {
            mapa.OnInitialized -= AlIniciarMapa;
            mapa.OnUpdated -= ReubicarNodos;
        }
        if (servicio != null) servicio.RedCargada -= AlCargarRed;
    }

    private void AlIniciarMapa()
    {
        mapaListo = true;
        if (cargarDesdeBaseDatos) IntentarConstruir();
        else Reconstruir();   // modo escena: convierte los nodos dibujados a lat/lon
    }

    private void AlCargarRed()
    {
        hayDatosNuevos = true;
        IntentarConstruir();
    }

    // Espera a que estén listos los dos: el mapa y los datos de la base
    private void IntentarConstruir()
    {
        if (!mapaListo || !hayDatosNuevos || servicio.Red == null) return;
        hayDatosNuevos = false;
        ConstruirDesdeDatos(servicio.Red);
    }

    // ==================================================================
    // Construcción
    // ==================================================================

    /// <summary>Modo escena: toma los NodoCamino hijos y calcula su lat/lon con el mapa.</summary>
    public void Reconstruir()
    {
        nodos.Clear();
        GetComponentsInChildren<NodoCamino>(nodos);

        // Asegura que toda conexión valga en ambos sentidos
        foreach (NodoCamino n in nodos)
            foreach (NodoCamino c in n.conexiones)
                if (c != null && !c.conexiones.Contains(n)) c.conexiones.Add(n);

        if (!mapaListo) return;   // sin mapa no se puede pasar a lat/lon; se repite en OnInitialized

        geo.Clear();
        entradaDe.Clear();
        foreach (NodoCamino n in nodos)
        {
            geo.Add(mapa.WorldToGeoPosition(n.transform.position));
            entradaDe.Add(n.entradaDeLugarId);
        }

        Dictionary<NodoCamino, int> indice = new Dictionary<NodoCamino, int>();
        for (int i = 0; i < nodos.Count; i++) indice[nodos[i]] = i;

        HashSet<(int, int)> pares = new HashSet<(int, int)>();
        for (int i = 0; i < nodos.Count; i++)
            foreach (NodoCamino c in nodos[i].conexiones)
                if (c != null && indice.TryGetValue(c, out int j) && j != i)
                    pares.Add(i < j ? (i, j) : (j, i));

        ConstruirGrafo(pares);
        Registro.Info($"Red de la escena: {nodos.Count} nodos, {tramos.Count} tramos");
    }

    /// <summary>Crea la red completa a partir de los datos de la base de datos.</summary>
    public void ConstruirDesdeDatos(LugaresService.RedDto red)
    {
        // Quita los nodos anteriores (los de la escena o los de una carga previa)
        foreach (Transform hijo in transform)
        {
            hijo.gameObject.SetActive(false);
            Destroy(hijo.gameObject);
        }
        nodos.Clear();
        geo.Clear();
        entradaDe.Clear();

        Dictionary<int, int> porId = new Dictionary<int, int>();

        if (red.nodos != null)
        {
            foreach (LugaresService.NodoDto dto in red.nodos)
            {
                GameObject go = new GameObject(string.IsNullOrEmpty(dto.nombre)
                    ? $"Nodo_{dto.id}" : $"{dto.nombre} ({dto.id})");
                go.transform.SetParent(transform, false);

                NodoCamino nodo = go.AddComponent<NodoCamino>();
                nodo.entradaDeLugarId = dto.lugar_id;

                porId[dto.id] = nodos.Count;
                nodos.Add(nodo);
                geo.Add(new Vector2d(dto.latitud, dto.longitud));
                entradaDe.Add(dto.lugar_id);
            }
        }

        HashSet<(int, int)> pares = new HashSet<(int, int)>();
        if (red.conexiones != null)
        {
            foreach (LugaresService.ConexionDto c in red.conexiones)
            {
                if (!porId.TryGetValue(c.nodo_a, out int a) || !porId.TryGetValue(c.nodo_b, out int b) || a == b) continue;
                pares.Add(a < b ? (a, b) : (b, a));
                nodos[a].conexiones.Add(nodos[b]);   // para ver los gizmos en la escena
                nodos[b].conexiones.Add(nodos[a]);
            }
        }

        ConstruirGrafo(pares);
        ReubicarNodos();
        Registro.Info($"Red construida: {nodos.Count} nodos, {tramos.Count} tramos");
    }

    private void ConstruirGrafo(IEnumerable<(int, int)> pares)
    {
        int n = geo.Count;

        if (n > 0)
        {
            lat0 = geo[0].x;
            lon0 = geo[0].y;
            metrosPorGradoLon = MetrosPorGradoLat * Math.Cos(lat0 * Math.PI / 180.0);
        }

        tramos.Clear();
        ady = new List<(int, double)>[n];
        for (int i = 0; i < n; i++) ady[i] = new List<(int, double)>();

        foreach ((int a, int b) in pares)
        {
            double m = Metros(geo[a], geo[b]);
            ady[a].Add((b, m));
            ady[b].Add((a, m));
            tramos.Add((a, b));
        }

        dist = new double[n];
        previo = new int[n];
    }

    // Pone los GameObject de los nodos donde corresponde en el mapa actual (solo visual / gizmos)
    private void ReubicarNodos()
    {
        if (!cargarDesdeBaseDatos || !mapaListo) return;
        for (int i = 0; i < nodos.Count && i < geo.Count; i++)
            if (nodos[i] != null) nodos[i].transform.position = mapa.GeoToWorldPosition(geo[i], false);
    }

    // ==================================================================
    // Cálculo de la ruta
    // ==================================================================

    /// <summary>
    /// Ruta desde 'origen' hasta el lugar 'lugarId' (ubicado en 'destino').
    /// distanciaDirectaM: si el destino está a menos de esto, se va en línea recta.
    /// </summary>
    public Ruta CalcularRuta(Vector2d origen, Vector2d destino, int lugarId, double distanciaDirectaM)
    {
        Ruta ruta = new Ruta();

        if (tramos.Count == 0) return Recta(ruta, origen, destino, "no hay red de caminos cargada");
        if (Metros(origen, destino) <= distanciaDirectaM) return Recta(ruta, origen, destino, "el destino está muy cerca");

        // 1. Entrada a la red: punto más cercano sobre un tramo
        Proyeccion inicio = ProyectarEnTramos(origen);

        // 2. Dijkstra desde ese punto (arranca en los dos extremos del tramo)
        Dijkstra(inicio);

        // 3. Elegir a dónde llegar
        List<Vector2d> final = new List<Vector2d>();   // puntos después del último nodo
        int fin = -1;
        double mejor = double.PositiveInfinity;
        bool directoPorElMismoTramo = false;

        for (int i = 0; i < entradaDe.Count && lugarId > 0; i++)
        {
            if (entradaDe[i] != lugarId || double.IsInfinity(dist[i])) continue;
            if (dist[i] < mejor) { mejor = dist[i]; fin = i; }
        }

        if (fin >= 0)
        {
            ruta.terminaEnEntrada = true;
        }
        else
        {
            // Sin entradas marcadas: al punto del camino más cercano al lugar y de ahí al lugar
            Proyeccion llegada = ProyectarEnTramos(destino);
            int a = llegada.a, b = llegada.b;

            double porA = dist[a] + Metros(geo[a], llegada.punto);
            double porB = dist[b] + Metros(geo[b], llegada.punto);
            if (porA <= porB) { fin = a; mejor = porA; } else { fin = b; mejor = porB; }

            // Si entras y sales por el mismo tramo, se camina directo por él
            if (MismoTramo(inicio, llegada))
            {
                double sobreTramo = Metros(inicio.punto, llegada.punto);
                if (sobreTramo <= mejor) { directoPorElMismoTramo = true; mejor = sobreTramo; }
            }

            final.Add(llegada.punto);
            final.Add(destino);
        }

        if (double.IsInfinity(mejor)) return Recta(ruta, origen, destino, "la red no conecta tu ubicación con el lugar");

        // 4. Armar los puntos: origen -> entrada a la red -> nodos -> llegada
        Agregar(ruta.puntos, origen);
        Agregar(ruta.puntos, inicio.punto);

        if (!directoPorElMismoTramo)
        {
            List<int> camino = new List<int>();
            for (int i = fin; i != -1; i = previo[i]) camino.Add(i);
            camino.Reverse();
            foreach (int i in camino) Agregar(ruta.puntos, geo[i]);
        }

        foreach (Vector2d p in final) Agregar(ruta.puntos, p);

        ruta.porCaminos = true;
        ruta.metrosHastaCamino = inicio.metros;
        for (int i = 1; i < ruta.puntos.Count; i++) ruta.metrosTotal += Metros(ruta.puntos[i - 1], ruta.puntos[i]);
        return ruta;
    }

    private Ruta Recta(Ruta ruta, Vector2d origen, Vector2d destino, string motivo)
    {
        ruta.puntos.Clear();
        ruta.puntos.Add(origen);
        ruta.puntos.Add(destino);
        ruta.porCaminos = false;
        ruta.motivoRecta = motivo;
        ruta.metrosTotal = Metros(origen, destino);
        return ruta;
    }

    // No agrega puntos repetidos (a menos de medio metro del anterior)
    private static void Agregar(List<Vector2d> lista, Vector2d p)
    {
        if (lista.Count > 0 && Metros(lista[lista.Count - 1], p) < 0.5) return;
        lista.Add(p);
    }

    private struct Proyeccion
    {
        public int a, b;          // extremos del tramo
        public Vector2d punto;    // punto más cercano sobre el tramo (lat, lon)
        public double metros;     // distancia desde el punto consultado
    }

    private static bool MismoTramo(Proyeccion p, Proyeccion q) =>
        (p.a == q.a && p.b == q.b) || (p.a == q.b && p.b == q.a);

    private Proyeccion ProyectarEnTramos(Vector2d p)
    {
        Proyeccion mejor = new Proyeccion { metros = double.PositiveInfinity };
        Vector2d pp = AMetros(p);

        foreach ((int a, int b) in tramos)
        {
            Vector2d pa = AMetros(geo[a]), pb = AMetros(geo[b]);
            double dx = pb.x - pa.x, dy = pb.y - pa.y;
            double largo2 = dx * dx + dy * dy;
            double t = largo2 > 1e-9 ? ((pp.x - pa.x) * dx + (pp.y - pa.y) * dy) / largo2 : 0.0;
            t = Math.Max(0.0, Math.Min(1.0, t));

            Vector2d q = new Vector2d(pa.x + t * dx, pa.y + t * dy);
            double d = Math.Sqrt((pp.x - q.x) * (pp.x - q.x) + (pp.y - q.y) * (pp.y - q.y));

            if (d < mejor.metros)
                mejor = new Proyeccion { a = a, b = b, punto = DeMetros(q), metros = d };
        }
        return mejor;
    }

    // Distancia mínima desde el punto de entrada a todos los nodos (Dijkstra con cola ordenada)
    private void Dijkstra(Proyeccion inicio)
    {
        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = double.PositiveInfinity;
            previo[i] = -1;
        }

        SortedSet<(double d, int i)> cola = new SortedSet<(double d, int i)>();
        Sembrar(inicio.a, Metros(inicio.punto, geo[inicio.a]), cola);
        Sembrar(inicio.b, Metros(inicio.punto, geo[inicio.b]), cola);

        while (cola.Count > 0)
        {
            (double d, int i) actual = cola.Min;
            cola.Remove(actual);

            foreach ((int vecino, double metros) in ady[actual.i])
            {
                double alt = actual.d + metros;
                if (alt < dist[vecino])
                {
                    if (!double.IsInfinity(dist[vecino])) cola.Remove((dist[vecino], vecino));
                    dist[vecino] = alt;
                    previo[vecino] = actual.i;
                    cola.Add((alt, vecino));
                }
            }
        }
    }

    private void Sembrar(int nodo, double d, SortedSet<(double, int)> cola)
    {
        if (d >= dist[nodo]) return;
        if (!double.IsInfinity(dist[nodo])) cola.Remove((dist[nodo], nodo));
        dist[nodo] = d;
        previo[nodo] = -1;
        cola.Add((d, nodo));
    }

    // ----- Geometría: proyección local en metros (x = este, y = norte) -----
    private Vector2d AMetros(Vector2d g) =>
        new Vector2d((g.y - lon0) * metrosPorGradoLon, (g.x - lat0) * MetrosPorGradoLat);

    private Vector2d DeMetros(Vector2d m) =>
        new Vector2d(lat0 + m.y / MetrosPorGradoLat, lon0 + m.x / metrosPorGradoLon);

    /// <summary>Distancia en metros entre dos puntos (lat, lon). Suficientemente precisa dentro del campus.</summary>
    public static double Metros(Vector2d a, Vector2d b)
    {
        double latMedia = (a.x + b.x) * 0.5 * Math.PI / 180.0;
        double dx = (b.y - a.y) * MetrosPorGradoLat * Math.Cos(latMedia);
        double dy = (b.x - a.x) * MetrosPorGradoLat;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
