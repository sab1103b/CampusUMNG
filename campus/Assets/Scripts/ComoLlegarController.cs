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
    [SerializeField] private float maxDistanciaAlosCaminosM = 80f;  // más lejos que esto = línea recta
    [SerializeField] private float recalcularCadaSegundos = 0.5f;

    [Header("Pruebas en el editor (0 = ninguno)")]
    [SerializeField] private int idPruebaEnEditor = 0;            // id de un lugar
    [SerializeField] private int idServicioPruebaEnEditor = 0;    // id de un servicio (tiene prioridad)

    private LugaresService.Lugar seleccionado;
    private LugaresService.Servicio servicioSel;
    private bool rutaActiva;
    private bool mapaListo;
    private double metrosPorUnidad;
    private float proximoCalculo;

    private void Awake()
    {
        mapa.OnInitialized += () => mapaListo = true;
    }

    private IEnumerator Start()
    {
        linea.useWorldSpace = true;
        linea.positionCount = 2;
        linea.startWidth = anchoLinea;
        linea.endWidth = anchoLinea;
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

    /// <summary>Llamar cuando el usuario elige un lugar (resultado de búsqueda o ficha).</summary>
    public void SeleccionarLugar(LugaresService.Lugar lugar)
    {
        seleccionado = lugar;
        servicioSel = null;
        proximoCalculo = 0f;
        if (lugar != null) lugares.RegistrarBusqueda(lugar.id);   // alimenta el reporte PDF
    }

    public void SeleccionarPorId(int id) => SeleccionarLugar(lugares.BuscarPorId(id));

    /// <summary>Llamar cuando el usuario elige un servicio. La ruta va al lugar al que pertenece.</summary>
    public void SeleccionarServicio(LugaresService.Servicio servicio)
    {
        if (servicio == null) { SeleccionarLugar(null); return; }

        SeleccionarLugar(lugares.BuscarPorId(servicio.lugar_id));   // registra la búsqueda del lugar
        servicioSel = servicio;
    }

    public void SeleccionarServicioPorId(int id) => SeleccionarServicio(lugares.BuscarServicioPorId(id));

    /// <summary>Conéctalo al OnClick del botón "CÓMO LLEGAR".</summary>
    public void MostrarRuta()
    {
        if (seleccionado == null)
        {
            if (textoRuta != null) textoRuta.text = "Elige un lugar primero";
            return;
        }
        rutaActiva = true;
        proximoCalculo = 0f;
        linea.enabled = true;
        if (pinDestino != null) pinDestino.gameObject.SetActive(true);
    }

    /// <summary>Conéctalo al botón de cerrar la ficha o cancelar la ruta.</summary>
    public void LimpiarRuta()
    {
        rutaActiva = false;
        linea.enabled = false;
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);
        if (textoRuta != null) textoRuta.text = "";
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

    private void Recalcular(Vector3 origen)
    {
        if (metrosPorUnidad <= 0) Calibrar(seleccionado.latitud, seleccionado.longitud);

        Vector3 destino = mapa.GeoToWorldPosition(
            new Vector2d(seleccionado.latitud, seleccionado.longitud), false);
        destino.y = origen.y;

        float maxUnidades = (float)(maxDistanciaAlosCaminosM / metrosPorUnidad);
        List<Vector3> ruta = red.CalcularRuta(origen, destino, seleccionado.id, maxUnidades, out bool porCaminos);

        // Todo a la misma altura y se mide el largo de la ruta
        float largoUnidades = 0f;
        for (int i = 0; i < ruta.Count; i++)
        {
            Vector3 p = ruta[i];
            p.y = origen.y;
            ruta[i] = p;
            if (i > 0) largoUnidades += Vector3.Distance(ruta[i - 1], ruta[i]);
        }

        linea.positionCount = ruta.Count;
        linea.SetPositions(ruta.ToArray());

        // El pin queda donde termina la ruta (el nodo de llegada; si no hay camino, el lugar)
        if (pinDestino != null) pinDestino.position = ruta[ruta.Count - 1];

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

        double metros = largoUnidades * metrosPorUnidad;
        if (metros <= distanciaLlegadaM)
        {
            textoRuta.text = $"Has llegado a {seleccionado.nombre}{detalle}";
            return;
        }

        int minutos = Mathf.Max(1, Mathf.CeilToInt((float)(metros / velocidadCaminarMs) / 60f));
        textoRuta.text = porCaminos
            ? $"{titulo} · {metros:0} m · {minutos} min a pie{detalle}"
            : $"{titulo} · {metros:0} m en línea recta (fuera de los caminos){detalle}";
    }

    // Calcula cuántos metros reales equivale una unidad de Unity en el mapa actual
    private void Calibrar(double lat, double lon)
    {
        Vector3 a = mapa.GeoToWorldPosition(new Vector2d(lat, lon), false);
        Vector3 b = mapa.GeoToWorldPosition(new Vector2d(lat + 0.001, lon), false);
        float unidades = Vector3.Distance(a, b);
        double metros = DistanciaMetros(lat, lon, lat + 0.001, lon);
        metrosPorUnidad = unidades > 0.0001f ? metros / unidades : 1.0;
    }

    private static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double aRad = System.Math.PI / 180.0;
        double dLat = (lat2 - lat1) * aRad;
        double dLon = (lon2 - lon1) * aRad;
        double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                   System.Math.Cos(lat1 * aRad) * System.Math.Cos(lat2 * aRad) *
                   System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
        return R * 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));
    }
}