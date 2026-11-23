using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;

/// <summary>
/// Botón "CÓMO LLEGAR": calcula la ruta más corta por la red de caminos
/// desde el marcador hasta la entrada del lugar elegido y la dibuja.
/// Si lo elegido es un servicio (un salón, por ejemplo), la ruta lleva al lugar al que
/// pertenece y el texto muestra el piso y la indicación para llegar dentro.
/// Partes: ComoLlegarController.Dibujo.cs (estilo, ancho, proyección y suavizado de la línea) y
/// ComoLlegarController.Texto.cs (texto de la ruta).
/// </summary>
public partial class ComoLlegarController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private UbicacionController ubicacion;
    [SerializeField] private LugaresService lugares;
    [SerializeField] private RedCaminos red;
    [SerializeField] private LineRenderer linea;
    [Tooltip("Opcional: si está vacío se crea por código un marcador de destino verde (ver CrearPinDestino)")]
    [SerializeField] private Transform pinDestino;
    [SerializeField] private TMP_Text textoRuta;       // opcional

    [Header("Ajustes")]
    [Tooltip("Ancho real de la línea de ruta en metros (se convierte a unidades según el zoom)")]
    [SerializeField] private float anchoRutaM = 4f;
    [Tooltip("El contorno blanco mide esto veces el ancho de la línea")]
    [SerializeField] private float factorContorno = 1.6f;
    [Tooltip("Ancho mínimo de la línea en píxeles de pantalla: con el zoom lejano la ruta no se vuelve un hilo")]
    [SerializeField] private float anchoMinimoPx = 6f;
    [Tooltip("Tamaño del marcador de destino creado por código, en metros reales")]
    [SerializeField] private float tamPinM = 6f;
    [Tooltip("Tamaño mínimo del marcador de destino en píxeles de pantalla (como el ancho mínimo de la ruta)")]
    [SerializeField] private float tamPinMinimoPx = 26f;
    [SerializeField] private float velocidadCaminarMs = 1.3f;       // metros por segundo
    [SerializeField] private float distanciaLlegadaM = 15f;
    [Tooltip("Si estás más lejos que esto del camino, el texto te avisa cuánto caminar hasta él (la ruta igual usa los caminos)")]
    [SerializeField] private float maxDistanciaAlosCaminosM = 80f;
    [Tooltip("Si el destino está a menos de estos metros, se va en línea recta")]
    [SerializeField] private float distanciaDirectaM = 25f;
    [Tooltip("Cada cuánto se revisa si hay que recalcular (solo se recalcula si algo cambió)")]
    [SerializeField] private float recalcularCadaSegundos = 0.5f;
    [Tooltip("Metros mínimos que debes moverte para que la ruta se recalcule (sube con la imprecisión del GPS)")]
    [SerializeField] private float moverseMinimoM = 4f;

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

    // Lo necesario para recalcular solo cuando algo cambia
    private bool pendiente;                 // hay que calcular en cuanto se pueda
    private bool redCambiada;               // llegó una red nueva (RedCargada)
    private bool mapaCambiado;              // el mapa se movió o cambió de zoom
    private bool origenCambiado;            // la ubicación pasó a real/aproximada
    private int cantidadRed = -1;
    private Vector2d origenCalculado;
    private Vector3 referenciaMundo;        // destino proyectado: si cambia, el mapa se movió
    private List<Vector2d> trazoGeo;        // ruta (ya suavizada) en lat/lon
    private string ultimoMotivoRecta;

    // Buffers reutilizados (evitan basura en cada recálculo o reproyección)
    private Vector3[] puntosMundo = new Vector3[0];
    private readonly List<Vector2d> trazoSuavizado = new List<Vector2d>(64);

    // Contorno blanco debajo de la línea y ancho según el zoom
    private LineRenderer contorno;
    private float zoomAnchoAplicado = float.NaN;
    private float unidadesPorMetroRuta = 1f;
    private float anchoAplicado = -1f;
    // Desfase vertical mínimo y fijo del contorno: solo evita el parpadeo de profundidad; el orden
    // de dibujo (contorno debajo) lo da el renderQueue de los materiales.
    private const float desfaseContorno = 0.01f;
    private Material materialLinea;      // copias propias: se destruyen en OnDestroy
    private Material materialContorno;
    private Camera camaraRuta;
    private float proximaBusquedaCamara;   // si no hay cámara activa, se busca como máximo 1 vez/s
    private bool pantallaSiempreEncendida;
    private bool pausado;   // la ruta sigue activa pero su pantalla no está a la vista (editor, formulario…)
    private bool pinCreado;          // el marcador de destino lo creó este script (se destruye en OnDestroy)
    private float escalaPinAplicada = -1f;
    // Inicio de la línea: solo se reescribe si el marcador se movió, y la cámara solo se despierta
    // (sale del ahorro de 30 fps) si se movió más de UmbralDespertarM desde el último despertar
    private Vector3 origenDibujado;
    private Vector3 origenDespierto;
    private const float UmbralDespertarM = 0.3f;
    // Pin del destino sin ruta (MostrarDestino), encuadre pendiente (MostrarRuta) y llegada ("Has llegado")
    private LugaresService.Lugar destinoMostrado;
    private bool encuadrarPendiente;
    private bool llegado;   // con la ruta en "Has llegado" la pantalla vuelve a apagarse sola
    private const string MsgNoDisponible = "Ese lugar ya no está disponible.";
    private const string MsgServicioNoDisponible = "Ese servicio ya no está disponible; la ruta va al lugar.";

    /// <summary>Se dispara cuando el destino elegido ya no existe (lo borraron): la ficha debería cerrarse.</summary>
    public event System.Action DestinoNoDisponible;

    /// <summary>
    /// Opcional: la interfaz indica si la ficha del destino está abierta. Si el destino se borra
    /// sin ruta activa ni ficha abierta, la selección se limpia en silencio (sin aviso falso).
    /// </summary>
    [System.NonSerialized] public System.Func<bool> FichaAbierta;

    // ----- Estado de solo lectura (lo usa el panel de pruebas del Editor) -----
    public LugaresService.Lugar Destino => seleccionado;
    public LugaresService.Servicio ServicioDestino => servicioSel;
    public bool RutaActiva => rutaActiva;
    public bool Pausado => pausado;
    public bool ListoParaCalcular => mapaListo && ubicacion.TieneOrigen;
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
        mapa.OnInitialized += AlIniciarMapa;
        mapa.OnUpdated += AlActualizarMapa;

        // Si el administrador edita o borra el lugar elegido, la ruta usa los datos nuevos
        lugares.LugaresCargados += AlCargarLugares;
        // La red llega DESPUÉS de los lugares: la ruta se vuelve a calcular con la red nueva
        lugares.RedCargada += AlCargarRed;
        ubicacion.EstadoCambiado += AlCambiarUbicacion;
    }

    private void OnDestroy()
    {
        if (mapa != null)
        {
            mapa.OnInitialized -= AlIniciarMapa;
            mapa.OnUpdated -= AlActualizarMapa;
        }
        if (lugares != null)
        {
            lugares.LugaresCargados -= AlCargarLugares;
            lugares.RedCargada -= AlCargarRed;
        }
        if (ubicacion != null) ubicacion.EstadoCambiado -= AlCambiarUbicacion;

        PantallaEncendida(false);
        if (materialLinea != null) Destroy(materialLinea);
        if (materialContorno != null) Destroy(materialContorno);
        if (pinCreado && pinDestino != null) Destroy(pinDestino.gameObject);
    }

    // Mientras hay una ruta en pantalla el celular no se apaga solo; al terminar vuelve a lo normal
    private void PantallaEncendida(bool encendida)
    {
        if (pantallaSiempreEncendida == encendida) return;
        pantallaSiempreEncendida = encendida;
        Screen.sleepTimeout = encendida ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
    }

    // Se marcan y se atienden en el siguiente Update (para entonces RedCaminos ya reconstruyó)
    private void AlIniciarMapa() => mapaListo = true;
    private void AlCargarLugares(List<LugaresService.Lugar> _) => RefrescarSeleccion();
    private void AlActualizarMapa() => mapaCambiado = true;
    private void AlCargarRed() => redCambiada = true;
    private void AlCambiarUbicacion() => origenCambiado = true;

    private IEnumerator Start()
    {
        linea.useWorldSpace = true;
        linea.positionCount = 2;
        linea.numCornerVertices = 4;   // uniones redondeadas entre segmentos
        linea.numCapVertices = 4;      // extremos redondeados
        PrepararEstiloLinea();
        if (textoRuta != null)
        {
            // Nombres o indicaciones largas no desbordan la barra de ruta
            textoRuta.overflowMode = TextOverflowModes.Ellipsis;
            textoRuta.maxVisibleLines = MaxLineasTexto;
        }
        linea.enabled = false;
        if (pinDestino == null) CrearPinDestino();   // la escena no trae marcador de destino
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);

        if (Application.isEditor && (idPruebaEnEditor > 0 || idServicioPruebaEnEditor > 0))
        {
            while (!lugares.CargaTerminada) yield return null;

            // Prueba del Editor: no cuenta como búsqueda real en las estadísticas
            bool registrar = registrarBusquedas;
            registrarBusquedas = false;
            if (idServicioPruebaEnEditor > 0) SeleccionarServicioPorId(idServicioPruebaEnEditor);
            else SeleccionarPorId(idPruebaEnEditor);
            registrarBusquedas = registrar;
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
            // El texto queda visible (no se borra hasta elegir otro destino) y además se avisa en pantalla
            if (textoRuta != null) textoRuta.text = MsgNoDisponible;
            AvisarNoDisponible();
            DestinoNoDisponible?.Invoke();
            return;
        }

        seleccionado = lugar;
        servicioSel = servicio;
        if (registrarBusquedas) lugares.RegistrarBusqueda(lugar.id);   // alimenta el reporte PDF

        if (rutaActiva)
        {
            PedirEncuadre();          // la ruta nueva se encuadra como al pulsar "Cómo llegar"
            RecalcularAhora(true);    // reemplaza la ruta que estaba en pantalla
        }
    }

    /// <summary>
    /// Muestra el pin de destino sobre 'lugar' sin calcular ruta (p. ej. al elegir un resultado de la
    /// búsqueda, mientras se ve su ficha) y, si 'centrar', lleva la cámara al lugar (CamaraMapa.IrA: deja
    /// de seguir al usuario). No cambia el destino elegido ni registra una búsqueda (eso lo hace
    /// SeleccionarLugar). Con una ruta activa solo centra: el pin es el de la ruta. Lo ocultan
    /// OcultarDestino y LimpiarRuta; Pausar lo oculta y lo vuelve a mostrar.
    /// </summary>
    public void MostrarDestino(LugaresService.Lugar lugar, bool centrar = true)
    {
        if (lugar == null) { OcultarDestino(); return; }
        if (centrar)
        {
            CamaraMapa cm = Camara();
            if (cm != null && mapaListo) cm.IrA(new Vector2d(lugar.latitud, lugar.longitud));
        }
        if (rutaActiva) return;
        destinoMostrado = lugar;
        if (pinDestino == null) return;
        if (mapaListo) ProyectarDestinoSinRuta();
        else mapaCambiado = true;   // Update lo pone en cuanto el mapa esté listo
    }

    /// <summary>Oculta el pin puesto con MostrarDestino (si no hay ruta activa).</summary>
    public void OcultarDestino()
    {
        destinoMostrado = null;
        if (!rutaActiva && pinDestino != null) pinDestino.gameObject.SetActive(false);
    }

    /// <summary>
    /// Conéctalo al OnClick del botón "CÓMO LLEGAR". Al dibujar la ruta, la cámara encuadra una vez
    /// el recorrido completo (tu ubicación y el destino); después se mueve libremente.
    /// </summary>
    public void MostrarRuta()
    {
        if (seleccionado == null)
        {
            if (textoRuta != null) textoRuta.text = "Elige un lugar primero.";
            return;
        }
        rutaActiva = true;
        destinoMostrado = null;   // el pin pasa a ser el de la ruta
        PedirEncuadre();
        llegado = false;
        PantallaEncendida(!pausado);
        linea.enabled = !pausado;
        if (contorno != null) contorno.enabled = !pausado;
        if (pinDestino != null) pinDestino.gameObject.SetActive(!pausado);
        RecalcularAhora(true);
    }

    /// <summary>Conéctalo al botón de cerrar la ficha o cancelar la ruta.</summary>
    public void LimpiarRuta()
    {
        rutaActiva = false;
        destinoMostrado = null;
        encuadrarPendiente = false;
        llegado = false;
        PantallaEncendida(false);
        pendiente = false;
        UltimaRuta = null;
        trazoGeo = null;
        linea.positionCount = 0;
        linea.enabled = false;
        if (contorno != null) { contorno.positionCount = 0; contorno.enabled = false; }
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);
        if (textoRuta != null) textoRuta.text = "";
    }

    /// <summary>
    /// Pausa la ruta mientras su pantalla no está a la vista (editor del mapa, formulario, admin):
    /// oculta la línea, el contorno y el pin, devuelve el apagado de pantalla al sistema y deja de
    /// despertar la cámara. Con false se reanuda (la línea se vuelve a proyectar). La ruta y el
    /// destino se conservan. La llama InterfazCampus al cambiar de pantalla.
    /// </summary>
    public void Pausar(bool pausar)
    {
        // La brújula del marcador solo se lee con el mapa a la vista (ahorra batería)
        if (ubicacion != null) ubicacion.BrujulaEnPausa = pausar;
        if (pausado == pausar) return;
        pausado = pausar;
        if (pausar)
        {
            if (linea != null) linea.enabled = false;
            if (contorno != null) contorno.enabled = false;
            if (pinDestino != null) pinDestino.gameObject.SetActive(false);
            PantallaEncendida(false);
            return;
        }

        if (!rutaActiva)
        {
            if (destinoMostrado != null) mapaCambiado = true;   // Update vuelve a poner el pin sin ruta
            return;
        }
        PantallaEncendida(isActiveAndEnabled && !llegado);
        bool hayTrazo = trazoGeo != null && trazoGeo.Count > 0;
        if (linea != null) linea.enabled = true;
        if (contorno != null) contorno.enabled = hayTrazo;
        if (pinDestino != null) pinDestino.gameObject.SetActive(hayTrazo);
        mapaCambiado = true;   // el mapa pudo moverse mientras tanto: se vuelve a proyectar
        proximoCalculo = 0f;   // y se revisa ya si te moviste
    }

    private UIManager uiManager;

    private void AvisarNoDisponible()
    {
        if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null) uiManager.Avisar(MsgNoDisponible, true);
    }

    private void OnDisable()
    {
        if (contorno != null) contorno.enabled = false;
        PantallaEncendida(false);
    }

    private void OnEnable()
    {
        if (!rutaActiva || pausado) return;
        PantallaEncendida(!llegado);
        mapaCambiado = true;   // el mapa pudo moverse mientras estaba desactivado: se vuelve a proyectar
        proximoCalculo = 0f;   // y se revisa ya si te moviste
    }

    // Borra la ruta vieja y, si 'inmediato', calcula la nueva en este mismo frame si ya se puede;
    // si no (o si todavía no hay mapa o ubicación), Update la calcula en cuanto pueda.
    // Tras una recarga de datos va con inmediato = false: la red nueva llega DESPUÉS de los lugares
    // (RedCargada), así la ruta no se calcula con la red vieja.
    private void RecalcularAhora(bool inmediato)
    {
        UltimaRuta = null;
        trazoGeo = null;
        PuntosEsperados = 0;
        linea.positionCount = 0;
        if (contorno != null) contorno.positionCount = 0;
        if (pinDestino != null) pinDestino.gameObject.SetActive(false);
        PonerTexto(ubicacion.TieneOrigen
            ? $"Calculando ruta a {Seguro(seleccionado.nombre)}…"
            : $"<b>{Seguro(seleccionado.nombre)}</b>\nBuscando tu ubicación…");

        pendiente = true;
        if (inmediato && mapaListo && ubicacion.TieneOrigen) Recalcular(ubicacion.PosicionMarcador);
    }

    // Tras una recarga de datos, cambia el lugar elegido por su versión nueva (o limpia si lo borraron)
    private void RefrescarSeleccion()
    {
        // Pin sin ruta: se mueve a la versión nueva del lugar o se oculta si lo borraron
        if (destinoMostrado != null)
        {
            LugaresService.Lugar mostrado = lugares.BuscarPorId(destinoMostrado.id);
            if (mostrado == null) OcultarDestino();
            else { destinoMostrado = mostrado; mapaCambiado = true; }
        }

        if (seleccionado == null) return;

        LugaresService.Lugar nuevo = lugares.BuscarPorId(seleccionado.id);
        LugaresService.Servicio nuevoServicio = servicioSel != null ? lugares.BuscarServicioPorId(servicioSel.id) : null;

        bool fichaAbierta = FichaAbierta != null && FichaAbierta();
        // Con la ruta en pausa (editor del mapa, formulario, admin) el usuario no ve la ruta ni la ficha:
        // los cambios se aplican en silencio, sin avisos
        bool aLaVista = !pausado && (rutaActiva || fichaAbierta);

        if (nuevo == null)
        {
            // Nada a la vista (p. ej. el administrador borró un lugar visto antes, o el destino de una
            // ruta en pausa): se olvida la selección y se limpia la ruta en silencio, sin aviso falso
            if (!aLaVista)
            {
                seleccionado = null;
                servicioSel = null;
                if (rutaActiva) LimpiarRuta();
                DestinoNoDisponible?.Invoke();   // por si la ficha sigue abierta: se cierra, sin aviso
                return;
            }
            Seleccionar(null, null);
            return;
        }

        // Se borró solo el servicio: el destino pasa a ser su lugar y se avisa si el usuario lo está viendo
        if (servicioSel != null && nuevoServicio == null && aLaVista)
        {
            if (uiManager == null) uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager != null) uiManager.Avisar(MsgServicioNoDisponible, true);
        }

        seleccionado = nuevo;
        servicioSel = nuevoServicio;
        // Se borra el trazo y queda pendiente: Update la calcula cuando ya llegó la red nueva
        if (rutaActiva) RecalcularAhora(false);
    }

    private void Update()
    {
        // Pausada (otra pantalla encima): ni se despierta la cámara ni se recalcula; al reanudar se pone al día.
        // La cámara solo se despierta cuando la ruta cambia de forma visible (recálculo, reproyección o el
        // inicio de la línea siguiendo al marcador): una ruta quieta deja entrar al modo de ahorro (30 fps).
        if (pausado) return;
        if (!rutaActiva)
        {
            // Pin del destino sin ruta: se reproyecta si el mapa cambió y mantiene su tamaño mínimo en pantalla
            if (destinoMostrado != null && mapaListo && pinDestino != null)
            {
                if (mapaCambiado) ProyectarDestinoSinRuta();
                else if (pinCreado) ActualizarAncho();
            }
            return;
        }
        if (!mapaListo || !ubicacion.TieneOrigen || seleccionado == null) return;

        Vector3 origen = ubicacion.PosicionMarcador;
        if (linea.positionCount > 0) ActualizarAncho();   // mantiene el ancho mínimo en pantalla al acercar/alejar

        // Cambios de red o de ubicación (real/aproximada) se atienden de inmediato
        bool redNueva = redCambiada || red.Cantidad != cantidadRed;
        if (pendiente || redNueva || origenCambiado)
        {
            Recalcular(origen);
            return;
        }

        if (Time.time >= proximoCalculo)
        {
            proximoCalculo = Time.time + recalcularCadaSegundos;

            // Solo se recalcula si te moviste más que max(4 m, precisión/2): el ruido del GPS no la redibuja
            float umbral = Mathf.Max(Mathf.Max(4f, moverseMinimoM), ubicacion.PrecisionM * 0.5f);
            if (RedCaminos.Metros(ubicacion.UbicacionActual, origenCalculado) > umbral)
            {
                Recalcular(origen);
                return;
            }

            // Si el mapa se movió o cambió de zoom, basta con volver a proyectar la misma ruta
            if (mapaCambiado || MapaSeMovio())
            {
                Proyectar(origen);
                return;
            }
        }

        // El inicio sigue al marcador sin recalcular todo (solo se escribe si se movió)
        if (linea.positionCount > 0 && (origen - origenDibujado).sqrMagnitude > 1e-8f)
        {
            linea.SetPosition(0, origen);
            if (contorno != null && contorno.positionCount > 0)
                contorno.SetPosition(0, origen + Vector3.down * desfaseContorno);
            origenDibujado = origen;
            float umbral = UmbralDespertarM * unidadesPorMetroRuta;
            if ((origen - origenDespierto).sqrMagnitude > umbral * umbral)
            {
                origenDespierto = origen;
                CamaraMapa.Despertar();
            }
        }
    }

    private Vector3 DestinoEnMundo() =>
        mapa.GeoToWorldPosition(new Vector2d(seleccionado.latitud, seleccionado.longitud), false);

    private bool MapaSeMovio() => (DestinoEnMundo() - referenciaMundo).sqrMagnitude > 1e-4f;

    private void Recalcular(Vector3 origenMundo)
    {
        pendiente = false;
        redCambiada = false;
        origenCambiado = false;
        cantidadRed = red.Cantidad;
        proximoCalculo = Time.time + recalcularCadaSegundos;

        Vector2d origen = ubicacion.UbicacionActual;
        Vector2d destino = new Vector2d(seleccionado.latitud, seleccionado.longitud);
        origenCalculado = origen;

        // La ruta se calcula en lat/lon y luego se proyecta al mundo de Unity,
        // así sigue alineada aunque el mapa se mueva o cambie de zoom
        RedCaminos.Ruta ruta = red.CalcularRuta(origen, destino, seleccionado.id, distanciaDirectaM);
        UltimaRuta = ruta;
        trazoGeo = suavizarRuta ? Suavizar(ruta.puntos) : ruta.puntos;

        Proyectar(origenMundo);

        // Se avisa en consola solo cuando cambia el motivo
        string motivo = ruta.porCaminos ? null : ruta.motivoRecta;
        if (motivo != ultimoMotivoRecta)
        {
            if (motivo != null) Registro.Info($"Ruta en línea recta: {motivo}");
            ultimoMotivoRecta = motivo;
        }

        ActualizarTexto(ruta);

        // "Has llegado": la pantalla vuelve a apagarse sola; si te alejas otra vez, se mantiene encendida
        llegado = Llegaste(ruta);
        PantallaEncendida(!llegado && !pausado && isActiveAndEnabled);
    }

}
