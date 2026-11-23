using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Campus > Integrar InterfazGrafica en Santiago
///
/// Trae el diseño de Assets/UI/Scenes/InterfazGrafica.unity a la escena Santiago y lo deja funcional:
///  1. Hace un respaldo de Santiago en Assets/Scenes/Respaldos.
///  2. Mueve los canvas Panel_Menu, Panel_Usuario y Panel_AdminPanel a Santiago
///     (el archivo InterfazGrafica.unity NO se modifica).
///  3. Quita los scripts que solo cambiaban paneles (UIButtonAction, CerrarPanel, GestorLugares).
///  4. Crea lo que el diseño no tenía: campo de búsqueda, lista de resultados, ficha del lugar,
///     barra de ruta, formulario de lugar, botón de PDF, campo de código en Registro y avisos.
///  5. Configura el UIManager y agrega InterfazCampus, que conecta todo con la base de datos.
///  6. Desactiva el Canvas provisional que tenía Santiago.
/// </summary>
public static class IntegradorInterfaz
{
    private const string RutaDiseno = "Assets/UI/Scenes/InterfazGrafica.unity";
    private const string RutaSantiago = "Assets/Scenes/Santiago.unity";
    private const string CarpetaRespaldos = "Assets/Scenes/Respaldos";
    private const string NombreRaiz = "UI_CampusConecta";
    private const string RutaPrefabEspacio = "Assets/UI/Prefabs/Panel_Administrador/EspacioInteres.prefab";
    private const string RutaPrefabEstadistica = "Assets/UI/Prefabs/Panel_Administrador/EstadisticaEspacios.prefab";

    // Paleta y tamaños: los mismos de la app (UIRapida), sin valores propios
    private static readonly Color Verde = UIRapida.Verde;   // #1c7126
    private static readonly Color Oscuro = UIRapida.Oscuro;
    private static readonly Color Menta = UIRapida.Menta;   // #adc1ad
    private static readonly Color Gris = UIRapida.Gris;
    private static readonly Color Rojo = UIRapida.Rojo;

    // Espaciado y medidas: tokens de UIRapida (escala de 8). Solo las POSICIONES propias de este diseño
    // (dónde va cada pieza en la pantalla de 1080x1920) quedan como constantes con nombre.
    private const float Esp1 = UIRapida.Esp1, Esp2 = UIRapida.Esp2, Esp3 = UIRapida.Esp3, Esp4 = UIRapida.Esp4;
    private const float Margen = UIRapida.Margen, Toque = UIRapida.ToqueMinimo;
    private const float AltoBoton = UIRapida.AltoBotonPrincipal;
    private const float AltoTitulo = UIRapida.TamTitulo + Esp4;                 // franja de un título TamTitulo
    private const float AltoFilaResultado = Toque + Esp3;
    private const float ArribaResultados = 600f;                                 // bajo el buscador del diseño
    private const float AltoResultados = UIRapida.AltoMaxResultados;            // mismo alto que en ejecución
    private const float AltoFicha = UIRapida.AltoMaxFicha;                       // mismo alto que en ejecución
    private const float ArribaBarraRuta = 220f;                                  // sobre la zona inferior del diseño
    private const float AltoBarraRuta = UIRapida.AltoBarraRuta;                 // mismo alto que en ejecución
    private const float AnchoBotonTerminar = UIRapida.AnchoBotonTerminar;
    private const float ArribaAviso = 60f, AltoAviso = UIRapida.AltoAviso;

    private static TMP_FontAsset fuente, fuenteNegrita;

    // ==================================================================
    // Menú
    // ==================================================================
    [MenuItem("Campus/Integrar InterfazGrafica en Santiago", false, 100)]
    private static void Integrar()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Campus", "Detén el modo Play antes de integrar la interfaz.", "Aceptar");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene santiago = EditorSceneManager.OpenScene(RutaSantiago, OpenSceneMode.Single);
        foreach (GameObject r in santiago.GetRootGameObjects())
        {
            if (r.name == NombreRaiz)
            {
                EditorUtility.DisplayDialog("Campus",
                    $"Santiago ya tiene la interfaz integrada (objeto '{NombreRaiz}').\n\n" +
                    "Si quieres volver a integrarla, borra ese objeto (o restaura un respaldo) y ejecuta esto de nuevo.", "Aceptar");
                return;
            }
        }

        if (!EditorUtility.DisplayDialog("Campus",
                "Se integrará el diseño de InterfazGrafica en la escena Santiago y se conectarán sus botones " +
                "con la base de datos.\n\nAntes se guarda un respaldo de Santiago en Assets/Scenes/Respaldos. " +
                "InterfazGrafica.unity no se modifica.",
                "Integrar", "Cancelar"))
            return;

        string respaldo = HacerRespaldo();
        Scene diseno = default;

        try
        {
            CargarFuentes();

            diseno = EditorSceneManager.OpenScene(RutaDiseno, OpenSceneMode.Additive);
            GameObject raiz = new GameObject(NombreRaiz);
            SceneManager.MoveGameObjectToScene(raiz, santiago);

            GameObject menu = Mover(diseno, santiago, "Panel_Menu", raiz.transform);
            GameObject usuario = Mover(diseno, santiago, "Panel_Usuario", raiz.transform);
            GameObject adminPanel = Mover(diseno, santiago, "Panel_AdminPanel", raiz.transform);

            EditorSceneManager.CloseScene(diseno, true);   // sin guardar: el archivo de diseño queda intacto
            diseno = default;

            QuitarScriptsViejos(raiz);
            List<string> avisos = Construir(santiago, raiz, menu, usuario, adminPanel);

            EditorSceneManager.MarkSceneDirty(santiago);
            EditorSceneManager.SaveScene(santiago);

            string extra = avisos.Count > 0 ? "\n\nRevisa:\n• " + string.Join("\n• ", avisos) : "";
            Debug.Log("Interfaz integrada en Santiago." + extra.Replace("\n\n", "\n"));
            EditorUtility.DisplayDialog("Campus",
                "Interfaz integrada y escena guardada.\n\nRespaldo: " + respaldo + extra, "Aceptar");

            Selection.activeGameObject = raiz;
        }
        catch (Exception e)
        {
            Debug.LogError("Integrar interfaz: " + e);
            if (diseno.IsValid() && diseno.isLoaded) EditorSceneManager.CloseScene(diseno, true);
            EditorUtility.DisplayDialog("Campus",
                "No se pudo integrar la interfaz:\n" + e.Message +
                "\n\nLa escena NO se guardó. Vuelve a abrir Santiago sin guardar los cambios.\n" +
                "Respaldo: " + respaldo, "Aceptar");
        }
    }

    private static string HacerRespaldo()
    {
        if (!AssetDatabase.IsValidFolder(CarpetaRespaldos)) AssetDatabase.CreateFolder("Assets/Scenes", "Respaldos");
        string destino = $"{CarpetaRespaldos}/Santiago_antes_UI_{DateTime.Now:yyyyMMdd_HHmmss}.unity";
        if (!AssetDatabase.CopyAsset(RutaSantiago, destino)) throw new Exception("No se pudo crear el respaldo de Santiago.");
        return destino;
    }

    private static GameObject Mover(Scene desde, Scene hacia, string nombre, Transform padre)
    {
        foreach (GameObject r in desde.GetRootGameObjects())
        {
            if (r.name != nombre) continue;
            SceneManager.MoveGameObjectToScene(r, hacia);
            r.transform.SetParent(padre, false);
            return r;
        }
        throw new Exception($"No encontré '{nombre}' en {RutaDiseno}.");
    }

    // Tipografía: los TMP_Text de la escena se guardan con LiberationSans SDF (asset persistente); en ejecución
    // UIRapida.AplicarFuentes los pasa a Roboto (TMP_FontAsset dinámico, que no se puede guardar en la escena).
    // Los Text/InputField antiguos sí reciben Roboto aquí mismo (UIRapida.FuenteLegacy es un Font importado).
    private static void CargarFuentes()
    {
        fuente = CargarFuente("LiberationSans SDF");
        if (fuente == null) fuente = TMP_Settings.defaultFontAsset;
        fuenteNegrita = fuente;   // la negrita va con FontStyles.Bold (en ejecución, Roboto Bold real)
    }

    private static TMP_FontAsset CargarFuente(string nombre)
    {
        foreach (string guid in AssetDatabase.FindAssets(nombre + " t:TMP_FontAsset"))
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(ruta) == nombre) return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ruta);
        }
        return null;
    }

    // ==================================================================
    // Limpieza: el diseño cambiaba paneles con estos scripts; ahora lo hace InterfazCampus
    // ==================================================================
    private static void QuitarScriptsViejos(GameObject raiz)
    {
        foreach (Button b in raiz.GetComponentsInChildren<Button>(true))
        {
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEngine.Object t = b.onClick.GetPersistentTarget(i);
                bool vieja = t == null || t is UIButtonAction || t is CerrarPanel || t is GestorLugares ||
                             string.IsNullOrEmpty(b.onClick.GetPersistentMethodName(i));
                if (vieja) UnityEventTools.RemovePersistentListener(b.onClick, i);
            }
        }

        foreach (UIButtonAction c in raiz.GetComponentsInChildren<UIButtonAction>(true)) UnityEngine.Object.DestroyImmediate(c);
        foreach (CerrarPanel c in raiz.GetComponentsInChildren<CerrarPanel>(true)) UnityEngine.Object.DestroyImmediate(c);
        foreach (GestorLugares c in raiz.GetComponentsInChildren<GestorLugares>(true)) UnityEngine.Object.DestroyImmediate(c);
        // PanelSwitcher (pestañas del panel de administración) se conserva: funciona tal cual
    }

    // ==================================================================
    // Construcción
    // ==================================================================
    private static List<string> Construir(Scene santiago, GameObject raiz, GameObject menu, GameObject usuario, GameObject adminPanel)
    {
        List<string> avisos = new List<string>();

        UIManager ui = Buscar<UIManager>(santiago);
        AdminService admin = Buscar<AdminService>(santiago);
        LugaresService lugares = Buscar<LugaresService>(santiago);
        ComoLlegarController comoLlegar = Buscar<ComoLlegarController>(santiago);
        UbicacionController ubicacion = Buscar<UbicacionController>(santiago);
        ClimaController clima = Buscar<ClimaController>(santiago);

        if (ui == null) throw new Exception("Santiago no tiene UIManager.");
        if (admin == null || lugares == null || comoLlegar == null) throw new Exception("Faltan AdminService, LugaresService o ComoLlegarController en Santiago.");

        InterfazCampus ic = raiz.AddComponent<InterfazCampus>();
        ic.ui = ui;
        ic.admin = admin;
        ic.lugares = lugares;
        ic.comoLlegar = comoLlegar;
        ic.ubicacion = ubicacion;

        // ---------------- Panel_Menu: menú principal, login y registro ----------------
        Transform cambiar = Requerido(menu.transform, "PantallasCambiar");
        Transform login = Requerido(cambiar, "Login", soloHijosDirectos: true);
        Transform registro = Requerido(cambiar, "Registro", soloHijosDirectos: true);
        Transform menuPrincipal = Requerido(cambiar, "MenuPrincipal", soloHijosDirectos: true);

        ic.btnExplorar = BotonEn(menuPrincipal, "BotonFondo");
        ic.btnModoAdmin = BotonEn(menuPrincipal, "BotonAdministrador");
        ic.btnCerrarSesion = BotonEn(menuPrincipal, "BotonCerrarSesion");

        // Login (ahora es el acceso de administradores)
        InputField[] camposLogin = login.GetComponentsInChildren<InputField>(true);
        if (camposLogin.Length < 2) throw new Exception("El Login del diseño no tiene los dos campos esperados.");
        ic.loginUsuario = ConfigurarCampo(camposLogin[0], "Usuario", InputField.ContentType.Standard, 50);
        ic.loginLlave = ConfigurarCampo(camposLogin[1], "Contraseña", InputField.ContentType.Password, 64);
        CambiarTexto(login, "comunidad", "Acceso para administradores de Campus Conecta");
        CambiarTexto(login, "registrarte", "¿Tienes un código de registro?");
        ic.btnIrRegistro = BotonEn(login, "Regsitro");
        CambiarTextoDe(ic.btnIrRegistro, "Regístrate aquí");
        ic.btnEntrar = BotonEn(login, "BotonInicioSesion");
        ic.loginError = TextoEn(login, n => n.StartsWith("MensajeContrase") && n.EndsWith("Incorrecta"));
        ic.loginOk = TextoEn(login, n => n.StartsWith("MensajeContrase") && !n.EndsWith("Incorrecta"));

        // Registro: usuario + contraseña + código de verificación
        InputField[] camposRegistro = registro.GetComponentsInChildren<InputField>(true);
        if (camposRegistro.Length < 2) throw new Exception("El Registro del diseño no tiene los dos campos esperados.");
        ic.regUsuario = ConfigurarCampo(camposRegistro[0], "Usuario (3 a 50 caracteres)", InputField.ContentType.Standard, 50);
        ic.regLlave = ConfigurarCampo(camposRegistro[1], "Contraseña (mín. 8, letras y números)", InputField.ContentType.Password, 64);

        Transform filaLlave = camposRegistro[1].transform.parent.parent;   // InputField > caja > fila
        GameObject filaCodigo = UnityEngine.Object.Instantiate(filaLlave.gameObject, filaLlave.parent);
        filaCodigo.name = "FilaCodigo";
        filaCodigo.transform.SetSiblingIndex(filaLlave.GetSiblingIndex() + 1);
        ic.regCodigo = ConfigurarCampo(filaCodigo.GetComponentInChildren<InputField>(true), "Código de verificación",
                                       InputField.ContentType.Standard, 20);

        CambiarTexto(registro, "Porfavor", "Escribe el usuario y la contraseña que quieras y el código de verificación que te entregaron.");
        ic.regTerminos = registro.GetComponentInChildren<Toggle>(true);
        ic.btnRegistrar = BotonEn(registro, "BotonInicioSesion");
        ic.btnRegVolver = BotonEn(registro, "BotonInicioSesion (1)");
        ic.regError = TextoEn(registro, n => n == "UsuarioNoRegistrado");
        ic.regOk = TextoEn(registro, n => n == "UsuarioRegistrado");

        // ---------------- Panel_Usuario: mapa, buscador, ficha y ruta ----------------
        Opcional(usuario.transform, "Fondo", soloHijosDirectos: true)?.gameObject.SetActive(false);   // deja ver el mapa
        Opcional(usuario.transform, "Mapa")?.gameObject.SetActive(false);                            // marcador de posición del diseño
        foreach (Image img in usuario.GetComponentsInChildren<Image>(true))
            if (img.color.a < 0.01f) img.raycastTarget = false;                                       // zonas invisibles no bloquean el mapa

        if (clima != null)
        {
            SerializedObject so = new SerializedObject(clima);
            AsignarRef(so, "textoTemperatura", TextoBajo(usuario.transform, "VariableClima"));
            AsignarRef(so, "textoDescripcion", TextoBajo(usuario.transform, "ZOnadrecha"));
            so.FindProperty("prefijoDescripcion").stringValue = "Cajicá: ";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else avisos.Add("No hay ClimaController en Santiago: el clima del encabezado no se actualizará.");

        Transform textoSuperior = Requerido(usuario.transform, "TextoSuperior");
        ic.buscador = CrearBuscador(Requerido(textoSuperior, "TextoBuscar", soloHijosDirectos: true));

        Transform opciones = Requerido(usuario.transform, "TextoInferiorOpciones");
        List<Button> chips = new List<Button>();
        foreach (Transform chip in opciones) chips.Add(HacerBoton(chip.gameObject));
        ic.chips = chips.ToArray();
        ic.referenciaChips = (RectTransform)opciones;

        ic.btnPerfil = BotonEn(usuario.transform, "BotonPerfil");
        Transform menuPausa = Requerido(usuario.transform, "BotonPausa");
        Transform zonaCerrar = Requerido(usuario.transform, "ZonaCerrar");
        ic.menuPerfil = menuPausa.gameObject;
        ic.zonaCerrarMenu = zonaCerrar.gameObject;
        ic.btnCerrarMenu = BotonEn(zonaCerrar, "Button");
        Transform botonesPausa = Requerido(menuPausa, "Botones");
        ic.btnOpcionPerfil = BotonEn(Requerido(botonesPausa, "Boton", soloHijosDirectos: true), "Opcion");
        ic.btnOpcionAjustes = BotonEn(Requerido(botonesPausa, "Boton (1)", soloHijosDirectos: true), "Opcion");
        ic.btnOpcionMenuPrincipal = BotonEn(Requerido(botonesPausa, "Boton (2)", soloHijosDirectos: true), "Opcion");
        CambiarTextoDe(ic.btnOpcionPerfil, "Administrador");

        Transform zonaAbajo = Requerido(usuario.transform, "ZonaAbajo");
        ic.btnExplorarAbajo = BotonEn(zonaAbajo, "BotonFondo");
        ic.referenciaBarraRuta = (RectTransform)zonaAbajo;
        ic.areaMapa = (RectTransform)Requerido(Requerido(usuario.transform, "ZonasInteres"), "ZonaCentro", soloHijosDirectos: true);

        GameObject modeloBoton = ic.btnExplorar.gameObject;   // botón verde del diseño, para que todo combine
        CrearResultados(usuario.transform, ic);
        GameObject ficha = CrearFicha(usuario.transform, modeloBoton, ic);
        TMP_Text textoRuta = CrearBarraRuta(usuario.transform, ic);

        SerializedObject soRuta = new SerializedObject(comoLlegar);
        AsignarRef(soRuta, "textoRuta", textoRuta);
        soRuta.ApplyModifiedPropertiesWithoutUndo();

        // ---------------- Panel_AdminPanel ----------------
        ic.btnSalirAdmin = BotonEn(adminPanel.transform, "BotonSalir");
        ic.tabLugares = BotonEn(adminPanel.transform, "GestionLugares");
        ic.tabReportes = BotonEn(adminPanel.transform, "ReportesuSO");

        Transform gestion = Requerido(adminPanel.transform, "GestionLugaresPanel");
        ic.btnAgregarLugar = BotonEn(gestion, "BotonFondo");
        ic.textoCantidadLugares = TextoBajo(gestion, "Lugares");
        ic.contenedorLugares = (RectTransform)Requerido(gestion, "Contenedor");
        ic.prefabEspacio = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPrefabEspacio);
        if (ic.prefabEspacio == null) avisos.Add("No encontré " + RutaPrefabEspacio);

        Transform reportes = Requerido(adminPanel.transform, "ReportesUso");
        ic.textoResumenFecha = TextoBajo(reportes, "Lugares (1)");
        ic.contenedorReportes = (RectTransform)Requerido(reportes, "Contenedor");
        ic.prefabEstadistica = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPrefabEstadistica);
        if (ic.prefabEstadistica == null) avisos.Add("No encontré " + RutaPrefabEstadistica);

        ic.btnPdf = ClonarBoton(ic.btnAgregarLugar.gameObject, reportes, "BotonPdf", "Descargar reporte PDF");
        Alto(ic.btnPdf.gameObject, AltoBoton);
        ic.textoEstadoPdf = Texto(reportes, "EstadoPdf", "", UIRapida.TamCuerpo, Oscuro, false, TextAlignmentOptions.Center);
        Alto(ic.textoEstadoPdf.gameObject, 2f * Esp4);

        Transform filaModelo = camposLogin[0].transform.parent.parent;   // fila de campo del diseño
        GameObject formulario = CrearFormulario(adminPanel.transform, filaModelo.gameObject, ic.btnAgregarLugar.gameObject, ic);

        // ---------------- Avisos ----------------
        CrearAvisos(raiz.transform, ic);

        // ---------------- UIManager ----------------
        List<UIManager.Pantalla> pantallas = new List<UIManager.Pantalla>
        {
            new UIManager.Pantalla { nombre = InterfazCampus.PMenu, panel = menuPrincipal.gameObject, contenedor = menu },
            new UIManager.Pantalla { nombre = InterfazCampus.PLogin, panel = login.gameObject, contenedor = menu },
            new UIManager.Pantalla { nombre = InterfazCampus.PRegistro, panel = registro.gameObject, contenedor = menu },
            new UIManager.Pantalla { nombre = InterfazCampus.PUsuario, panel = usuario },
            new UIManager.Pantalla { nombre = InterfazCampus.PFicha, panel = ficha, superpuesta = true },
            new UIManager.Pantalla { nombre = InterfazCampus.PAdmin, panel = adminPanel, requiereAdmin = true },
            new UIManager.Pantalla { nombre = InterfazCampus.PFormulario, panel = formulario, superpuesta = true, requiereAdmin = true },
        };
        ui.ConfigurarDesdeEditor(pantallas, InterfazCampus.PMenu, InterfazCampus.PMenu, InterfazCampus.PLogin, admin, ic.textoAviso);
        EditorUtility.SetDirty(ui);

        // Estado inicial en el Editor (el UIManager lo ajusta al dar Play)
        menu.SetActive(true);
        menuPrincipal.gameObject.SetActive(true);
        login.gameObject.SetActive(false);
        registro.gameObject.SetActive(false);
        usuario.SetActive(false);
        adminPanel.SetActive(false);

        // ---------------- Canvas provisional de Santiago ----------------
        foreach (GameObject r in santiago.GetRootGameObjects())
        {
            if (r != raiz && r.GetComponent<Canvas>() != null && r.transform.Find("Panel_Menu") != null)
            {
                r.SetActive(false);
                r.name = "Canvas_Provisional (desactivado)";
            }
        }

        // ---------------- Cámara de la UI provisional ----------------
        // Solo dibujaba ese Canvas (capa UI) y no limpiaba la pantalla: con los canvas nuevos
        // (Overlay) sobra, y su fondo "Nothing" deja restos de cuadros anteriores en pantalla.
        foreach (Camera cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool soloUI = cam.cullingMask == (1 << 5);
            if (cam.gameObject.scene == santiago && soloUI && cam.clearFlags == CameraClearFlags.Nothing)
            {
                cam.gameObject.SetActive(false);
                avisos.Add($"Se desactivó '{cam.name}' (solo dibujaba la UI provisional y dejaba restos en pantalla).");
            }
        }

        // La interfaz (Overlay) va en Display 1, la pantalla del celular: la cámara del mapa también
        foreach (Camera cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (cam.gameObject.scene != santiago || cam.targetTexture != null || cam.targetDisplay == 0) continue;
            avisos.Add($"'{cam.name}' dibujaba en Display {cam.targetDisplay + 1}; se pasó a Display 1.");
            cam.targetDisplay = 0;
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(ic);
        return avisos;
    }

    // ==================================================================
    // Piezas nuevas
    // ==================================================================

    // El diseño tenía solo un texto: se convierte en un campo de búsqueda real
    private static TMP_InputField CrearBuscador(Transform caja)
    {
        TMP_Text placeholder = Requerido(caja, "Textos", soloHijosDirectos: true).GetComponent<TMP_Text>();
        placeholder.text = "¿A dónde quieres ir? (Ej. Biblioteca)";

        bool activo = caja.gameObject.activeSelf;
        caja.gameObject.SetActive(false);   // se configura apagado para que TMP_InputField arranque completo

        GameObject escritoGO = UnityEngine.Object.Instantiate(placeholder.gameObject, caja);
        escritoGO.name = "TextoEscrito";
        TMP_Text escrito = escritoGO.GetComponent<TMP_Text>();
        escrito.text = "";
        escrito.color = Oscuro;
        escrito.fontStyle = FontStyles.Normal;

        if (caja.GetComponent<RectMask2D>() == null) caja.gameObject.AddComponent<RectMask2D>();
        TMP_InputField campo = caja.gameObject.AddComponent<TMP_InputField>();
        campo.textViewport = (RectTransform)caja;
        campo.textComponent = escrito;
        campo.placeholder = placeholder;
        Transform fondoCaja = caja.Find("Image");
        if (fondoCaja != null) campo.targetGraphic = fondoCaja.GetComponent<Image>();
        campo.fontAsset = escrito.font;
        campo.pointSize = escrito.fontSize;
        campo.lineType = TMP_InputField.LineType.SingleLine;
        campo.characterLimit = 60;

        caja.gameObject.SetActive(activo);
        return campo;
    }

    private static void CrearResultados(Transform canvasUsuario, InterfazCampus ic)
    {
        GameObject panel = Nuevo("Resultados", canvasUsuario, typeof(Image), typeof(ScrollRect));
        Anclar(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -ArribaResultados), new Vector2(-2f * Margen, AltoResultados));
        panel.GetComponent<Image>().color = UIRapida.Superficie;

        GameObject viewport = Nuevo("Viewport", panel.transform, typeof(RectMask2D));
        Estirar(viewport, Esp2);

        GameObject contenido = Nuevo("Contenido", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        Anclar(contenido, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vlg = contenido.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = Esp1;
        vlg.padding = Relleno(Esp1, Esp1, Esp1, Esp1);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        contenido.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect sr = panel.GetComponent<ScrollRect>();
        sr.viewport = (RectTransform)viewport.transform;
        sr.content = (RectTransform)contenido.transform;
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30;

        GameObject fila = Nuevo("FilaResultado", contenido.transform, typeof(Image), typeof(Button), typeof(LayoutElement));
        fila.GetComponent<Image>().color = UIRapida.FondoCampo;
        fila.GetComponent<LayoutElement>().preferredHeight = AltoFilaResultado;
        fila.GetComponent<Button>().targetGraphic = fila.GetComponent<Image>();
        TMP_Text t = Texto(fila.transform, "Texto", "Lugar", UIRapida.TamSubtitulo, Oscuro, false, TextAlignmentOptions.MidlineLeft);
        Estirar(t.gameObject, 0);
        ((RectTransform)t.transform).offsetMin = new Vector2(Esp4, 0);

        TMP_Text vacio = Texto(contenido.transform, "SinResultados", "", UIRapida.TamCuerpo, Gris, false, TextAlignmentOptions.Center);
        Alto(vacio.gameObject, Toque);

        ic.panelResultados = panel;
        ic.contenedorResultados = (RectTransform)contenido.transform;
        ic.plantillaResultado = fila.GetComponent<Button>();
        ic.textoSinResultados = vacio;
        panel.SetActive(false);
    }

    private static GameObject CrearFicha(Transform canvasUsuario, GameObject modeloBoton, InterfazCampus ic)
    {
        GameObject ficha = Nuevo("Ficha", canvasUsuario, typeof(Image), typeof(VerticalLayoutGroup));
        Anclar(ficha, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, AltoFicha));
        ficha.GetComponent<Image>().color = UIRapida.SuperficieOpaca;
        VerticalLayoutGroup vlg = ficha.GetComponent<VerticalLayoutGroup>();
        vlg.padding = Relleno(Margen, Margen, Esp4, Esp4);
        vlg.spacing = Esp2;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ic.fichaNombre = Texto(ficha.transform, "Nombre", "Lugar", UIRapida.TamTitulo, Verde, true, TextAlignmentOptions.Left);
        Alto(ic.fichaNombre.gameObject, AltoTitulo);
        ic.fichaCategoria = Texto(ficha.transform, "Categoria", "Categoría", UIRapida.TamSecundario, Gris, false, TextAlignmentOptions.Left);
        Alto(ic.fichaCategoria.gameObject, UIRapida.AltoEtiquetaCampo + Esp1);
        ic.fichaDetalle = Texto(ficha.transform, "Detalle", "", UIRapida.TamCuerpo, Oscuro, false, TextAlignmentOptions.TopLeft);
        ic.fichaDetalle.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement le = ic.fichaDetalle.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = 1;

        GameObject filaBotones = Nuevo("Botones", ficha.transform, typeof(HorizontalLayoutGroup));
        Alto(filaBotones, AltoBoton);
        HorizontalLayoutGroup hlg = filaBotones.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = Esp3;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;

        ic.btnComoLlegar = ClonarBoton(modeloBoton, filaBotones.transform, "BotonComoLlegar", "Cómo llegar");
        ic.btnCerrarFicha = ClonarBoton(modeloBoton, filaBotones.transform, "BotonCerrar", "Cerrar");
        Secundario(ic.btnCerrarFicha);

        ficha.SetActive(false);
        return ficha;
    }

    private static TMP_Text CrearBarraRuta(Transform canvasUsuario, InterfazCampus ic)
    {
        GameObject barra = Nuevo("BarraRuta", canvasUsuario, typeof(Image), typeof(HorizontalLayoutGroup));
        Anclar(barra, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, ArribaBarraRuta), new Vector2(-2f * Margen, AltoBarraRuta));
        // Mismos valores que InterfazCampus.Estilo en ejecución: RutaColor opaco, relleno Esp3/Esp3/Esp2/Esp2 y separación Esp2
        barra.GetComponent<Image>().color = UIRapida.RutaColor;
        HorizontalLayoutGroup hlg = barra.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = Relleno(Esp3, Esp3, Esp2, Esp2);
        hlg.spacing = Esp2;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        TMP_Text texto = Texto(barra.transform, "TextoRuta", "", UIRapida.TamCuerpo, Color.white, false, TextAlignmentOptions.MidlineLeft);
        texto.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        GameObject boton = Nuevo("BotonTerminar", barra.transform, typeof(Image), typeof(Button), typeof(LayoutElement));
        boton.GetComponent<Image>().color = UIRapida.SuperficieOpaca;
        boton.GetComponent<LayoutElement>().preferredWidth = AnchoBotonTerminar;
        boton.GetComponent<Button>().targetGraphic = boton.GetComponent<Image>();
        TMP_Text tb = Texto(boton.transform, "Texto", "Terminar", UIRapida.TamCuerpo, UIRapida.RutaColor, true, TextAlignmentOptions.Center);   // como en ejecución
        Estirar(tb.gameObject, 0);

        ic.barraRuta = barra;
        ic.btnTerminarRuta = boton.GetComponent<Button>();
        barra.SetActive(false);
        return texto;
    }

    private static GameObject CrearFormulario(Transform canvasAdmin, GameObject filaModelo, GameObject modeloBoton, InterfazCampus ic)
    {
        GameObject form = Nuevo("FormularioLugar", canvasAdmin, typeof(Image), typeof(VerticalLayoutGroup));
        Estirar(form, 0);
        form.GetComponent<Image>().color = UIRapida.SuperficieOpaca;   // hoja que tapa el mapa (no Menta)
        VerticalLayoutGroup vlg = form.GetComponent<VerticalLayoutGroup>();
        vlg.padding = Relleno(2f * Margen, 2f * Margen, Toque, 2f * Margen);
        vlg.spacing = Esp1;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ic.tituloFormulario = Texto(form.transform, "Titulo", "Nuevo lugar", UIRapida.TamTitulo, Verde, true, TextAlignmentOptions.Left);
        Alto(ic.tituloFormulario.gameObject, AltoTitulo);

        ic.fNombre = CampoFormulario(form.transform, filaModelo, "Nombre *", "Ej. Edificio C - Laboratorios");
        ic.fCategoria = CampoFormulario(form.transform, filaModelo, "Categoría *", "Ej. Aulas, Cafetería, Biblioteca");
        ic.fDescripcion = CampoFormulario(form.transform, filaModelo, "Descripción", "Qué se encuentra en este lugar");
        ic.fIndicacion = CampoFormulario(form.transform, filaModelo, "Cómo encontrarlo", "Ej. Entrada por el costado norte");
        ic.fHorario = CampoFormulario(form.transform, filaModelo, "Horario", "Ej. L-V 7:00 - 18:00");
        ic.fLatitud = CampoFormulario(form.transform, filaModelo, "Latitud *", "Ej. 4.942603");
        ic.fLongitud = CampoFormulario(form.transform, filaModelo, "Longitud *", "Ej. -74.010816");

        ic.btnUsarUbicacion = ClonarBoton(modeloBoton, form.transform, "BotonUsarUbicacion", "Usar mi ubicación");
        Secundario(ic.btnUsarUbicacion);
        Alto(ic.btnUsarUbicacion.gameObject, Toque);

        ic.formError = Texto(form.transform, "Error", "", UIRapida.TamSecundario, Rojo, false, TextAlignmentOptions.Left);
        Alto(ic.formError.gameObject, UIRapida.AltoEtiquetaCampo + Esp4);

        GameObject filaBotones = Nuevo("Botones", form.transform, typeof(HorizontalLayoutGroup));
        Alto(filaBotones, AltoBoton);
        HorizontalLayoutGroup hlg = filaBotones.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = Esp3;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
        ic.btnGuardarLugar = ClonarBoton(modeloBoton, filaBotones.transform, "BotonGuardar", "Guardar");
        ic.btnCancelarLugar = ClonarBoton(modeloBoton, filaBotones.transform, "BotonCancelar", "Cancelar");
        Secundario(ic.btnCancelarLugar);

        form.SetActive(false);
        return form;
    }

    private static InputField CampoFormulario(Transform form, GameObject filaModelo, string etiqueta, string ejemplo)
    {
        TMP_Text e = Texto(form, "Etiqueta " + etiqueta, etiqueta, UIRapida.TamSecundario, Oscuro, true, TextAlignmentOptions.BottomLeft);
        Alto(e.gameObject, UIRapida.AltoEtiquetaCampo);

        GameObject fila = UnityEngine.Object.Instantiate(filaModelo, form);
        fila.name = "Campo " + etiqueta;
        Alto(fila, Toque);

        // Se oculta el ícono de la fila del login (era de correo/contraseña)
        InputField campo = fila.GetComponentInChildren<InputField>(true);
        foreach (Transform hijo in fila.transform)
            if (!campo.transform.IsChildOf(hijo)) hijo.gameObject.SetActive(false);

        return ConfigurarCampo(campo, ejemplo, InputField.ContentType.Standard, 300);
    }

    private static void CrearAvisos(Transform raiz, InterfazCampus ic)
    {
        GameObject canvasGO = new GameObject("Avisos", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(raiz, false);
        canvasGO.layer = 5;
        Canvas c = canvasGO.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 100;   // siempre encima
        CanvasScaler cs = canvasGO.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1080, 1920);
        cs.matchWidthOrHeight = 0.5f;

        GameObject fondo = Nuevo("FondoAviso", canvasGO.transform, typeof(Image));
        // Laterales con Margen (como en ejecución); el redondeo, la sombra y la animación los pone InterfazCampus
        Anclar(fondo, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -ArribaAviso), new Vector2(-2f * Margen, AltoAviso));
        Image img = fondo.GetComponent<Image>();
        img.color = UIRapida.FondoAviso;
        img.raycastTarget = false;

        TMP_Text t = Texto(fondo.transform, "TextoAviso", "", UIRapida.TamCuerpo, Color.white, false, TextAlignmentOptions.Center);
        Estirar(t.gameObject, Esp2);

        ic.fondoAviso = fondo;
        ic.textoAviso = t;
        fondo.SetActive(false);
    }

    // ==================================================================
    // Utilidades
    // ==================================================================
    private static T Buscar<T>(Scene escena) where T : Component
    {
        foreach (GameObject r in escena.GetRootGameObjects())
        {
            T c = r.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    private static Transform Opcional(Transform raiz, string nombre, bool soloHijosDirectos = false)
    {
        if (soloHijosDirectos)
        {
            foreach (Transform h in raiz) if (h.name == nombre) return h;
            return null;
        }
        return Profundo(raiz, n => n == nombre);
    }

    private static Transform Requerido(Transform raiz, string nombre, bool soloHijosDirectos = false)
    {
        Transform t = Opcional(raiz, nombre, soloHijosDirectos);
        if (t == null) throw new Exception($"No encontré '{nombre}' dentro de '{raiz.name}'.");
        return t;
    }

    // Búsqueda en anchura: devuelve el más cercano a la raíz con ese nombre
    private static Transform Profundo(Transform raiz, Func<string, bool> coincide)
    {
        Queue<Transform> cola = new Queue<Transform>();
        cola.Enqueue(raiz);
        while (cola.Count > 0)
        {
            Transform t = cola.Dequeue();
            if (t != raiz && coincide(t.name)) return t;
            foreach (Transform h in t) cola.Enqueue(h);
        }
        return null;
    }

    private static Button BotonEn(Transform raiz, string nombre)
    {
        Button b = Requerido(raiz, nombre).GetComponent<Button>();
        if (b == null) throw new Exception($"'{nombre}' no tiene componente Button.");
        return b;
    }

    private static TMP_Text TextoEn(Transform raiz, Func<string, bool> coincide)
    {
        Transform t = Profundo(raiz, coincide);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static TMP_Text TextoBajo(Transform raiz, string nombre)
    {
        Transform t = Requerido(raiz, nombre);
        return t.GetComponentInChildren<TMP_Text>(true);
    }

    // Cambia el primer texto que contenga 'fragmento'
    private static void CambiarTexto(Transform raiz, string fragmento, string nuevo)
    {
        foreach (TMP_Text t in raiz.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.text != null && t.text.IndexOf(fragmento, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                t.text = nuevo;
                return;
            }
        }
    }

    private static void CambiarTextoDe(Button b, string nuevo)
    {
        if (b == null) return;
        TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = nuevo;
    }

    private static InputField ConfigurarCampo(InputField campo, string placeholder, InputField.ContentType tipo, int limite)
    {
        if (campo == null) throw new Exception("Falta un campo de texto en el diseño.");
        campo.contentType = tipo;
        campo.characterLimit = limite;
        campo.text = "";
        Text ph = campo.placeholder as Text;
        if (ph != null) ph.text = placeholder;
        // Roboto también en los campos antiguos (texto y placeholder): el Font importado sí se guarda en la escena
        UIRapida.FuenteLegacyEn(campo.textComponent);
        UIRapida.FuenteLegacyEn(ph);
        EditorUtility.SetDirty(campo);
        return campo;
    }

    private static Button HacerBoton(GameObject go)
    {
        Button b = go.GetComponent<Button>();
        if (b == null) b = go.AddComponent<Button>();   // (sin '??': en el Editor GetComponent devuelve un "null falso")
        Image img = go.GetComponentInChildren<Image>(true);
        if (img != null) b.targetGraphic = img;
        return b;
    }

    private static Button ClonarBoton(GameObject modelo, Transform padre, string nombre, string texto)
    {
        GameObject copia = UnityEngine.Object.Instantiate(modelo, padre);
        copia.name = nombre;
        copia.SetActive(true);
        Button b = copia.GetComponent<Button>();
        for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
        CambiarTextoDe(b, texto);

        // El ícono del botón original no aplica a las copias
        foreach (Transform hijo in copia.transform)
            if (hijo.GetComponent<TMP_Text>() == null && hijo.GetComponentInChildren<TMP_Text>(true) == null)
                hijo.gameObject.SetActive(false);
        return b;
    }

    // Botón secundario (Cerrar, Cancelar, Usar mi ubicación): Menta con texto SobreMenta, como UIRapida.BotonSecundario
    private static void Secundario(Button b)
    {
        if (b == null) return;
        Image img = b.GetComponent<Image>();
        if (img != null) img.color = Menta;
        foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true)) t.color = UIRapida.SobreMenta;
    }

    private static RectOffset Relleno(float izq, float der, float arriba, float abajo) =>
        new RectOffset(Mathf.RoundToInt(izq), Mathf.RoundToInt(der), Mathf.RoundToInt(arriba), Mathf.RoundToInt(abajo));

    private static GameObject Nuevo(string nombre, Transform padre, params Type[] componentes)
    {
        List<Type> tipos = new List<Type> { typeof(RectTransform) };
        tipos.AddRange(componentes);
        GameObject go = new GameObject(nombre, tipos.ToArray());
        go.layer = 5;   // UI
        go.transform.SetParent(padre, false);
        return go;
    }

    private static TMP_Text Texto(Transform padre, string nombre, string texto, float tam, Color color, bool negrita,
                                  TextAlignmentOptions alineacion)
    {
        GameObject go = Nuevo(nombre, padre, typeof(TextMeshProUGUI));
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.font = negrita ? fuenteNegrita : fuente;
        if (negrita) t.fontStyle = FontStyles.Bold;
        t.text = texto;
        t.fontSize = tam;
        t.color = color;
        t.alignment = alineacion;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static void Anclar(GameObject go, Vector2 min, Vector2 max, Vector2 pivote, Vector2 posicion, Vector2 tamano)
    {
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;
    }

    private static void Estirar(GameObject go, float margen)
    {
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margen, margen);
        rt.offsetMax = new Vector2(-margen, -margen);
    }

    private static void Alto(GameObject go, float alto)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = alto;
        le.preferredHeight = alto;
        le.flexibleHeight = 0;
    }

    private static void AsignarRef(SerializedObject so, string campo, UnityEngine.Object valor)
    {
        SerializedProperty p = so.FindProperty(campo);
        if (p == null) throw new Exception($"{so.targetObject.GetType().Name} no tiene el campo '{campo}'.");
        p.objectReferenceValue = valor;
    }
}
