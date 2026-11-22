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
///   Menú principal ─┬─ EXPLORAR CAMPUS ──► Usuario (mapa, buscador, ficha, ruta)
///                   ├─ MODO ADMINISTRADOR ► Login (usuario + contraseña) ─► Panel de administración
///                   │                        └─ Registro (usuario + contraseña + código)
///                   └─ CERRAR SESIÓN (solo con sesión de administrador)
///
/// Las referencias las asigna la herramienta Campus > Integrar InterfazGrafica en Santiago.
/// Los listeners se agregan por código en Awake (no hay OnClick configurados a mano).
/// </summary>
public class InterfazCampus : MonoBehaviour
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
    [SerializeField] private float segundosConfirmarEliminar = 3f;

    [Header("Espacio libre del mapa (se calcula solo; ZonaCentro del diseño)")]
    public RectTransform areaMapa;
    [SerializeField] private float altoMaxResultados = 900f;
    [SerializeField] private float altoMaxFicha = 820f;
    [SerializeField] private float altoBarraRuta = 190f;
    [SerializeField] private float altoAviso = 120f;

    // ----- Estado -----
    // En la pantalla de usuario solo puede haber UNA capa abierta encima del mapa:
    // abrir una cierra las demás (así nunca se sobreponen).
    private enum Capa { Ninguna, Resultados, Ficha, MenuPerfil }

    private string categoriaFiltro;                  // null = sin filtro; "*" = todas
    private string ultimoAviso;
    private ModelosMapa modelosMapa;
    private Button btnModificarMapa;
    private int lugarEditando;                       // 0 = nuevo
    private Button eliminarPendiente;
    private string textoEliminarOriginal;
    private float eliminarHasta;
    private readonly List<GameObject> filasResultados = new List<GameObject>();

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
        Click(btnCerrarSesion, () => { admin.CerrarSesion(); ui.Avisar("Sesión de administrador cerrada."); ActualizarMenu(); });

        // Login
        Click(btnEntrar, Entrar);
        Click(btnIrRegistro, () => ui.Ir(PRegistro));

        // Registro
        Click(btnRegistrar, Registrar);
        Click(btnRegVolver, () => ui.Atras());

        // Usuario
        if (buscador != null)
        {
            buscador.onValueChanged.AddListener(_ => { if (buscador.text.Length > 0) categoriaFiltro = null; ActualizarResultados(); });
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
        Click(btnPerfil, () => MostrarMenuPerfil(menuPerfil == null || !menuPerfil.activeSelf));
        Click(btnCerrarMenu, () => MostrarMenuPerfil(false));
        Click(btnOpcionPerfil, () => { CerrarCapasExcepto(Capa.Ninguna); AbrirModoAdmin(); });
        Click(btnOpcionMenuPrincipal, () => { CerrarCapasExcepto(Capa.Ninguna); ui.IrRaiz(PMenu); });

        // Ficha y ruta
        Click(btnComoLlegar, () => { comoLlegar.MostrarRuta(); CerrarCapasExcepto(Capa.Ninguna); });
        Click(btnCerrarFicha, () => CerrarCapasExcepto(Capa.Ninguna));
        Click(btnTerminarRuta, () => comoLlegar.LimpiarRuta());

        // Administrador
        Click(btnSalirAdmin, () => admin.CerrarSesion());   // el UIManager vuelve al menú
        Click(tabLugares, RefrescarLugaresAdmin);
        Click(tabReportes, RefrescarReportes);
        Click(btnAgregarLugar, () => AbrirFormulario(null));
        Click(btnPdf, GenerarPdf);

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
            textoAviso.fontSizeMin = 20;
            textoAviso.fontSizeMax = 32;
        }
        if (loginLlave != null) loginLlave.contentType = InputField.ContentType.Password;
        if (regLlave != null) regLlave.contentType = InputField.ContentType.Password;

        if (lugares != null) lugares.LugaresCargados += _ => AlRecargarDatos();
        if (admin != null) admin.SesionCambiada += _ => ActualizarMenu();
        if (ui != null) ui.PantallaCambiada += AlCambiarPantalla;

        ActualizarMenu();
    }

    private void Update()
    {
        // El fondo del aviso solo se ve cuando hay texto; cada aviso nuevo se ubica según la pantalla
        if (fondoAviso != null && textoAviso != null)
        {
            bool hayTexto = !string.IsNullOrEmpty(textoAviso.text);
            if (hayTexto && textoAviso.text != ultimoAviso) AcomodarAviso();
            ultimoAviso = hayTexto ? textoAviso.text : null;
            if (fondoAviso.activeSelf != hayTexto) fondoAviso.SetActive(hayTexto);
        }

        // La barra de ruta se ve mientras hay ruta y el mapa está despejado (sin otra capa encima)
        if (barraRuta != null && comoLlegar != null && ui != null)
        {
            bool verBarra = comoLlegar.RutaActiva && ui.PantallaActual == PUsuario && CapaAbierta() == Capa.Ninguna;
            if (barraRuta.activeSelf != verBarra)
            {
                barraRuta.SetActive(verBarra);
                if (verBarra) AcomodarEnFranja((RectTransform)barraRuta.transform, altoBarraRuta, pegadoArriba: false);
            }
        }

        // Si no se confirmó a tiempo, el botón Eliminar vuelve a su texto
        if (eliminarPendiente != null && Time.unscaledTime > eliminarHasta) CancelarEliminar();
    }

    private void AlCambiarPantalla(string pantalla)
    {
        if (pantalla == PLogin)
        {
            Mostrar(loginError, false);
            Mostrar(loginOk, false);
            if (loginLlave != null) loginLlave.text = "";
        }
        else if (pantalla == PRegistro)
        {
            Mostrar(regError, false);
            Mostrar(regOk, false);
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

        // Al salir del mapa no queda nada abierto para la próxima vez
        if (pantalla != PUsuario && pantalla != PFicha)
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
        if (menuPerfil != null && menuPerfil.activeSelf) return Capa.MenuPerfil;
        if (panelResultados != null && panelResultados.activeSelf) return Capa.Resultados;
        return Capa.Ninguna;
    }

    /// <summary>Cierra todas las capas del mapa menos la indicada (Ninguna = cierra todas).</summary>
    private void CerrarCapasExcepto(Capa queda)
    {
        if (queda != Capa.Resultados) OcultarResultados();
        if (queda != Capa.MenuPerfil) MostrarMenuPerfil(false);
        if (queda != Capa.Ficha && ui != null && ui.EstaVisible(PFicha)) ui.Cerrar(PFicha);
    }

    private void AlRecargarDatos()
    {
        if (ui == null) return;
        if (ui.EstaVisible(PAdmin)) { RefrescarLugaresAdmin(); RefrescarReportes(); }
        if (panelResultados != null && panelResultados.activeSelf) ActualizarResultados();
    }

    // ==================================================================
    // Menú principal y login
    // ==================================================================
    private void ActualizarMenu()
    {
        if (btnCerrarSesion != null) btnCerrarSesion.gameObject.SetActive(admin != null && admin.EstaAutenticado);
    }

    private void AbrirModoAdmin()
    {
        if (admin != null && admin.EstaAutenticado) ui.Ir(PAdmin);
        else ui.Ir(PLogin);
    }

    private void Entrar()
    {
        string usuario = loginUsuario != null ? loginUsuario.text.Trim() : "";
        string llave = loginLlave != null ? loginLlave.text : "";
        Mostrar(loginOk, false);

        if (usuario.Length == 0 || llave.Length == 0)
        {
            Error(loginError, "Escribe tu usuario y tu contraseña.");
            return;
        }

        btnEntrar.interactable = false;
        admin.IniciarSesion(usuario, llave, (ok, mensaje) =>
        {
            btnEntrar.interactable = true;
            loginLlave.text = "";   // la contraseña nunca se queda escrita

            if (!ok) { Error(loginError, mensaje); return; }

            Mostrar(loginError, false);
            ui.Reemplazar(PAdmin);   // el login no queda en el historial
            ui.Avisar($"Bienvenido, {usuario}.");
        });
    }

    private void Registrar()
    {
        Mostrar(regOk, false);

        if (regTerminos != null && !regTerminos.isOn)
        {
            Error(regError, "Debes aceptar los términos y condiciones.");
            return;
        }

        string usuario = regUsuario != null ? regUsuario.text.Trim() : "";
        btnRegistrar.interactable = false;

        admin.Registrar(usuario, regLlave.text, regCodigo.text, (ok, mensaje) =>
        {
            btnRegistrar.interactable = true;
            regLlave.text = "";

            if (!ok) { Error(regError, mensaje); return; }

            Mostrar(regError, false);
            regOk.text = mensaje;
            Mostrar(regOk, true);
            regCodigo.text = "";
            regUsuario.text = "";
            if (regTerminos != null) regTerminos.isOn = false;
            if (loginUsuario != null) loginUsuario.text = usuario;   // listo para entrar
            StartCoroutine(VolverAlLoginLuego());
        });
    }

    private IEnumerator VolverAlLoginLuego()
    {
        yield return new WaitForSeconds(1.8f);
        if (ui.PantallaActual == PRegistro) ui.Atras();
    }

    // ==================================================================
    // Usuario: buscador y resultados
    // ==================================================================
    private void FiltrarCategoria(string categoria)
    {
        string nuevo = Normalizar(categoria) == "todos" ? "*" : categoria;

        // Tocar otra vez la misma categoría cierra la lista
        if (panelResultados != null && panelResultados.activeSelf && categoriaFiltro == nuevo)
        {
            OcultarResultados();
            return;
        }

        categoriaFiltro = nuevo;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        ActualizarResultados();
    }

    private void OcultarResultados()
    {
        categoriaFiltro = null;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        if (panelResultados != null) panelResultados.SetActive(false);
        ActualizarChips();
    }

    private void ActualizarResultados()
    {
        if (panelResultados == null || lugares == null) return;

        string q = Normalizar(buscador != null ? buscador.text : "");
        if (q.Length == 0 && categoriaFiltro == null)
        {
            panelResultados.SetActive(false);
            ActualizarChips();
            return;
        }

        foreach (GameObject f in filasResultados) Destroy(f);
        filasResultados.Clear();

        int n = 0;
        foreach (LugaresService.Lugar l in lugares.Lugares)
        {
            if (n >= maxResultados) break;
            if (!CoincideCategoria(l.categoria)) continue;
            if (q.Length > 0 && !Contiene(q, l.nombre, l.categoria, l.descripcion)) continue;

            LugaresService.Lugar lugar = l;
            AgregarFila($"<b>{l.nombre}</b>\n<size=75%><color=#5d6b62>{l.categoria}</color></size>",
                        () => ElegirResultado(lugar, null));
            n++;
        }

        // Servicios (aulas, biblioteca, laboratorios, oficinas...): al escribir, o al elegir una categoría
        // que coincida con la suya (por ejemplo "Biblioteca" muestra la biblioteca del Camacho Leiva)
        bool porCategoria = q.Length == 0 && categoriaFiltro != null && categoriaFiltro != "*";
        if (q.Length > 0 || porCategoria)
        {
            foreach (LugaresService.Servicio s in lugares.Servicios)
            {
                if (n >= maxResultados) break;
                LugaresService.Lugar dueño = lugares.BuscarPorId(s.lugar_id);
                if (dueño == null) continue;
                if (porCategoria) { if (!CoincideCategoria(s.categoria)) continue; }
                else if (!CoincideCategoria(dueño.categoria) && !CoincideCategoria(s.categoria)) continue;
                if (q.Length > 0 && !Contiene(q, s.nombre, s.categoria)) continue;

                LugaresService.Servicio servicio = s;
                string piso = string.IsNullOrEmpty(s.piso) ? "" : $" · piso {s.piso}";
                AgregarFila($"<b>{s.nombre}</b>\n<size=75%><color=#5d6b62>{s.categoria} en {dueño.nombre}{piso}</color></size>",
                            () => ElegirResultado(dueño, servicio));
                n++;
            }
        }

        if (textoSinResultados != null)
        {
            textoSinResultados.gameObject.SetActive(n == 0);
            textoSinResultados.text = "No encontramos lugares con ese nombre.";
            textoSinResultados.transform.SetAsLastSibling();
        }

        CerrarCapasExcepto(Capa.Resultados);
        panelResultados.SetActive(true);
        panelResultados.transform.SetAsLastSibling();   // por encima de todo lo del mapa
        AcomodarEnFranja((RectTransform)panelResultados.transform, altoMaxResultados, pegadoArriba: true);
        ActualizarChips();
    }

    private void AgregarFila(string texto, UnityAction alElegir)
    {
        Button fila = Instantiate(plantillaResultado, contenedorResultados);
        fila.gameObject.SetActive(true);
        fila.GetComponentInChildren<TMP_Text>(true).text = texto;
        fila.onClick.AddListener(alElegir);
        filasResultados.Add(fila.gameObject);
    }

    private bool CoincideCategoria(string categoria)
    {
        if (categoriaFiltro == null || categoriaFiltro == "*") return true;
        string a = Normalizar(categoria), b = Normalizar(categoriaFiltro);
        // "Aulas" coincide con "Aula", "Cafetería" con "Cafeteria"...
        return a == b || a.StartsWith(b.TrimEnd('s')) || b.StartsWith(a.TrimEnd('s'));
    }

    private void ElegirResultado(LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
        OcultarResultados();

        // Igual que el buscador: registra la búsqueda y, si hay una ruta, la cambia al nuevo destino
        if (servicio != null) comoLlegar.SeleccionarServicio(servicio);
        else comoLlegar.SeleccionarLugar(lugar);

        MostrarFicha(lugar, servicio);
    }

    // ==================================================================
    // Usuario: ficha
    // ==================================================================
    private void MostrarFicha(LugaresService.Lugar l, LugaresService.Servicio s)
    {
        if (fichaNombre != null) fichaNombre.text = s != null ? s.nombre : l.nombre;
        if (fichaCategoria != null)
            fichaCategoria.text = (s != null ? $"{s.categoria} · {l.nombre}" : l.categoria).ToUpperInvariant();

        List<LugaresService.Servicio> servicios = lugares.ServiciosDe(l.id);
        string descripcion = s != null && !string.IsNullOrEmpty(s.descripcion) ? s.descripcion : l.descripcion;
        string horario = s != null && !string.IsNullOrEmpty(s.horario) ? s.horario : l.horario;
        string encontrarlo = !string.IsNullOrEmpty(l.indicacion) && l.indicacion != "--" ? l.indicacion : null;

        if (fichaFilas != null)
        {
            // Ficha con filas: cada dato con su ícono y título; los datos vacíos no aparecen
            if (fichaDetalle != null)
            {
                fichaDetalle.text = descripcion ?? "";
                fichaDetalle.gameObject.SetActive(!string.IsNullOrEmpty(descripcion));
            }
            for (int k = fichaFilas.childCount - 1; k >= 0; k--) Destroy(fichaFilas.GetChild(k).gameObject);

            if (s != null && !string.IsNullOrEmpty(s.piso)) FilaFicha("capas", "Piso", s.piso);
            if (s != null && !string.IsNullOrEmpty(s.indicacion)) FilaFicha("puerta", "Dentro del lugar", s.indicacion);
            if (!string.IsNullOrEmpty(horario)) FilaFicha("reloj", "Horario", horario);
            if (encontrarlo != null) FilaFicha("puerta", s != null ? $"Cómo llegar a {l.nombre}" : "Cómo encontrarlo", encontrarlo);
            if (s == null && servicios.Count > 0)
                FilaFicha("lista", "Aquí encuentras", string.Join(", ", servicios.ConvertAll(x => x.nombre)));

            if (fichaScroll != null) fichaScroll.verticalNormalizedPosition = 1f;   // siempre desde arriba
        }
        else if (fichaDetalle != null)
        {
            // Respaldo: todo en un solo texto (si la ficha no se pudo reorganizar)
            StringBuilder d = new StringBuilder();
            if (!string.IsNullOrEmpty(descripcion)) d.AppendLine(descripcion);
            if (s != null && !string.IsNullOrEmpty(s.piso)) d.AppendLine($"<b>Piso:</b> {s.piso}");
            if (s != null && !string.IsNullOrEmpty(s.indicacion)) d.AppendLine($"<b>Dentro del lugar:</b> {s.indicacion}");
            if (!string.IsNullOrEmpty(horario)) d.AppendLine($"<b>Horario:</b> {horario}");
            if (encontrarlo != null) d.AppendLine($"<b>Cómo encontrarlo:</b> {encontrarlo}");
            if (s == null && servicios.Count > 0) d.AppendLine($"<b>Aquí encuentras:</b> {string.Join(", ", servicios.ConvertAll(x => x.nombre))}");
            fichaDetalle.text = d.ToString().Trim();
        }

        CerrarCapasExcepto(Capa.Ficha);
        if (fichaRaiz != null) AcomodarEnFranja(fichaRaiz, altoMaxFicha, pegadoArriba: false);
        if (!ui.EstaVisible(PFicha)) ui.Ir(PFicha);   // si ya estaba abierta, solo se actualiza su contenido
    }

    // ==================================================================
    // Diseño de la ficha del lugar (se reorganiza al iniciar sobre la ficha creada por la integración)
    // ==================================================================
    private RectTransform fichaRaiz;
    private RectTransform fichaFilas;
    private ScrollRect fichaScroll;
    private TMP_FontAsset fuenteFicha;

    private void EstilizarFicha()
    {
        if (fichaNombre == null || fichaCategoria == null || fichaDetalle == null) return;
        fichaRaiz = (RectTransform)fichaNombre.transform.parent;
        fuenteFicha = fichaDetalle.font != null ? fichaDetalle.font : fichaNombre.font;

        // ---------- Tarjeta: sombra + cara blanca redondeada (la raíz queda invisible) ----------
        Image raiz = fichaRaiz.GetComponent<Image>();
        if (raiz != null) raiz.color = new Color(1, 1, 1, 0);
        fichaRaiz.sizeDelta = new Vector2(-36f, fichaRaiz.sizeDelta.y);   // margen a los lados

        Image sombra = UIRapida.Panel("Sombra", fichaRaiz, new Color(0f, 0f, 0f, 0.16f));
        UIRapida.Redondear(sombra, 40f);
        sombra.raycastTarget = false;
        sombra.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(sombra.rectTransform, -6, -10, -6, 2);
        sombra.transform.SetAsFirstSibling();

        Image cara = UIRapida.Panel("Cara", fichaRaiz, Color.white);
        UIRapida.Redondear(cara, 36f);
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        cara.transform.SetSiblingIndex(1);

        Image asa = UIRapida.Panel("Asa", fichaRaiz, new Color(0.82f, 0.86f, 0.83f, 1f));
        UIRapida.Redondear(asa, 6f);
        asa.raycastTarget = false;
        asa.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Anclar(asa.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(90, 10));
        asa.transform.SetSiblingIndex(2);

        VerticalLayoutGroup v = fichaRaiz.GetComponent<VerticalLayoutGroup>();
        if (v != null)
        {
            v.padding = new RectOffset(38, 38, 44, 32);
            v.spacing = 16;
        }

        // ---------- Encabezado: ícono · nombre + etiqueta de categoría · botón cerrar ----------
        RectTransform encabezado = UIRapida.Rect("Encabezado", fichaRaiz);
        encabezado.SetSiblingIndex(3);
        encabezado.gameObject.AddComponent<LayoutElement>().preferredHeight = 116;
        HorizontalLayoutGroup he = encabezado.gameObject.AddComponent<HorizontalLayoutGroup>();
        he.spacing = 22;
        he.childAlignment = TextAnchor.MiddleLeft;
        he.childControlWidth = he.childControlHeight = true;
        he.childForceExpandWidth = he.childForceExpandHeight = false;

        Image circulo = UIRapida.Panel("IconoLugar", encabezado, UIRapida.Menta);
        circulo.sprite = UIRapida.Circulo();
        circulo.preserveAspect = true;
        circulo.raycastTarget = false;
        LayoutElement lc = circulo.gameObject.AddComponent<LayoutElement>();
        lc.preferredWidth = lc.preferredHeight = 92;
        Image pin = UIRapida.Panel("Pin", circulo.transform, UIRapida.Verde);
        pin.sprite = UIRapida.Icono("pin");
        pin.raycastTarget = false;
        UIRapida.Estirar(pin.rectTransform, 20, 18, 20, 20);

        RectTransform textos = UIRapida.Rect("Textos", encabezado);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup vt = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        vt.spacing = 8;
        vt.childAlignment = TextAnchor.MiddleLeft;
        vt.childControlWidth = vt.childControlHeight = true;
        vt.childForceExpandWidth = false;
        vt.childForceExpandHeight = false;

        fichaNombre.transform.SetParent(textos, false);
        QuitarAlto(fichaNombre.gameObject);
        fichaNombre.enableAutoSizing = true;
        fichaNombre.fontSizeMin = 30;
        fichaNombre.fontSizeMax = 44;
        fichaNombre.textWrappingMode = TextWrappingModes.Normal;
        fichaNombre.overflowMode = TextOverflowModes.Ellipsis;
        fichaNombre.maxVisibleLines = 2;

        // Etiqueta de categoría: fondo menta redondeado que se ajusta al texto
        Image etiqueta = UIRapida.Panel("Etiqueta", textos, UIRapida.Menta);
        UIRapida.Redondear(etiqueta, 12f);
        etiqueta.raycastTarget = false;
        HorizontalLayoutGroup hl = etiqueta.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(16, 16, 6, 6);
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        etiqueta.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fichaCategoria.transform.SetParent(etiqueta.transform, false);
        QuitarAlto(fichaCategoria.gameObject);
        fichaCategoria.enableAutoSizing = false;
        fichaCategoria.fontSize = 22;
        fichaCategoria.fontStyle = FontStyles.Bold;
        fichaCategoria.color = UIRapida.VerdeClaro;
        fichaCategoria.characterSpacing = 4f;
        fichaCategoria.textWrappingMode = TextWrappingModes.NoWrap;

        Image cerrar = UIRapida.Panel("BotonCerrarX", encabezado, UIRapida.Menta);
        cerrar.sprite = UIRapida.Circulo();
        cerrar.preserveAspect = true;
        LayoutElement lx = cerrar.gameObject.AddComponent<LayoutElement>();
        lx.preferredWidth = lx.preferredHeight = 76;
        Button bx = cerrar.gameObject.AddComponent<Button>();
        bx.targetGraphic = cerrar;
        bx.onClick.AddListener(() => CerrarCapasExcepto(Capa.Ninguna));
        Image x = UIRapida.Panel("X", cerrar.transform, UIRapida.Gris);
        x.sprite = UIRapida.Icono("eliminar");
        x.raycastTarget = false;
        UIRapida.Estirar(x.rectTransform, 22, 22, 22, 22);

        // ---------- Separador ----------
        Image linea = UIRapida.Panel("Separador", fichaRaiz, new Color(0.88f, 0.92f, 0.89f, 1f));
        linea.raycastTarget = false;
        linea.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;
        linea.transform.SetSiblingIndex(encabezado.GetSiblingIndex() + 1);

        // ---------- Contenido desplazable: descripción + filas ----------
        RectTransform vista = UIRapida.Rect("Contenido", fichaRaiz);
        vista.SetSiblingIndex(linea.transform.GetSiblingIndex() + 1);
        LayoutElement lv = vista.gameObject.AddComponent<LayoutElement>();
        lv.flexibleHeight = 1;
        lv.minHeight = 120;
        vista.gameObject.AddComponent<RectMask2D>();
        fichaScroll = vista.gameObject.AddComponent<ScrollRect>();

        RectTransform info = UIRapida.Rect("Info", vista);
        UIRapida.Anclar(info, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vi = info.gameObject.AddComponent<VerticalLayoutGroup>();
        vi.spacing = 26;
        vi.childControlWidth = vi.childControlHeight = true;
        vi.childForceExpandWidth = true;
        vi.childForceExpandHeight = false;
        info.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fichaScroll.viewport = vista;
        fichaScroll.content = info;
        fichaScroll.horizontal = false;
        fichaScroll.movementType = ScrollRect.MovementType.Clamped;
        fichaScroll.scrollSensitivity = 30;

        fichaDetalle.transform.SetParent(info, false);
        QuitarAlto(fichaDetalle.gameObject);
        fichaDetalle.enableAutoSizing = false;
        fichaDetalle.fontSize = 40;
        fichaDetalle.lineSpacing = 4f;
        fichaDetalle.color = UIRapida.Oscuro;
        fichaDetalle.overflowMode = TextOverflowModes.Overflow;
        fichaDetalle.alignment = TextAlignmentOptions.TopLeft;

        fichaFilas = UIRapida.Rect("Filas", info);
        VerticalLayoutGroup vf = fichaFilas.gameObject.AddComponent<VerticalLayoutGroup>();
        vf.spacing = 24;
        vf.childControlWidth = vf.childControlHeight = true;
        vf.childForceExpandWidth = true;
        vf.childForceExpandHeight = false;

        // ---------- Botón principal ----------
        if (btnCerrarFicha != null) btnCerrarFicha.gameObject.SetActive(false);   // ahora cierra la X de arriba
        if (btnComoLlegar != null)
        {
            EstiloBotonPrincipal(btnComoLlegar, fichaNombre.font);
            Image icono = UIRapida.Panel("IconoRuta", btnComoLlegar.transform, Color.white);
            icono.sprite = UIRapida.Icono("pin");
            icono.preserveAspect = true;
            icono.raycastTarget = false;
            UIRapida.Anclar(icono.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(54, 54));

            LayoutElement fila = btnComoLlegar.transform.parent.GetComponent<LayoutElement>();
            if (fila != null) fila.preferredHeight = fila.minHeight = 112;
        }
    }

    // Fila de dato: ícono verde + título pequeño en mayúsculas + valor
    private void FilaFicha(string icono, string titulo, string valor)
    {
        RectTransform fila = UIRapida.Rect("Fila " + titulo, fichaFilas);
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 24;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        Image img = UIRapida.Panel("Icono", fila, UIRapida.Verde);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        LayoutElement li = img.gameObject.AddComponent<LayoutElement>();
        li.preferredWidth = li.preferredHeight = li.minWidth = 62;

        RectTransform textos = UIRapida.Rect("Textos", fila);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup v = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        TMP_Text t = UIRapida.Texto(textos, "Titulo", titulo.ToUpperInvariant(), 28, UIRapida.Gris, fuenteFicha, true);
        t.characterSpacing = 4f;
        UIRapida.Texto(textos, "Valor", valor, 38, UIRapida.Oscuro, fuenteFicha);
    }

    // Quita las alturas fijas que la integración había puesto (el texto decide su alto)
    private static void QuitarAlto(GameObject go)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) return;
        le.minHeight = le.preferredHeight = -1;
        le.flexibleHeight = -1;
    }

    private void MostrarMenuPerfil(bool ver)
    {
        if (ver) CerrarCapasExcepto(Capa.MenuPerfil);
        if (menuPerfil != null) menuPerfil.SetActive(ver);
        if (zonaCerrarMenu != null) zonaCerrarMenu.SetActive(ver);
        if (!ver || tarjetaPerfil == null) return;

        // Menú nuevo: datos de la sesión, debajo del botón de perfil, encima de todo, con animación
        ActualizarMenuPerfil();
        zonaCerrarMenu.transform.SetAsLastSibling();
        tarjetaPerfil.SetAsLastSibling();
        ColocarBajoPerfil();
        if (animacionMenu != null) StopCoroutine(animacionMenu);
        animacionMenu = StartCoroutine(AnimarMenu());
    }

    // ==================================================================
    // Estilo de la pantalla de usuario: buscador, categorías, barra de ruta y "Explorar campus"
    // (se aplica sobre los objetos del diseño al iniciar)
    // ==================================================================
    private static readonly Color ColorTextoAyuda = new Color(0.45f, 0.50f, 0.47f, 1f);
    private readonly List<(Image fondo, TMP_Text texto, string categoria)> chipsEstilo =
        new List<(Image, TMP_Text, string)>();

    private void EstilizarPantallaUsuario()
    {
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        // ---------- Buscador: píldora blanca ----------
        if (buscador != null)
        {
            Image fondo = buscador.targetGraphic as Image;
            if (fondo != null)
            {
                UIRapida.Redondear(fondo, 48f);
                fondo.color = new Color(1f, 1f, 1f, 0.97f);
            }
            if (buscador.placeholder is TMP_Text ayuda)
            {
                ayuda.color = ColorTextoAyuda;
                ayuda.fontStyle = FontStyles.Normal;
                AjustarTextoCampo((RectTransform)ayuda.transform);
            }
            if (buscador.textComponent != null)
            {
                buscador.textComponent.color = UIRapida.Oscuro;
                AjustarTextoCampo((RectTransform)buscador.textComponent.rectTransform);
            }
            buscador.caretColor = UIRapida.Verde;
            buscador.customCaretColor = true;
            buscador.selectionColor = new Color(UIRapida.Verde.r, UIRapida.Verde.g, UIRapida.Verde.b, 0.25f);
        }

        // ---------- Categorías: píldoras; la activa en verde ----------
        chipsEstilo.Clear();
        if (chips != null)
        {
            foreach (Button chip in chips)
            {
                if (chip == null) continue;
                Image fondo = chip.targetGraphic as Image;
                TMP_Text texto = chip.GetComponentInChildren<TMP_Text>(true);
                if (fondo == null || texto == null) continue;

                UIRapida.Redondear(fondo, 14f);   // solo las esquinas redondeadas (no píldora)
                ColorBlock cb = chip.colors;            // el color lo pone ActualizarChips; el botón solo oscurece al tocar
                cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white;
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                chip.colors = cb;
                texto.fontStyle = FontStyles.Bold;
                texto.enableAutoSizing = true;
                texto.fontSizeMin = 16;
                texto.fontSizeMax = Mathf.Max(texto.fontSize, 28);
                chipsEstilo.Add((fondo, texto, texto.text.Trim()));
            }
        }
        ActualizarChips();

        // ---------- "Explorar campus": píldora verde sin marco ----------
        if (btnExplorarAbajo != null) EstiloBotonPrincipal(btnExplorarAbajo, fuente);

        // ---------- Barra de ruta: tarjeta redondeada con ícono ----------
        if (barraRuta != null)
        {
            Image fondo = barraRuta.GetComponent<Image>();
            if (fondo != null)
            {
                UIRapida.Redondear(fondo, 34f);
                fondo.color = new Color(UIRapida.Verde.r, UIRapida.Verde.g, UIRapida.Verde.b, 0.96f);
            }

            HorizontalLayoutGroup hl = barraRuta.GetComponent<HorizontalLayoutGroup>();
            if (hl != null)
            {
                hl.padding = new RectOffset(26, 22, 20, 20);
                hl.spacing = 20;
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.childForceExpandHeight = false;
            }

            if (barraRuta.transform.Find("IconoRuta") == null)
            {
                Image icono = UIRapida.Panel("IconoRuta", barraRuta.transform, Color.white);
                icono.sprite = UIRapida.Icono("pin");
                icono.preserveAspect = true;
                icono.raycastTarget = false;
                LayoutElement le = icono.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 64;
                icono.transform.SetAsFirstSibling();
            }

            TMP_Text textoRuta = barraRuta.GetComponentInChildren<TMP_Text>(true);
            if (textoRuta != null && textoRuta.transform.parent == barraRuta.transform)
            {
                textoRuta.enableAutoSizing = true;
                textoRuta.fontSizeMin = 20;
                textoRuta.fontSizeMax = 30;
                textoRuta.lineSpacing = -6f;
                LayoutElement le = textoRuta.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = 150;
            }
        }

        if (btnTerminarRuta != null)
        {
            Image fondo = btnTerminarRuta.targetGraphic as Image;
            if (fondo != null) UIRapida.Redondear(fondo, 40f);
            LayoutElement le = btnTerminarRuta.GetComponent<LayoutElement>();
            if (le != null) { le.preferredWidth = 230; le.preferredHeight = 96; }
            TMP_Text t = UIRapida.TextoDe(btnTerminarRuta);
            if (t != null) { t.fontStyle = FontStyles.Bold; t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = 30; }
        }
    }

    // ==================================================================
    // Menú principal: foto tipo portada, botones con ícono y textos con tildes
    // ==================================================================
    private void EstilizarMenuPrincipal()
    {
        if (btnExplorar == null) return;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        Transform menu = btnExplorar.transform;
        while (menu != null && menu.name != "MenuPrincipal") menu = menu.parent;
        Canvas canvas = btnExplorar.GetComponentInParent<Canvas>(true);

        // ---------- Textos del encabezado (Panel_Menu) ----------
        if (canvas != null)
        {
            foreach (TMP_Text t in canvas.rootCanvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.text.Contains("mas cerca") || t.text.Contains("Encuentra tu lugar"))
                    t.text = "Tu campus, más cerca. Encuentra tu lugar en Cajicá.";
                if (t.text.Trim() == "Campus Conecta") t.color = UIRapida.Verde;
            }
            Transform barra = BuscarHijo(canvas.rootCanvas.transform, "Barra");
            if (barra != null && barra.GetComponentInChildren<Image>(true) is Image linea) UIRapida.Redondear(linea, 6f);
        }

        // ---------- Foto del campus: redondeada, con sombra y leyenda ----------
        Transform marco = menu != null ? BuscarHijo(menu, "Image (1)") : null;
        Image foto = marco != null && marco.childCount > 0 ? marco.GetChild(0).GetComponent<Image>() : null;
        Image imgMarco = marco != null ? marco.GetComponent<Image>() : null;
        if (imgMarco != null && foto != null && marco.Find("Leyenda") == null)
        {
            // El marco pasa a ser la máscara redondeada; la foto lo llena entero
            UIRapida.Redondear(imgMarco, 36f);
            imgMarco.color = Color.white;
            Mask mascara = marco.gameObject.AddComponent<Mask>();
            mascara.showMaskGraphic = true;
            RectTransform rf = foto.rectTransform;
            rf.anchorMin = Vector2.zero;
            rf.anchorMax = Vector2.one;
            rf.offsetMin = rf.offsetMax = Vector2.zero;
            foto.preserveAspect = false;

            // Sombra detrás del marco (mismo tamaño, un poco más abajo)
            RectTransform rm = (RectTransform)marco;
            Image sombra = UIRapida.Panel("SombraFoto", rm.parent, new Color(0f, 0f, 0f, 0.18f));
            UIRapida.Redondear(sombra, 40f);
            sombra.raycastTarget = false;
            RectTransform rs = sombra.rectTransform;
            rs.anchorMin = rm.anchorMin;
            rs.anchorMax = rm.anchorMax;
            rs.pivot = rm.pivot;
            rs.sizeDelta = rm.sizeDelta + new Vector2(8f, 8f);
            rs.anchoredPosition = rm.anchoredPosition + new Vector2(0f, -10f);
            sombra.transform.SetSiblingIndex(marco.GetSiblingIndex());

            // Degradado oscuro abajo + leyenda
            Image grad = UIRapida.Panel("Degradado", marco, Color.white);
            grad.sprite = UIRapida.Degradado();
            grad.color = new Color(1f, 1f, 1f, 0.75f);
            grad.raycastTarget = false;
            UIRapida.Anclar(grad.rectTransform, Vector2.zero, new Vector2(1f, 0.42f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);

            RectTransform leyenda = UIRapida.Rect("Leyenda", marco);
            UIRapida.Anclar(leyenda, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(-56f, 110f));
            TMP_Text l1 = UIRapida.Texto(leyenda, "Sede", "Sede Campus · Cajicá", 40, Color.white, fuente, true, TextAlignmentOptions.BottomLeft);
            UIRapida.Estirar((RectTransform)l1.transform, 0, 44, 0, 0);
            TMP_Text l2 = UIRapida.Texto(leyenda, "Detalle", "Universidad Militar Nueva Granada", 26, new Color(1f, 1f, 1f, 0.85f), fuente, false, TextAlignmentOptions.BottomLeft);
            UIRapida.Estirar((RectTransform)l2.transform, 0, 0, 0, 70);
        }

        // ---------- Botones ----------
        EstiloBotonPrincipal(btnExplorar, fuente);
        IconoEnBoton(btnExplorar, "pin", Color.white);

        if (btnModoAdmin != null)
        {
            EstiloBotonPrincipal(btnModoAdmin, fuente);
            ((Image)btnModoAdmin.targetGraphic).color = UIRapida.Menta;
            TMP_Text t = UIRapida.TextoDe(btnModoAdmin);
            if (t != null) t.color = UIRapida.Verde;
            IconoEnBoton(btnModoAdmin, "llave", UIRapida.Verde);
        }

        if (btnCerrarSesion != null)
        {
            EstiloBotonPrincipal(btnCerrarSesion, fuente);
            ((Image)btnCerrarSesion.targetGraphic).color = new Color(1f, 1f, 1f, 0.9f);
            TMP_Text t = UIRapida.TextoDe(btnCerrarSesion);
            if (t != null) t.color = UIRapida.Rojo;
            IconoEnBoton(btnCerrarSesion, "salir", UIRapida.Rojo);
        }
    }

    // ==================================================================
    // Panel de administración: encabezado, pestañas, botones y filas de lugares
    // ==================================================================
    private GameObject panelGestion;
    private TMP_Text textoTabLugares, textoTabReportes;
    private Image barraTabLugares, barraTabReportes;

    private void EstilizarPanelAdmin()
    {
        if (btnSalirAdmin == null) return;
        Canvas canvas = btnSalirAdmin.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform raiz = canvas.rootCanvas.transform;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        // El encabezado y el título "Panel de administración UMNG" se dejan como en el diseño original

        // Salir: círculo blanco con ícono rojo
        Image imgSalir = btnSalirAdmin.targetGraphic as Image;
        if (imgSalir == null) imgSalir = btnSalirAdmin.GetComponent<Image>();
        if (imgSalir != null && btnSalirAdmin.transform.Find("IconoSalir") == null)
        {
            imgSalir.sprite = UIRapida.Circulo();
            imgSalir.color = Color.white;
            imgSalir.preserveAspect = true;
            Image ic = UIRapida.Panel("IconoSalir", btnSalirAdmin.transform, UIRapida.Rojo);
            ic.sprite = UIRapida.Icono("salir");
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIRapida.Estirar(ic.rectTransform, 18, 18, 18, 18);
        }

        // ---------- Pestañas: fondo blanco; la activa en verde con línea redondeada ----------
        if (tabLugares != null)
        {
            Transform barraTabs = tabLugares.transform.parent != null ? tabLugares.transform.parent.parent : null;
            Image fondoTabs = barraTabs != null ? barraTabs.GetComponent<Image>() : null;
            if (fondoTabs != null) fondoTabs.color = Color.white;

            textoTabLugares = UIRapida.TextoDe(tabLugares);
            barraTabLugares = tabLugares.transform.parent.Find("Barra")?.GetComponent<Image>();
        }
        if (tabReportes != null)
        {
            textoTabReportes = UIRapida.TextoDe(tabReportes);
            barraTabReportes = tabReportes.transform.parent.Find("Barra")?.GetComponent<Image>();
        }
        foreach (Image b in new[] { barraTabLugares, barraTabReportes })
        {
            if (b == null) continue;
            UIRapida.Redondear(b, 4f);
            b.color = UIRapida.Verde;
        }
        if (tabLugares != null) tabLugares.onClick.AddListener(ActualizarPestanas);
        if (tabReportes != null) tabReportes.onClick.AddListener(ActualizarPestanas);
        if (contenedorLugares != null)
        {
            Transform t = contenedorLugares;
            while (t != null && t.name != "GestionLugaresPanel") t = t.parent;
            panelGestion = t != null ? t.gameObject : null;
        }
        ActualizarPestanas();

        // ---------- Botones: agregar (principal) y modificar mapa (secundario) ----------
        if (btnAgregarLugar != null)
        {
            EstiloBotonPrincipal(btnAgregarLugar, fuente);
            TMP_Text t = UIRapida.TextoDe(btnAgregarLugar);
            if (t != null) t.text = "AGREGAR NUEVO LUGAR";
            IconoEnBoton(btnAgregarLugar, "mas", Color.white);
        }
        if (btnModificarMapa != null)
        {
            EstiloBotonPrincipal(btnModificarMapa, fuente);
            ((Image)btnModificarMapa.targetGraphic).color = UIRapida.Menta;
            TMP_Text t = UIRapida.TextoDe(btnModificarMapa);
            if (t != null) t.color = UIRapida.Verde;
            IconoEnBoton(btnModificarMapa, "mapa", UIRapida.Verde);

            // Más cerca de "Agregar": su fila deja de crecer y el botón se pega arriba
            Transform fila = btnModificarMapa.transform.parent != null ? btnModificarMapa.transform.parent.parent : null;
            LayoutElement le = fila != null ? fila.GetComponent<LayoutElement>() : null;
            if (le != null)
            {
                le.preferredHeight = 120f;
                le.flexibleHeight = 0f;
            }
            RectTransform rb = (RectTransform)btnModificarMapa.transform;
            rb.offsetMin = new Vector2(rb.offsetMin.x, 26f);    // margen abajo
            rb.offsetMax = new Vector2(rb.offsetMax.x, -6f);    // casi sin margen arriba
        }

        // ---------- "Espacios del campus" + conteo como etiqueta ----------
        Transform guion = BuscarHijo(raiz, "NumeroLugares");
        if (guion != null) guion.gameObject.SetActive(false);   // el "-" suelto del diseño
        foreach (TMP_Text t in raiz.GetComponentsInChildren<TMP_Text>(true))
            if (t.text.Trim() == "Espacios del campus") { t.fontStyle = FontStyles.Bold; t.color = UIRapida.Oscuro; }

        if (textoCantidadLugares != null && textoCantidadLugares.transform.parent.Find("FondoConteo") == null)
        {
            Image fondoConteo = UIRapida.Panel("FondoConteo", textoCantidadLugares.transform.parent, UIRapida.Menta);
            UIRapida.Redondear(fondoConteo, 18f);
            fondoConteo.raycastTarget = false;
            RectTransform rc = fondoConteo.rectTransform, rt = (RectTransform)textoCantidadLugares.transform;
            rc.anchorMin = rt.anchorMin; rc.anchorMax = rt.anchorMax; rc.pivot = rt.pivot;
            rc.anchoredPosition = rt.anchoredPosition; rc.sizeDelta = rt.sizeDelta;
            fondoConteo.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            fondoConteo.transform.SetSiblingIndex(textoCantidadLugares.transform.GetSiblingIndex());
            textoCantidadLugares.color = UIRapida.Verde;
            textoCantidadLugares.fontStyle = FontStyles.Bold;
            textoCantidadLugares.alignment = TextAlignmentOptions.Center;
        }

        // ---------- Lista: sin fondo gris, filas separadas ----------
        if (contenedorLugares != null)
        {
            ScrollRect sr = contenedorLugares.GetComponentInParent<ScrollRect>(true);
            Image fondoLista = sr != null ? sr.GetComponent<Image>() : null;
            if (fondoLista != null) fondoLista.color = new Color(1, 1, 1, 0);
            VerticalLayoutGroup vl = contenedorLugares.GetComponent<VerticalLayoutGroup>();
            if (vl != null) { vl.spacing = 16; vl.padding = new RectOffset(4, 4, 8, 16); }
        }
    }

    // La pestaña visible en verde y negrita con su línea; la otra en gris
    private void ActualizarPestanas()
    {
        bool lugares = panelGestion == null || panelGestion.activeSelf;
        if (textoTabLugares != null)
        {
            textoTabLugares.color = lugares ? UIRapida.Verde : UIRapida.Gris;
            textoTabLugares.fontStyle = lugares ? FontStyles.Bold : FontStyles.Normal;
        }
        if (textoTabReportes != null)
        {
            textoTabReportes.color = lugares ? UIRapida.Gris : UIRapida.Verde;
            textoTabReportes.fontStyle = lugares ? FontStyles.Normal : FontStyles.Bold;
        }
    }

    // Fila de un lugar: tarjeta redondeada, ícono, nombre en negrita, categoría como etiqueta, íconos de acción
    private static void EstilizarFilaLugar(Transform fila)
    {
        Image fondo = fila.GetComponent<Image>();
        if (fondo != null) { UIRapida.Redondear(fondo, 26f); fondo.color = Color.white; }

        // Sin imagen decorativa a la izquierda: el nombre y la categoría usan ese espacio
        Transform deco = fila.Find("Image");
        if (deco != null) deco.gameObject.SetActive(false);

        TMP_Text nombre = BuscarHijo(fila, "Nombre")?.GetComponentInChildren<TMP_Text>(true);
        if (nombre != null)
        {
            nombre.fontStyle = FontStyles.Bold;
            nombre.color = UIRapida.Oscuro;
            nombre.enableAutoSizing = true;
            nombre.fontSizeMin = 24;
            nombre.fontSizeMax = 38;
        }
        TMP_Text categoria = BuscarHijo(fila, "Categoria")?.GetComponentInChildren<TMP_Text>(true);
        if (categoria != null)
        {
            categoria.text = categoria.text.ToUpperInvariant();
            categoria.fontStyle = FontStyles.Bold;
            categoria.color = UIRapida.VerdeClaro;
            categoria.characterSpacing = 4f;
            categoria.enableAutoSizing = true;
            categoria.fontSizeMin = 20;
            categoria.fontSizeMax = 30;
        }

        AccionFila(BuscarHijo(fila, "Editar"), "lapiz", UIRapida.Verde);
        AccionFila(BuscarHijo(fila, "Eliminar"), "basura", UIRapida.Rojo);
    }

    // Editar / Eliminar: ícono pequeño (tamaño fijo, centrado en su espacio) con el color de su texto
    private const float TamanoIconoAccion = 56f;

    private static void AccionFila(Transform accion, string icono, Color color)
    {
        if (accion == null) return;
        Transform espacio = accion.Find("Imagen");
        Image img = espacio != null ? espacio.GetComponent<Image>() : null;
        if (img != null && espacio.Find("Icono") == null)
        {
            img.sprite = null;
            img.color = new Color(1, 1, 1, 0);   // el espacio del diseño queda, pero vacío
            Image ic = UIRapida.Panel("Icono", espacio, color);
            ic.sprite = UIRapida.Icono(icono);
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIRapida.Anclar(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            Vector2.zero, new Vector2(TamanoIconoAccion, TamanoIconoAccion));
        }
        TMP_Text t = accion.GetComponentInChildren<TMP_Text>(true);
        if (t != null)
        {
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true;
            t.fontSizeMin = 14;
            t.fontSizeMax = 20;
        }
    }

    // Ícono a la izquierda dentro de un botón (una sola vez)
    private static void IconoEnBoton(Button b, string icono, Color color)
    {
        if (b == null || b.transform.Find("Icono_" + icono) != null) return;
        Image img = UIRapida.Panel("Icono_" + icono, b.transform, color);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 0), new Vector2(52, 52));
    }

    // Margen interno del texto dentro de la píldora del buscador
    private static void AjustarTextoCampo(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(34f, 4f);
        rt.offsetMax = new Vector2(-24f, -4f);
        TMP_Text t = rt.GetComponent<TMP_Text>();
        if (t != null)
        {
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>Botón principal del diseño como píldora verde: sin marcos internos, texto blanco en negrita.</summary>
    private static void EstiloBotonPrincipal(Button b, TMP_FontAsset fuente)
    {
        Image fondo = b.targetGraphic as Image;
        if (fondo == null) fondo = b.GetComponent<Image>();
        if (fondo == null) return;

        UIRapida.Redondear(fondo, 48f);
        fondo.color = UIRapida.Verde;
        foreach (Transform hijo in b.transform)
            if (hijo.GetComponent<TMP_Text>() == null) hijo.gameObject.SetActive(false);   // marcos e íconos del diseño

        TMP_Text t = UIRapida.TextoDe(b);
        if (t != null)
        {
            t.color = Color.white;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 4f;
        }
    }

    // La categoría elegida se ve en verde con texto blanco; las demás, menta con texto verde
    private void ActualizarChips()
    {
        string activa = categoriaFiltro == "*" ? "todos" : Normalizar(categoriaFiltro);
        bool hayLista = panelResultados != null && panelResultados.activeSelf;

        foreach (var (fondo, texto, categoria) in chipsEstilo)
        {
            bool sel = hayLista && categoriaFiltro != null && Normalizar(categoria) == activa;
            fondo.color = sel ? UIRapida.Verde : new Color(1f, 1f, 1f, 0.95f);
            texto.color = sel ? Color.white : UIRapida.Verde;
        }
    }

    // ==================================================================
    // Menú de perfil (reemplaza el recuadro gris del diseño)
    // ==================================================================
    private RectTransform tarjetaPerfil;
    private TMP_Text perfilNombre, perfilRol, textoItemAdmin;
    private GameObject itemSalir;
    private Coroutine animacionMenu;

    private void ConstruirMenuPerfil()
    {
        if (btnPerfil == null) return;
        Canvas canvas = btnPerfil.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform raiz = canvas.rootCanvas.transform;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        // El menú y la zona de cierre del diseño quedan apagados
        if (menuPerfil != null) menuPerfil.SetActive(false);
        if (zonaCerrarMenu != null) zonaCerrarMenu.SetActive(false);

        // Fondo que oscurece un poco y cierra el menú al tocar fuera
        Image fondo = UIRapida.Panel("CierreMenuPerfil", raiz, new Color(0f, 0f, 0f, 0.18f));
        UIRapida.Estirar(fondo.rectTransform);
        Button cerrar = fondo.gameObject.AddComponent<Button>();
        cerrar.transition = Selectable.Transition.None;
        cerrar.onClick.AddListener(() => MostrarMenuPerfil(false));

        // Tarjeta
        Image tarjeta = UIRapida.Panel("MenuPerfil", raiz, Color.white);
        UIRapida.Redondear(tarjeta, 30f);
        tarjetaPerfil = tarjeta.rectTransform;
        tarjetaPerfil.anchorMin = tarjetaPerfil.anchorMax = new Vector2(0.5f, 0.5f);
        tarjetaPerfil.pivot = new Vector2(1f, 1f);
        tarjetaPerfil.sizeDelta = new Vector2(580f, 0f);
        tarjeta.gameObject.AddComponent<CanvasGroup>();
        VerticalLayoutGroup v = tarjeta.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(26, 26, 26, 18);
        v.spacing = 4;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tarjeta.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Sombra suave detrás de la tarjeta (fuera del layout)
        Image sombra = UIRapida.Panel("Sombra", tarjeta.transform, new Color(0f, 0f, 0f, 0.16f));
        UIRapida.Redondear(sombra, 34f);
        sombra.raycastTarget = false;
        sombra.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(sombra.rectTransform, -6, -12, -6, 2);
        sombra.transform.SetAsFirstSibling();
        // La tarjeta blanca va encima de su propia sombra: se pinta con un fondo hijo
        Image cara = UIRapida.Panel("Cara", tarjeta.transform, Color.white);
        UIRapida.Redondear(cara, 30f);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        cara.transform.SetSiblingIndex(1);
        tarjeta.color = new Color(1, 1, 1, 0);   // la tarjeta en sí es invisible (solo recibe toques)

        // Encabezado: avatar + nombre + rol
        RectTransform encabezado = UIRapida.Rect("Encabezado", tarjeta.transform);
        encabezado.gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
        HorizontalLayoutGroup he = encabezado.gameObject.AddComponent<HorizontalLayoutGroup>();
        he.spacing = 22;
        he.childAlignment = TextAnchor.MiddleLeft;
        he.childControlWidth = he.childControlHeight = true;
        he.childForceExpandWidth = he.childForceExpandHeight = false;

        Image avatar = UIRapida.Panel("Avatar", encabezado, UIRapida.Menta);
        avatar.sprite = UIRapida.Circulo();
        avatar.preserveAspect = true;
        LayoutElement la = avatar.gameObject.AddComponent<LayoutElement>();
        la.preferredWidth = la.preferredHeight = 88;
        Image figura = UIRapida.Panel("Icono", avatar.transform, UIRapida.Verde);
        figura.sprite = UIRapida.Icono("persona");
        figura.raycastTarget = false;
        UIRapida.Estirar(figura.rectTransform, 18, 16, 18, 18);

        RectTransform textos = UIRapida.Rect("Textos", encabezado);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup vt = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        vt.childAlignment = TextAnchor.MiddleLeft;
        vt.childControlWidth = vt.childControlHeight = true;
        vt.childForceExpandHeight = false;
        perfilNombre = UIRapida.Texto(textos, "Nombre", "Visitante", 36, UIRapida.Oscuro, fuente, true);
        perfilRol = UIRapida.Texto(textos, "Rol", "Explorando el campus", 26, UIRapida.Gris, fuente);

        // Separador
        Image linea = UIRapida.Panel("Separador", tarjeta.transform, new Color(0.86f, 0.91f, 0.88f, 1f));
        linea.raycastTarget = false;
        linea.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;

        // Opciones
        textoItemAdmin = ItemMenu(tarjeta.transform, "llave", "Modo administrador", UIRapida.Verde, UIRapida.Oscuro, fuente,
                                  () => { MostrarMenuPerfil(false); AbrirModoAdmin(); });
        ItemMenu(tarjeta.transform, "inicio", "Menú principal", UIRapida.Verde, UIRapida.Oscuro, fuente,
                 () => { CerrarCapasExcepto(Capa.Ninguna); ui.IrRaiz(PMenu); });
        itemSalir = ItemMenu(tarjeta.transform, "salir", "Cerrar sesión", UIRapida.Rojo, UIRapida.Rojo, fuente, () =>
        {
            MostrarMenuPerfil(false);
            admin.CerrarSesion();
            ui.Avisar("Sesión de administrador cerrada.");
            ActualizarMenu();
        }).transform.parent.gameObject;

        menuPerfil = tarjeta.gameObject;
        zonaCerrarMenu = fondo.gameObject;
        menuPerfil.SetActive(false);
        zonaCerrarMenu.SetActive(false);
    }

    // Fila del menú: ícono + texto; al tocarla se ilumina en menta
    private static TMP_Text ItemMenu(Transform padre, string icono, string texto, Color colorIcono, Color colorTexto,
                                     TMP_FontAsset fuente, UnityAction accion)
    {
        Image fondo = UIRapida.Panel("Opcion " + texto, padre, Color.white);
        UIRapida.Redondear(fondo, 20f);
        fondo.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
        Button b = fondo.gameObject.AddComponent<Button>();
        b.targetGraphic = fondo;
        ColorBlock cb = b.colors;
        cb.normalColor = new Color(1, 1, 1, 0);
        cb.highlightedColor = UIRapida.Menta;
        cb.pressedColor = new Color(0.82f, 0.94f, 0.87f, 1f);
        cb.selectedColor = new Color(1, 1, 1, 0);
        b.colors = cb;
        b.onClick.AddListener(accion);

        Image img = UIRapida.Panel("Icono", fondo.transform, colorIcono);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(54, 54));

        TMP_Text t = UIRapida.Texto(fondo.transform, "Texto", texto, 32, colorTexto, fuente, false, TextAlignmentOptions.MidlineLeft);
        UIRapida.Estirar((RectTransform)t.transform, 96, 0, 16, 0);
        return t;
    }

    private void ActualizarMenuPerfil()
    {
        bool sesion = admin != null && admin.EstaAutenticado;
        if (perfilNombre != null) perfilNombre.text = sesion ? admin.NicknameActual : "Visitante";
        if (perfilRol != null) perfilRol.text = sesion ? "Administrador · sesión activa" : "Explorando el campus";
        if (textoItemAdmin != null) textoItemAdmin.text = sesion ? "Panel de administración" : "Modo administrador";
        if (itemSalir != null) itemSalir.SetActive(sesion);
    }

    // La esquina superior derecha de la tarjeta queda justo debajo del botón de perfil
    private void ColocarBajoPerfil()
    {
        RectTransform padre = (RectTransform)tarjetaPerfil.parent;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tarjetaPerfil);

        Vector3[] c = new Vector3[4];
        ((RectTransform)btnPerfil.transform).GetWorldCorners(c);          // 3 = abajo-derecha
        Vector3 local = padre.InverseTransformPoint(c[3]);

        float margen = 24f;
        float x = Mathf.Clamp(local.x, padre.rect.xMin + margen + tarjetaPerfil.rect.width, padre.rect.xMax - margen);
        tarjetaPerfil.anchoredPosition = new Vector2(x, local.y - 14f);
    }

    // Aparece desde la esquina del botón: un poco más pequeña y transparente → tamaño normal
    private System.Collections.IEnumerator AnimarMenu()
    {
        CanvasGroup g = tarjetaPerfil.GetComponent<CanvasGroup>();
        const float duracion = 0.14f;
        for (float t = 0f; t < duracion; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / duracion, 3f);   // ease-out
            g.alpha = k;
            tarjetaPerfil.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);
            yield return null;
        }
        g.alpha = 1f;
        tarjetaPerfil.localScale = Vector3.one;
        animacionMenu = null;
    }

    // ==================================================================
    // Administrador: lugares
    // ==================================================================
    private void RefrescarLugaresAdmin()
    {
        if (lugares == null) return;
        if (textoCantidadLugares != null) textoCantidadLugares.text = $"{lugares.Lugares.Count} lugares";
        if (contenedorLugares == null || prefabEspacio == null) return;

        Limpiar(contenedorLugares);
        foreach (LugaresService.Lugar l in lugares.Lugares)
        {
            LugaresService.Lugar lugar = l;
            GameObject fila = Instantiate(prefabEspacio, contenedorLugares);
            PonerTexto(fila.transform, "Nombre", l.nombre);
            PonerTexto(fila.transform, "Categoria", l.categoria);
            EstilizarFilaLugar(fila.transform);

            Button editar = BuscarHijo(fila.transform, "Editar")?.GetComponent<Button>();
            Button eliminar = BuscarHijo(fila.transform, "Eliminar")?.GetComponent<Button>();
            Click(editar, () => AbrirFormulario(lugar));
            if (eliminar != null) eliminar.onClick.AddListener(() => PedirEliminar(eliminar, lugar));
        }
    }

    // Primer toque: pide confirmación. Segundo toque (antes de 3 s): elimina.
    private void PedirEliminar(Button boton, LugaresService.Lugar lugar)
    {
        if (eliminarPendiente == boton && Time.unscaledTime <= eliminarHasta)
        {
            CancelarEliminar();
            admin.EliminarLugar(lugar.id, (ok, msg) =>
                ui.Avisar(ok ? $"\"{lugar.nombre}\" eliminado." : msg));
            return;
        }

        CancelarEliminar();
        eliminarPendiente = boton;
        eliminarHasta = Time.unscaledTime + segundosConfirmarEliminar;
        TMP_Text t = boton.GetComponentInChildren<TMP_Text>(true);
        if (t != null) { textoEliminarOriginal = t.text; t.text = "¿Seguro?"; }
        ui.Avisar($"Toca otra vez para eliminar \"{lugar.nombre}\" (también sus servicios y búsquedas).");
    }

    private void CancelarEliminar()
    {
        if (eliminarPendiente != null)
        {
            TMP_Text t = eliminarPendiente.GetComponentInChildren<TMP_Text>(true);
            if (t != null && textoEliminarOriginal != null) t.text = textoEliminarOriginal;
        }
        eliminarPendiente = null;
    }

    // ==================================================================
    // Administrador: reportes
    // ==================================================================
    private void RefrescarReportes()
    {
        if (!BaseDatosLocal.Lista) return;

        DateTime hoy = DateTime.Now;
        if (textoResumenFecha != null) textoResumenFecha.text = $"Resumen mensual - {Meses[hoy.Month - 1]} {hoy.Year}";
        if (contenedorReportes == null || prefabEstadistica == null) return;

        List<RepositorioCampus.EstadisticaLugar> datos;
        try { datos = RepositorioCampus.EstadisticasDelMes(hoy); }
        catch (Exception e) { Debug.LogError("Reportes: " + e.Message); return; }

        Limpiar(contenedorReportes);
        foreach (RepositorioCampus.EstadisticaLugar d in datos)
        {
            GameObject fila = Instantiate(prefabEstadistica, contenedorReportes);
            PonerTexto(fila.transform, "Nombre", d.nombre);
            PonerTexto(fila.transform, "Categoria", d.categoria);
            PonerTexto(fila.transform, "Busquedas(mes)", d.mesActual.ToString());
            PonerTexto(fila.transform, "Tendencias", Tendencia(d.mesActual, d.mesAnterior));
        }
    }

    private static string Tendencia(int actual, int anterior)
    {
        int dif = actual - anterior;
        if (anterior == 0 && actual > 0) return "<color=#2f8a3c>Nuevo</color>";
        if (dif > 0) return $"<color=#2f8a3c>Sube +{dif}</color>";
        if (dif < 0) return $"<color=#b3402e>Baja {dif}</color>";
        return "<color=#7d7d7d>Igual</color>";
    }

    private void GenerarPdf()
    {
        if (textoEstadoPdf != null) textoEstadoPdf.text = "Generando reporte...";
        admin.GenerarReportePdf((ok, resultado) =>
        {
            string texto = ok
                ? (Application.isEditor
                    ? $"PDF guardado: {Path.GetFileName(resultado)}"
                    : $"PDF guardado en Android/data/{Application.identifier}/files/Reportes")
                : resultado;
            if (textoEstadoPdf != null) textoEstadoPdf.text = texto;
            ui.Avisar(ok ? "Reporte PDF generado." : resultado);
        });
    }

    // ==================================================================
    // Formulario de lugar
    // ==================================================================
    private void AbrirFormulario(LugaresService.Lugar l)
    {
        lugarEditando = l != null ? l.id : 0;
        if (tituloFormulario != null) tituloFormulario.text = l != null ? "Editar lugar" : "Nuevo lugar";

        Poner(fNombre, l?.nombre);
        Poner(fCategoria, l?.categoria);
        Poner(fDescripcion, l?.descripcion);
        Poner(fIndicacion, l?.indicacion);
        Poner(fHorario, l?.horario);
        Poner(fLatitud, l != null ? l.latitud.ToString("0.000000", CultureInfo.InvariantCulture) : "");
        Poner(fLongitud, l != null ? l.longitud.ToString("0.000000", CultureInfo.InvariantCulture) : "");
        Mostrar(formError, false);

        ui.Ir(PFormulario);
    }

    private void UsarMiUbicacion()
    {
        if (ubicacion == null || !ubicacion.TieneUbicacion)
        {
            Error(formError, "Todavía no hay ubicación del GPS.");
            return;
        }
        Poner(fLatitud, ubicacion.UbicacionReal.x.ToString("0.000000", CultureInfo.InvariantCulture));
        Poner(fLongitud, ubicacion.UbicacionReal.y.ToString("0.000000", CultureInfo.InvariantCulture));
        Mostrar(formError, false);
    }

    private void GuardarLugar()
    {
        if (!LeerNumero(fLatitud, out double lat) || !LeerNumero(fLongitud, out double lon))
        {
            Error(formError, "Escribe la latitud y la longitud como números (ej. 4.942603 y -74.010816).");
            return;
        }

        AdminService.LugarEditable datos = new AdminService.LugarEditable
        {
            nombre = Texto(fNombre),
            categoria = Texto(fCategoria),
            descripcion = Texto(fDescripcion),
            indicacion = Texto(fIndicacion),
            horario = Texto(fHorario),
            latitud = lat,
            longitud = lon,
        };

        btnGuardarLugar.interactable = false;
        Action<bool, string> alTerminar = (ok, mensaje) =>
        {
            btnGuardarLugar.interactable = true;
            if (!ok) { Error(formError, mensaje); return; }
            ui.Cerrar(PFormulario);
            ui.Avisar($"\"{datos.nombre}\" guardado.");
        };

        if (lugarEditando > 0) admin.ActualizarLugar(lugarEditando, datos, alTerminar);
        else admin.CrearLugar(datos, alTerminar);
    }

    // ==================================================================
    // Modificar mapa (administrador): nodos, puertas y edificaciones 3D
    // ==================================================================
    private void PrepararEditorMapa()
    {
        if (ui == null || admin == null || lugares == null) return;

        Mapbox.Unity.Map.AbstractMap mapa = FindFirstObjectByType<Mapbox.Unity.Map.AbstractMap>();
        CamaraMapa camara = FindFirstObjectByType<CamaraMapa>();
        if (mapa == null || camara == null) { Debug.LogWarning("Modificar mapa: falta el mapa o la cámara del mapa."); return; }

        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        EditorMapa editor = EditorMapa.Crear(transform, ui, admin, lugares, mapa, modelosMapa, camara, fuente,
                                             ubicacion != null && ubicacion.MapaListo);

        ui.RegistrarPantalla(new UIManager.Pantalla
        {
            nombre = EditorMapa.NombrePantalla,
            panel = editor.gameObject,
            requiereAdmin = true,
        });

        // Botón "MODIFICAR MAPA" en Gestión de lugares, con el mismo estilo que "+ AGREGAR NUEVO LUGAR"
        if (btnAgregarLugar == null) return;
        Transform filaAgregar = btnAgregarLugar.transform.parent != null ? btnAgregarLugar.transform.parent.parent : null;
        if (filaAgregar == null || filaAgregar.parent == null) return;

        GameObject fila = Instantiate(filaAgregar.gameObject, filaAgregar.parent);
        fila.name = "OpcionModificarMapa";
        fila.transform.SetSiblingIndex(filaAgregar.GetSiblingIndex() + 1);
        btnModificarMapa = fila.GetComponentInChildren<Button>(true);
        if (btnModificarMapa == null) { Destroy(fila); return; }

        btnModificarMapa.onClick.RemoveAllListeners();
        TMP_Text t = btnModificarMapa.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = "MODIFICAR MAPA";
        btnModificarMapa.onClick.AddListener(() => ui.Ir(EditorMapa.NombrePantalla));
    }

    // ==================================================================
    // Controles del mapa: mover y zoom (se crean solos si la escena no los tiene)
    // ==================================================================

    private void PrepararControlesMapa()
    {
        // 1. La cámara del mapa necesita CamaraMapa (arrastrar, pellizco, rueda)
        CamaraMapa control = FindFirstObjectByType<CamaraMapa>();
        if (control == null)
        {
            Camera cam = BuscarCamaraMapa();
            if (cam == null) { Debug.LogWarning("No encontré la cámara del mapa para los controles de mover y zoom."); return; }

            control = cam.gameObject.AddComponent<CamaraMapa>();
            control.Configurar(ubicacion != null ? ubicacion.Marcador : null,
                               FindFirstObjectByType<Mapbox.Unity.Map.AbstractMap>(),
                               ubicacion != null && ubicacion.MapaListo);
            Debug.Log($"Controles del mapa agregados a '{cam.name}'.");
        }

        // 2. Botones +, −, Yo y 3D en la pantalla de usuario
        if (referenciaChips == null) return;
        Canvas canvas = referenciaChips.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform usuario = canvas.rootCanvas.transform;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        Transform grupo = usuario.Find("ControlesMapa");   // puede existir si se creó con la herramienta del Editor
        if (grupo == null)
        {
            GameObject go = new GameObject("ControlesMapa", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.layer = 5;
            go.transform.SetParent(usuario, false);
            grupo = go.transform;
            RectTransform rt = (RectTransform)grupo;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-28f, 40f);
            VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 20f;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = true;

            // Por debajo de la lista, la ficha y la barra: cuando se abren, quedan encima
            if (panelResultados != null) grupo.SetSiblingIndex(panelResultados.transform.GetSiblingIndex());

            CrearBotonMapa(grupo, "BotonZoomMas", "+", 80, fuente, control.ZoomMas);
            CrearBotonMapa(grupo, "BotonZoomMenos", "-", 96, fuente, control.ZoomMenos);
            CrearBotonMapa(grupo, "BotonMiUbicacion", "Yo", 40, fuente, control.CentrarEnUsuario);
        }

        // Botón de perspectiva: dice "3D" en la vista de arriba y "2D" en la isométrica
        Transform perspectiva = grupo.Find("BotonPerspectiva");
        Button botonPerspectiva = perspectiva != null
            ? perspectiva.GetComponent<Button>()
            : CrearBotonMapa(grupo, "BotonPerspectiva", "3D", 40, fuente, null);
        if (perspectiva == null) botonPerspectiva.onClick.AddListener(control.AlternarPerspectiva);

        TMP_Text etiqueta = botonPerspectiva.GetComponentInChildren<TMP_Text>(true);
        if (etiqueta != null)
        {
            etiqueta.text = control.Es3D ? "2D" : "3D";
            control.PerspectivaCambiada += tresD => etiqueta.text = tresD ? "2D" : "3D";
        }

        // El grupo crece según cuántos botones tenga
        int n = grupo.childCount;
        ((RectTransform)grupo).sizeDelta = new Vector2(130f, n * 130f + (n - 1) * 20f);
    }

    // Mismo botón redondo que usa la pantalla "Modificar mapa" (UIRapida.BotonRedondo)
    private static Button CrearBotonMapa(Transform padre, string nombre, string texto, float tamano,
                                         TMP_FontAsset fuente, UnityAction accion) =>
        UIRapida.BotonRedondo(padre, nombre, texto, tamano, fuente, accion);

    // La cámara base que dibuja el mapa (no la de solo UI)
    private static Camera BuscarCamaraMapa()
    {
        Camera mejor = null;
        foreach (Camera c in Camera.allCameras)
        {
            if (c.targetTexture != null || !EsCamaraBase(c)) continue;
            if (c.name == "Camera_Mapa") return c;
            if (c.cullingMask != (1 << 5) && mejor == null) mejor = c;
        }
        return mejor;
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

    private static void Limpiar(Transform contenedor)
    {
        for (int i = contenedor.childCount - 1; i >= 0; i--) Destroy(contenedor.GetChild(i).gameObject);
    }

    private static Transform BuscarHijo(Transform raiz, string nombre)
    {
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
            if (t.name == nombre) return t;
        return null;
    }

    private static void PonerTexto(Transform raiz, string hijo, string texto)
    {
        Transform t = BuscarHijo(raiz, hijo);
        TMP_Text tmp = t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        if (tmp != null) tmp.text = texto ?? "";
    }

    // ==================================================================
    // Espacio libre del mapa
    // Todo lo que se abre sobre el mapa (resultados, ficha, barra de ruta, avisos) se acomoda SOLO
    // en la franja libre: debajo de las categorías y encima de la zona inferior del diseño.
    // Se calcula con la pantalla real, así no tapa el encabezado ni los botones en ningún celular.
    // ==================================================================
    private const float Margen = 14f;

    // Límites de la franja libre en coordenadas de mundo (en un Canvas Overlay = píxeles de pantalla)
    private bool FranjaLibre(out float yArriba, out float yAbajo)
    {
        yArriba = float.PositiveInfinity;
        yAbajo = float.NegativeInfinity;
        Canvas.ForceUpdateCanvases();
        Vector3[] c = new Vector3[4];   // 0 = abajo-izquierda, 1 = arriba-izquierda

        if (areaMapa != null)
        {
            areaMapa.GetWorldCorners(c);
            yArriba = c[1].y;
            yAbajo = c[0].y;
        }
        if (referenciaChips != null)
        {
            referenciaChips.GetWorldCorners(c);
            yArriba = Mathf.Min(yArriba, c[0].y);
        }
        if (referenciaBarraRuta != null)
        {
            referenciaBarraRuta.GetWorldCorners(c);
            yAbajo = Mathf.Max(yAbajo, c[1].y);
        }
        // El botón "Explorar campus" sobresale de su zona: se mide el botón real
        if (btnExplorarAbajo != null && btnExplorarAbajo.gameObject.activeInHierarchy)
        {
            ((RectTransform)btnExplorarAbajo.transform).GetWorldCorners(c);
            yAbajo = Mathf.Max(yAbajo, c[1].y);
        }
        return !float.IsInfinity(yArriba) && !float.IsInfinity(yAbajo) && yArriba > yAbajo;
    }

    /// <summary>
    /// Ubica un panel dentro de la franja libre: pegado arriba (bajo las categorías) o abajo
    /// (sobre la zona inferior), con un alto máximo que nunca se sale de la franja.
    /// </summary>
    private void AcomodarEnFranja(RectTransform panel, float altoMaximo, bool pegadoArriba)
    {
        if (panel == null || !FranjaLibre(out float yArribaMundo, out float yAbajoMundo)) return;

        RectTransform padre = (RectTransform)panel.parent;
        float arriba = padre.InverseTransformPoint(new Vector3(0, yArribaMundo, 0)).y - Margen;
        float abajo = padre.InverseTransformPoint(new Vector3(0, yAbajoMundo, 0)).y + Margen;
        float alto = Mathf.Max(80f, Mathf.Min(altoMaximo, arriba - abajo));

        if (pegadoArriba)
        {
            panel.anchorMin = new Vector2(panel.anchorMin.x, 1);
            panel.anchorMax = new Vector2(panel.anchorMax.x, 1);
            panel.pivot = new Vector2(0.5f, 1);
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, arriba - padre.rect.yMax);
        }
        else
        {
            panel.anchorMin = new Vector2(panel.anchorMin.x, 0);
            panel.anchorMax = new Vector2(panel.anchorMax.x, 0);
            panel.pivot = new Vector2(0.5f, 0);
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, abajo - padre.rect.yMin);
        }
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, alto);
    }

    // El aviso: en el mapa va arriba de la franja libre (no tapa encabezado ni buscador);
    // en las demás pantallas, abajo, lejos de títulos y botones.
    private void AcomodarAviso()
    {
        if (fondoAviso == null) return;
        RectTransform rt = (RectTransform)fondoAviso.transform;
        string p = ui != null ? ui.PantallaActual : null;

        if ((p == PUsuario || p == PFicha) && FranjaLibre(out _, out _))
        {
            AcomodarEnFranja(rt, altoAviso, pegadoArriba: true);
            return;
        }

        rt.anchorMin = new Vector2(rt.anchorMin.x, 0);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 110f);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, altoAviso);
    }

    // Si la escena se integró antes de existir 'areaMapa', se busca la ZonaCentro del diseño
    private void BuscarAreaMapa()
    {
        if (areaMapa != null || referenciaChips == null) return;
        Canvas canvas = referenciaChips.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform zonas = BuscarHijo(canvas.rootCanvas.transform, "ZonasInteres");
        if (zonas == null) return;
        foreach (Transform hijo in zonas)
            if (hijo.name == "ZonaCentro") { areaMapa = (RectTransform)hijo; return; }
    }

    private static bool Contiene(string q, params string[] campos)
    {
        foreach (string c in campos)
            if (!string.IsNullOrEmpty(c) && Normalizar(c).Contains(q)) return true;
        return false;
    }

    // Minúsculas y sin tildes: "Cafetería" = "cafeteria"
    private static string Normalizar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim().ToLowerInvariant())
        {
            switch (ch)
            {
                case 'á': case 'à': case 'ä': sb.Append('a'); break;
                case 'é': case 'è': case 'ë': sb.Append('e'); break;
                case 'í': case 'ì': case 'ï': sb.Append('i'); break;
                case 'ó': case 'ò': case 'ö': sb.Append('o'); break;
                case 'ú': case 'ù': case 'ü': sb.Append('u'); break;
                case 'ñ': sb.Append('n'); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
