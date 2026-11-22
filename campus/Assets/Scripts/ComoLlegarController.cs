using System.Collections;
using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;

/// <summary>
/// Botón "CÓMO LLEGAR": calcula la ruta más corta por la red de caminos
/// desde el marcador hasta la entrada del lugar elegido y la dibuja.
/// Si lo elegido es un servicio (un salón, por ejemplo), la ruta lleva al lugar al que
/// pertenece y el texto muestra el piso y la indicación para llegar dentro.
/// </summary>
public class ComoLlegarController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private UbicacionController ubicacion;
    [SerializeField] private LugaresService lugares;
    [SerializeField] private RedCaminos red;
    [SerializeField] private LineRenderer linea;
    [SerializeField] private Transform pinDestino;     // opcional
    [SerializeField] private TMP_Text textoRuta;       // opcional

    [Header("Ajustes")]
    [SerializeField] private float anchoLinea = 3f;
    [SerializeField] private float velocidadCaminarMs = 1.3f;       // metros por segundo
    [SerializeField] private float distanciaLlegadaM = 15f;
    [Tooltip("Si estás más lejos que esto del camino, el texto te avisa cuánto caminar hasta él (la ruta igual usa los caminos)")]
    [SerializeField] private float maxDistanciaAlosCaminosM = 80f;
    [Tooltip("Si el destino está a menos de estos metros, se va en línea recta")]
    [SerializeField] private float distanciaDirectaM = 25f;
    [SerializeField] private float recalcularCadaSegundos = 0.5f;

    [Header("Suavizado de la línea (solo visual: la distancia y el recorrido no cambian)")]
    [SerializeField] private bool suavizarRuta = true;
    [Tooltip("Radio máximo de la curva en cada esquina, en metros. Nunca pasa del 45% del tramo más corto, así no se sale del camino.")]
    [SerializeField, Range(1f, 30f)] private float radioCurvaM = 8f;
    [Tooltip("Puntos con los que se dibuja cada curva (más = más redonda)")]
    [SerializeField, Range(2, 16)] private int puntosPorCurva = 8;
    [Tooltip("Giros menores a este ángulo se dejan rectos (no vale la pena curvarlos)")]
    [SerializeField, Range(0f, 45f)] private float anguloMinimoGrados = 8f;

    [Header("Pruebas en el editor (0 = ninguno)")]
    [SerializeField] private int idPruebaEnEditor = 0;            // id de un lugar
    [SerializeField] private int idServicioPruebaEnEditor = 0;    // id de un servicio (tiene prioridad)

    private LugaresService.Lugar seleccionado;
    private LugaresService.Servicio servicioSel;
    private bool rutaActiva;
    private bool mapaListo;
    private float proximoCalculo;

    // ----- Estado de solo lectura (lo usa el panel de pruebas del Editor) -----
    public LugaresService.Lugar Destino => seleccionado;
    public LugaresService.Servicio ServicioDestino => servicioSel;
    public bool RutaActiva => rutaActiva;
    public bool ListoParaCalcular => mapaListo && ubicacion.TieneUbicacion;
    /// <summary>Última ruta calculada para el destino actual (null mientras se calcula o si no hay ruta).</summary>
    public RedCaminos.Ruta UltimaRuta { get; private set; }
    public int PuntosDibujados => linea.positionCount;
    /// <summary>Puntos que debería tener la línea para la ruta actual (incluye los de las curvas).</summary>
    public int PuntosEsperados { get; private set; }
    public string TextoMostrado => textoRuta != null ? textoRuta.text : "";

    /// <summary>Si es false, elegir un destino no suma al ranking del PDF (lo apagan las pruebas automáticas).</summary>
    [System.NonSerialized] public bool registrarBusquedas = true;

    private void Awake()
    {
        mapa.OnInitialized += () => mapaListo = true;

        // Si el administrador edita o borra el lugar elegido, la ruta usa los datos nuevos
        lugares.LugaresCargados += _ => RefrescarSeleccion();
    }

    private IEnumerator Start()
    {
        linea.useWorldSpace = true;
        linea.positionCount = 2;
        linea.startWidth = anchoLinea;
        linea.endWidth = anchoLinea;
        linea.numCornerVertices = 4;   // uniones redondeadas entre segmentos
        linea.numCapVertices = 4;      // extremos redondeados
        linea.enabled = false;
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);

        if (Application.isEditor && (idPruebaEnEditor > 0 || idServicioPruebaEnEditor > 0))
        {
            while (!lugares.CargaTerminada) yield return null;

            if (idServicioPruebaEnEditor > 0) SeleccionarServicioPorId(idServicioPruebaEnEditor);
            else SeleccionarPorId(idPruebaEnEditor);
            MostrarRuta();
        }
    }

    /// <summary>
    /// Llamar cuando el usuario elige un lugar (resultado de búsqueda o ficha).
    /// Si ya hay una ruta en pantalla, se reemplaza de inmediato por la del lugar nuevo.
    /// </summary>
    public void SeleccionarLugar(LugaresService.Lugar lugar) => Seleccionar(lugar, null);

    public void SeleccionarPorId(int id) => SeleccionarLugar(lugares.BuscarPorId(id));

    /// <summary>Llamar cuando el usuario elige un servicio. La ruta va al lugar al que pertenece.</summary>
    public void SeleccionarServicio(LugaresService.Servicio servicio)
    {
        if (servicio == null) { Seleccionar(null, null); return; }
        Seleccionar(lugares.BuscarPorId(servicio.lugar_id), servicio);
    }

    public void SeleccionarServicioPorId(int id) => SeleccionarServicio(lugares.BuscarServicioPorId(id));

    /// <summary>Elige el lugar y muestra la ruta en un solo paso (útil para el botón de un resultado).</summary>
    public void IrA(LugaresService.Lugar lugar) { SeleccionarLugar(lugar); MostrarRuta(); }

    /// <summary>Elige el servicio y muestra la ruta a su lugar en un solo paso.</summary>
    public void IrAServicio(LugaresService.Servicio servicio) { SeleccionarServicio(servicio); MostrarRuta(); }

    // Punto único de cambio de destino: lugar y servicio se fijan juntos antes de recalcular
    private void Seleccionar(LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
        if (lugar == null)
        {
            // Destino inválido (no existe o fue borrado): no se deja la ruta anterior en pantalla
            seleccionado = null;
            servicioSel = null;
            LimpiarRuta();
            if (textoRuta != null) textoRuta.text = "Ese lugar ya no está disponible";
            return;
        }

        seleccionado = lugar;
        servicioSel = servicio;
        if (registrarBusquedas) lugares.RegistrarBusqueda(lugar.id);   // alimenta el reporte PDF

        if (rutaActiva) RecalcularAhora();     // reemplaza la ruta que estaba en pantalla
    }

    /// <summary>Conéctalo al OnClick del botón "CÓMO LLEGAR".</summary>
    public void MostrarRuta()
    {
        if (seleccionado == null)
        {
            if (textoRuta != null) textoRuta.text = "Elige un lugar primero";
            return;
        }
        rutaActiva = true;
        linea.enabled = true;
        if (pinDestino != null) pinDestino.gameObject.SetActive(true);
        RecalcularAhora();
    }

    /// <summary>Conéctalo al botón de cerrar la ficha o cancelar la ruta.</summary>
    public void LimpiarRuta()
    {
        rutaActiva = false;
        UltimaRuta = null;
        linea.positionCount = 0;
        linea.enabled = false;
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);
        if (textoRuta != null) textoRuta.text = "";
    }

    // Borra la ruta vieja y calcula la nueva en este mismo frame si ya se puede;
    // si todavía no hay mapa o ubicación, Update la calcula en cuanto estén listos.
    private void RecalcularAhora()
    {
        UltimaRuta = null;
        PuntosEsperados = 0;
        linea.positionCount = 0;
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);
        if (textoRuta != null) textoRuta.text = $"Calculando ruta a {seleccionado.nombre}...";

        proximoCalculo = 0f;
        if (mapaListo && ubicacion.TieneUbicacion)
        {
            proximoCalculo = Time.time + recalcularCadaSegundos;
            Recalcular(ubicacion.PosicionMarcador);
        }
    }

    // Tras una recarga de datos, cambia el lugar elegido por su versión nueva (o limpia si lo borraron)
    private void RefrescarSeleccion()
    {
        if (seleccionado == null) return;

        LugaresService.Lugar nuevo = lugares.BuscarPorId(seleccionado.id);
        LugaresService.Servicio nuevoServicio = servicioSel != null ? lugares.BuscarServicioPorId(servicioSel.id) : null;

        if (nuevo == null) { Seleccionar(null, null); return; }

        seleccionado = nuevo;
        servicioSel = nuevoServicio;
        if (rutaActiva) RecalcularAhora();
    }

    private void Update()
    {
        if (!rutaActiva || !mapaListo || !ubicacion.TieneUbicacion || seleccionado == null) return;

        Vector3 origen = ubicacion.PosicionMarcador;

        if (Time.time >= proximoCalculo)
        {
            proximoCalculo = Time.time + recalcularCadaSegundos;
            Recalcular(origen);
        }
        else if (linea.positionCount > 0)
        {
            linea.SetPosition(0, origen);   // el inicio sigue al marcador sin recalcular todo
        }
    }

    private void Recalcular(Vector3 origenMundo)
    {
        Vector2d origen = ubicacion.UbicacionActual;
        Vector2d destino = new Vector2d(seleccionado.latitud, seleccionado.longitud);

        // La ruta se calcula en lat/lon; se pasa al mundo de Unity en cada cálculo,
        // así sigue alineada aunque el mapa se mueva o cambie de zoom
        RedCaminos.Ruta ruta = red.CalcularRuta(origen, destino, seleccionado.id, distanciaDirectaM);
        UltimaRuta = ruta;

        List<Vector2d> trazo = suavizarRuta ? Suavizar(ruta.puntos) : ruta.puntos;

        Vector3[] puntos = new Vector3[trazo.Count];
        for (int i = 0; i < puntos.Length; i++)
        {
            Vector3 p = mapa.GeoToWorldPosition(trazo[i], false);
            p.y = origenMundo.y;
            puntos[i] = p;
        }
        puntos[0] = origenMundo;   // el inicio sale exactamente del marcador

        PuntosEsperados = puntos.Length;
        linea.positionCount = puntos.Length;
        linea.SetPositions(puntos);

        // El pin queda donde termina la ruta (la entrada del lugar o el lugar)
        if (pinDestino != null)
        {
            pinDestino.position = puntos[puntos.Length - 1];
            pinDestino.gameObject.SetActive(true);
        }

        if (!ruta.porCaminos) Debug.Log($"Ruta en línea recta: {ruta.motivoRecta}");

        if (textoRuta == null) return;

        // Texto: si es un servicio, se agrega su piso e indicación para llegar dentro del lugar
        string titulo = seleccionado.nombre;
        string detalle = "";
        if (servicioSel != null)
        {
            titulo = $"{servicioSel.nombre} · {seleccionado.nombre}";
            if (!string.IsNullOrEmpty(servicioSel.piso)) detalle = $"Piso {servicioSel.piso}";
            if (!string.IsNullOrEmpty(servicioSel.indicacion))
                detalle += (detalle.Length > 0 ? " · " : "") + servicioSel.indicacion;
            if (detalle.Length > 0) detalle = "\n" + detalle;
        }

        double metros = ruta.metrosTotal;
        if (metros <= distanciaLlegadaM)
        {
            textoRuta.text = $"<b>Has llegado</b>\n{seleccionado.nombre}{Pequeno(detalle)}";
            return;
        }

        // Línea 1: destino en negrita · Línea 2: distancia y tiempo · Línea 3 (pequeña): avisos y piso
        int minutos = Mathf.Max(1, Mathf.CeilToInt((float)(metros / velocidadCaminarMs) / 60f));
        string resumen = $"<b>{titulo}</b>\n{metros:0} m  ·  {minutos} min a pie";

        string aviso = "";
        if (!ruta.porCaminos && ruta.motivoRecta != "el destino está muy cerca") aviso = "\nEn línea recta (fuera de los caminos)";
        else if (ruta.porCaminos && ruta.metrosHastaCamino > maxDistanciaAlosCaminosM)
            aviso = $"\nCamina {ruta.metrosHastaCamino:0} m hasta el camino más cercano";

        textoRuta.text = resumen + Pequeno(aviso + detalle);
    }

    // Texto secundario (avisos, piso, indicación) más pequeño y un poco transparente
    private static string Pequeno(string texto) =>
        string.IsNullOrEmpty(texto) ? "" : $"<size=80%><alpha=#CC>{texto}</alpha></size>";

    /// <summary>
    /// Redondea cada esquina de la ruta con una curva (Bézier cuadrática) que empieza y termina
    /// a 'radio' metros del nodo. Los tramos rectos no se tocan, y el radio nunca pasa del 45%
    /// de los tramos vecinos, así dos curvas seguidas no se cruzan y la línea no se sale del camino.
    /// El primer y el último punto (tu ubicación y el destino) quedan exactamente igual.
    /// </summary>
    private List<Vector2d> Suavizar(List<Vector2d> pts)
    {
        if (pts.Count < 3) return pts;

        List<Vector2d> salida = new List<Vector2d>(pts.Count * puntosPorCurva) { pts[0] };
        double cosMinimo = System.Math.Cos(anguloMinimoGrados * System.Math.PI / 180.0);

        for (int i = 1; i < pts.Count - 1; i++)
        {
            Vector2d anterior = pts[i - 1], esquina = pts[i], siguiente = pts[i + 1];
            double d1 = RedCaminos.Metros(anterior, esquina);
            double d2 = RedCaminos.Metros(esquina, siguiente);
            double r = System.Math.Min(radioCurvaM, 0.45 * System.Math.Min(d1, d2));

            // Giro casi recto o tramos muy cortos: se deja la esquina tal cual
            if (r < 0.5 || CosenoGiro(anterior, esquina, siguiente) > cosMinimo)
            {
                salida.Add(esquina);
                continue;
            }

            Vector2d a = Interpolar(esquina, anterior, r / d1);    // donde empieza la curva
            Vector2d b = Interpolar(esquina, siguiente, r / d2);   // donde termina

            for (int k = 0; k <= puntosPorCurva; k++)
            {
                double t = (double)k / puntosPorCurva;
                double u = 1.0 - t;
                // Bézier cuadrática con la esquina como punto de control
                salida.Add(new Vector2d(
                    u * u * a.x + 2 * u * t * esquina.x + t * t * b.x,
                    u * u * a.y + 2 * u * t * esquina.y + t * t * b.y));
            }
        }

        salida.Add(pts[pts.Count - 1]);
        return salida;
    }

    private static Vector2d Interpolar(Vector2d desde, Vector2d hacia, double t) =>
        new Vector2d(desde.x + (hacia.x - desde.x) * t, desde.y + (hacia.y - desde.y) * t);

    // Coseno del ángulo entre la dirección de llegada y la de salida (1 = sigue recto)
    private static double CosenoGiro(Vector2d a, Vector2d b, Vector2d c)
    {
        double k = System.Math.Cos(b.x * System.Math.PI / 180.0);   // corrige la longitud según la latitud
        double x1 = (b.y - a.y) * k, y1 = b.x - a.x;
        double x2 = (c.y - b.y) * k, y2 = c.x - b.x;
        double n = System.Math.Sqrt((x1 * x1 + y1 * y1) * (x2 * x2 + y2 * y2));
        return n < 1e-18 ? 1.0 : (x1 * x2 + y1 * y2) / n;
    }
}
