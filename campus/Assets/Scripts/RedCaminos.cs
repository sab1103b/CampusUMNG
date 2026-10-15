using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

/// <summary>
/// Reúne los NodoCamino y calcula la ruta más corta entre dos puntos usando solo
/// los caminos de la red (algoritmo de Dijkstra). Ponlo en el objeto padre "RedCaminos"
/// (Position 0,0,0 - Rotation 0,0,0 - Scale 1,1,1).
///
/// Dos modos:
///  - "Cargar Desde Api" apagado: usa los nodos que dibujaste en la escena (para armar la red inicial).
///  - "Cargar Desde Api" encendido: crea los nodos desde la base de datos al abrir la app,
///    así el administrador puede agregar o quitar puntos.
/// </summary>
public class RedCaminos : MonoBehaviour
{
    [Header("Cargar la red desde la API (para que el administrador pueda editarla)")]
    [SerializeField] private bool cargarDesdeApi = false;
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private LugaresService servicio;

    private readonly List<NodoCamino> nodos = new List<NodoCamino>();
    private Dictionary<NodoCamino, int> indice = new Dictionary<NodoCamino, int>();
    private List<(int vecino, float peso)>[] adyacencia = new List<(int vecino, float peso)>[0];
    private float[] dist = new float[0];
    private int[] previo = new int[0];

    private bool mapaListo;
    private bool hayDatosNuevos;

    public int Cantidad => nodos.Count;

    private void Awake()
    {
        Reconstruir();

        if (!cargarDesdeApi) return;

        if (mapa == null || servicio == null)
        {
            Debug.LogWarning("RedCaminos: asigna Mapa y Servicio para cargar la red desde la API.");
            return;
        }

        mapa.OnInitialized += () => { mapaListo = true; IntentarConstruir(); };
        servicio.RedCargada += () => { hayDatosNuevos = true; IntentarConstruir(); };
    }

    // Espera a que estén listos los dos: el mapa (para convertir lat/lon) y los datos de la API
    private void IntentarConstruir()
    {
        if (!mapaListo || !hayDatosNuevos || servicio.Red == null) return;
        hayDatosNuevos = false;
        ConstruirDesdeDatos(servicio.Red);
    }

    /// <summary>Recoge los nodos que ya existen como hijos (modo escena).</summary>
    public void Reconstruir()
    {
        nodos.Clear();
        GetComponentsInChildren<NodoCamino>(nodos);

        // Asegura que toda conexión valga en ambos sentidos
        foreach (NodoCamino n in nodos)
        {
            foreach (NodoCamino c in n.conexiones)
            {
                if (c != null && !c.conexiones.Contains(n)) c.conexiones.Add(n);
            }
        }

        ConstruirGrafo();
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

        Dictionary<int, NodoCamino> porId = new Dictionary<int, NodoCamino>();

        if (red.nodos != null)
        {
            foreach (LugaresService.NodoDto dto in red.nodos)
            {
                GameObject go = new GameObject(string.IsNullOrEmpty(dto.nombre)
                    ? $"Nodo_{dto.id}" : $"{dto.nombre} ({dto.id})");
                go.transform.SetParent(transform, false);
                go.transform.position = mapa.GeoToWorldPosition(new Vector2d(dto.latitud, dto.longitud), false);

                NodoCamino nodo = go.AddComponent<NodoCamino>();
                nodo.entradaDeLugarId = dto.lugar_id;
                porId[dto.id] = nodo;
                nodos.Add(nodo);
            }
        }

        if (red.conexiones != null)
        {
            foreach (LugaresService.ConexionDto c in red.conexiones)
            {
                if (porId.TryGetValue(c.nodo_a, out NodoCamino a) && porId.TryGetValue(c.nodo_b, out NodoCamino b))
                {
                    a.conexiones.Add(b);
                    b.conexiones.Add(a);
                }
            }
        }

        ConstruirGrafo();
        Debug.Log($"Red construida: {nodos.Count} nodos");
    }

    // Prepara las listas de vecinos con sus distancias (se hace una vez, no en cada cálculo)
    private void ConstruirGrafo()
    {
        int n = nodos.Count;

        indice = new Dictionary<NodoCamino, int>(n);
        for (int i = 0; i < n; i++) indice[nodos[i]] = i;

        adyacencia = new List<(int vecino, float peso)>[n];
        for (int i = 0; i < n; i++)
        {
            adyacencia[i] = new List<(int vecino, float peso)>();
            foreach (NodoCamino c in nodos[i].conexiones)
            {
                if (c == null || !indice.TryGetValue(c, out int j) || j == i) continue;
                float peso = Vector3.Distance(nodos[i].transform.position, nodos[j].transform.position);
                adyacencia[i].Add((j, peso));
            }
        }

        dist = new float[n];
        previo = new int[n];
    }

    /// <summary>Nodo más cercano a una posición (se mide en el plano, sin altura).</summary>
    public NodoCamino NodoMasCercano(Vector3 pos, out float distancia)
    {
        NodoCamino mejor = null;
        distancia = float.PositiveInfinity;

        foreach (NodoCamino n in nodos)
        {
            float dx = n.transform.position.x - pos.x;
            float dz = n.transform.position.z - pos.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < distancia) { distancia = d; mejor = n; }
        }
        return mejor;
    }

    /// <summary>
    /// Devuelve los puntos de la ruta: origen -> nodos del camino (termina en el nodo de llegada).
    /// Si el lugar tiene entradas marcadas, usa la más corta POR CAMINO; si no tiene, usa el nodo
    /// más cercano al destino. Si no hay red, el origen queda lejos de los caminos o no hay
    /// conexión, devuelve una línea recta y porCaminos = false.
    /// </summary>
    public List<Vector3> CalcularRuta(Vector3 origen, Vector3 destino, int lugarId,
                                      float maxUnidadesAlosCaminos, out bool porCaminos)
    {
        porCaminos = false;
        List<Vector3> recta = new List<Vector3> { origen, destino };

        if (nodos.Count == 0) return recta;

        NodoCamino inicio = NodoMasCercano(origen, out float distInicio);
        if (inicio == null || distInicio > maxUnidadesAlosCaminos) return recta;

        Dijkstra(indice[inicio]);

        // ¿A qué nodo hay que llegar?
        int fin = -1;
        bool tieneEntradas = false;
        float mejor = float.PositiveInfinity;

        if (lugarId > 0)
        {
            for (int i = 0; i < nodos.Count; i++)
            {
                if (nodos[i].entradaDeLugarId != lugarId) continue;
                tieneEntradas = true;
                if (dist[i] < mejor) { mejor = dist[i]; fin = i; }
            }
        }

        if (!tieneEntradas)
        {
            NodoCamino cerca = NodoMasCercano(destino, out _);
            fin = cerca != null ? indice[cerca] : -1;
        }

        if (fin < 0 || float.IsInfinity(dist[fin])) return recta;   // la red no conecta esos puntos

        List<Vector3> camino = new List<Vector3>();
        for (int i = fin; i != -1; i = previo[i]) camino.Add(nodos[i].transform.position);
        camino.Reverse();

        // La ruta TERMINA en el nodo de llegada: no continúa hasta las coordenadas del lugar
        porCaminos = true;
        List<Vector3> ruta = new List<Vector3> { origen };
        ruta.AddRange(camino);
        return ruta;
    }

    // Distancia mínima desde un nodo a todos los demás (Dijkstra con cola ordenada)
    private void Dijkstra(int origen)
    {
        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = float.PositiveInfinity;
            previo[i] = -1;
        }
        dist[origen] = 0f;

        SortedSet<(float d, int i)> cola = new SortedSet<(float d, int i)>();
        cola.Add((0f, origen));

        while (cola.Count > 0)
        {
            (float d, int i) actual = cola.Min;
            cola.Remove(actual);

            foreach ((int vecino, float peso) in adyacencia[actual.i])
            {
                float alt = actual.d + peso;
                if (alt < dist[vecino])
                {
                    if (!float.IsInfinity(dist[vecino])) cola.Remove((dist[vecino], vecino));
                    dist[vecino] = alt;
                    previo[vecino] = actual.i;
                    cola.Add((alt, vecino));
                }
            }
        }
    }
}