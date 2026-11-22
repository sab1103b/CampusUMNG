using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Toque = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Pantalla "Modificar mapa" del administrador. Sobre el mapa (que se mueve y hace zoom como siempre)
/// hay una barra de herramientas:
///   Mover     · navegar; tocar un modelo 3D lo selecciona para editarlo (y tocar el mapa lo mueve ahí)
///   Nodo      · tocar el mapa crea un nodo unido al nodo seleccionado (amarillo): se dibujan caminos punto a punto
///   Conectar  · tocar dos nodos los une (o los separa si ya estaban unidos)
///   Puerta    · tocar un nodo y elegir de qué lugar es la entrada (naranja)
///   Eliminar  · tocar un nodo o modelo y otra vez para confirmar
///   Cubo / Rectángulo / Cilindro · tocar el mapa coloca esa edificación
/// Todo se guarda en la base de datos al instante (los modelos, al tocar GUARDAR o al cambiar de selección).
/// InterfazCampus crea esta pantalla y la registra en el UIManager como "EditorMapa".
/// </summary>
public class EditorMapa : MonoBehaviour
{
    public const string NombrePantalla = "EditorMapa";

    public enum Herramienta { Mover, Nodo, Conectar, Puerta, Eliminar, Cubo, Rectangulo, Cilindro }

    // Colores de los nodos
    private static readonly Color ColorPaso = new Color(0.16f, 0.47f, 0.85f, 1f);
    private static readonly Color ColorPuerta = new Color(1.00f, 0.60f, 0.05f, 1f);
    private static readonly Color ColorSeleccion = new Color(1.00f, 0.86f, 0.10f, 1f);
    private static readonly Color ColorEliminar = new Color(0.85f, 0.20f, 0.15f, 1f);
    private static readonly Color ColorBarra = new Color(0.20f, 0.52f, 0.90f, 1f);
    private static readonly Color ColorModeloSel = new Color(1.00f, 0.90f, 0.45f, 1f);

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
    private RectTransform listaPuertas;
    private TMP_Text tituloPuerta;

    // ----- Visual de la red (solo mientras se edita) -----
    private Transform raizVisual;
    private readonly Dictionary<int, NodoVisual> nodos = new Dictionary<int, NodoVisual>();
    private readonly List<Transform> barras = new List<Transform>();
    private bool mapaListo;

    // ----- Estado -----
    private Herramienta actual = Herramienta.Mover;
    private int nodoSeleccionado;
    private int nodoPorEliminar;
    private int modeloPorEliminar;
    private int nodoPuerta;
    private ModeloCampus modeloSel;
    private int idModeloSel;   // se recuerda aparte: al redibujar, el objeto anterior se destruye
    private LugaresService.Modelo copiaModelo;
    private bool modeloCambiado;
    private int seleccionarModeloAlRedibujar;
    private bool ocupado;

    // ----- Toques -----
    private Vector2 inicioToque;
    private float tiempoToque;
    private bool toqueEnCurso;
    private bool toqueSobreUI;
    private readonly List<RaycastResult> impactosUI = new List<RaycastResult>();

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
        if (mapa != null) mapa.OnInitialized += () => e.mapaListo = true;
        e.ConstruirInterfaz();

        if (lugares != null) lugares.RedCargada += e.AlCambiarRed;
        if (modelos != null) modelos.Redibujados += e.AlRedibujarModelos;
        return e;
    }

    private void ConstruirInterfaz()
    {
        // ---------- Barra superior ----------
        Image barra = UIRapida.Panel("BarraSuperior", transform, UIRapida.Verde);
        UIRapida.Anclar(barra.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 170));
        TMP_Text titulo = UIRapida.Texto(barra.transform, "Titulo", "Modificar mapa", 50, Color.white, fuente, true, TextAlignmentOptions.MidlineLeft);
        UIRapida.Estirar((RectTransform)titulo.transform, 40, 20, 300, 20);
        Button listo = UIRapida.Boton(barra.transform, "BotonListo", "LISTO", 36, Color.white, UIRapida.Verde, fuente, Salir);
        UIRapida.Redondear((Image)listo.targetGraphic, 24f);
        UIRapida.Anclar((RectTransform)listo.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, -10), new Vector2(230, 100));

        // ---------- Ayuda y estado ----------
        Image ayuda = UIRapida.Panel("Ayuda", transform, new Color(UIRapida.Menta.r, UIRapida.Menta.g, UIRapida.Menta.b, 0.95f));
        UIRapida.Anclar(ayuda.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(0, 170));
        ayuda.raycastTarget = false;
        textoAyuda = UIRapida.Texto(ayuda.transform, "TextoAyuda", "", 30, UIRapida.Oscuro, fuente, false, TextAlignmentOptions.TopLeft);
        UIRapida.Estirar((RectTransform)textoAyuda.transform, 36, 60, 36, 16);
        textoEstado = UIRapida.Texto(ayuda.transform, "TextoEstado", "", 28, UIRapida.VerdeClaro, fuente, true, TextAlignmentOptions.BottomLeft);
        UIRapida.Estirar((RectTransform)textoEstado.transform, 36, 12, 36, 100);

        // ---------- Zoom y perspectiva (a la derecha, como en la pantalla de usuario) ----------
        // Mismos botones redondos (+, -, 3D) y misma posición que en el mapa del usuario
        if (camara != null)
        {
            RectTransform zoom = UIRapida.Rect("ControlesZoom", transform);
            UIRapida.Anclar(zoom, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-28, 40), new Vector2(130, 430));
            VerticalLayoutGroup vz = zoom.gameObject.AddComponent<VerticalLayoutGroup>();
            vz.spacing = 20;
            vz.childControlWidth = vz.childControlHeight = true;
            vz.childForceExpandWidth = vz.childForceExpandHeight = true;
            UIRapida.BotonRedondo(zoom, "BotonZoomMas", "+", 80, fuente, camara.ZoomMas);
            UIRapida.BotonRedondo(zoom, "BotonZoomMenos", "-", 96, fuente, camara.ZoomMenos);
            Button p = UIRapida.BotonRedondo(zoom, "BotonPerspectiva", camara.Es3D ? "2D" : "3D", 40, fuente, camara.AlternarPerspectiva);
            TMP_Text tp = UIRapida.TextoDe(p);
            camara.PerspectivaCambiada += tresD => tp.text = tresD ? "2D" : "3D";
        }

        // ---------- Herramientas (abajo): tarjeta redondeada con dos grupos ----------
        const float altoBarra = 466f, oculto = 30f;   // la parte baja se esconde fuera de pantalla (sin esquinas abajo)

        Image sombra = UIRapida.Panel("SombraHerramientas", transform, new Color(0f, 0f, 0f, 0.14f));
        UIRapida.Redondear(sombra, 34f);
        sombra.raycastTarget = false;
        UIRapida.Anclar(sombra.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -oculto), new Vector2(0, altoBarra + 8));

        Image herramientas = UIRapida.Panel("Herramientas", transform, Color.white);
        UIRapida.Redondear(herramientas, 34f);
        UIRapida.Anclar(herramientas.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -oculto), new Vector2(0, altoBarra));
        VerticalLayoutGroup vh = herramientas.gameObject.AddComponent<VerticalLayoutGroup>();
        vh.padding = new RectOffset(24, 24, 20, (int)oculto + 20);
        vh.spacing = 8;
        vh.childControlWidth = vh.childControlHeight = true;
        vh.childForceExpandWidth = true;
        vh.childForceExpandHeight = false;

        // Red de caminos: se oculta mientras se edita un modelo 3D (su lugar lo toma la edición del modelo)
        Transform filaRed = SeccionHerramientas(herramientas.transform, "RED DE CAMINOS", out TMP_Text _);
        seccionRed = filaRed.parent.gameObject;
        AgregarHerramienta(filaRed, Herramienta.Mover, "Mover", "mover");
        AgregarHerramienta(filaRed, Herramienta.Nodo, "Nodo", "nodo");
        AgregarHerramienta(filaRed, Herramienta.Conectar, "Conectar", "conectar");
        AgregarHerramienta(filaRed, Herramienta.Puerta, "Puerta", "puerta");
        AgregarHerramienta(filaRed, Herramienta.Eliminar, "Eliminar", "eliminar");

        // ---------- Edición del modelo seleccionado (mismo espacio que la red de caminos) ----------
        Transform filaModelo1 = SeccionHerramientas(herramientas.transform, "", out textoModelo, dosFilas: true);
        panelModelo = filaModelo1.parent.gameObject;
        textoModelo.characterSpacing = 0f;
        textoModelo.color = UIRapida.Oscuro;
        textoModelo.fontSize = 28;
        Transform filaModelo2 = panelModelo.transform.GetChild(panelModelo.transform.childCount - 1);

        BotonModelo(filaModelo1, "Girar -15°", () => CambiarModelo(rot: -15f));
        BotonModelo(filaModelo1, "Girar +15°", () => CambiarModelo(rot: 15f));
        BotonModelo(filaModelo1, "Más alto", () => CambiarModelo(alto: 2f));
        BotonModelo(filaModelo1, "Menos alto", () => CambiarModelo(alto: -2f));
        BotonAccionModelo(filaModelo1, "GUARDAR", UIRapida.Verde, () => { GuardarModelo(true); TerminarEdicionModelo(); });

        BotonModelo(filaModelo2, "Más ancho", () => CambiarModelo(ancho: 2f));
        BotonModelo(filaModelo2, "Menos ancho", () => CambiarModelo(ancho: -2f));
        BotonModelo(filaModelo2, "Más largo", () => CambiarModelo(largo: 2f));
        BotonModelo(filaModelo2, "Menos largo", () => CambiarModelo(largo: -2f));
        BotonAccionModelo(filaModelo2, "DESCARTAR", UIRapida.Gris, () => { DescartarModelo(); TerminarEdicionModelo(); });
        panelModelo.SetActive(false);

        Transform filaEdif = SeccionHerramientas(herramientas.transform, "EDIFICACIONES 3D", out TMP_Text _);
        AgregarHerramienta(filaEdif, Herramienta.Cubo, "Cubo", "cubo");
        AgregarHerramienta(filaEdif, Herramienta.Rectangulo, "Rectángulo", "rectangulo");
        AgregarHerramienta(filaEdif, Herramienta.Cilindro, "Cilindro", "cilindro");

        // ---------- Elegir de qué lugar es la puerta ----------
        Image pp = UIRapida.Panel("PanelPuerta", transform, Color.white);
        UIRapida.Redondear(pp, 34f);
        UIRapida.Anclar(pp.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        panelPuerta = pp.gameObject;
        tituloPuerta = UIRapida.Texto(pp.transform, "Titulo", "¿De qué lugar es esta puerta?", 38, UIRapida.Verde, fuente, true, TextAlignmentOptions.MidlineLeft);
        UIRapida.Anclar((RectTransform)tituloPuerta.transform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(-60, 90));

        Image vista = UIRapida.Panel("Lista", pp.transform, new Color(1, 1, 1, 0));
        UIRapida.Estirar(vista.rectTransform, 30, 130, 30, 120);
        vista.gameObject.AddComponent<RectMask2D>();
        ScrollRect sr = vista.gameObject.AddComponent<ScrollRect>();
        listaPuertas = UIRapida.Rect("Contenido", vista.transform);
        UIRapida.Anclar(listaPuertas, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vlg = listaPuertas.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        listaPuertas.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport = vista.rectTransform;
        sr.content = listaPuertas;
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;

        Button cancelar = UIRapida.Boton(pp.transform, "Cancelar", "CANCELAR", 32, UIRapida.Gris, Color.white, fuente, CerrarPuerta);
        UIRapida.Redondear((Image)cancelar.targetGraphic, 24f);
        UIRapida.Anclar((RectTransform)cancelar.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 25), new Vector2(400, 90));
        panelPuerta.SetActive(false);
    }

    // Sección de la barra: título pequeño + una fila de herramientas (o dos filas de 70 para editar un modelo).
    // Siempre mide lo mismo, así cambiar de sección no mueve el resto de la barra.
    private Transform SeccionHerramientas(Transform padre, string titulo, out TMP_Text textoTitulo, bool dosFilas = false)
    {
        RectTransform seccion = UIRapida.Rect("Seccion " + (titulo.Length > 0 ? titulo : "Modelo"), padre);
        seccion.gameObject.AddComponent<LayoutElement>().preferredHeight = 194;
        VerticalLayoutGroup v = seccion.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        textoTitulo = UIRapida.Texto(seccion, "Titulo", titulo, 24, UIRapida.Gris, fuente, true, TextAlignmentOptions.BottomLeft);
        textoTitulo.characterSpacing = 6f;
        textoTitulo.textWrappingMode = TextWrappingModes.NoWrap;
        textoTitulo.overflowMode = TextOverflowModes.Ellipsis;
        textoTitulo.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;

        Transform primera = Fila(seccion, dosFilas ? 70 : 150);
        if (dosFilas) Fila(seccion, 70);
        return primera;
    }

    private static Transform Fila(Transform padre, float alto)
    {
        RectTransform fila = UIRapida.Rect("Fila", padre);
        fila.gameObject.AddComponent<LayoutElement>().preferredHeight = alto;
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        return fila;
    }

    // Botón de herramienta: tarjeta redondeada con ícono dibujado y nombre
    private void AgregarHerramienta(Transform padre, Herramienta h, string texto, string icono)
    {
        Image fondo = UIRapida.Panel("Herramienta_" + h, padre, UIRapida.Menta);
        UIRapida.Redondear(fondo, 26f);
        Button b = fondo.gameObject.AddComponent<Button>();
        b.targetGraphic = fondo;
        b.onClick.AddListener(() => ElegirHerramienta(h));

        Image img = UIRapida.Panel("Icono", fondo.transform, UIRapida.Verde);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(66, 66));

        TMP_Text t = UIRapida.Texto(fondo.transform, "Texto", texto, 28, UIRapida.Oscuro, fuente, true, TextAlignmentOptions.Center);
        UIRapida.Anclar((RectTransform)t.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(-12, 46));
        t.enableAutoSizing = true;
        t.fontSizeMin = 18;
        t.fontSizeMax = 28;

        botones[h] = b;
        iconosHerramienta[h] = img;
    }

    private void BotonAccionModelo(Transform padre, string texto, Color fondo, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, texto, texto, 28, fondo, Color.white, fuente, accion);
        UIRapida.Redondear((Image)b.targetGraphic, 20f);
    }

    private void BotonModelo(Transform padre, string texto, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, texto, texto, 28, UIRapida.Menta, UIRapida.Oscuro, fuente, accion);
        UIRapida.Redondear((Image)b.targetGraphic, 20f);
    }

    // ==================================================================
    // Ciclo de vida
    // ==================================================================
    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        if (raizVisual == null) raizVisual = new GameObject("VisualRedEditor").transform;
        raizVisual.gameObject.SetActive(true);
        if (camara != null) camara.DejarDeSeguir();   // al editar, el mapa no vuelve solo a tu ubicación

        nodoSeleccionado = nodoPorEliminar = modeloPorEliminar = 0;
        CerrarPuerta();
        ElegirHerramienta(Herramienta.Mover);
        Estado("");
        DibujarRed();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        if (modeloSel != null) GuardarModelo(false);   // al salir no se pierden cambios
        DeseleccionarModelo();
        if (raizVisual != null) raizVisual.gameObject.SetActive(false);
    }

    private void Update()
    {
        DetectarToque();
        EscalarVisual();
    }

    private void Salir()
    {
        if (ui != null && ui.PantallaActual == NombrePantalla) ui.Atras();
    }

    // ==================================================================
    // Herramientas
    // ==================================================================
    private void ElegirHerramienta(Herramienta h)
    {
        if (h != Herramienta.Mover && modeloSel != null) { GuardarModelo(false); DeseleccionarModelo(); }

        actual = h;
        nodoPorEliminar = modeloPorEliminar = 0;
        if (h != Herramienta.Nodo && h != Herramienta.Conectar) nodoSeleccionado = 0;
        ColorearNodos();

        foreach (var par in botones)
        {
            bool activa = par.Key == h;
            Color colorActiva = par.Key == Herramienta.Eliminar ? UIRapida.Rojo : UIRapida.Verde;
            ((Image)par.Value.targetGraphic).color = activa ? colorActiva : UIRapida.Menta;
            UIRapida.TextoDe(par.Value).color = activa ? Color.white : UIRapida.Oscuro;
            if (iconosHerramienta.TryGetValue(par.Key, out Image icono))
                icono.color = activa ? Color.white : (par.Key == Herramienta.Eliminar ? UIRapida.Rojo : UIRapida.Verde);
        }

        switch (h)
        {
            case Herramienta.Mover:
                Ayuda("Arrastra para moverte y usa + / - o pellizca para el zoom. Toca un modelo 3D para editarlo."); break;
            case Herramienta.Nodo:
                Ayuda("Toca el mapa para crear un nodo: se une al nodo <b>amarillo</b>. Toca un nodo para seguir desde él; tócalo otra vez para soltarlo."); break;
            case Herramienta.Conectar:
                Ayuda("Toca dos nodos para unirlos. Si ya estaban unidos, se separan."); break;
            case Herramienta.Puerta:
                Ayuda("Toca un nodo para marcarlo como <b>puerta</b> (entrada) de un lugar. Las puertas se ven en naranja."); break;
            case Herramienta.Eliminar:
                Ayuda("Toca un nodo o un modelo 3D y vuelve a tocarlo para eliminarlo."); break;
            default:
                Ayuda($"Toca el mapa donde quieras el <b>{NombreTipo(TipoDe(h))}</b>."); break;
        }
    }

    // ==================================================================
    // Toques sobre el mapa (un toque corto, sin arrastrar)
    // ==================================================================
    private void DetectarToque()
    {
        if (panelPuerta.activeSelf) return;   // mientras se elige el lugar de una puerta

        if (Toque.activeTouches.Count > 0)
        {
            if (Toque.activeTouches.Count > 1) { toqueEnCurso = false; return; }   // pellizco: no es un toque
            Toque t = Toque.activeTouches[0];
            if (t.phase == UnityEngine.InputSystem.TouchPhase.Began) EmpezarToque(t.screenPosition);
            else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended) TerminarToque(t.screenPosition);
            else if (t.phase == UnityEngine.InputSystem.TouchPhase.Canceled) toqueEnCurso = false;
            return;
        }

        Mouse m = Mouse.current;
        if (m == null) return;
        if (m.leftButton.wasPressedThisFrame) EmpezarToque(m.position.ReadValue());
        else if (m.leftButton.wasReleasedThisFrame) TerminarToque(m.position.ReadValue());
    }

    private void EmpezarToque(Vector2 pos)
    {
        toqueEnCurso = true;
        inicioToque = pos;
        tiempoToque = Time.unscaledTime;
        toqueSobreUI = SobreUI(pos);
    }

    private void TerminarToque(Vector2 pos)
    {
        if (!toqueEnCurso) return;
        toqueEnCurso = false;
        float tolerancia = Mathf.Max(12f, Screen.dpi > 0 ? Screen.dpi * 0.08f : 15f);
        bool esToque = !toqueSobreUI && Vector2.Distance(pos, inicioToque) < tolerancia && Time.unscaledTime - tiempoToque < 0.6f;
        if (esToque) Tocar(pos);
    }

    private void Tocar(Vector2 pos)
    {
        if (ocupado || !mapaListo) return;

        ObjetoTocado(pos, out NodoVisual nodo, out ModeloCampus modelo);
        bool haySuelo = GeoMapa.PuntoEnSuelo(cam, pos, out Vector3 suelo);

        switch (actual)
        {
            case Herramienta.Mover:
                if (modelo != null) SeleccionarModelo(modelo);
                else if (modeloSel != null && haySuelo) MoverModeloA(suelo);
                break;

            case Herramienta.Nodo:
                if (nodo != null) { nodoSeleccionado = nodoSeleccionado == nodo.id ? 0 : nodo.id; ColorearNodos(); }
                else if (haySuelo) CrearNodo(suelo);
                break;

            case Herramienta.Conectar:
                if (nodo == null) { nodoSeleccionado = 0; ColorearNodos(); }
                else if (nodoSeleccionado == 0) { nodoSeleccionado = nodo.id; ColorearNodos(); Estado($"Nodo #{nodo.id} elegido: toca el otro nodo."); }
                else if (nodoSeleccionado == nodo.id) { nodoSeleccionado = 0; ColorearNodos(); }
                else AlternarConexion(nodoSeleccionado, nodo.id);
                break;

            case Herramienta.Puerta:
                if (nodo != null) AbrirPuerta(nodo);
                break;

            case Herramienta.Eliminar:
                if (nodo != null) PedirEliminarNodo(nodo.id);
                else if (modelo != null) PedirEliminarModelo(modelo);
                else { nodoPorEliminar = modeloPorEliminar = 0; ColorearNodos(); ColorearModelosEliminar(); }
                break;

            default:
                if (haySuelo) CrearModelo(TipoDe(actual), suelo);
                break;
        }
    }

    private void ObjetoTocado(Vector2 pos, out NodoVisual nodo, out ModeloCampus modelo)
    {
        nodo = null;
        modelo = null;
        RaycastHit[] impactos = Physics.RaycastAll(cam.ScreenPointToRay(pos), 100000f);
        System.Array.Sort(impactos, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in impactos)
        {
            nodo = h.collider.GetComponent<NodoVisual>();
            if (nodo != null) return;
            modelo = h.collider.GetComponent<ModeloCampus>();
            if (modelo != null) return;
        }
    }

    // ==================================================================
    // Nodos y conexiones
    // ==================================================================
    private void CrearNodo(Vector3 suelo)
    {
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        int unirCon = nodoSeleccionado;

        Operar("Creando nodo...");
        admin.CrearNodo(new AdminService.NodoEditable
        {
            latitud = geo.x,
            longitud = geo.y,
            conectar_con_nodo = unirCon,
        }, (ok, resp) =>
        {
            Terminar(ok, resp, ok ? (unirCon > 0 ? $"Nodo creado y unido al #{unirCon}." : "Nodo creado.") : null);
            if (ok) { nodoSeleccionado = LeerId(resp); ColorearNodos(); }
        });
    }

    private void AlternarConexion(int a, int b)
    {
        bool existe = Conectados(a, b);
        Operar(existe ? "Separando nodos..." : "Uniendo nodos...");
        System.Action<bool, string> fin = (ok, resp) =>
        {
            Terminar(ok, resp, ok ? (existe ? $"Nodos #{a} y #{b} separados." : $"Nodos #{a} y #{b} unidos.") : null);
            if (ok) { nodoSeleccionado = b; ColorearNodos(); }   // se sigue desde el último nodo
        };
        if (existe) admin.EliminarConexion(a, b, fin);
        else admin.CrearConexion(a, b, fin);
    }

    private bool Conectados(int a, int b)
    {
        if (lugares.Red?.conexiones == null) return false;
        foreach (var c in lugares.Red.conexiones)
            if ((c.nodo_a == a && c.nodo_b == b) || (c.nodo_a == b && c.nodo_b == a)) return true;
        return false;
    }

    private void PedirEliminarNodo(int id)
    {
        modeloPorEliminar = 0;
        ColorearModelosEliminar();
        if (nodoPorEliminar != id)
        {
            nodoPorEliminar = id;
            ColorearNodos();
            Estado($"Toca otra vez el nodo #{id} (rojo) para eliminarlo. Sus conexiones también se borran.");
            return;
        }

        nodoPorEliminar = 0;
        if (nodoSeleccionado == id) nodoSeleccionado = 0;
        Operar("Eliminando nodo...");
        admin.EliminarNodo(id, (ok, resp) => Terminar(ok, resp, ok ? $"Nodo #{id} eliminado." : null));
    }

    // ---------- Puertas ----------
    private void AbrirPuerta(NodoVisual nodo)
    {
        nodoPuerta = nodo.id;
        string actualLugar = "ninguno (punto de paso)";
        if (nodo.lugarId > 0)
        {
            LugaresService.Lugar l = lugares.BuscarPorId(nodo.lugarId);
            if (l != null) actualLugar = l.nombre;
        }
        tituloPuerta.text = $"Nodo #{nodo.id}: ¿de qué lugar es la puerta?\n<size=70%><color=#5d6b62>Ahora: {actualLugar}</color></size>";

        for (int i = listaPuertas.childCount - 1; i >= 0; i--) Destroy(listaPuertas.GetChild(i).gameObject);
        FilaPuerta("Ninguno (punto de paso)", 0, nodo.lugarId == 0);
        foreach (LugaresService.Lugar l in lugares.Lugares) FilaPuerta($"{l.nombre}  <size=75%>· {l.categoria}</size>", l.id, nodo.lugarId == l.id);

        panelPuerta.SetActive(true);
        panelPuerta.transform.SetAsLastSibling();
    }

    private void FilaPuerta(string texto, int lugarId, bool actualSeleccionado)
    {
        Button b = UIRapida.Boton(listaPuertas, "Lugar_" + lugarId, texto, 32,
                                  actualSeleccionado ? UIRapida.Verde : UIRapida.Menta,
                                  actualSeleccionado ? Color.white : UIRapida.Oscuro, fuente, () => AsignarPuerta(lugarId));
        UIRapida.Redondear((Image)b.targetGraphic, 22f);
        TMP_Text t = UIRapida.TextoDe(b);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        ((RectTransform)t.transform).offsetMin = new Vector2(30, 4);
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 100;
    }

    private void AsignarPuerta(int lugarId)
    {
        int id = nodoPuerta;
        CerrarPuerta();

        LugaresService.NodoDto dto = BuscarNodoDto(id);
        if (dto == null) { Estado("Ese nodo ya no existe."); return; }

        string nombreLugar = lugarId > 0 ? lugares.BuscarPorId(lugarId)?.nombre : null;
        Operar("Guardando puerta...");
        admin.ActualizarNodo(id, new AdminService.NodoEditable
        {
            nombre = dto.nombre,
            latitud = dto.latitud,
            longitud = dto.longitud,
            lugar_id = lugarId,
        }, (ok, resp) => Terminar(ok, resp, ok
            ? (lugarId > 0 ? $"El nodo #{id} ahora es puerta de {nombreLugar}." : $"El nodo #{id} ahora es un punto de paso.")
            : null));
    }

    private void CerrarPuerta()
    {
        if (panelPuerta != null) panelPuerta.SetActive(false);
    }

    private LugaresService.NodoDto BuscarNodoDto(int id)
    {
        if (lugares.Red?.nodos == null) return null;
        foreach (var n in lugares.Red.nodos) if (n.id == id) return n;
        return null;
    }

    // ==================================================================
    // Modelos 3D
    // ==================================================================
    private static string TipoDe(Herramienta h) =>
        h == Herramienta.Cubo ? "cubo" : h == Herramienta.Rectangulo ? "rectangulo" : "cilindro";

    private static string NombreTipo(string tipo) =>
        tipo == "cubo" ? "cubo" : tipo == "rectangulo" ? "rectángulo" : "cilindro";

    private void CrearModelo(string tipo, Vector3 suelo)
    {
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        AdminService.ModeloEditable m = new AdminService.ModeloEditable
        {
            tipo = tipo,
            latitud = geo.x,
            longitud = geo.y,
            rotacion = 0f,
            ancho = tipo == "rectangulo" ? 24f : tipo == "cilindro" ? 10f : 12f,
            largo = tipo == "rectangulo" ? 12f : tipo == "cilindro" ? 10f : 12f,
            alto = tipo == "rectangulo" ? 8f : 12f,
        };

        Operar($"Agregando {NombreTipo(tipo)}...");
        admin.CrearModelo(m, (ok, resp) =>
        {
            Terminar(ok, resp, ok ? $"{char.ToUpper(NombreTipo(tipo)[0])}{NombreTipo(tipo).Substring(1)} agregado. Ajústalo abajo." : null);
            if (!ok) return;
            seleccionarModeloAlRedibujar = LeerId(resp);   // se selecciona cuando aparezca en el mapa
            ElegirHerramienta(Herramienta.Mover);
        });
    }

    private void AlRedibujarModelos()
    {
        if (!isActiveAndEnabled) return;
        int id = seleccionarModeloAlRedibujar != 0 ? seleccionarModeloAlRedibujar : idModeloSel;
        seleccionarModeloAlRedibujar = 0;
        modeloSel = null;
        idModeloSel = 0;
        if (id != 0)
        {
            ModeloCampus mc = modelos.Buscar(id);
            if (mc != null) SeleccionarModelo(mc, guardarAnterior: false);
            else MostrarSeccionModelo(false);
        }
        ColorearModelosEliminar();
    }

    private void SeleccionarModelo(ModeloCampus mc, bool guardarAnterior = true)
    {
        if (modeloSel == mc) return;
        if (guardarAnterior && modeloSel != null) GuardarModelo(false);
        DeseleccionarModelo();

        modeloSel = mc;
        idModeloSel = mc.datos.id;
        copiaModelo = Copia(mc.datos);
        modeloCambiado = false;
        mc.Resaltar(true, ColorModeloSel);
        MostrarSeccionModelo(true);
        ActualizarTextoModelo();
        Ayuda("Ajusta el modelo con los botones. Toca el mapa para <b>moverlo</b> ahí. GUARDAR deja los cambios en la base de datos.");
    }

    private void DeseleccionarModelo()
    {
        if (modeloSel != null) modeloSel.Resaltar(false, ColorModeloSel);
        modeloSel = null;
        idModeloSel = 0;
        copiaModelo = null;
        modeloCambiado = false;
        MostrarSeccionModelo(false);
    }

    // En la barra: edición del modelo en lugar de "Red de caminos", o al revés
    private void MostrarSeccionModelo(bool editando)
    {
        if (panelModelo != null) panelModelo.SetActive(editando);
        if (seccionRed != null) seccionRed.SetActive(!editando);
    }

    // GUARDAR o DESCARTAR: se suelta el modelo y vuelve la red de caminos
    private void TerminarEdicionModelo()
    {
        DeseleccionarModelo();
        ElegirHerramienta(Herramienta.Mover);
    }

    private void CambiarModelo(float rot = 0f, float ancho = 0f, float largo = 0f, float alto = 0f)
    {
        if (modeloSel == null) return;
        LugaresService.Modelo d = modeloSel.datos;

        d.rotacion = Mathf.Repeat(d.rotacion + rot, 360f);
        if (d.tipo == "cubo" || d.tipo == "cilindro")
        {
            // El cubo y el cilindro conservan la base pareja (cuadrada o redonda)
            float lado = Mathf.Clamp(d.ancho + ancho + largo, 2f, 300f);
            d.ancho = d.largo = lado;
        }
        else
        {
            d.ancho = Mathf.Clamp(d.ancho + ancho, 2f, 300f);
            d.largo = Mathf.Clamp(d.largo + largo, 2f, 300f);
        }
        d.alto = Mathf.Clamp(d.alto + alto, 2f, 200f);

        modeloCambiado = true;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
    }

    private void MoverModeloA(Vector3 suelo)
    {
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        modeloSel.datos.latitud = geo.x;
        modeloSel.datos.longitud = geo.y;
        modeloCambiado = true;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
    }

    private void ActualizarTextoModelo()
    {
        if (modeloSel == null) return;
        LugaresService.Modelo d = modeloSel.datos;
        string medidas = d.tipo == "cilindro" ? $"Ø {d.ancho:0} m" : $"{d.ancho:0} × {d.largo:0} m";
        textoModelo.text = $"{char.ToUpper(NombreTipo(d.tipo)[0])}{NombreTipo(d.tipo).Substring(1)} #{d.id} · {medidas} · {d.alto:0} m de alto · {d.rotacion:0}°" +
                           (modeloCambiado ? "  <color=#b3402e>(sin guardar)</color>" : "");
    }

    private void GuardarModelo(bool avisar)
    {
        if (modeloSel == null) return;
        if (!modeloCambiado) { if (avisar) Estado("No hay cambios por guardar."); return; }

        LugaresService.Modelo d = modeloSel.datos;
        int id = d.id;
        modeloCambiado = false;
        ActualizarTextoModelo();

        Operar("Guardando modelo...");
        admin.ActualizarModelo(id, new AdminService.ModeloEditable
        {
            tipo = d.tipo, nombre = d.nombre, latitud = d.latitud, longitud = d.longitud,
            rotacion = d.rotacion, ancho = d.ancho, largo = d.largo, alto = d.alto,
        }, (ok, resp) => Terminar(ok, resp, ok ? $"Modelo #{id} guardado." : null));
    }

    private void DescartarModelo()
    {
        if (modeloSel == null || copiaModelo == null) return;
        LugaresService.Modelo d = modeloSel.datos;
        d.latitud = copiaModelo.latitud; d.longitud = copiaModelo.longitud; d.rotacion = copiaModelo.rotacion;
        d.ancho = copiaModelo.ancho; d.largo = copiaModelo.largo; d.alto = copiaModelo.alto;
        modeloCambiado = false;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
        Estado("Cambios descartados.");
    }

    private void PedirEliminarModelo(ModeloCampus mc)
    {
        nodoPorEliminar = 0;
        ColorearNodos();
        int id = mc.datos.id;
        if (modeloPorEliminar != id)
        {
            modeloPorEliminar = id;
            ColorearModelosEliminar();
            Estado($"Toca otra vez el modelo #{id} (rojo) para eliminarlo.");
            return;
        }

        modeloPorEliminar = 0;
        if (modeloSel == mc) DeseleccionarModelo();
        Operar("Eliminando modelo...");
        admin.EliminarModelo(id, (ok, resp) => Terminar(ok, resp, ok ? $"Modelo #{id} eliminado." : null));
    }

    private void ColorearModelosEliminar()
    {
        if (modelos == null) return;
        foreach (LugaresService.Modelo d in lugares.Modelos)
        {
            ModeloCampus mc = modelos.Buscar(d.id);
            if (mc == null) continue;
            if (d.id == modeloPorEliminar) mc.Resaltar(true, ColorEliminar);
            else mc.Resaltar(mc == modeloSel, ColorModeloSel);
        }
    }

    private static LugaresService.Modelo Copia(LugaresService.Modelo d) => new LugaresService.Modelo
    {
        id = d.id, tipo = d.tipo, nombre = d.nombre, latitud = d.latitud, longitud = d.longitud,
        rotacion = d.rotacion, ancho = d.ancho, largo = d.largo, alto = d.alto,
    };

    // ==================================================================
    // Dibujo de la red mientras se edita
    // ==================================================================
    private void AlCambiarRed()
    {
        if (isActiveAndEnabled) DibujarRed();
    }

    private void DibujarRed()
    {
        if (raizVisual == null || lugares == null || lugares.Red == null || !mapaListo) return;

        for (int i = raizVisual.childCount - 1; i >= 0; i--) Destroy(raizVisual.GetChild(i).gameObject);
        nodos.Clear();
        barras.Clear();

        Dictionary<int, Vector3> posiciones = new Dictionary<int, Vector3>();
        if (lugares.Red.nodos != null)
        {
            foreach (var n in lugares.Red.nodos)
            {
                Vector3 p = mapa.GeoToWorldPosition(new Vector2d(n.latitud, n.longitud), false);
                p.y = 0.5f;
                posiciones[n.id] = p;

                GameObject esfera = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                esfera.name = $"Nodo_{n.id}";
                esfera.transform.SetParent(raizVisual, false);
                esfera.transform.position = p;
                esfera.GetComponent<Renderer>().material = GeoMapa.Material(ColorPaso);

                NodoVisual nv = esfera.AddComponent<NodoVisual>();
                nv.id = n.id;
                nv.lugarId = n.lugar_id;
                nodos[n.id] = nv;
            }
        }

        if (lugares.Red.conexiones != null)
        {
            foreach (var c in lugares.Red.conexiones)
            {
                if (!posiciones.TryGetValue(c.nodo_a, out Vector3 a) || !posiciones.TryGetValue(c.nodo_b, out Vector3 b)) continue;
                GameObject barra = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barra.name = $"Camino_{c.nodo_a}_{c.nodo_b}";
                Destroy(barra.GetComponent<Collider>());   // los toques deben llegar a los nodos, no a las barras
                barra.transform.SetParent(raizVisual, false);
                barra.transform.position = (a + b) * 0.5f;
                Vector3 dir = b - a;
                if (dir.sqrMagnitude > 1e-6f) barra.transform.rotation = Quaternion.LookRotation(dir);
                barra.transform.localScale = new Vector3(1f, 0.2f, dir.magnitude);
                barra.GetComponent<Renderer>().material = GeoMapa.Material(ColorBarra);
                barras.Add(barra.transform);
            }
        }

        if (nodoSeleccionado != 0 && !nodos.ContainsKey(nodoSeleccionado)) nodoSeleccionado = 0;
        ColorearNodos();
        EscalarVisual();
    }

    private void ColorearNodos()
    {
        foreach (NodoVisual nv in nodos.Values)
        {
            Color c = nv.id == nodoPorEliminar ? ColorEliminar
                    : nv.id == nodoSeleccionado ? ColorSeleccion
                    : nv.lugarId > 0 ? ColorPuerta
                    : ColorPaso;
            nv.PonerColor(c);
        }
    }

    // Los nodos y caminos se ven del mismo tamaño en pantalla sin importar el zoom
    private void EscalarVisual()
    {
        if (cam == null || nodos.Count == 0) return;
        float altura = Mathf.Max(5f, cam.transform.position.y);
        float diametro = altura * 0.035f;
        float grosor = altura * 0.008f;

        foreach (NodoVisual nv in nodos.Values)
        {
            float d = nv.id == nodoSeleccionado || nv.id == nodoPorEliminar ? diametro * 1.35f : diametro;
            nv.transform.localScale = new Vector3(d, d, d);
        }
        foreach (Transform b in barras)
        {
            Vector3 s = b.localScale;
            b.localScale = new Vector3(grosor, grosor * 0.4f, s.z);
        }
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
        Estado(ok ? mensajeOk : "<color=#b3402e>" + respuesta + "</color>");
    }

    private void Ayuda(string texto) { if (textoAyuda != null) textoAyuda.text = texto; }
    private void Estado(string texto) { if (textoEstado != null) textoEstado.text = texto ?? ""; }

    private static int LeerId(string json)
    {
        Match m = Regex.Match(json ?? "", "\"id\"\\s*:\\s*(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private bool SobreUI(Vector2 pos)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;
        PointerEventData datos = new PointerEventData(es) { position = pos };
        impactosUI.Clear();
        es.RaycastAll(datos, impactosUI);
        return impactosUI.Count > 0;
    }
}
