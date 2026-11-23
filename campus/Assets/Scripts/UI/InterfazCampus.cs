using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Front de Campus Conecta: conecta los botones del diseño (InterfazGrafica) con la navegación
/// (UIManager) y con la base de datos (LugaresService / AdminService / ComoLlegarController).
///
/// Flujo:
///   Menú principal ─┬─ Explorar campus ──► Usuario (mapa, buscador, ficha, ruta)
///                   ├─ Modo administrador ► Login (usuario + contraseña) ─► Panel de administración
///                   │                        └─ Registro (usuario + contraseña + código)
///                   └─ Cerrar sesión (solo con sesión de administrador)
///
/// Las referencias las asigna la herramienta Campus > Integrar InterfazGrafica en Santiago.
/// Los listeners se agregan por código en Awake (no hay OnClick configurados a mano).
/// </summary>
public partial class InterfazCampus : MonoBehaviour
{
    // Nombres de las pantallas en el UIManager
    public const string PMenu = "Menu", PLogin = "AdminLogin", PRegistro = "Registro", PUsuario = "Usuario",
                        PFicha = "Ficha", PAdmin = "AdminPanel", PFormulario = "AdminLugar";

    [Header("Servicios")]
    public UIManager ui;
    public AdminService admin;
    public LugaresService lugares;
    public ComoLlegarController comoLlegar;
    public UbicacionController ubicacion;

    [Header("Menú principal")]
    public Button btnExplorar;
    public Button btnModoAdmin;
    public Button btnCerrarSesion;

    [Header("Login de administrador")]
    public InputField loginUsuario;
    public InputField loginLlave;
    public Button btnEntrar;
    public Button btnIrRegistro;
    public TMP_Text loginError;
    public TMP_Text loginOk;

    [Header("Registro de administrador")]
    public InputField regUsuario;
    public InputField regLlave;
    public InputField regCodigo;
    public Toggle regTerminos;
    public Button btnRegistrar;
    public Button btnRegVolver;
    public TMP_Text regError;
    public TMP_Text regOk;

    [Header("Usuario: buscador")]
    public TMP_InputField buscador;
    public Button[] chips;
    public RectTransform referenciaChips;          // el resultado aparece justo debajo
    public GameObject panelResultados;
    public RectTransform contenedorResultados;
    public Button plantillaResultado;
    public TMP_Text textoSinResultados;
    public Button btnExplorarAbajo;

    [Header("Usuario: menú de perfil")]
    public Button btnPerfil;
    public GameObject menuPerfil;
    public GameObject zonaCerrarMenu;
    public Button btnCerrarMenu;
    public Button btnOpcionPerfil;
    public Button btnOpcionAjustes;
    public Button btnOpcionMenuPrincipal;

    [Header("Usuario: ficha del lugar y ruta")]
    public TMP_Text fichaNombre;
    public TMP_Text fichaCategoria;
    public TMP_Text fichaDetalle;
    public Button btnComoLlegar;
    public Button btnCerrarFicha;
    public GameObject barraRuta;
    public RectTransform referenciaBarraRuta;      // la barra de ruta aparece justo encima
    public Button btnTerminarRuta;

    [Header("Administrador: gestión de lugares")]
    public Button btnSalirAdmin;
    public Button tabLugares;
    public Button tabReportes;
    public Button btnAgregarLugar;
    public TMP_Text textoCantidadLugares;
    public RectTransform contenedorLugares;
    public GameObject prefabEspacio;

    [Header("Administrador: reportes de uso")]
    public RectTransform contenedorReportes;
    public GameObject prefabEstadistica;
    public TMP_Text textoResumenFecha;
    public Button btnPdf;
    public TMP_Text textoEstadoPdf;

    [Header("Formulario de lugar (crear / editar)")]
    public TMP_Text tituloFormulario;
    public InputField fNombre;
    public InputField fCategoria;
    public InputField fDescripcion;
    public InputField fIndicacion;
    public InputField fHorario;
    public InputField fLatitud;
    public InputField fLongitud;
    public Button btnUsarUbicacion;
    public Button btnGuardarLugar;
    public Button btnCancelarLugar;
    public TMP_Text formError;

    [Header("Avisos")]
    public GameObject fondoAviso;
    public TMP_Text textoAviso;

    [Header("Ajustes")]
    [SerializeField] private int maxResultados = 30;

    [Header("Espacio libre del mapa (se calcula solo; ZonaCentro del diseño)")]
    public RectTransform areaMapa;
    // Altos máximos de la franja libre: tokens compartidos con la integración (UIRapida.Tokens):
    // AltoMaxResultados, AltoBarraRuta, AltoMaxFicha y AltoAviso.

    // ----- Estado -----
    // En la pantalla de usuario solo puede haber UNA capa abierta encima del mapa:
    // abrir una cierra las demás (así nunca se sobreponen).
    private enum Capa { Ninguna, Resultados, Ficha, MenuPerfil }

    private string categoriaFiltro;                  // null = sin filtro; "*" = todas
    private ModelosMapa modelosMapa;
    private FormularioLugarMapa formularioMapa;
    private Button btnModificarMapa;
    private int lugarEditando;                       // 0 = nuevo
    private bool avisoVencimientoDado;               // "Tu sesión vence en 5 minutos." una sola vez por sesión

    private static readonly string[] Meses =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    // ==================================================================
    // Ciclo de vida
    // ==================================================================
    private void Awake()
    {
        AsegurarCamaraEnPantalla();

        // Estilo del mapa (zonas verdes, edificios low-poly): antes de que el mapa cargue
        Mapbox.Unity.Map.AbstractMap mapaCampus = FindFirstObjectByType<Mapbox.Unity.Map.AbstractMap>();
        if (FindFirstObjectByType<EstiloMapa>() == null && mapaCampus != null)
            mapaCampus.gameObject.AddComponent<EstiloMapa>().Configurar(mapaCampus);

        // Edificaciones 3D guardadas por el administrador (se ven también en el modo usuario)
        modelosMapa = FindFirstObjectByType<ModelosMapa>();
        if (modelosMapa == null && mapaCampus != null)
        {
            modelosMapa = new GameObject("ModelosCampus").AddComponent<ModelosMapa>();
            modelosMapa.Configurar(mapaCampus, lugares, ubicacion != null && ubicacion.MapaListo);
        }

        // Menú principal
        Click(btnExplorar, () => ui.Ir(PUsuario));
        Click(btnModoAdmin, AbrirModoAdmin);
        // El aviso "Sesión de administrador cerrada." lo da solo el UIManager (un único lugar)
        // Cierre pedido por el usuario fuera del panel: aquí se confirma (el UIManager solo avisa dentro de admin)
        Click(btnCerrarSesion, () => { CerrarSesionPedida(); ActualizarMenu(); });

        // Login
        Click(btnEntrar, Entrar);
        Click(btnIrRegistro, () => ui.Ir(PRegistro));

        // Registro
        Click(btnRegistrar, Registrar);
        Click(btnRegVolver, () => ui.Atras());

        // Usuario
        if (buscador != null)
        {
            buscador.onValueChanged.AddListener(AlEscribirBusqueda);
            buscador.onSelect.AddListener(_ => CerrarCapasExcepto(Capa.Resultados));   // al tocar el buscador se cierra lo demás
        }
        if (chips != null)
            foreach (Button chip in chips)
            {
                if (chip == null) continue;
                TMP_Text t = chip.GetComponentInChildren<TMP_Text>(true);
                string categoria = t != null ? t.text.Trim() : "";
                Click(chip, () => FiltrarCategoria(categoria));
            }
        Click(btnExplorarAbajo, () => FiltrarCategoria("Todos"));
        Click(btnPerfil, () => MostrarMenuPerfil(CapaAbierta() != Capa.MenuPerfil));   // si se estaba cerrando, se vuelve a abrir
        Click(btnCerrarMenu, () => MostrarMenuPerfil(false));
        Click(btnOpcionPerfil, () => { CerrarCapasExcepto(Capa.Ninguna); AbrirModoAdmin(); });
        Click(btnOpcionMenuPrincipal, () => { CerrarCapasExcepto(Capa.Ninguna); ui.IrRaiz(PMenu); });

        // Ficha y ruta
        Click(btnComoLlegar, () => { if (comoLlegar != null) comoLlegar.MostrarRuta(); CerrarCapasExcepto(Capa.Ninguna); });
        Click(btnCerrarFicha, () => CerrarCapasExcepto(Capa.Ninguna));
        Click(btnTerminarRuta, () => { if (comoLlegar != null) comoLlegar.LimpiarRuta(); });

        // Administrador
        Click(btnSalirAdmin, SalirDeAdmin);   // cierra la sesión y vuelve al menú principal
        Click(tabLugares, RefrescarLugaresAdmin);
        Click(tabReportes, RefrescarReportes);
        Click(btnAgregarLugar, () => AbrirFormulario(null));
        Click(btnPdf, () => { if (isActiveAndEnabled) StartCoroutine(GenerarPdf()); });

        // Formulario
        Click(btnUsarUbicacion, UsarMiUbicacion);
        Click(btnGuardarLugar, GuardarLugar);
        Click(btnCancelarLugar, () => ui.Cerrar(PFormulario));
    }

    private void Start()
    {
        Mostrar(loginError, false);
        Mostrar(loginOk, false);
        Mostrar(regError, false);
        Mostrar(regOk, false);
        if (btnOpcionAjustes != null) btnOpcionAjustes.transform.parent.gameObject.SetActive(false);   // fuera del MVP
        if (plantillaResultado != null) plantillaResultado.gameObject.SetActive(false);
        if (panelResultados != null) panelResultados.SetActive(false);
        if (barraRuta != null) barraRuta.SetActive(false);
        MostrarMenuPerfil(false);
        BuscarAreaMapa();
        CorregirTextosEscena();   // antes de estilizar: el estilo depende del texto final (p. ej. mayúsculas)
        maxResultados = Mathf.Max(maxResultados, 80);   // con los espacios interiores, una categoría puede pasar de 30
        PrepararControlesMapa();
        PrepararEditorMapa();
        ConstruirMenuPerfil();
        EstilizarPantallaUsuario();
        EstilizarFicha();
        EstilizarMenuPrincipal();
        EstilizarPanelAdmin();
        if (textoAviso != null)
        {
            // Los avisos largos se achican en vez de salirse del recuadro
            textoAviso.enableAutoSizing = true;
            textoAviso.fontSizeMin = UIRapida.TamMinimo;
            textoAviso.fontSizeMax = UIRapida.TamCuerpo;
        }
        EstilizarCamposAcceso();
        PrepararEnlaceTerminos();

        if (lugares != null) lugares.LugaresCargados += AlCargarLugares;
        if (admin != null) admin.SesionCambiada += AlCambiarSesion;
        if (ui != null) ui.PantallaCambiada += AlCambiarPantalla;
        if (ui != null) ui.AvisoMostrado += AcomodarAviso;   // cada aviso nuevo se ubica según la pantalla
        if (ui != null) ui.RegistrarManejadorAtras(AtrasEnCapas);
        if (comoLlegar != null)
        {
            comoLlegar.DestinoNoDisponible += AlPerderDestino;
            comoLlegar.FichaAbierta = EstaFichaAbierta;   // para no avisar de un destino que ya nadie mira
        }

        PrepararPantallaMovil();
        ActualizarMenu();
        AplicarFuentesEscena();   // al final: después de estilizar y de corregir los textos
    }

    // Las corrutinas se detienen al desactivar el objeto: se liberan los botones y las banderas
    private void OnDisable()
    {
        generacionAcceso++;   // los callbacks de login/registro pendientes se ignoran al llegar
        if (accesoEnCurso)
        {
            accesoEnCurso = false;
            QuitarProgreso(btnEntrar, textoBotonEntrar);
            QuitarProgreso(btnRegistrar, textoBotonRegistrar);
            textoBotonEntrar = textoBotonRegistrar = null;
        }
        if (generandoPdf)
        {
            generandoPdf = false;
            QuitarProgreso(btnPdf, textoBotonPdf);
            textoBotonPdf = null;
        }
        TerminarCierresAnimados();
        CerrarConfirmacion(animar: false);
    }

    private void OnDestroy()
    {
        if (ui != null)
        {
            ui.QuitarManejadorAtras(AtrasEnCapas);
            ui.PantallaCambiada -= AlCambiarPantalla;
            ui.AvisoMostrado -= AcomodarAviso;
        }
        if (lugares != null) lugares.LugaresCargados -= AlCargarLugares;
        if (admin != null) admin.SesionCambiada -= AlCambiarSesion;
        if (comoLlegar != null)
        {
            comoLlegar.DestinoNoDisponible -= AlPerderDestino;
            comoLlegar.FichaAbierta = null;
        }
        if (camaraControles != null) camaraControles.PerspectivaCambiada -= AlCambiarPerspectiva;
    }

    private bool EstaFichaAbierta() => ui != null && ui.EstaVisible(PFicha);

    // El lugar de la ficha se borró: se cierra la ficha (el aviso lo muestra ComoLlegarController)
    private void AlPerderDestino()
    {
        if (ui != null && ui.EstaVisible(PFicha)) CerrarFichaAnimada();
    }

    private void AlCargarLugares(List<LugaresService.Lugar> _) => AlRecargarDatos();

    private void AlCambiarSesion(bool activa)
    {
        avisoVencimientoDado = false;   // cada sesión nueva puede avisar una vez
        ActualizarMenu();
    }

    /// <summary>Cierra la sesión de administrador y vuelve al menú principal.</summary>
    private void SalirDeAdmin()
    {
        if (admin != null) admin.CerrarSesion();   // el UIManager ya lleva al menú si había sesión
        if (ui != null && ui.PantallaActual != PMenu) ui.IrRaiz(PMenu);
    }

    /// <summary>
    /// "Cerrar sesión" tocado por el usuario (menú principal o menú de perfil). El UIManager solo anuncia el cierre
    /// dentro de las pantallas de administrador; aquí el usuario lo pidió, así que se confirma siempre.
    /// </summary>
    private void CerrarSesionPedida()
    {
        if (admin == null || !admin.EstaAutenticado) return;
        bool anunciado = ui != null && ui.AnunciaCierreDeSesion;
        admin.CerrarSesion();
        if (ui != null && !anunciado) ui.Avisar("Sesión de administrador cerrada.");
    }

    // ----- Microcopy del diseño (la escena no se edita: se corrige al iniciar) -----
    private static readonly Dictionary<string, string> CorreccionesTexto = new Dictionary<string, string>
    {
        { "click par aceptar nuestros terminos y condiciones", "Acepto los términos de uso" },   // mismo nombre que el modal
        { "Acepto los términos y condiciones", "Acepto los términos de uso" },
        { "Catergoria", "Categoría" },
        { "Analitica del campus", "Analítica del campus" },
        { "Busquedas (mes)", "Búsquedas (mes)" },
        { "Iniciar sesion", "Iniciar sesión" },
        // Botones en tipo oración (decisión de microcopia: sin MAYÚSCULAS en botones)
        { "CERRAR SESION", "Cerrar sesión" },   // igual que en el menú de perfil
        { "EXPLORAR CAMPUS", "Explorar campus" },
        { "MODO ADMINISTRADOR", "Modo administrador" },
        { "+ AGREGAR NUEVO LUGAR", "Agregar lugar" },
        { "DESCARGAR REPORTE PDF", "Descargar reporte PDF" },
        { "CÓMO LLEGAR", "Cómo llegar" },
        { "USAR MI UBICACIÓN ACTUAL", "Usar mi ubicación actual" },
        { "TERMINAR", "Terminar" },
        { "ENTRAR", "Entrar" },
        { "GUARDAR", "Guardar" },
        { "CANCELAR", "Cancelar" },
        { "CERRAR", "Cerrar" },
        { "REGISTRO", "Registro" },
        { "No tienes permiso para entrar aqui", "No tienes permiso para entrar aquí." },
        { "Correo o Contraseña Incorrectos", "Usuario o contraseña incorrectos." },
        { "UMNG . Campus Cajica", "UMNG · Campus Cajicá" },
        { "Menu Principal", "Menú principal" },
        { "Reportes de Uso", "Reportes de uso" },
        { "BIENVENIDO", "Bienvenido" },
        { "Inicio Sesion Valido", "Inicio de sesión válido." },
        { "Usuario registrado", "Usuario registrado." },
        { "Usuario no registrado", "Usuario no registrado." },
        { "Espacios del campus", "Lugares del campus" },   // "espacio interior" queda solo para los servicios
        // La regla real (AdminService) pide 8 caracteres con letras y números
        { "Contraseña (mínimo 6 caracteres)", "Contraseña (mín. 8, letras y números)" },
    };

    private static void CorregirTextosEscena()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas) continue;
            foreach (TMP_Text t in c.GetComponentsInChildren<TMP_Text>(true))
            {
                if (TextoCorregido(t.text, out string nuevo)) t.text = nuevo;
                AjustarTamanoEscala(t);   // 45, 68,35, 39,65... de la escena pasan al paso más cercano de la escala
            }
            foreach (Text t in c.GetComponentsInChildren<Text>(true))   // textos antiguos (login y registro)
                if (TextoCorregido(t.text, out string nuevo)) t.text = nuevo;
        }
    }

    // Escala tipográfica de la app (TamTitulo, TamNombreFicha, TamSubtitulo, TamCuerpo, TamSecundario, TamMinimo)
    private static readonly float[] EscalaTipografica =
        { UIRapida.TamTitulo, UIRapida.TamNombreFicha, UIRapida.TamSubtitulo, UIRapida.TamCuerpo, UIRapida.TamSecundario, UIRapida.TamMinimo };

    // Paso más cercano de la escala; en empate gana el mayor (legibilidad). Lo más grande se limita a TamTitulo.
    private static float PasoEscala(float tam)
    {
        float mejor = EscalaTipografica[0], dif = float.MaxValue;
        foreach (float paso in EscalaTipografica)   // de mayor a menor: con '<' estricto el empate queda en el mayor
        {
            float d = Mathf.Abs(paso - tam);
            if (d < dif - 0.01f) { dif = d; mejor = paso; }
        }
        return mejor;
    }

    // Se llama en Start antes de construir la interfaz por código: solo toca los textos de la escena
    private static void AjustarTamanoEscala(TMP_Text t)
    {
        if (t == null) return;
        if (t.enableAutoSizing)
        {
            float min = PasoEscala(t.fontSizeMin), max = PasoEscala(t.fontSizeMax);
            t.fontSizeMin = Mathf.Min(min, max);
            t.fontSizeMax = Mathf.Max(min, max);
        }
        else t.fontSize = PasoEscala(t.fontSize);
    }

    private static bool TextoCorregido(string texto, out string nuevo)
    {
        nuevo = null;
        if (string.IsNullOrEmpty(texto)) return false;
        if (CorreccionesTexto.TryGetValue(texto.Trim(), out nuevo)) return true;
        if (texto.Contains("mas buscados")) { nuevo = texto.Replace("mas buscados", "más buscados"); return true; }
        return false;
    }

    /// <summary>Botón Atrás de Android: cierra primero lo que esté abierto encima del mapa.</summary>
    private bool AtrasEnCapas()
    {
        if (ConfirmacionAbierta) { CerrarConfirmacion(); return true; }   // Atrás = Cancelar
        if (confirmarCerrando) return true;                                 // el modal aún se desvanece: no salir del panel
        if (ui != null && ui.PantallaActual == PFicha)
        {
            CerrarFichaAnimada();   // la misma animación de cierre que la X
            return true;
        }
        if (ui == null || ui.PantallaActual != PUsuario) return false;   // las demás pantallas las cierra el UIManager

        Capa capa = CapaAbierta();
        if (capa == Capa.MenuPerfil || capa == Capa.Resultados)
        {
            CerrarCapasExcepto(Capa.Ninguna);
            return true;
        }
        if (comoLlegar != null && comoLlegar.RutaActiva)
        {
            comoLlegar.LimpiarRuta();
            return true;
        }
        return false;
    }

    /// <summary>App vertical, y el contenido fuera de la cámara frontal y de la barra de gestos.</summary>
    private void PrepararPantallaMovil()
    {
        Application.targetFrameRate = 60;   // Android limita a 30 por defecto: el mapa se mueve más suave
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.orientation = ScreenOrientation.Portrait;

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            ZonaSegura.En(c.gameObject, UIRapida.Verde, Color.white);   // arriba barra de estado verde; abajo blanco (hojas blancas)
        }
    }

    private void Update()
    {
        // El aviso lo muestra y oculta el UIManager (con animación); aquí solo se ubica (evento AvisoMostrado)
        RevisarClima();   // la píldora del clima se ajusta cuando el ClimaController cambia el texto

        // La barra de ruta se ve mientras hay ruta y el mapa está despejado (sin otra capa encima)
        if (barraRuta != null && comoLlegar != null && ui != null)
        {
            bool verBarra = comoLlegar.RutaActiva && ui.PantallaActual == PUsuario && CapaAbierta() == Capa.Ninguna;
            if (barraRuta.activeSelf != verBarra)
            {
                barraRuta.SetActive(verBarra);
                if (verBarra)
                {
                    RectTransform rb = (RectTransform)barraRuta.transform;
                    AcomodarEnFranja(rb, UIRapida.AltoBarraRuta, pegadoArriba: false);
                    animacionBarraRuta = Aparecer(rb, animacionBarraRuta);
                }
            }
        }

        // Búsqueda con espera corta: no se recalcula en cada tecla
        if (busquedaPendiente && Time.unscaledTime >= busquedaEn)
        {
            busquedaPendiente = false;
            ActualizarResultados();
        }

        // Aviso único cuando a la sesión le quedan 5 minutos: solo en una pantalla de administrador
        // (en el mapa no importa); si está en otra, se difiere hasta que entre al panel
        if (admin != null && !avisoVencimientoDado && admin.SesionPorVencer && ui != null && EnPantallaAdmin())
        {
            avisoVencimientoDado = true;
            ui.Avisar("Tu sesión vence en 5 minutos.");
        }
    }

    private bool EnPantallaAdmin()
    {
        string p = ui != null ? ui.PantallaActual : null;
        return p == PAdmin || p == PFormulario || p == EditorMapa.NombrePantalla;
    }

    // ----- Animación de entrada (ficha, barra de ruta, menú de perfil): la misma de toda la app -----
    private Coroutine animacionFicha, animacionBarraRuta;

    private Coroutine Aparecer(RectTransform rt, Coroutine anterior)
    {
        if (anterior != null) StopCoroutine(anterior);
        if (rt == null) return null;
        CanvasGroup g = rt.GetComponent<CanvasGroup>();
        if (g == null) g = rt.gameObject.AddComponent<CanvasGroup>();
        if (!isActiveAndEnabled) { UIRapida.TerminarAparicion(rt, g); return null; }
        return StartCoroutine(UIRapida.Aparecer(rt, g));
    }

    private void AlCambiarPantalla(string pantalla)
    {
        // Navegar por las pantallas de administración cuenta como uso (la sesión vence tras 15 min sin uso)
        if (admin != null && EnPantallaAdmin()) admin.RegistrarActividad();

        if (pantalla == PLogin)
        {
            Mostrar(loginError, false);
            Mostrar(loginOk, false);
            if (loginLlave != null) loginLlave.text = "";
            OcultarContrasenas();
            MostrarAvisoPendienteBaseDatos();
        }
        else if (pantalla == PRegistro)
        {
            Mostrar(regError, false);
            Mostrar(regOk, false);
            OcultarContrasenas();
        }
        else if (pantalla == PAdmin)
        {
            RefrescarLugaresAdmin();
            RefrescarReportes();
            ActualizarPestanas();
        }
        else if (pantalla == PMenu)
        {
            ActualizarMenu();
        }
        if (pantalla != PAdmin) CerrarConfirmacion(animar: false);   // p. ej. la sesión venció con el modal abierto
        if (ui != null && ui.AvisoVisible) AcomodarAviso();   // un aviso visible se reubica para la pantalla nueva

        // Fuera del mapa la ruta se pausa (sin línea ni pin en el editor/formulario, pantalla con apagado normal)
        bool enMapa = pantalla == PUsuario || pantalla == PFicha;
        if (comoLlegar != null) comoLlegar.Pausar(!enMapa);

        // Al salir del mapa no queda nada abierto para la próxima vez
        if (!enMapa)
        {
            MostrarMenuPerfil(false);
            OcultarResultados();
        }
    }

    // ==================================================================
    // Capas de la pantalla de usuario (solo una a la vez)
    // ==================================================================
    private Capa CapaAbierta()
    {
        if (ui != null && ui.EstaVisible(PFicha)) return Capa.Ficha;
        if (menuPerfil != null && menuPerfil.activeSelf && cierreMenu == null) return Capa.MenuPerfil;   // cerrándose = ya cerrado
        if (panelResultados != null && panelResultados.activeSelf) return Capa.Resultados;
        return Capa.Ninguna;
    }

    /// <summary>Cierra todas las capas del mapa menos la indicada (Ninguna = cierra todas).</summary>
    private void CerrarCapasExcepto(Capa queda)
    {
        if (queda != Capa.Resultados) OcultarResultados();
        if (queda != Capa.MenuPerfil) MostrarMenuPerfil(false);
        if (queda != Capa.Ficha && ui != null && ui.EstaVisible(PFicha)) CerrarFichaAnimada();
    }

    private void AlRecargarDatos()
    {
        indiceBusquedaListo = false;   // los textos normalizados se recalculan con los datos nuevos
        if (ui == null) return;
        if (ui.EstaVisible(PAdmin)) { RefrescarLugaresAdmin(); RefrescarReportes(); }
        if (panelResultados != null && panelResultados.activeSelf) ActualizarResultados();
        if (EstaFichaAbierta() && cierreFicha == null) RefrescarFichaAbierta();   // cerrándose: no se vuelve a llenar
    }

    // La ficha abierta se vuelve a llenar con los datos nuevos (si el lugar se borró la cierra
    // ComoLlegarController; si solo se borró el servicio, queda la ficha del lugar)
    private void RefrescarFichaAbierta()
    {
        if (lugares == null || fichaLugarId == 0) return;
        LugaresService.Lugar l = lugares.BuscarPorId(fichaLugarId);
        if (l == null) return;
        LugaresService.Servicio s = null;
        if (fichaServicioId != 0)
            foreach (LugaresService.Servicio x in lugares.Servicios)
                if (x.id == fichaServicioId) { s = x; break; }
        MostrarFicha(l, s);
    }

    // ==================================================================
    // Utilidades
    // ==================================================================

    /// <summary>
    /// Los canvas de la interfaz dibujan en Display 1 (la pantalla del celular). Si ninguna cámara
    /// base dibuja ahí, nadie limpia la imagen: cada panel queda pintado encima del anterior
    /// y el mapa no se ve. En ese caso se pasa la cámara base a Display 1.
    /// </summary>
    private static void AsegurarCamaraEnPantalla()
    {
        Camera[] camaras = Camera.allCameras;   // solo las activas
        foreach (Camera c in camaras)
            if (c.targetTexture == null && c.targetDisplay == 0 && EsCamaraBase(c)) return;

        foreach (Camera c in camaras)
        {
            if (c.targetTexture != null || !EsCamaraBase(c)) continue;
            Debug.LogWarning($"'{c.name}' dibujaba en Display {c.targetDisplay + 1} y la interfaz está en Display 1: " +
                             "se pasa a Display 1. Ajústalo en su Inspector (Output > Target Display) para dejarlo guardado.");
            c.targetDisplay = 0;
            return;
        }
        Debug.LogError("No hay ninguna cámara activa que dibuje en la pantalla (Display 1).");
    }

    private static bool EsCamaraBase(Camera c)
    {
        var datos = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        return datos == null || datos.renderType == UnityEngine.Rendering.Universal.CameraRenderType.Base;
    }

    private static void Click(Button b, UnityAction accion)
    {
        if (b != null) b.onClick.AddListener(accion);
    }

    private static void Mostrar(Component c, bool ver)
    {
        if (c != null) c.gameObject.SetActive(ver);
    }

    private static void Error(TMP_Text t, string mensaje)
    {
        if (t == null) { Debug.LogWarning(mensaje); return; }
        t.text = mensaje;
        t.gameObject.SetActive(true);
    }

    private static void Poner(InputField f, string valor)
    {
        if (f != null) f.text = valor ?? "";
    }

    private static string Texto(InputField f)
    {
        if (f == null) return null;
        string t = f.text.Trim();
        return t.Length == 0 ? null : t;
    }

    private static bool LeerNumero(InputField f, out double valor)
    {
        valor = 0;
        if (f == null) return false;
        return double.TryParse(f.text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
    }

    // Búsqueda en profundidad sin crear arreglos (incluye la raíz y los hijos inactivos)
    private static Transform BuscarHijo(Transform raiz, string nombre)
    {
        if (raiz == null) return null;
        if (raiz.name == nombre) return raiz;
        for (int i = 0; i < raiz.childCount; i++)
        {
            Transform t = BuscarHijo(raiz.GetChild(i), nombre);
            if (t != null) return t;
        }
        return null;
    }

    // RectOffset con la escala de espaciado (Esp1/Esp2/Esp3/Margen)
    private static RectOffset Relleno(float izq, float der, float arriba, float abajo) =>
        new RectOffset(Mathf.RoundToInt(izq), Mathf.RoundToInt(der), Mathf.RoundToInt(arriba), Mathf.RoundToInt(abajo));

    private static void PonerTexto(Transform raiz, string hijo, string texto)
    {
        Transform t = BuscarHijo(raiz, hijo);
        TMP_Text tmp = t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        if (tmp != null) tmp.text = texto ?? "";
    }
}
