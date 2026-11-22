using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Campus > Pruebas en Play: herramienta SOLO del Editor para probar sin tener la UI terminada.
///  1. Buscar un lugar o servicio (registra la búsqueda y traza la ruta, como el buscador).
///  2. Ruta: ver la ruta actual, mover tu ubicación simulada y probar el cambio de destino.
///  3. Ver el ranking de búsquedas en vivo.
///  4. Generar el reporte PDF (sin login: es una herramienta de desarrollo, no va en el APK).
/// </summary>
public class PanelPruebas : EditorWindow
{
    private string filtro = "";
    private Vector2 scrollVentana, scrollLista;
    private List<RepositorioCampus.LugarRanking> ranking = new List<RepositorioCampus.LugarRanking>();
    private int totalBusquedas;
    private string mensaje = "";

    // Ruta
    private bool verRuta = true;
    private int lugarUbicacion;
    private double latEditada, lonEditada;
    private bool ubicacionLeida;
    private readonly List<(string nombre, bool ok, string detalle)> resultados = new List<(string, bool, string)>();

    [MenuItem("Campus/Pruebas en Play (buscar y PDF)", false, 0)]
    private static void Abrir()
    {
        PanelPruebas w = GetWindow<PanelPruebas>(false, "Pruebas Campus", true);
        w.minSize = new Vector2(400, 520);
        w.ActualizarRanking();
    }

    private void OnEnable() => EditorApplication.playModeStateChanged += AlCambiarPlay;
    private void OnDisable() => EditorApplication.playModeStateChanged -= AlCambiarPlay;

    private void AlCambiarPlay(PlayModeStateChange _)
    {
        ubicacionLeida = false;
        resultados.Clear();
        ActualizarRanking();
        Repaint();
    }

    // Refresca la ventana mientras corre Play (para ver la ruta en vivo)
    private void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    private void ActualizarRanking()
    {
        try { ranking = RepositorioCampus.RankingBusquedas(10, out totalBusquedas); }
        catch (System.Exception e) { mensaje = "No se pudo leer la base: " + e.Message; }
    }

    private void OnGUI()
    {
        LugaresService servicio = Application.isPlaying ? FindFirstObjectByType<LugaresService>() : null;
        ComoLlegarController comoLlegar = Application.isPlaying ? FindFirstObjectByType<ComoLlegarController>() : null;
        UbicacionController ubicacion = Application.isPlaying ? FindFirstObjectByType<UbicacionController>() : null;
        bool datosListos = servicio != null && servicio.CargaTerminada;

        scrollVentana = EditorGUILayout.BeginScrollView(scrollVentana);

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Dale Play a la escena Santiago para buscar y probar rutas. El PDF funciona sin Play.", MessageType.Info);
        else if (!datosListos)
            EditorGUILayout.HelpBox("Cargando la base de datos...", MessageType.None);

        if (datosListos) DibujarBuscar(servicio, comoLlegar);
        if (datosListos) DibujarRuta(servicio, comoLlegar, ubicacion);
        DibujarRanking();
        DibujarPdf();

        if (!string.IsNullOrEmpty(mensaje))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(mensaje, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================
    // 1. Buscar
    // ==================================================================
    private void DibujarBuscar(LugaresService servicio, ComoLlegarController comoLlegar)
    {
        EditorGUILayout.LabelField("1. Buscar (como lo haría el usuario)", EditorStyles.boldLabel);

        filtro = EditorGUILayout.TextField("Nombre o categoría", filtro);
        string f = filtro.Trim().ToLowerInvariant();

        scrollLista = EditorGUILayout.BeginScrollView(scrollLista, GUILayout.Height(150));

        foreach (LugaresService.Lugar l in servicio.Lugares)
        {
            if (f.Length > 0 && !Coincide(f, l.nombre, l.categoria)) continue;
            if (Fila($"{l.nombre}  ·  {l.categoria}", "Buscar"))
            {
                if (comoLlegar != null) comoLlegar.IrA(l);
                else servicio.RegistrarBusqueda(l.id);
                mensaje = $"Búsqueda registrada: {l.nombre}";
                ActualizarRanking();
            }
        }

        foreach (LugaresService.Servicio s in servicio.Servicios)
        {
            if (f.Length > 0 && !Coincide(f, s.nombre, s.categoria)) continue;
            LugaresService.Lugar dueño = servicio.BuscarPorId(s.lugar_id);
            if (Fila($"   {s.nombre}  (en {dueño?.nombre ?? "?"})", "Buscar"))
            {
                if (comoLlegar != null) comoLlegar.IrAServicio(s);
                else servicio.RegistrarBusqueda(s.lugar_id);
                mensaje = $"Búsqueda registrada: {s.nombre} → cuenta para {dueño?.nombre}";
                ActualizarRanking();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    // ==================================================================
    // 2. Ruta
    // ==================================================================
    private void DibujarRuta(LugaresService servicio, ComoLlegarController comoLlegar, UbicacionController ubicacion)
    {
        EditorGUILayout.Space();
        verRuta = EditorGUILayout.Foldout(verRuta, "2. Ruta (Cómo llegar)", true, EditorStyles.foldoutHeader);
        if (!verRuta) return;

        if (comoLlegar == null)
        {
            EditorGUILayout.HelpBox("No hay ComoLlegarController en la escena.", MessageType.Warning);
            return;
        }

        // ----- Estado actual -----
        using (new EditorGUI.IndentLevelScope())
        {
            RedCaminos.Ruta r = comoLlegar.UltimaRuta;
            string destino = comoLlegar.Destino == null ? "(ninguno)" : comoLlegar.Destino.nombre;
            if (comoLlegar.ServicioDestino != null) destino = $"{comoLlegar.ServicioDestino.nombre} · {destino}";

            EditorGUILayout.LabelField("Destino", destino);
            EditorGUILayout.LabelField("Estado", !comoLlegar.RutaActiva ? "Sin ruta"
                : !comoLlegar.ListoParaCalcular ? "Esperando mapa o ubicación"
                : r == null ? "Calculando..." : "Ruta en pantalla");

            if (r != null)
            {
                EditorGUILayout.LabelField("Tipo", r.porCaminos
                    ? (r.terminaEnEntrada ? "Por caminos → entrada del lugar" : "Por caminos → punto más cercano al lugar")
                    : $"Línea recta ({r.motivoRecta})");
                EditorGUILayout.LabelField("Distancia", $"{r.metrosTotal:0} m");
                if (r.porCaminos) EditorGUILayout.LabelField("Hasta el primer camino", $"{r.metrosHastaCamino:0} m");

                bool coincide = comoLlegar.PuntosDibujados == comoLlegar.PuntosEsperados;
                EditorGUILayout.LabelField("Puntos de la ruta", r.puntos.Count.ToString());
                EditorGUILayout.LabelField("Puntos dibujados (con curvas)",
                    $"{comoLlegar.PuntosDibujados}" + (coincide ? "  ✓" : $"  ✗ se esperaban {comoLlegar.PuntosEsperados}"));
            }

            string texto = comoLlegar.TextoMostrado;
            if (!string.IsNullOrEmpty(texto)) EditorGUILayout.LabelField("Texto en pantalla", texto.Replace("\n", " | "), EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Mostrar ruta")) comoLlegar.MostrarRuta();
        if (GUILayout.Button("Limpiar ruta")) comoLlegar.LimpiarRuta();
        EditorGUILayout.EndHorizontal();

        // ----- Mi ubicación simulada -----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Mi ubicación simulada", EditorStyles.miniBoldLabel);

        if (ubicacion == null)
        {
            EditorGUILayout.HelpBox("No hay UbicacionController en la escena.", MessageType.Warning);
        }
        else
        {
            SerializedObject so = new SerializedObject(ubicacion);
            SerializedProperty simular = so.FindProperty("usarSimulacionEnEditor");
            SerializedProperty lat = so.FindProperty("latSimulada");
            SerializedProperty lon = so.FindProperty("lonSimulada");

            if (simular != null && !simular.boolValue)
                EditorGUILayout.HelpBox("La simulación está apagada en UbicacionController ('Usar Simulacion En Editor').", MessageType.Warning);

            if (!ubicacionLeida) { latEditada = lat.doubleValue; lonEditada = lon.doubleValue; ubicacionLeida = true; }

            if (ubicacion.TieneUbicacion)
            {
                EditorGUILayout.LabelField("Dentro del campus", ubicacion.EstaEnCampus
                    ? "Sí (el marcador está en tu posición)"
                    : "No (el marcador usa el punto fijo de inicio)");
            }

            latEditada = EditorGUILayout.DoubleField("Latitud", latEditada);
            lonEditada = EditorGUILayout.DoubleField("Longitud", lonEditada);

            if (GUILayout.Button("Moverme a esta posición"))
                MoverUbicacion(so, lat, lon, latEditada, lonEditada, "posición escrita");

            string[] nombres = servicio.Lugares.ConvertAll(l => l.nombre).ToArray();
            if (nombres.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                lugarUbicacion = Mathf.Clamp(EditorGUILayout.Popup("Junto a", lugarUbicacion, nombres), 0, nombres.Length - 1);
                if (GUILayout.Button("Moverme", GUILayout.Width(80)))
                {
                    LugaresService.Lugar l = servicio.Lugares[lugarUbicacion];
                    // Unos 40 m al sur del lugar, para que haya algo de recorrido
                    MoverUbicacion(so, lat, lon, l.latitud - 0.00036, l.longitud, $"junto a {l.nombre}");
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ----- Prueba automática -----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prueba automática de cambio de destino", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField(
            "Elige uno tras otro todos los lugares y servicios y verifica que cada vez la ruta se reemplace. " +
            "No suma búsquedas al ranking.", EditorStyles.wordWrappedMiniLabel);

        GUI.enabled = comoLlegar.ListoParaCalcular;
        if (GUILayout.Button("Ejecutar prueba", GUILayout.Height(24))) ProbarCambios(servicio, comoLlegar);
        GUI.enabled = true;
        if (!comoLlegar.ListoParaCalcular)
            EditorGUILayout.LabelField("Espera a que el mapa y la ubicación estén listos.", EditorStyles.miniLabel);

        foreach (var (nombre, ok, detalle) in resultados)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ok ? "✓" : "✗", GUILayout.Width(18));
            EditorGUILayout.LabelField(nombre, GUILayout.MinWidth(120));
            EditorGUILayout.LabelField(detalle, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void MoverUbicacion(SerializedObject so, SerializedProperty lat, SerializedProperty lon,
                                double nuevaLat, double nuevaLon, string descripcion)
    {
        lat.doubleValue = nuevaLat;
        lon.doubleValue = nuevaLon;
        so.ApplyModifiedProperties();
        latEditada = nuevaLat;
        lonEditada = nuevaLon;
        mensaje = $"Ubicación simulada movida ({descripcion}). La ruta se recalcula sola en menos de un segundo.";
    }

    // Elige todos los destinos en orden y comprueba, en el mismo frame, que la ruta nueva reemplazó a la anterior
    private void ProbarCambios(LugaresService servicio, ComoLlegarController comoLlegar)
    {
        resultados.Clear();
        comoLlegar.registrarBusquedas = false;

        try
        {
            foreach (LugaresService.Lugar l in servicio.Lugares)
            {
                comoLlegar.IrA(l);
                resultados.Add(Verificar(l.nombre, comoLlegar, l.id, null));
            }
            foreach (LugaresService.Servicio s in servicio.Servicios)
            {
                comoLlegar.IrAServicio(s);
                resultados.Add(Verificar(s.nombre, comoLlegar, s.lugar_id, s.id));
            }
        }
        finally
        {
            comoLlegar.registrarBusquedas = true;
        }

        int fallas = resultados.FindAll(x => !x.ok).Count;
        mensaje = fallas == 0
            ? $"Prueba superada: {resultados.Count} cambios de destino, todos reemplazaron la ruta."
            : $"{fallas} de {resultados.Count} cambios fallaron. Revisa la lista.";
        Debug.Log("Pruebas Campus: " + mensaje);
    }

    private static (string, bool, string) Verificar(string nombre, ComoLlegarController c, int lugarEsperado, int? servicioEsperado)
    {
        RedCaminos.Ruta r = c.UltimaRuta;

        if (c.Destino == null || c.Destino.id != lugarEsperado) return (nombre, false, "el destino no cambió");
        if (servicioEsperado.HasValue && (c.ServicioDestino == null || c.ServicioDestino.id != servicioEsperado.Value))
            return (nombre, false, "el servicio no cambió");
        if (!servicioEsperado.HasValue && c.ServicioDestino != null) return (nombre, false, "quedó el servicio anterior");
        if (r == null) return (nombre, false, "no se calculó la ruta");
        if (c.PuntosDibujados != c.PuntosEsperados) return (nombre, false, "la línea dibujada es de otra ruta");

        string tipo = r.porCaminos ? (r.terminaEnEntrada ? "caminos → entrada" : "caminos") : $"recta: {r.motivoRecta}";
        bool ok = r.porCaminos || r.motivoRecta == "el destino está muy cerca";
        return (nombre, ok, $"{r.metrosTotal:0} m · {tipo}");
    }

    // ==================================================================
    // 3. Ranking y 4. PDF
    // ==================================================================
    private void DibujarRanking()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"3. Ranking actual  (total: {totalBusquedas} búsquedas)", EditorStyles.boldLabel);
        if (GUILayout.Button("Actualizar", GUILayout.Width(80))) ActualizarRanking();
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < ranking.Count; i++)
            EditorGUILayout.LabelField($"{i + 1}. {ranking[i].nombre}", ranking[i].total.ToString());
    }

    private void DibujarPdf()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4. Reporte PDF", EditorStyles.boldLabel);
        if (GUILayout.Button("Generar y abrir carpeta del PDF", GUILayout.Height(28)))
        {
            try { mensaje = "PDF guardado en:\n" + AdminService.GuardarReportePdf(); }
            catch (System.Exception e) { mensaje = "Error al generar el PDF: " + e.Message; }
        }
    }

    // ==================================================================
    // Utilidades
    // ==================================================================
    private static bool Coincide(string filtro, params string[] campos)
    {
        foreach (string c in campos)
            if (!string.IsNullOrEmpty(c) && c.ToLowerInvariant().Contains(filtro)) return true;
        return false;
    }

    private static bool Fila(string texto, string boton)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(texto);
        bool clic = GUILayout.Button(boton, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();
        return clic;
    }
}
