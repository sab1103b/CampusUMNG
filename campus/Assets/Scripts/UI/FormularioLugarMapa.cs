using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>
/// Pantalla "Nuevo lugar / Editar lugar" del administrador, con el mapa de fondo.
///
///  · Edificio: el lugar general. Se ubica tocando el mapa o arrastrando el pin (sin escribir coordenadas).
///    Al guardar, si hay un nodo de la red a menos de 25 m y el edificio aún no tiene puerta, ese nodo
///    queda como su puerta. Si ya hay otro lugar a menos de 5 m, pide confirmar (segundo toque).
///  · Espacio interior: un salón, laboratorio u oficina DENTRO de un edificio (se guarda como servicio).
///    Se elige el edificio de una lista; no necesita pin propio.
///
/// Si la sesión de administrador vence (al guardar o mientras se escribe), lo escrito se conserva en memoria
/// y se restaura al volver a abrir el formulario.
///
/// InterfazCampus la crea y la registra en el UIManager como "AdminLugar" (reemplaza el formulario de texto).
/// Partes: FormularioLugarMapa.Interfaz.cs (construcción y listas de opciones), FormularioLugarMapa.Mapa.cs
/// (pin, toques y puerta automática) y FormularioLugarMapa.Guardar.cs (validar, guardar y borrador).
/// Orden de ejecución 100: su LateUpdate proyecta el pin con la cámara DESPUÉS de que CamaraMapa (orden 0) la movió
/// en ese mismo cuadro; así el pin no va un cuadro atrasado ni tiembla al desplazar o hacer zoom.
/// </summary>
[DefaultExecutionOrder(100)]
public partial class FormularioLugarMapa : MonoBehaviour
{
    public const float RadioPuertaM = 25f;
    public const float RadioDuplicadoM = 5f;
    public const int NombreMin = 3, NombreMax = 80;

    private static readonly string[] CategoriasEdificio =
    {
        "Académico", "Aulas", "Laboratorio", "Auditorio", "Biblioteca", "Administrativo", "Bienestar",
        "Salud", "Cafetería", "Deportes", "Parqueadero", "Cultura", "Acceso", "Punto de referencia",
    };

    private static readonly string[] CategoriasInterior =
    {
        "Aulas", "Laboratorio", "Oficina", "Facultad", "Biblioteca", "Sala de estudio", "Auditorio",
        "Administrativo", "Bienestar", "Cafetería", "Baños", "Deportes", "Otro",
    };

    private const string TextoGuardar = "Guardar", TextoGuardarIgual = "Guardar de todos modos", TextoGuardando = "Guardando…";
    private const float CentroControlesY = 0.65f;   // centro del mapa visible entre la ayuda y la tarjeta
    private const string MensajeSesion = "Tu sesión venció. Inicia sesión de nuevo; lo escrito se conservó.";
    // Aviso global (UIManager) cuando la sesión vence con cambios: lo escrito queda en el borrador
    private const string MensajeBorrador = "Se guardó lo que escribiste; lo recuperas al volver a iniciar sesión.";
    private static readonly Regex Espacios = new Regex("\\s+");

    // ----- Referencias -----
    private UIManager ui;
    private AdminService admin;
    private LugaresService lugares;
    private AbstractMap mapa;
    private CamaraMapa camara;
    private Camera cam;
    private TMP_FontAsset fuente;
    private string nombrePantalla;
    private Func<bool> manejadorAtras;   // se crea una sola vez para poder quitarlo del UIManager

    // ----- Interfaz -----
    private TMP_Text titulo, textoAyuda, textoPuerta, textoError, textoBotonGuardar;
    private TMP_Text errorNombre, errorCategoria, errorEdificio;
    private Button tabEdificio, tabInterior, btnGuardar;
    private RectTransform pin;
    private PinArrastrable arrastre;
    private GameObject filaEdificioPadre, filaPiso;
    private Button selectorEdificio, selectorCategoria;
    private TMP_InputField campoNombre, campoDescripcion, campoIndicacion, campoHorario, campoPiso;
    private TMP_Text etiquetaIndicacion;
    private GameObject panelOpciones;
    private CanvasGroup grupoOpciones;
    private RectTransform cajaOpciones;
    private Coroutine animOpciones;
    private TMP_Text tituloOpciones;
    private RectTransform listaOpciones;
    private UIRapida.ModalDeLista modalOpciones;   // lista de opciones compartida (UIRapida.ModalLista)
    // Modal "¿Descartar los cambios?" (Atrás o X con cambios sin guardar)
    private UIRapida.ModalConfirmacion modalDescartar;
    private ScrollRect scrollFormulario;

    // ----- Estado -----
    private bool esInterior;
    private int lugarEditando;          // 0 = nuevo
    private int servicioEditando;       // >0 = se edita ese espacio interior (servicio)
    private Vector2d ubicacion;         // lat, lon del pin (en modo interior: la del edificio padre)
    private bool hayUbicacion;
    private Vector2d ubicacionEdificio; // posición del pin guardada al pasar a "Espacio interior"
    private bool hayUbicacionEdificio;
    private Vector2d ubicacionOriginal; // la del lugar en edición
    private int edificioPadre;          // lugar_id para un espacio interior
    private string categoria;
    private int nodoPuerta;             // nodo que se usará como puerta (0 = ninguno)
    private float metrosPuerta;
    private bool mapaListo;
    private bool ocupado;
    private bool confirmarDuplicado;    // primer toque de Guardar encontró otro lugar a < 5 m
    private bool seguiaAntes;           // la cámara seguía al usuario antes de abrir

    // Último texto de puerta mostrado (para no reconstruir el texto en cada frame del arrastre)
    private int puertaEstado = -1, puertaNodo = -1, puertaMetros = -1;

    private readonly DetectorToque detector = new DetectorToque();
    private string firmaInicial;        // contenido al abrir: si cambia, hay algo sin guardar

    // Lo escrito cuando la sesión venció al guardar (solo en memoria)
    private class Borrador
    {
        public int lugarId;
        public int servicioId;
        public bool esInterior;
        public string nombre, descripcion, indicacion, horario, piso, categoria;
        public int edificioPadre;
        public Vector2d ubicacionEdificio;
        public bool hayUbicacionEdificio;
    }
    private Borrador borrador;

    // ==================================================================
    // Creación
    // ==================================================================
    public static FormularioLugarMapa Crear(Transform padre, string nombrePantalla, UIManager ui, AdminService admin,
                                            LugaresService lugares, AbstractMap mapa, CamaraMapa camara,
                                            TMP_FontAsset fuente, bool mapaYaIniciado)
    {
        GameObject go = new GameObject("Panel_LugarMapa", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = 5;
        go.transform.SetParent(padre, false);
        go.SetActive(false);

        Canvas c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 6;
        CanvasScaler cs = go.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1080, 1920);
        cs.matchWidthOrHeight = 0.5f;

        FormularioLugarMapa f = go.AddComponent<FormularioLugarMapa>();
        f.nombrePantalla = nombrePantalla;
        f.ui = ui;
        f.admin = admin;
        f.lugares = lugares;
        f.mapa = mapa;
        f.camara = camara;
        f.cam = camara != null ? camara.GetComponent<Camera>() : Camera.main;
        f.fuente = fuente;
        f.mapaListo = mapaYaIniciado && mapa != null;
        f.manejadorAtras = f.ManejarAtras;
        if (mapa != null) mapa.OnInitialized += f.AlIniciarMapa;
        if (admin != null) admin.SesionCambiada += f.AlCambiarSesion;
        f.Construir();
        // Contenido dentro de la zona segura; franja de la cámara en verde (barra) y la de gestos en blanco (tarjeta)
        ZonaSegura.En(go, UIRapida.Verde, UIRapida.SuperficieOpaca);
        UIRapida.AplicarFuentes(go.transform);   // Roboto (Regular/Medium/Bold reales); ver UIRapida.Tipografia
        return f;
    }

    private void AlIniciarMapa() => mapaListo = true;

    private void OnDestroy()
    {
        if (mapa != null) mapa.OnInitialized -= AlIniciarMapa;
        if (admin != null) admin.SesionCambiada -= AlCambiarSesion;
    }

    // La sesión venció o se cerró con el formulario abierto: se guarda lo escrito antes de que se cierre la pantalla
    private void AlCambiarSesion(bool activa)
    {
        if (!activa && isActiveAndEnabled) GuardarBorradorSiHayCambios();
    }

    // ==================================================================
    // Abrir / cerrar
    // ==================================================================
    /// <summary>Prepara la pantalla para un lugar nuevo (null) o para editar uno existente.</summary>
    public void Abrir(LugaresService.Lugar lugar)
    {
        lugarEditando = lugar != null ? lugar.id : 0;
        servicioEditando = 0;
        titulo.text = lugar != null ? "Editar lugar" : "Nuevo lugar";

        campoNombre.text = lugar != null && lugar.nombre != null ? lugar.nombre : "";
        campoDescripcion.text = lugar != null && lugar.descripcion != null ? lugar.descripcion : "";
        campoIndicacion.text = lugar != null && lugar.indicacion != null && lugar.indicacion != "--" ? lugar.indicacion : "";
        campoHorario.text = lugar != null && lugar.horario != null ? lugar.horario : "";
        campoPiso.text = "";
        categoria = lugar != null ? lugar.categoria : null;
        edificioPadre = 0;

        // Estado limpio: sin modo interior previo ni posición guardada de otra apertura
        esInterior = false;
        hayUbicacionEdificio = false;
        ocupado = false;
        CerrarDescartar(false);
        PonerGuardando(false);
        AnularConfirmacion();
        LimpiarErrores();

        hayUbicacion = lugar != null;
        if (lugar != null)
        {
            ubicacionOriginal = new Vector2d(lugar.latitud, lugar.longitud);
            ubicacion = ubicacionOriginal;
        }

        // Editar un lugar existente: siempre es un edificio (los interiores se crean aparte). La pestaña que no
        // aplica se oculta en vez de quedar desactivada sin explicación.
        MostrarTabs(true, lugar == null);

        firmaInicial = Firma(false);
        bool interior = false;
        if (borrador != null && borrador.lugarId == lugarEditando && borrador.servicioId == 0) interior = RestaurarBorrador();

        PonerModo(interior);
        if (interior) ActualizarCamara();
    }

    /// <summary>Abre el formulario para editar un espacio interior (servicio) existente: aula, laboratorio, oficina...</summary>
    public void AbrirServicio(LugaresService.Servicio s)
    {
        if (s == null) { Abrir(null); return; }
        lugarEditando = 0;
        servicioEditando = s.id;
        titulo.text = "Editar espacio interior";

        campoNombre.text = s.nombre ?? "";
        campoDescripcion.text = s.descripcion ?? "";
        campoIndicacion.text = s.indicacion != null && s.indicacion != "--" ? s.indicacion : "";
        campoHorario.text = s.horario ?? "";
        campoPiso.text = s.piso ?? "";
        categoria = string.IsNullOrEmpty(s.categoria) ? null : s.categoria;
        edificioPadre = s.lugar_id;

        esInterior = false;
        hayUbicacion = false;
        hayUbicacionEdificio = false;
        ocupado = false;
        CerrarDescartar(false);
        PonerGuardando(false);
        AnularConfirmacion();
        LimpiarErrores();

        // Un espacio interior existente no se convierte en edificio: solo se muestra su pestaña
        MostrarTabs(false, true);

        firmaInicial = Firma(true);
        if (borrador != null && borrador.servicioId == servicioEditando) RestaurarBorrador();

        PonerModo(true);
        ActualizarCamara();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        panelOpciones.SetActive(false);
        CerrarDescartar(false);
        detector.Reiniciar();
        if (ui != null) ui.RegistrarManejadorAtras(manejadorAtras);

        if (camara != null)
        {
            seguiaAntes = camara.Siguiendo;
            camara.DejarDeSeguir();
        }

        // Lugar nuevo: el pin arranca en el centro de lo que se está viendo. Eso no es un cambio del usuario:
        // si la firma era la del formulario recién abierto, se actualiza con la ubicación inicial.
        bool sinTocar = firmaInicial != null && firmaInicial == Firma(esInterior);
        if (!hayUbicacion) PonerEnCentro();
        else ActualizarCamara();
        if (sinTocar) firmaInicial = Firma(esInterior);

        scrollFormulario.verticalNormalizedPosition = 1f;
        ActualizarPuerta();
    }

    private void OnDisable()
    {
        // La sesión venció y el UIManager cerró la pantalla: lo escrito no se pierde
        GuardarBorradorSiHayCambios();

        EnhancedTouchSupport.Disable();
        if (ui != null) ui.QuitarManejadorAtras(manejadorAtras);
        if (panelOpciones != null) panelOpciones.SetActive(false);
        CerrarDescartar(false);
        if (pin != null) pin.gameObject.SetActive(false);   // no queda visible al cerrar ni al reabrir
        detector.Reiniciar();

        // Si la cámara seguía al usuario antes de abrir, vuelve a seguirlo (no queda "bloqueada")
        if (camara != null && seguiaAntes) camara.CentrarEnUsuario();
        seguiaAntes = false;
    }

    // Atrás de Android: con el modal de descartar abierto = "Seguir editando"; luego cierra la lista abierta;
    // con cambios sin guardar abre el modal; si no, el UIManager cierra la pantalla
    private bool ManejarAtras()
    {
        if (!isActiveAndEnabled) return false;
        if (modalDescartar != null && modalDescartar.ManejarAtras()) return true;   // = Seguir editando
        if (panelOpciones != null && panelOpciones.activeSelf)
        {
            if (OpcionesAbiertas) CerrarOpciones();   // si ya se está cerrando, solo se consume el Atrás
            return true;
        }
        return PedirConfirmarDescarte();
    }

    // Atrás/X con cambios sin guardar: abre el modal "¿Descartar los cambios?".
    // Devuelve true si se consumió el toque (todavía no hay que cerrar).
    private bool PedirConfirmarDescarte()
    {
        if (ocupado || !HayCambios()) return false;
        AbrirDescartar();
        return true;
    }

    // Botón X de la barra: mismo modal que Atrás si hay cambios sin guardar
    private void CerrarConX()
    {
        if (PedirConfirmarDescarte()) return;
        Cancelar();
    }

    private void PonerModo(bool interior)
    {
        if (interior != esInterior)
        {
            if (interior)
            {
                // Recuerda dónde estaba el pin del edificio para volver a él
                ubicacionEdificio = ubicacion;
                hayUbicacionEdificio = hayUbicacion;
            }
            else
            {
                RestaurarUbicacionEdificio();
            }
        }
        esInterior = interior;
        AnularConfirmacion();

        if (!interior && categoria != null && Array.IndexOf(CategoriasEdificio, categoria) < 0 && lugarEditando == 0) categoria = null;
        if (interior && categoria != null && Array.IndexOf(CategoriasInterior, categoria) < 0 && servicioEditando == 0) categoria = null;

        Marcar(tabEdificio, !interior);
        Marcar(tabInterior, interior);
        filaEdificioPadre.SetActive(interior);
        filaPiso.SetActive(interior);
        if (!interior) OcultarError(errorEdificio);
        etiquetaIndicacion.text = interior ? "Cómo llegar dentro del edificio" : "Cómo encontrarlo";
        arrastre.Activo = !interior;

        TextoSelector(selectorCategoria, categoria != null ? UIRapida.TextoSeguro(categoria) : "Elige una categoría", categoria != null);
        LugaresService.Lugar padre = lugares != null ? lugares.BuscarPorId(edificioPadre) : null;
        TextoSelector(selectorEdificio, padre != null ? UIRapida.TextoSeguro(padre.nombre) : "Elige el edificio", padre != null);
        if (interior && padre != null) { ubicacion = new Vector2d(padre.latitud, padre.longitud); hayUbicacion = true; }

        textoAyuda.text = interior
            ? "Elige abajo el edificio donde está. El pin muestra ese edificio."
            : "Toca el mapa o arrastra el pin hasta el lugar.";
        ActualizarPuerta();
    }

    // Al volver a "Edificio": posición previa del pin, o la del lugar en edición, o el centro de la vista
    private void RestaurarUbicacionEdificio()
    {
        if (hayUbicacionEdificio) { ubicacion = ubicacionEdificio; hayUbicacion = true; }
        else if (lugarEditando > 0) { ubicacion = ubicacionOriginal; hayUbicacion = true; }
        else { hayUbicacion = false; PonerEnCentro(); }
        hayUbicacionEdificio = false;
        if (isActiveAndEnabled) ActualizarCamara();
    }

    private bool PonerEnCentro()
    {
        if (!mapaListo || mapa == null || cam == null) return false;
        if (!GeoMapa.PuntoEnSuelo(cam, new Vector2(Screen.width * 0.5f, Screen.height * 0.62f), out Vector3 suelo)) return false;
        ubicacion = mapa.WorldToGeoPosition(suelo);
        hayUbicacion = true;
        return true;
    }

    private void ActualizarCamara()
    {
        if (hayUbicacion && camara != null && mapaListo && mapa != null)
            camara.IrA(mapa.GeoToWorldPosition(ubicacion, false));
    }

    private void MostrarTabs(bool edificio, bool interior)
    {
        tabEdificio.gameObject.SetActive(edificio);
        tabInterior.gameObject.SetActive(interior);
        tabEdificio.interactable = tabInterior.interactable = true;
    }

    // Pestaña elegida / no elegida: estilo único de selección de la app (chips, pestañas y opciones de lista)
    private static void Marcar(Button b, bool activo) => UIRapida.MarcarSeleccion(b, activo);

    // ==================================================================
    // Utilidades
    // ==================================================================
    private static string Limpio(TMP_InputField c, bool unaLinea)
    {
        if (c == null || c.text == null) return null;
        string t = c.text.Trim();
        if (unaLinea && t.Length > 0) t = Espacios.Replace(t, " ");
        return t.Length == 0 ? null : t;
    }

    private static int LeerId(string json)
    {
        Match m = Regex.Match(json ?? "", "\"id\"\\s*:\\s*(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
