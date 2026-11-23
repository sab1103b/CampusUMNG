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
/// permitido: el proyecto tiene activeInputHandler = 2 (ambos sistemas: el antiguo y el Input System)
/// y el Input System nuevo no ofrece GPS en Android. Toques y mouse sí usan el Input System nuevo (CamaraMapa, EntradaMapa).
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
    // Quieto, Android puede no entregar datos nuevos aunque el servicio esté en marcha (Running): mientras
    // el servicio corra, el ÚLTIMO dato aceptado sigue valiendo hasta SegundosDatoQuieto antes de marcar
    // "señal débil". segundosMaxDatoViejo solo decide si un dato que llega se acepta como real.
    private const double SegundosDatoQuieto = 120.0;

    [Tooltip("Con la ubicación aproximada, cada cuántos segundos se revisa si ya hay permiso y GPS para reintentar")]
    [SerializeField] private float segundosEntreReintentos = 12f;

    private bool mapaListo;
    private bool tieneUbicacion;
    private bool estaEnCampus;
    private bool primerFix = true;
    private float tiempoCambio = -1f;
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
    private double ultimoTimestampGps = -1;   // lastData.timestamp ya procesado: el mismo dato no se procesa cada frame
    private float proximoReintento;

    // Para escribir el texto de estado solo cuando cambia algo visible: lo último escrito, campo por campo
    private bool textoPendiente = true;   // true = reescribir sí o sí (al cambiar de estado o de mensaje)
    private Estado textoEstadoEscrito;
    private bool textoEnCampus, textoDebil, textoSimulada;
    private int textoPrecisionM;

    // Otros scripts (por ejemplo "CÓMO LLEGAR") leen estos datos
    /// <summary>true solo si hay un dato real del GPS (o la simulación del Editor).</summary>
    public bool TieneUbicacion => tieneUbicacion;
    /// <summary>true si hay un punto de partida para rutas: el real o el aproximado (entrada principal).</summary>
    public bool TieneOrigen => estado != Estado.Esperando;
    /// <summary>true si el GPS no está disponible y se usa la entrada principal como punto de partida.</summary>
    public bool UsandoUbicacionAproximada => estado == Estado.Aproximada;
    /// <summary>
    /// true si el punto de partida NO es la posición real del usuario sino la entrada principal: ubicación
    /// aproximada, o ubicación real fuera del campus con usarPuntoFijoFuera (el marcador queda anclado en la
    /// entrada). Con el origen anclado no se puede afirmar "Has llegado".
    /// </summary>
    public bool OrigenAnclado => estado == Estado.Aproximada || (estado == Estado.Real && usarPuntoFijoFuera && !estaEnCampus);
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

    private void Awake()
    {
        // Awake corre antes que el Start del mapa, así no nos perdemos el evento
        mapa.OnInitialized += AlIniciarMapa;
    }

    private void AlIniciarMapa() => mapaListo = true;

    private void Start()
    {
        limiteEspera = Time.realtimeSinceStartup + segundosMaxEspera;
        EstiloBaseMarcador();

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
        ultimoTimestampGps = -1;
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

        // Precisión deseada (m) y distancia mínima de actualización (m): con 0,5 m sigue llegando algún dato
        // aunque el usuario esté casi quieto (con 1 m algunos teléfonos no entregaban ninguno)
        Input.location.Start(5f, 0.5f);
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
    // Si la brújula falla una vez (API de entrada antigua no disponible: InvalidOperationException), se
    // apaga para siempre en esta sesión: no se reintenta ni se registra el error en cada frame.
    private bool brujulaNoDisponible;

    private void PonerBrujula(bool encender)
    {
        if (brujulaNoDisponible) encender = false;
        if (brujulaEncendida == encender) return;
        brujulaEncendida = encender;
        rumboFiltrado = float.NaN;   // al volver a encenderla, el filtro arranca del primer dato
        try { Input.compass.enabled = encender; }
        catch (InvalidOperationException e) { DesactivarBrujula(e); }
    }

    private void DesactivarBrujula(Exception e)
    {
        if (!brujulaNoDisponible) Registro.Info("Brújula no disponible: " + e.Message);
        brujulaNoDisponible = true;
        brujulaEncendida = false;
        girandoBrujula = false;
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
        textoPendiente = true;   // fuerza a reescribir el texto de estado
        EstadoCambiado?.Invoke();
    }

    // Colores del "Tú estás aquí" por código (no dependen del material que traiga la escena): el cuerpo del
    // marcador (renderers en la raíz) en UIRapida.RutaColor (el azul de "tú" y de la ruta, distinto del verde
    // del destino); las piezas hijas (la flecha de rumbo) en UIRapida.SobreColor, para que se lean sobre el
    // cuerpo azul; y un anillo blanco plano debajo, algo más ancho que el marcador, que hace de borde sobre el
    // mapa. Se llama una vez en Start.
    private const float FactorAnilloMarcador = 1.35f;   // diámetro del anillo / ancho del marcador

    private void EstiloBaseMarcador()
    {
        if (marcador == null) return;
        Renderer[] renderers = marcador.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Material azul = GeoMapa.Material(UIRapida.RutaColor);
        Material blanco = GeoMapa.Material(UIRapida.SobreColor);
        bool hayLimites = false;
        Bounds limites = default;
        foreach (Renderer r in renderers)
        {
            if (r.GetComponent<TMP_Text>() != null) continue;   // un texto 3D conserva su material de fuente
            bool cuerpo = r.transform == marcador;              // las piezas hijas (flecha) van en blanco
            Color color = cuerpo ? UIRapida.RutaColor : UIRapida.SobreColor;
            Material mat = cuerpo ? azul : blanco;
            if (r is SpriteRenderer sr) sr.color = color;
            else if (r is MeshRenderer && mat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            else continue;
            if (!hayLimites) { limites = r.bounds; hayLimites = true; }
            else limites.Encapsulate(r.bounds);
        }
        if (!hayLimites) return;

        // Anillo blanco: disco plano (cilindro sin colisionador) bajo el marcador, en su espacio local
        // (gira con la brújula sin notarse: es un círculo). Medidas tomadas de los límites del marcador.
        Vector3 escalaMundo = marcador.lossyScale;
        float sx = Mathf.Max(1e-4f, Mathf.Abs(escalaMundo.x)), sy = Mathf.Max(1e-4f, Mathf.Abs(escalaMundo.y)), sz = Mathf.Max(1e-4f, Mathf.Abs(escalaMundo.z));
        Vector3 centroLocal = marcador.InverseTransformPoint(limites.center);
        float ancho = 2f * Mathf.Max(limites.extents.x / sx, limites.extents.z / sz) * FactorAnilloMarcador;
        float altoLocal = limites.extents.y / sy;
        float grosor = Mathf.Max(0.002f, ancho * 0.01f);

        GameObject anillo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        anillo.name = "AnilloMarcador";
        Collider col = anillo.GetComponent<Collider>();
        if (col != null) Destroy(col);
        anillo.transform.SetParent(marcador, false);
        // Apenas por debajo del marcador: desde arriba el cuerpo azul tapa el centro y queda el borde blanco
        anillo.transform.localPosition = new Vector3(centroLocal.x, centroLocal.y - altoLocal - grosor, centroLocal.z);
        anillo.transform.localRotation = Quaternion.identity;
        anillo.transform.localScale = new Vector3(ancho, grosor, ancho);   // el cilindro mide 2 de alto: grosor·2
        MeshRenderer mr = anillo.GetComponent<MeshRenderer>();
        mr.sharedMaterial = blanco != null ? blanco : GeoMapa.Material(UIRapida.SobreColor);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // La ubicación aproximada (entrada principal) se distingue SOLO con el halo (más grande y con borde tenue
    // y fijo, ver UbicacionController.Halo.cs): el marcador conserva siempre RutaColor pleno (aclararlo hacia
    // el blanco daba un azul pastel que parecía un error de color).

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
            ultimoTimestampGps = -1;   // al quitar la simulación, el dato del GPS se vuelve a procesar
        }
        else if (!pausada && gpsIniciado && Input.location.status == LocationServiceStatus.Running)
        {
            LocationInfo d = Input.location.lastData;
            // Running sin ningún dato todavía devuelve 0,0. Un dato ya procesado (mismo timestamp) no se vuelve a
            // procesar: el marcador conserva su posición; solo se mira si ese dato ya envejeció (señal débil).
            if (d.timestamp > 0 && d.timestamp == ultimoTimestampGps)
            {
                // Mismo dato de antes con el servicio en marcha: el usuario puede estar quieto. Solo se marca
                // señal débil si el dato pasa de SegundosDatoQuieto (no por segundosMaxDatoViejo)
                if (!senalDebil && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - d.timestamp > SegundosDatoQuieto)
                    senalDebil = true;
            }
            else if (d.timestamp > 0)
            {
                ultimoTimestampGps = d.timestamp;
                // timestamp = segundos desde 1970 (UTC). Al iniciar o volver a la app, Android puede
                // entregar un dato guardado de hace mucho: no se toma como real hasta que llegue uno fresco.
                double edad = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - d.timestamp;
                // Antes del primer dato fresco se exige segundosMaxDatoViejo; después, con el servicio en marcha,
                // un dato de menos de SegundosDatoQuieto sigue siendo válido (quieto llegan pocos datos)
                bool viejo = edad > (exigirDatoFresco ? segundosMaxDatoViejo : SegundosDatoQuieto);
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
        float rumbo;
        try
        {
            rumbo = Input.compass.trueHeading;
            if (rumbo == 0f) rumbo = Input.compass.magneticHeading;
        }
        catch (InvalidOperationException e) { DesactivarBrujula(e); return; }

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
        if (simulada) precision = -1;   // simulada no muestra precisión: no cuenta como cambio
        if (!textoPendiente && textoEstadoEscrito == estado && textoEnCampus == estaEnCampus &&
            textoDebil == debil && textoSimulada == simulada && textoPrecisionM == precision) return;
        textoPendiente = false;
        textoEstadoEscrito = estado;
        textoEnCampus = estaEnCampus;
        textoDebil = debil;
        textoSimulada = simulada;
        textoPrecisionM = precision;

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
            tiempoCambio = -1f;
            return;
        }

        // tiempoCambio guarda el instante (tiempo real) del primer dato que discrepa; -1 = sin cambio pendiente.
        // Se mide con el reloj y no sumando deltaTime, porque esta función solo corre cuando llega un dato del GPS (~1/s).
        if (dentroAhora != estaEnCampus)
        {
            if (tiempoCambio < 0f) tiempoCambio = Time.realtimeSinceStartup;
            else if (Time.realtimeSinceStartup - tiempoCambio >= segundosParaCambiar)
            {
                estaEnCampus = dentroAhora;
                tiempoCambio = -1f;
            }
        }
        else
        {
            tiempoCambio = -1f;
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
