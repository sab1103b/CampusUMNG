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
/// Pantalla "Modificar mapa" del administrador. Sobre el mapa (que se mueve y hace zoom como siempre)
/// hay una barra de herramientas:
///   Mover     · navegar; tocar un modelo 3D lo selecciona para editarlo (y tocar el mapa lo mueve ahí)
///   Nodo      · tocar el mapa crea un nodo unido al nodo seleccionado (amarillo): se dibujan caminos punto a punto
///   Conectar  · tocar dos nodos los une (o los separa si ya estaban unidos)
///   Puerta    · tocar un nodo y elegir de qué lugar es la entrada (naranja)
///   Eliminar  · nodo: tocarlo dos veces; modelo 3D: tocarlo y confirmar en el modal (ver "Confirmación de borrado")
///   Cubo / Rectángulo / Cilindro · tocar el mapa coloca esa edificación
/// Todo se guarda en la base de datos al instante (los modelos, al tocar Guardar o al cambiar de selección).
/// Si la sesión vence con un modelo cambiado sin guardar, los cambios se conservan en memoria y se recuperan al volver.
/// InterfazCampus crea esta pantalla y la registra en el UIManager como "EditorMapa".
///
/// Confirmación de borrado:
///  · Modelos 3D (y descartar los cambios de un modelo): modal de confirmación (UIRapida.ModalConfirmar), el mismo
///    patrón que el "¿Descartar los cambios?" del formulario: velo (tocarlo = cancelar), "Cancelar"/"Seguir editando"
///    y la acción en Rojo; Atrás = cancelar. Mientras el modal está abierto el modelo se ve en Rojo.
///  · Nodos (decisión de diseño documentada): doble toque. El primero pinta el nodo en Rojo, el botón pasa a
///    "¿Eliminar?" y la línea de estado lo explica; solo el segundo toque sobre el MISMO nodo borra. Tocar otra cosa,
///    pulsar Atrás o dejar pasar SegundosConfirmar (8 s) cancela. Se mantiene porque al editar la red se borran
///    muchos nodos seguidos y la confirmación queda junto al nodo en el mapa (un modal lo taparía).
/// Partes: EditorMapa.Interfaz.cs (barra y paneles), EditorMapa.Red.cs (toques, nodos, conexiones, puertas
/// y dibujo de la red) y EditorMapa.Modelos.cs (edificaciones 3D).
/// </summary>
public partial class EditorMapa : MonoBehaviour
{
    public const string NombrePantalla = "EditorMapa";

    public enum Herramienta { Mover, Nodo, Conectar, Puerta, Eliminar, Cubo, Rectangulo, Cilindro }

    // Colores de la red en 3D (tokens de UIRapida): nodos de paso y caminos usan el azul de la ruta
    private static readonly Color ColorPaso = UIRapida.RutaColor;
    // Puertas: token Acento (naranja oscuro, se distingue del azul de los caminos y del verde del suelo)
    private static readonly Color ColorPuerta = UIRapida.Acento;
    // Nodo seleccionado: amarillo de relleno + aro Oscuro (el aro blanco casi no contrastaba con el suelo)
    private static readonly Color ColorSeleccion = UIRapida.Seleccion;
    private static readonly Color ColorEliminar = UIRapida.Rojo;
    // Modelo 3D seleccionado: Verde de la marca (4,6:1 contra el gris del edificio; el amarillo daba 1,09:1),
    // además del marco/base Oscuro a ras de suelo (ActualizarMarcoModelo)
    private static readonly Color ColorModeloSel = UIRapida.Verde;
    // Colores para texto enriquecido: error sobre la franja Menta, "sin guardar" sobre blanco y texto secundario
    private static readonly string ColorError = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.RojoSobreMenta);
    private static readonly string ColorSinGuardar = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Rojo);
    private static readonly string ColorGris = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Gris);
    private const float SegundosConfirmar = 8f;             // tiempo para el segundo toque de "Eliminar" sobre un nodo
    private const string MensajeSesionModelo = "Cambios del modelo sin guardar: la sesión venció. Se recuperan al volver a Modificar mapa.";
    private const string MensajeGuardadoAlSalir = "Cambios del modelo guardados.";

    // Raycast de toques sin asignar memoria: buffer y comparador reutilizados
    private static readonly RaycastHit[] impactos = new RaycastHit[64];
    private static readonly IComparer<RaycastHit> PorDistancia = new ComparadorDistancia();
    private sealed class ComparadorDistancia : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    // ----- Referencias -----
    private UIManager ui;
    private AdminService admin;
    private LugaresService lugares;
    private AbstractMap mapa;
    private ModelosMapa modelos;
    private CamaraMapa camara;
    private Camera cam;
    private TMP_FontAsset fuente;

    // ----- Interfaz -----
    private TMP_Text textoAyuda;
    private TMP_Text textoEstado;
    private readonly Dictionary<Herramienta, Button> botones = new Dictionary<Herramienta, Button>();
    private readonly Dictionary<Herramienta, Image> iconosHerramienta = new Dictionary<Herramienta, Image>();
    private GameObject panelModelo;    // sección de la barra para editar el modelo seleccionado
    private GameObject seccionRed;     // sección "Red de caminos" (se oculta mientras se edita un modelo)
    private TMP_Text textoModelo;
    private GameObject panelPuerta;
    private UIRapida.ModalDeLista modalPuerta;   // lista de lugares para la puerta (Vaciar entre usos)
    private RectTransform cajaPuerta;
    private CanvasGroup grupoPuerta;
    private Coroutine animPuerta;
    private UIRapida.ModalConfirmacion modalConfirmar;   // descartar cambios del modelo / eliminar un modelo
    private RectTransform listaPuertas;
    private TMP_Text tituloPuerta;

    // ----- Visual de la red (solo mientras se edita) -----
    private Transform raizVisual;
    private readonly Dictionary<int, NodoVisual> nodos = new Dictionary<int, NodoVisual>();
    // Pool por id: al recargar la red se reubican las esferas/barras existentes y solo se crean o destruyen las que cambian
    private readonly Dictionary<long, Transform> barras = new Dictionary<long, Transform>();
    private readonly HashSet<int> vistosNodos = new HashSet<int>();
    private readonly HashSet<long> vistasBarras = new HashSet<long>();
    private readonly List<int> sobrantesNodos = new List<int>();
    private readonly List<long> sobrantesBarras = new List<long>();
    private readonly Dictionary<int, Vector3> posicionesRed = new Dictionary<int, Vector3>();
    private bool mapaListo;

    // ----- Estado -----
    private Herramienta actual = Herramienta.Mover;
    private int nodoSeleccionado;
    private int nodoPorEliminar;
    private int modeloPorEliminar;
    private int nodoPuerta;
    private ModeloCampus modeloSel;
    private int idModeloSel;   // se recuerda aparte: al redibujar, el objeto anterior se destruye
    private LugaresService.Modelo originalModelo;   // datos compartidos (LugaresService.Modelos): no se tocan al editar
    private LugaresService.Modelo trabajoModelo;    // copia sobre la que se edita
    private bool modeloCambiado;
    private LugaresService.Modelo borradorModelo;   // cambios sin guardar cuando venció la sesión (solo en memoria)
    private float limiteEliminar;                    // hasta cuándo vale el segundo toque de "Eliminar"
    private TMP_Text textoBotonEliminar;
    private Func<bool> manejadorAtras;
    private bool seguiaAntes;                         // la cámara seguía al usuario antes de entrar (se restaura al salir)
    private Action alIniciarMapa;
    private Action<bool> alCambiarPerspectiva;

    // Caché para no reescalar la red cada frame
    private float ultimaAltura = -1f;
    private bool visualSucio = true;
    private int seleccionarModeloAlRedibujar;
    private bool ocupado;

    // ----- Toques -----
    private readonly DetectorToque detector = new DetectorToque();

    // ==================================================================
    // Creación
    // ==================================================================
    public static EditorMapa Crear(Transform padre, UIManager ui, AdminService admin, LugaresService lugares,
                                   AbstractMap mapa, ModelosMapa modelos, CamaraMapa camara, TMP_FontAsset fuente,
                                   bool mapaYaIniciado)
    {
        GameObject go = new GameObject("Panel_EditorMapa", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = 5;
        go.transform.SetParent(padre, false);
        go.SetActive(false);   // se configura apagado; el UIManager lo enciende al entrar

        Canvas c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5;
        CanvasScaler cs = go.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1080, 1920);
        cs.matchWidthOrHeight = 0.5f;

        EditorMapa e = go.AddComponent<EditorMapa>();
        e.ui = ui;
        e.admin = admin;
        e.lugares = lugares;
        e.mapa = mapa;
        e.modelos = modelos;
        e.camara = camara;
        e.cam = camara != null ? camara.GetComponent<Camera>() : Camera.main;
        e.fuente = fuente;
        e.mapaListo = mapaYaIniciado;
        e.alIniciarMapa = () => { e.mapaListo = true; if (e.isActiveAndEnabled) e.DibujarRed(); };
        if (mapa != null) mapa.OnInitialized += e.alIniciarMapa;
        e.ConstruirInterfaz();
        // Contenido dentro de la zona segura; franja de la cámara en verde (barra) y la de gestos en blanco (herramientas)
        ZonaSegura.En(go, UIRapida.Verde, UIRapida.SuperficieOpaca);

        if (lugares != null) lugares.RedCargada += e.AlCambiarRed;
        if (modelos != null) modelos.Redibujados += e.AlRedibujarModelos;
        UIRapida.AplicarFuentes(go.transform);   // Roboto (Regular/Medium/Bold reales); ver UIRapida.Tipografia
        return e;
    }

    // ==================================================================
    // Ciclo de vida
    // ==================================================================
    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        if (raizVisual == null) raizVisual = new GameObject("VisualRedEditor").transform;
        raizVisual.gameObject.SetActive(true);
        // Al editar, el mapa no vuelve solo a tu ubicación; al salir se restaura el seguimiento (como FormularioLugarMapa)
        if (camara != null)
        {
            seguiaAntes = camara.Siguiendo;
            camara.DejarDeSeguir();
        }

        nodoSeleccionado = nodoPorEliminar = modeloPorEliminar = 0;
        ocupado = false;
        detector.Reiniciar();
        if (modalConfirmar != null) modalConfirmar.Ocultar(false);
        CerrarPuerta();
        ElegirHerramienta(Herramienta.Mover);
        Estado("");
        DibujarRed();
        RestaurarBorradorModelo();

        // Botón Atrás de Android: primero se cierran las capas internas del editor
        if (ui == null) ui = FindFirstObjectByType<UIManager>();
        if (manejadorAtras == null) manejadorAtras = ManejarAtras;
        if (ui != null) ui.RegistrarManejadorAtras(manejadorAtras);
    }

    private void OnDisable()
    {
        if (ui != null && manejadorAtras != null) ui.QuitarManejadorAtras(manejadorAtras);
        EnhancedTouchSupport.Disable();
        if (modeloSel != null)
        {
            // Al salir no se pierden cambios: se guardan, o si la sesión venció se conservan para la próxima vez
            if (modeloCambiado && admin != null && !admin.EstaAutenticado) ConservarBorradorModelo();
            else GuardarModelo(false, MensajeGuardadoAlSalir);   // se avisa: Atrás descarta, la X guarda
        }
        DeseleccionarModelo();
        CancelarEliminar();          // ningún nodo/modelo queda en rojo en el modo usuario
        CerrarPuerta();
        if (modalConfirmar != null) modalConfirmar.Ocultar(false);
        BorrarVisual();              // la red del editor no queda en la escena al salir
        if (raizVisual != null) raizVisual.gameObject.SetActive(false);
        if (camara != null && seguiaAntes) camara.CentrarEnUsuario();
        seguiaAntes = false;
    }

    private void OnDestroy()
    {
        if (mapa != null && alIniciarMapa != null) mapa.OnInitialized -= alIniciarMapa;
        if (lugares != null) lugares.RedCargada -= AlCambiarRed;
        if (modelos != null) modelos.Redibujados -= AlRedibujarModelos;
        if (camara != null && alCambiarPerspectiva != null) camara.PerspectivaCambiada -= alCambiarPerspectiva;
        if (raizVisual != null) Destroy(raizVisual.gameObject);
    }

    private void Update()
    {
        DetectarToque();
        RevisarConfirmacion();
        EscalarVisual();
        ActualizarMarcoModelo();
    }

    // Atrás: cierra el modal (= cancelar) o el panel de puerta, termina la edición del modelo (con cambios sin
    // guardar, pregunta con el modal), cancela la confirmación de eliminar o la herramienta activa.
    // Si no hay nada abierto, deja que el UIManager salga del editor.
    private bool ManejarAtras()
    {
        if (!isActiveAndEnabled) return false;
        if (modalConfirmar != null && modalConfirmar.ManejarAtras()) return true;
        if (panelPuerta != null && panelPuerta.activeSelf) { if (PuertaAbierta) CerrarPuerta(); return true; }
        if (modeloSel != null) { PedirDescartarModelo(); return true; }
        if (nodoPorEliminar != 0 || modeloPorEliminar != 0) { CancelarEliminar(); Estado(""); return true; }
        if (actual == Herramienta.Conectar && nodoSeleccionado != 0) { nodoSeleccionado = 0; ColorearNodos(); Estado(""); return true; }
        if (actual != Herramienta.Mover) { ElegirHerramienta(Herramienta.Mover); return true; }
        return false;
    }

    private void Salir()
    {
        if (ui != null && ui.PantallaActual == NombrePantalla) ui.Atras();
    }

    // ==================================================================
    // Utilidades
    // ==================================================================
    private void Operar(string mensaje)
    {
        ocupado = true;
        Estado(mensaje);
    }

    private void Terminar(bool ok, string respuesta, string mensajeOk)
    {
        ocupado = false;
        Estado(ok ? mensajeOk : "<color=" + ColorError + ">" + UIRapida.TextoSeguro(ConPunto(respuesta ?? "No se pudo completar. Intenta de nuevo.")) + "</color>");
    }

    // Todo mensaje al usuario termina en punto (las respuestas del servicio pueden venir sin él)
    private static string ConPunto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        char u = texto[texto.Length - 1];
        return u == '.' || u == '?' || u == '!' || u == '\u2026' ? texto : texto + ".";
    }

    // Quita el punto final (para insertar una respuesta del servicio en medio de una frase)
    private static string SinPunto(string texto) => string.IsNullOrEmpty(texto) ? "" : texto.TrimEnd('.', ' ');

    private void Ayuda(string texto) { if (textoAyuda != null) textoAyuda.text = texto; }
    private void Estado(string texto) { if (textoEstado != null) textoEstado.text = texto ?? ""; }

    private static int LeerId(string json)
    {
        Match m = Regex.Match(json ?? "", "\"id\"\\s*:\\s*(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
