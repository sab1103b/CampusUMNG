using System.Collections;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Marcador "Tú estás aquí":
///  - Si el usuario está DENTRO del polígono del campus (esquinas que tú defines),
///    el marcador muestra su ubicación real.
///  - Si está FUERA, el marcador se coloca en un punto fijo (por ejemplo, el inicio del campus).
/// En ambos casos el marcador gira según la brújula del teléfono.
/// </summary>
public class UbicacionController : MonoBehaviour
{
    [System.Serializable]
    public struct Coordenada
    {
        public double latitud;
        public double longitud;
    }

    [Header("Referencias")]
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private Transform marcador;
    [SerializeField] private TMP_Text textoEstado;   // opcional

    [Header("Límites del campus (esquinas EN ORDEN alrededor del perímetro)")]
    [SerializeField] private Coordenada[] esquinasCampus = new Coordenada[4];
    [Tooltip("Segundos que debe mantenerse el cambio dentro/fuera antes de aceptarlo (evita parpadeo en el borde)")]
    [SerializeField] private float segundosParaCambiar = 2f;

    [Header("Punto fijo del marcador cuando estás FUERA del campus")]
    [SerializeField] private bool usarPuntoFijoFuera = true;   // false = mostrar siempre la ubicación real
    [SerializeField] private double puntoFijoLat = 4.942958;
    [SerializeField] private double puntoFijoLon = -74.015656;

    [Header("Ajustes del marcador")]
    [SerializeField] private float alturaMarcador = 1f;
    [SerializeField] private float suavizadoPosicion = 5f;
    [SerializeField] private float suavizadoRotacion = 6f;

    [Header("Pruebas en el editor (el PC no tiene GPS ni brújula)")]
    [SerializeField] private bool usarSimulacionEnEditor = true;
    [SerializeField] private double latSimulada = 4.9431;     // cambia estos valores para simular dentro/fuera
    [SerializeField] private double lonSimulada = -74.0156;

    private bool mapaListo;
    private bool tieneUbicacion;
    private bool estaEnCampus;
    private bool primerFix = true;
    private float tiempoCambio;
    private bool avisoEsquinas;
    private Vector2d mostrada;
    private Vector2d real;

    // Otros scripts (por ejemplo "CÓMO LLEGAR") leen estos datos
    public bool TieneUbicacion => tieneUbicacion;
    public bool EstaEnCampus => estaEnCampus;
    public Vector2d UbicacionActual => mostrada;   // la que se dibuja (anclada o real)
    public Vector2d UbicacionReal => real;         // la del GPS
    public Vector3 PosicionMarcador => marcador.position;

    private void Awake()
    {
        // Awake corre antes que el Start del mapa, así no nos perdemos el evento
        mapa.OnInitialized += () => mapaListo = true;
    }

    private IEnumerator Start()
    {
        if (usarSimulacionEnEditor && Application.isEditor)
        {
            Debug.Log("Ubicación simulada (editor)");
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            float t = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && t < 30f)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
#endif

        if (!Input.location.isEnabledByUser)
        {
            Mostrar("Activa la ubicación del teléfono y da permiso a la app");
            yield break;
        }

        Input.location.Start(1f, 1f);   // precisión deseada (m), distancia mínima de actualización (m)
        Input.compass.enabled = true;   // brújula interna

        int espera = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && espera > 0)
        {
            yield return new WaitForSeconds(1f);
            espera--;
        }

        if (espera <= 0 || Input.location.status == LocationServiceStatus.Failed)
            Mostrar("No se pudo obtener la ubicación");
    }

    private void Update()
    {
        if (!mapaListo) return;

        bool simulada = usarSimulacionEnEditor && Application.isEditor;
        float precisionM = 0f;

        if (simulada)
        {
            real = new Vector2d(latSimulada, lonSimulada);
        }
        else if (Input.location.status == LocationServiceStatus.Running)
        {
            LocationInfo d = Input.location.lastData;
            real = new Vector2d(d.latitude, d.longitude);
            precisionM = d.horizontalAccuracy;
        }
        else
        {
            return;   // todavía no hay ubicación
        }

        ActualizarEstadoCampus(real.x, real.y);

        mostrada = (usarPuntoFijoFuera && !estaEnCampus)
            ? new Vector2d(puntoFijoLat, puntoFijoLon)
            : real;
        tieneUbicacion = true;

        if (textoEstado != null)
        {
            string lugar = estaEnCampus
                ? "En el campus"
                : (usarPuntoFijoFuera ? "Fuera del campus · punto de inicio" : "Fuera del campus");
            string precision = simulada ? "simulada" : $"±{precisionM:0} m";
            textoEstado.text = $"{lugar} · precisión {precision}";
        }

        // Latitud/longitud -> posición dentro del mundo de Unity
        Vector3 destino = mapa.GeoToWorldPosition(mostrada, false);
        destino.y += alturaMarcador;

        if (primerFix)
        {
            marcador.position = destino;   // la primera vez salta directo
            primerFix = false;
        }
        else
        {
            marcador.position = Vector3.Lerp(marcador.position, destino, Time.deltaTime * suavizadoPosicion);
        }

        // Brújula: el norte del mapa es el eje +Z, así que el rumbo es un giro en Y
        if (!simulada && Input.compass.enabled)
        {
            float rumbo = Input.compass.trueHeading;
            if (rumbo == 0f) rumbo = Input.compass.magneticHeading;

            Quaternion objetivo = Quaternion.Euler(0f, rumbo, 0f);
            marcador.rotation = Quaternion.Slerp(marcador.rotation, objetivo, Time.deltaTime * suavizadoRotacion);
        }
    }

    // Decide si estás dentro del campus. El cambio solo se acepta si se mantiene unos segundos,
    // porque el GPS "salta" unos metros y cerca del borde parpadearía.
    private void ActualizarEstadoCampus(double lat, double lon)
    {
        bool dentroAhora = DentroDelPoligono(lat, lon);

        if (primerFix)
        {
            estaEnCampus = dentroAhora;
            tiempoCambio = 0f;
            return;
        }

        if (dentroAhora != estaEnCampus)
        {
            tiempoCambio += Time.deltaTime;
            if (tiempoCambio >= segundosParaCambiar)
            {
                estaEnCampus = dentroAhora;
                tiempoCambio = 0f;
            }
        }
        else
        {
            tiempoCambio = 0f;
        }
    }

    // Algoritmo del rayo: cuenta cuántas veces cruza los bordes un rayo desde el punto.
    private bool DentroDelPoligono(double lat, double lon)
    {
        int n = esquinasCampus != null ? esquinasCampus.Length : 0;
        if (n < 3)
        {
            if (!avisoEsquinas)
            {
                Debug.LogWarning("UbicacionController: define al menos 3 esquinas del campus.");
                avisoEsquinas = true;
            }
            return false;
        }

        bool dentro = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double yi = esquinasCampus[i].latitud, xi = esquinasCampus[i].longitud;
            double yj = esquinasCampus[j].latitud, xj = esquinasCampus[j].longitud;

            bool cruza = ((yi > lat) != (yj > lat)) &&
                         (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi);
            if (cruza) dentro = !dentro;
        }
        return dentro;
    }

    private void Mostrar(string mensaje)
    {
        Debug.Log("Ubicación: " + mensaje);
        if (textoEstado != null) textoEstado.text = mensaje;
    }
}