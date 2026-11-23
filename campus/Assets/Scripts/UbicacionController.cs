using System;
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
///  - Si no hay permiso, el GPS está apagado o no responde, se usa ese mismo punto fijo
///    como ubicación aproximada (UsandoUbicacionAproximada = true), para que la ruta nunca
///    quede esperando para siempre.
/// En ambos casos el marcador gira según la brújula del teléfono (si está disponible).
///
/// Partes: este archivo (GPS, estados, marcador) y UbicacionController.Halo.cs (halo de precisión).
///
/// Nota técnica: usa Input.location / Input.compass (API de entrada antigua). Es intencional y está
/// permitido: el proyecto tiene activeInputHandler = 1 (ambos sistemas) y el Input System nuevo no
/// ofrece GPS en Android. Toques y mouse sí usan el Input System nuevo (CamaraMapa, EntradaMapa).
/// </summary>
public partial class UbicacionController : MonoBehaviour
{
    [System.Serializable]
    public struct Coordenada
    {
        public double latitud;
        public double longitud;
    }

    private enum Estado { Esperando, Real, Aproximada }

    [Header("Referencias")]
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private Transform marcador;
    [SerializeField] private TMP_Text textoEstado;   // opcional

    [Header("Límites del campus (esquinas EN ORDEN alrededor del perímetro)")]
    [SerializeField] private Coordenada[] esquinasCampus = new Coordenada[4];
    [Tooltip("Segundos que debe mantenerse el cambio dentro/fuera antes de aceptarlo (evita parpadeo en el borde)")]
    [SerializeField] private float segundosParaCambiar = 2f;

    [Header("Punto fijo (entrada principal): fuera del campus o sin ubicación")]
    [SerializeField] private bool usarPuntoFijoFuera = true;   // false = mostrar siempre la ubicación real
    [SerializeField] private double puntoFijoLat = 4.942958;
    [SerializeField] private double puntoFijoLon = -74.015656;

    [Header("Espera del GPS")]
    [Tooltip("Segundos máximos esperando el permiso o el primer dato del GPS antes de usar el punto fijo")]
    [SerializeField] private float segundosMaxEspera = 20f;

    [Header("Ajustes del marcador")]
    [SerializeField] private float alturaMarcador = 1f;
    [SerializeField] private float suavizadoPosicion = 5f;
    [SerializeField] private float suavizadoRotacion = 6f;

    [Header("Pruebas en el editor (el PC no tiene GPS ni brújula)")]
    [SerializeField] private bool usarSimulacionEnEditor = true;
    [SerializeField] private double latSimulada = 4.9431;     // cambia estos valores para simular dentro/fuera
    [SerializeField] private double lonSimulada = -74.0156;

    private const string MsgSinUbicacion = "Ubicación no disponible: se usa la entrada principal como punto de partida.";
    private const string MsgSinPermiso = "Sin permiso de ubicación: se usa la entrada principal como punto de partida.";
    private const string MsgGpsApagado = "GPS apagado: se usa la entrada principal como punto de partida.";
    private const string MsgSenalDebil = "Señal GPS débil: se usa la entrada principal como punto de partida.";
    private const string MsgSenalDebilEspera = "Señal GPS débil. Buscando tu ubicación…";
    private const string MsgSenalDebilReal = "Señal GPS débil · última ubicación conocida";

    [Tooltip("Un dato del GPS más viejo que esto (segundos) no se toma como ubicación real")]
    [SerializeField] private double segundosMaxDatoViejo = 30.0;

    [Tooltip("Con la ubicación aproximada, cada cuántos segundos se revisa si ya hay permiso y GPS para reintentar")]
    [SerializeField] private float segundosEntreReintentos = 12f;

    private bool mapaListo;
    private bool tieneUbicacion;
    private bool estaEnCampus;
    private bool primerFix = true;
    private float tiempoCambio;
    private bool avisoEsquinas;
    private Vector2d mostrada;
    private Vector2d real;

    private Estado estado = Estado.Esperando;
    private string mensajeEstado = "Buscando tu ubicación…";
    private float limiteEspera;
    private bool gpsIniciado;
    private bool pausada;
    private Coroutine rutinaGps;
    private bool exigirDatoFresco = true;   // al iniciar y al volver a la app: el lastData guardado puede ser viejo
    private bool senalDebil;
    private float precisionActualM;
    private float proximoReintento;

    // Para escribir el texto de estado solo cuando cambia algo visible
    private int claveTexto = int.MinValue;

    // Marcador con ubicación aproximada: colores apagados (mitad de alfa y aclarados)
    private MaterialPropertyBlock bloqueMarcador;
    private bool marcadorAproximado;

    // Otros scripts (por ejemplo "CÓMO LLEGAR") leen estos datos
    /// <summary>true solo si hay un dato real del GPS (o la simulación del Editor).</summary>
    public bool TieneUbicacion => tieneUbicacion;
    /// <summary>true si hay un punto de partida para rutas: el real o el aproximado (entrada principal).</summary>
    public bool TieneOrigen => estado != Estado.Esperando;
    /// <summary>true si el GPS no está disponible y se usa la entrada principal como punto de partida.</summary>
    public bool UsandoUbicacionAproximada => estado == Estado.Aproximada;
    /// <summary>Mensaje corto en español sobre el estado de la ubicación.</summary>
    public string MensajeEstado => mensajeEstado;
    /// <summary>Se dispara cuando cambia el estado (esperando / real / aproximada) o su mensaje.</summary>
    public event Action EstadoCambiado;

    public bool EstaEnCampus => estaEnCampus;
    public Vector2d UbicacionActual => mostrada;   // la que se dibuja (anclada, aproximada o real)
    public Vector2d UbicacionReal => real;         // la del GPS (válida solo si TieneUbicacion)
    public Vector3 PosicionMarcador => marcador.position;
    public Transform Marcador => marcador;
    public bool MapaListo => mapaListo;
    /// <summary>Precisión horizontal del último dato real del GPS en metros (0 si es simulada o aproximada).</summary>
    public float PrecisionM => estado == Estado.Real ? precisionActualM : 0f;

    private bool Simulada => usarSimulacionEnEditor && Application.isEditor;

    // Por debajo de esto el marcador ya está en su sitio y no se vuelve a escribir su transform
    private const float UmbralPosicion2 = 1e-6f;    // (0,001 unidades)²
    // Brújula: el rumbo se filtra (media circular exponencial) y el marcador solo gira si el rumbo filtrado
    // se aparta más de UmbralGiroGrados: el temblor del sensor no reescribe el transform en cada frame
    private const float UmbralGiroGrados = 3f;
    private const float FiltroRumbo = 4f;   // rapidez del filtro (1/s)
    private float rumboFiltrado = float.NaN;
    private bool brujulaEncendida;
    private bool girandoBrujula;

    /// <summary>
    /// true mientras el mapa no está a la vista (lo pone ComoLlegarController.Pausar al cambiar de pantalla):
    /// la brújula se apaga para ahorrar batería y se vuelve a encender al volver al mapa.
    /// </summary>
    [NonSerialized] public bool BrujulaEnPausa;

    // Marcador con ubicación aproximada: se aclara hacia el blanco y queda a esta opacidad
    private const float AlfaAproximado = 0.5f;

    private void Awake()
    {
        // Awake corre antes que el Start del mapa, así no nos perdemos el evento
        mapa.OnInitialized += AlIniciarMapa;
    }

    private void AlIniciarMapa() => mapaListo = true;

    private void Start()
    {
        limiteEspera = Time.realtimeSinceStartup + segundosMaxEspera;

        if (Simulada)
        {
            Registro.Info("Ubicación simulada (editor)");
            return;
        }

        SolicitarPermisoEIniciar();
    }

    // ---------- Permiso y arranque del GPS ----------

    private void SolicitarPermisoEIniciar()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += AlConcederPermiso;
            callbacks.PermissionDenied += AlNegarPermiso;
            Permission.RequestUserPermission(Permission.FineLocation, callbacks);
            return;   // si nadie responde, Update aplica el respaldo al vencer la espera
        }
#endif
        IniciarGps();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // El diálogo del permiso responde más tarde: si el controlador ya se destruyó o se desactivó, no se hace nada
    private void AlConcederPermiso(string permiso)
    {
        if (this == null || !isActiveAndEnabled) return;
        IniciarGps();
    }

    private void AlNegarPermiso(string permiso)
    {
        if (this == null || !isActiveAndEnabled) return;
        UsarRespaldo(MsgSinPermiso);
    }
#endif

    // Espera reutilizada (sin basura en cada vuelta); WaitForSecondsRealtime se reinicia sola al terminar
    private readonly WaitForSecondsRealtime esperaGps = new WaitForSecondsRealtime(0.5f);

    private void IniciarGps()
    {
        exigirDatoFresco = true;
        if (rutinaGps != null) StopCoroutine(rutinaGps);
        esperaGps.Reset();   // por si la rutina anterior se detuvo a mitad de una espera
        rutinaGps = StartCoroutine(RutinaGps());
    }

    private IEnumerator RutinaGps()
    {
        if (!Input.location.isEnabledByUser)
        {
            UsarRespaldo(MsgGpsApagado);
            rutinaGps = null;
            yield break;
        }

        Input.location.Start(5f, 1f);   // precisión deseada (m), distancia mínima de actualización (m)
        gpsIniciado = true;
        PonerBrujula(!BrujulaEnPausa);   // brújula interna (solo con el mapa a la vista)

        float limite = Time.realtimeSinceStartup + segundosMaxEspera;
        while (Input.location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < limite)
            yield return esperaGps;

        if (Input.location.status != LocationServiceStatus.Running)
            UsarRespaldo(MsgSinUbicacion);   // tiempo agotado o fallo

        rutinaGps = null;
    }

    private void DetenerGps()
    {
        if (rutinaGps != null) { StopCoroutine(rutinaGps); rutinaGps = null; }
        if (gpsIniciado) Input.location.Stop();
        gpsIniciado = false;
        PonerBrujula(false);
    }

    // Enciende o apaga la brújula solo si cambia (no se toca Input.compass en cada frame)
    private void PonerBrujula(bool encender)
    {
        if (brujulaEncendida == encender) return;
        brujulaEncendida = encender;
        Input.compass.enabled = encender;
        rumboFiltrado = float.NaN;   // al volver a encenderla, el filtro arranca del primer dato
    }

    // Al salir de la app se apaga el GPS (ahorra batería); al volver se reanuda.
    // Si el usuario dio el permiso o encendió el GPS desde Ajustes, aquí se detecta.
    private void OnApplicationPause(bool pausa)
    {
        if (Simulada || pausada == pausa) return;
        pausada = pausa;

        if (pausa)
        {
            DetenerGps();
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) return;   // sigue con el respaldo
#endif
        IniciarGps();
    }

    // Al volver de un diálogo o de Ajustes (sin pausar la app) se revisa si ya se puede usar el GPS
    private void OnApplicationFocus(bool foco)
    {
        if (foco) ReintentarGpsSiSePuede();
    }

    // Con la ubicación aproximada: si ya hay permiso y el GPS está encendido, se vuelve a arrancar.
    // No vuelve a pedir el permiso (no se insiste con el diálogo).
    private void ReintentarGpsSiSePuede()
    {
        proximoReintento = Time.realtimeSinceStartup + segundosEntreReintentos;
        if (Simulada || pausada || estado != Estado.Aproximada || rutinaGps != null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) return;
#endif
        if (!Input.location.isEnabledByUser) return;
        if (gpsIniciado && Input.location.status == LocationServiceStatus.Running) return;   // ya corre: falta el dato
        IniciarGps();
    }

    private void OnDestroy()
    {
        if (mapa != null) mapa.OnInitialized -= AlIniciarMapa;
        if (!Simulada) DetenerGps();
        DestruirHalo();
    }

    // ---------- Estados ----------

    private void UsarRespaldo(string mensaje)
    {
        tieneUbicacion = false;
        estaEnCampus = false;
        mostrada = new Vector2d(puntoFijoLat, puntoFijoLon);
        CambiarEstado(Estado.Aproximada, mensaje);
    }

    private void CambiarEstado(Estado nuevo, string mensaje)
    {
        if (estado == nuevo && mensajeEstado == mensaje) return;
        estado = nuevo;
        mensajeEstado = mensaje;
        Registro.Info("Ubicación: " + mensaje);
        claveTexto = int.MinValue;   // fuerza a reescribir el texto de estado
        EstiloMarcador();
        EstadoCambiado?.Invoke();
    }

    // La ubicación aproximada (entrada principal) no debe verse igual que la real: el marcador se apaga
    // (alfa AlfaAproximado si su material es transparente; además se aclara hacia el blanco en la misma
    // proporción, que se nota también en materiales opacos)
    // y el halo de precisión muestra un radio de RadioAproximadoM. Con la ubicación real vuelve a sus colores.
    private void EstiloMarcador()
    {
        bool aprox = estado == Estado.Aproximada;
        if (marcador == null || aprox == marcadorAproximado) return;
        marcadorAproximado = aprox;

        if (bloqueMarcador == null) bloqueMarcador = new MaterialPropertyBlock();
        foreach (Renderer r in marcador.GetComponentsInChildren<Renderer>(true))
        {
            if (!aprox) { r.SetPropertyBlock(null); continue; }
            Material m = r.sharedMaterial;
            if (m == null) continue;
            string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : null;
            if (prop == null) continue;
            Color c = m.GetColor(prop);
            Color apagado = Color.Lerp(c, Color.white, 1f - AlfaAproximado);
            apagado.a = c.a * AlfaAproximado;
            bloqueMarcador.Clear();
            bloqueMarcador.SetColor(prop, apagado);
            r.SetPropertyBlock(bloqueMarcador);
        }
    }

    // ---------- Cada frame ----------

    private void Update()
    {
        bool simulada = Simulada;
        float precisionM = 0f;
        bool hayDatoReal = false;

        if (simulada)
        {
            real = new Vector2d(latSimulada, lonSimulada);
            hayDatoReal = true;
        }
        else if (!pausada && gpsIniciado && Input.location.status == LocationServiceStatus.Running)
        {
            LocationInfo d = Input.location.lastData;
            if (d.timestamp > 0)   // Running sin ningún dato todavía devuelve 0,0
            {
                // timestamp = segundos desde 1970 (UTC). Al iniciar o volver a la app, Android puede
                // entregar un dato guardado de hace mucho: no se toma como real hasta que llegue uno fresco.
                double edad = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - d.timestamp;
                bool viejo = edad > segundosMaxDatoViejo;
                if (!viejo) exigirDatoFresco = false;

                if (viejo)
                {
                    // Dato viejo: señal débil. Antes del primer dato fresco no se usa; después se
                    // conserva la última posición conocida (no se mueve el marcador a un dato antiguo).
                    senalDebil = true;
                    if (exigirDatoFresco && estado == Estado.Esperando) CambiarEstado(Estado.Esperando, MsgSenalDebilEspera);
                }
                else
                {
                    senalDebil = false;
                    real = new Vector2d(d.latitude, d.longitude);
                    precisionM = d.horizontalAccuracy;
                    precisionActualM = precisionM;
                    hayDatoReal = true;
                }
            }
        }
        else if (!pausada && gpsIniciado && rutinaGps == null && Input.location.status == LocationServiceStatus.Failed)
        {
            DetenerGps();
            UsarRespaldo(MsgSinUbicacion);
        }

        if (hayDatoReal)
        {
            ActualizarEstadoCampus(real.x, real.y);
            mostrada = (usarPuntoFijoFuera && !estaEnCampus)
                ? new Vector2d(puntoFijoLat, puntoFijoLon)
                : real;
            tieneUbicacion = true;
            CambiarEstado(Estado.Real, estaEnCampus ? "En el campus" : "Fuera del campus");
        }
        else if (estado == Estado.Esperando && Time.realtimeSinceStartup >= limiteEspera)
        {
            UsarRespaldo(senalDebil ? MsgSenalDebil : MsgSinUbicacion);   // ni permiso ni GPS respondieron a tiempo
        }

        if (estado == Estado.Aproximada && Time.realtimeSinceStartup >= proximoReintento)
            ReintentarGpsSiSePuede();

        if (estado == Estado.Esperando) return;   // todavía no hay punto de partida

        // Sin dato en este frame (p. ej. el GPS se reinicia al volver a la app y aún no está Running):
        // se muestra la última precisión conocida, o "señal débil" si nunca hubo una (nunca "±0 m")
        bool sinDatoAhora = !simulada && !hayDatoReal;
        if (sinDatoAhora) precisionM = precisionActualM;
        ActualizarTextoEstado(simulada, precisionM, sinDatoAhora && precisionM <= 0f);

        if (!mapaListo) return;

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
            // Solo se escribe si falta acercarse: quieto (marcador ya en su sitio) no toca el transform
            Vector3 actual = marcador.position;
            if ((destino - actual).sqrMagnitude > UmbralPosicion2)
            {
                Vector3 siguiente = Vector3.Lerp(actual, destino, Suavizado(suavizadoPosicion));
                if ((destino - siguiente).sqrMagnitude <= UmbralPosicion2) siguiente = destino;   // llega exacto
                marcador.position = siguiente;
            }
        }

        ActualizarHalo(destino.y - alturaMarcador);

        // Brújula: solo con el mapa a la vista (BrujulaEnPausa) y con el GPS en marcha
        if (!simulada) PonerBrujula(gpsIniciado && !pausada && !BrujulaEnPausa);
        if (!simulada && brujulaEncendida) GirarConBrujula();
    }

    // El norte del mapa es el eje +Z, así que el rumbo es un giro en Y
    private void GirarConBrujula()
    {
        float rumbo = Input.compass.trueHeading;
        if (rumbo == 0f) rumbo = Input.compass.magneticHeading;

        // Filtro: media exponencial sobre el círculo (LerpAngle pasa bien por 359° → 0°)
        rumboFiltrado = float.IsNaN(rumboFiltrado)
            ? rumbo
            : Mathf.LerpAngle(rumboFiltrado, rumbo, Suavizado(FiltroRumbo));

        // Empieza a girar si el rumbo se aparta más de UmbralGiroGrados y sigue hasta quedar a menos de
        // 0,5° (histéresis): quieto no toca el transform y al girar no se queda corto
        Quaternion objetivo = Quaternion.Euler(0f, rumboFiltrado, 0f);
        Quaternion rotActual = marcador.rotation;
        float pendiente = Quaternion.Angle(rotActual, objetivo);
        if (!girandoBrujula && pendiente > UmbralGiroGrados) girandoBrujula = true;
        if (!girandoBrujula) return;
        if (pendiente < 0.5f) { girandoBrujula = false; return; }
        marcador.rotation = Quaternion.Slerp(rotActual, objetivo, Suavizado(suavizadoRotacion));
    }

    // Factor de suavizado independiente de los FPS (misma sensación a 30 o a 120 FPS)
    private static float Suavizado(float k) => 1f - Mathf.Exp(-k * Time.deltaTime);

    // Escribe el texto solo si cambió el estado, dentro/fuera o la precisión redondeada
    private void ActualizarTextoEstado(bool simulada, float precisionM, bool sinPrecision)
    {
        if (textoEstado == null) return;

        int precision = Mathf.RoundToInt(precisionM);
        bool debil = senalDebil || sinPrecision;
        int clave = (int)estado * 1000003 + (estaEnCampus ? 500000 : 0) + (debil ? 250000 : 0) + (simulada ? -1 : precision);
        if (clave == claveTexto) return;
        claveTexto = clave;

        if (estado == Estado.Aproximada)
        {
            textoEstado.text = mensajeEstado;
            return;
        }

        if (debil && !simulada)
        {
            textoEstado.text = MsgSenalDebilReal;
            return;
        }

        string lugar = estaEnCampus
            ? "En el campus"
            : (usarPuntoFijoFuera ? "Fuera del campus · punto de inicio" : "Fuera del campus");
        string textoPrecision = simulada ? "simulada" : $"±{precision} m";
        textoEstado.text = $"{lugar} · precisión {textoPrecision}";
    }

    // Decide si estás dentro del campus. El cambio solo se acepta si se mantiene unos segundos,
    // porque el GPS "salta" unos metros y cerca del borde parpadearía.
    private void ActualizarEstadoCampus(double lat, double lon)
    {
        bool dentroAhora = DentroDelPoligono(lat, lon);

        if (primerFix || !tieneUbicacion)
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
}
