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

/// <summary> InterfazCampus: menú principal, login y registro de administrador. </summary>
public partial class InterfazCampus
{
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

    private bool accesoEnCurso;   // evita un segundo toque mientras se verifica (se libera en OnDisable)
    private string textoBotonEntrar, textoBotonRegistrar;   // textos originales para restaurarlos en OnDisable
    // Sube en OnDisable: un callback de login/registro que llega después (la corrutina ya murió) se ignora
    private int generacionAcceso;

    // ¿El callback pertenece a una espera que ya no existe? Entonces solo limpia la contraseña y libera el botón.
    private bool AccesoVencido(int generacion, InputField llave, Button boton)
    {
        if (generacion == generacionAcceso && isActiveAndEnabled) return false;
        if (llave != null) llave.text = "";
        if (!accesoEnCurso) UIRapida.EstadoDeshabilitado(boton, false);
        return true;
    }

    private void Entrar()
    {
        if (accesoEnCurso || !isActiveAndEnabled) return;
        StartCoroutine(EntrarConProgreso());
    }

    private IEnumerator EntrarConProgreso()
    {
        string usuario = loginUsuario != null ? loginUsuario.text.Trim() : "";
        string llave = loginLlave != null ? loginLlave.text : "";
        Mostrar(loginOk, false);
        Mostrar(loginError, false);   // el error del intento anterior no se queda visible durante el progreso

        if (usuario.Length == 0 || llave.Length == 0)
        {
            Error(loginError, "Escribe tu usuario y tu contraseña.");
            yield break;
        }
        if (admin == null) { Error(loginError, "No se pudo verificar el usuario."); yield break; }

        // Estado de progreso: el botón dice qué pasa y se bloquea; se espera un cuadro para que se vea
        accesoEnCurso = true;
        textoBotonEntrar = PonerProgreso(btnEntrar, "Verificando…");
        yield return null;

        // La verificación (PBKDF2) corre en otro hilo: el callback llega unos fotogramas después, siempre
        // en el hilo principal. Se espera sin límite de tiempo con "Verificando…" y el botón deshabilitado.
        bool termino = false;
        int generacion = generacionAcceso;
        try
        {
            admin.IniciarSesion(usuario, llave, (ok, mensaje) =>
            {
                termino = true;
                if (AccesoVencido(generacion, loginLlave, btnEntrar)) return;
                if (loginLlave != null) loginLlave.text = "";   // la contraseña nunca se queda escrita

                if (!ok) { Error(loginError, mensaje); return; }

                Mostrar(loginError, false);
                if (ui == null) return;
                if (ui.PantallaActual != PLogin)
                {
                    // Salió del login mientras se verificaba: no se deja una sesión abierta que ya no pidió
                    // (se cierra en silencio, sin saludo ni aviso de cierre)
                    ui.SilenciarProximoCierreDeSesion();
                    if (admin != null) admin.CerrarSesion();
                    return;
                }
                ui.Reemplazar(PAdmin);   // el login no queda en el historial
                // Saludo con el nombre guardado de la cuenta (no con lo que se tecleó: mayúsculas/espacios)
                string nombre = admin != null && !string.IsNullOrEmpty(admin.NicknameActual) ? admin.NicknameActual : usuario;
                ui.Avisar($"Hola, {TextoSeguro(nombre)}.");
            });
            while (!termino) yield return null;
        }
        finally
        {
            QuitarProgreso(btnEntrar, textoBotonEntrar);
            textoBotonEntrar = null;
            accesoEnCurso = false;
        }
    }

    private void Registrar()
    {
        if (accesoEnCurso || !isActiveAndEnabled) return;
        StartCoroutine(RegistrarConProgreso());
    }

    private IEnumerator RegistrarConProgreso()
    {
        Mostrar(regOk, false);
        Mostrar(regError, false);

        if (regTerminos != null && !regTerminos.isOn)
        {
            Error(regError, "Debes aceptar los términos de uso.");
            yield break;
        }
        if (admin == null) { Error(regError, "No se pudo registrar la cuenta. Intenta de nuevo."); yield break; }

        string usuario = regUsuario != null ? regUsuario.text.Trim() : "";
        string llave = regLlave != null ? regLlave.text : "";
        string codigo = regCodigo != null ? regCodigo.text : "";

        accesoEnCurso = true;
        textoBotonRegistrar = PonerProgreso(btnRegistrar, "Creando cuenta…");
        yield return null;

        bool termino = false;   // el registro hace el cálculo pesado en segundo plano: el callback llega después
        int generacion = generacionAcceso;
        try
        {
            admin.Registrar(usuario, llave, codigo, (ok, mensaje) =>
            {
                termino = true;
                if (AccesoVencido(generacion, regLlave, btnRegistrar)) return;
                if (regLlave != null) regLlave.text = "";

                if (!ok) { Error(regError, mensaje); return; }

                Mostrar(regError, false);
                if (ui != null && ui.PantallaActual != PRegistro) ui.Avisar(mensaje);   // salió del registro mientras se creaba
                if (regOk != null)
                {
                    regOk.text = mensaje;
                    Mostrar(regOk, true);
                }
                if (regCodigo != null) regCodigo.text = "";
                if (regUsuario != null) regUsuario.text = "";
                if (regTerminos != null) regTerminos.isOn = false;
                if (loginUsuario != null) loginUsuario.text = usuario;   // listo para entrar
                if (isActiveAndEnabled) StartCoroutine(VolverAlLoginLuego());
            });
            // Sin límite de tiempo: AdminService siempre llama el callback (también si se interrumpe)
            while (!termino) yield return null;
        }
        finally
        {
            QuitarProgreso(btnRegistrar, textoBotonRegistrar);
            textoBotonRegistrar = null;
            accesoEnCurso = false;
        }
    }

    // Cacheada: WaitForSecondsRealtime se reinicia sola al terminar, así no se crea una por registro
    private static readonly WaitForSecondsRealtime EsperaVolverAlLogin = new WaitForSecondsRealtime(1.8f);

    private IEnumerator VolverAlLoginLuego()
    {
        yield return EsperaVolverAlLogin;   // no depende de timeScale
        if (ui != null && ui.PantallaActual == PRegistro) ui.Atras();
    }

    // Texto de progreso en un botón del diseño (Text antiguo o TMP); devuelve el texto original
    private static string PonerProgreso(Button b, string progreso)
    {
        if (b == null) return null;
        UIRapida.EstadoDeshabilitado(b, true);   // fondo plano y texto Oscuro (≥4,5:1), no el tinte gris de "presionado"
        Text t = b.GetComponentInChildren<Text>(true);
        if (t != null) { t.color = UIRapida.TextoDeshabilitado; string antes = t.text; t.text = progreso; return antes; }
        TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { string antes = tmp.text; tmp.text = progreso; return antes; }
        return null;
    }

    private static void QuitarProgreso(Button b, string original)
    {
        if (b == null) return;
        UIRapida.EstadoDeshabilitado(b, false);   // devuelve los colores y reactiva el botón
        Text t = b.GetComponentInChildren<Text>(true);
        if (t != null) t.color = UIRapida.SobreColor;   // Text antiguo (Entrar/Registrar): blanco sobre la píldora verde
        if (original == null) return;
        if (t != null) { t.text = original; return; }
        TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = original;
    }

    // ==================================================================
    // Campos del login y del registro (InputField antiguos de la escena): contraste y "mostrar contraseña".
    // El registro conserva exactamente 3 campos (usuario, contraseña, código): no se agrega "confirmar".
    // ==================================================================
    private readonly List<(InputField campo, Image ojo)> camposContrasena = new List<(InputField, Image)>();

    private void EstilizarCamposAcceso()
    {
        // Login, registro y el formulario de texto de respaldo (fNombre…fLongitud): el mismo estilo de campo
        foreach (InputField f in new[] { loginUsuario, loginLlave, regUsuario, regLlave, regCodigo,
                                         fNombre, fCategoria, fDescripcion, fIndicacion, fHorario, fLatitud, fLongitud })
            EstilizarCampoAntiguo(f);
        AreaToqueCasilla(regTerminos);

        if (loginLlave != null) loginLlave.contentType = InputField.ContentType.Password;
        if (regLlave != null) regLlave.contentType = InputField.ContentType.Password;
        BotonMostrarContrasena(loginLlave);
        BotonMostrarContrasena(regLlave);

        // "Entrar" y "Registrar" (#255e1f de la escena, con capa interna): la misma píldora Verde de toda la app.
        // Se aplica al iniciar, antes de cualquier PonerProgreso, para que el texto restaurado conserve el estilo.
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        if (btnEntrar != null) EstiloBotonPrincipal(btnEntrar, fuente);
        if (btnRegistrar != null) EstiloBotonPrincipal(btnRegistrar, fuente);

        // Mensajes de resultado: verde de la marca (contraste ≥ 4,5:1) y nunca por debajo del mínimo
        if (regOk != null) regOk.color = UIRapida.Verde;
        if (loginOk != null) { loginOk.color = UIRapida.Verde; loginOk.text = "Sesión iniciada."; }   // la escena traía #15b21e (2,83:1)
        if (loginError != null) loginError.color = UIRapida.Rojo;   // el rojo de la escena (#760E0E) no es el de la app
        if (regError != null) regError.color = UIRapida.Rojo;
        foreach (TMP_Text t in new[] { regOk, regError, loginOk, loginError })
            if (t != null) t.fontSizeMin = UIRapida.TamMinimo;
    }

    // Texto oscuro, ayuda en gris (sin cursiva), fondo de campo redondeado y letra del tamaño del cuerpo.
    // Tipografía: estos InputField antiguos (uGUI) no usan TMP; se les asigna la misma Roboto Regular de la app
    // como Font (UIRapida.FuenteLegacy). No se migran a TMP_InputField (cambiaría la escena y la integración).
    private static void EstilizarCampoAntiguo(InputField f)
    {
        if (f == null) return;
        Image fondo = f.targetGraphic as Image;
        if (fondo == null) fondo = f.GetComponent<Image>();
        if (fondo != null)
        {
            UIRapida.Redondear(fondo, UIRapida.RadioControl);
            fondo.color = UIRapida.FondoCampo;
            // Contorno BordeCampo (el campo se distingue del blanco, WCAG 1.4.11); con foco Verde y más grueso
            Image borde = UIRapida.BordeDe(fondo, UIRapida.RadioControl);
            UIRapida.IndicadorFoco(f, borde, UIRapida.RadioControl);
        }
        if (f.textComponent != null)
        {
            f.textComponent.color = UIRapida.Oscuro;
            f.textComponent.fontSize = Mathf.RoundToInt(UIRapida.TamCuerpo);
            f.textComponent.fontStyle = FontStyle.Normal;
            UIRapida.FuenteLegacyEn(f.textComponent);   // Roboto Regular
        }
        if (f.placeholder is Text ayuda)
        {
            ayuda.color = UIRapida.Gris;
            ayuda.fontStyle = FontStyle.Normal;
            ayuda.fontSize = Mathf.RoundToInt(UIRapida.TamCuerpo);
            UIRapida.FuenteLegacyEn(ayuda);
        }
        else if (f.placeholder is TMP_Text ayudaTmp)
        {
            ayudaTmp.color = UIRapida.Gris;
            ayudaTmp.fontStyle = FontStyles.Normal; UIRapida.Peso(ayudaTmp, false);
        }
        f.caretColor = UIRapida.Verde;
        f.customCaretColor = true;

        UIRapida.EstadosCampo(f);   // los mismos estados que UIRapida.Campo
    }

    // Botón con ícono de ojo a la derecha del campo: muestra u oculta lo escrito
    private void BotonMostrarContrasena(InputField campo)
    {
        if (campo == null || campo.transform.Find("MostrarContrasena") != null) return;

        Image area = UIRapida.Panel("MostrarContrasena", campo.transform, UIRapida.Transparente);
        area.sprite = UIRapida.Circulo();   // el tinte al tocar es un círculo (no un cuadro gris)
        area.preserveAspect = true;
        UIRapida.Anclar(area.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        Vector2.zero, new Vector2(UIRapida.ToqueMinimo, UIRapida.ToqueMinimo));
        Button b = area.gameObject.AddComponent<Button>();
        b.targetGraphic = area;
        UIRapida.EstadosTransparente(b);   // el mismo tinte claro (PresionadoClaro) de los botones sobre fondo claro

        Image ojo = UIRapida.Panel("Icono", area.transform, UIRapida.Gris);
        ojo.sprite = UIRapida.Icono("ojo");
        ojo.preserveAspect = true;
        ojo.raycastTarget = false;
        UIRapida.Anclar(ojo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));

        // El texto no pasa por debajo del ojo
        if (campo.textComponent != null)
        {
            RectTransform rt = campo.textComponent.rectTransform;
            rt.offsetMax = new Vector2(rt.offsetMax.x - UIRapida.ToqueMinimo, rt.offsetMax.y);
        }
        if (campo.placeholder != null)
        {
            RectTransform rp = campo.placeholder.rectTransform;
            rp.offsetMax = new Vector2(rp.offsetMax.x - UIRapida.ToqueMinimo, rp.offsetMax.y);
        }

        camposContrasena.Add((campo, ojo));
        b.onClick.AddListener(() => MostrarContrasena(campo, ojo, campo.contentType == InputField.ContentType.Password));
    }

    private static void MostrarContrasena(InputField campo, Image ojo, bool ver)
    {
        if (campo == null) return;
        campo.contentType = ver ? InputField.ContentType.Standard : InputField.ContentType.Password;
        campo.ForceLabelUpdate();
        if (ojo != null)
        {
            ojo.sprite = UIRapida.Icono(ver ? "ojo_tachado" : "ojo");   // tachado = tocar para ocultar
            ojo.color = ver ? UIRapida.Verde : UIRapida.Gris;
        }
    }

    // ==================================================================
    // Términos de uso: la etiqueta junto a la casilla abre el texto completo (modal de solo lectura)
    // ==================================================================
    private const string TituloTerminos = "Términos de uso";
    private const string TextoTerminos =
        "Términos de uso del modo administrador:\n" +
        "1) La cuenta es personal e intransferible.\n" +
        "2) Solo se registra información institucional y verificable sobre los espacios del campus.\n" +
        "3) No se publican datos personales de estudiantes ni funcionarios.\n" +
        "4) El acceso puede retirarse en cualquier momento.";

    private void PrepararEnlaceTerminos()
    {
        if (regTerminos == null) return;
        Canvas canvas = regTerminos.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;

        TMP_Text etiqueta = null;
        foreach (TMP_Text t in canvas.rootCanvas.GetComponentsInChildren<TMP_Text>(true))
            if (t.text.Contains("términos de uso") || t.text.Contains("términos y condiciones")) { etiqueta = t; break; }
        if (etiqueta == null || etiqueta.GetComponent<Button>() != null) return;

        // Se ve como enlace: la parte "términos de uso" en verde y subrayada (mismo nombre que el modal)
        etiqueta.richText = true;
        etiqueta.text = $"Acepto los <color=#{ColorUtility.ToHtmlStringRGB(UIRapida.Verde)}><u>términos de uso</u></color>";
        etiqueta.raycastTarget = true;

        // Área táctil de al menos ToqueMinimo de alto
        RectTransform rt = etiqueta.rectTransform;
        if (rt.rect.height < UIRapida.ToqueMinimo)
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + (UIRapida.ToqueMinimo - rt.rect.height));

        // Respuesta visible al tocar: fondo hermano DETRÁS del texto (un hijo lo taparía), transparente en reposo y
        // PresionadoClaro al presionar. Copia el rect del texto (área de ToqueMinimo) y no participa del layout.
        Image fondoEnlace = UIRapida.Panel("FondoEnlaceTerminos", rt.parent, Color.white);
        UIRapida.Redondear(fondoEnlace, UIRapida.RadioControl);
        fondoEnlace.raycastTarget = false;
        fondoEnlace.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        fondoEnlace.transform.SetSiblingIndex(rt.GetSiblingIndex());   // justo antes: se dibuja detrás
        fondoEnlace.gameObject.AddComponent<SeguirRect>().objetivo = rt;
        RectTransform rf = fondoEnlace.rectTransform;
        rf.anchorMin = rt.anchorMin; rf.anchorMax = rt.anchorMax; rf.pivot = rt.pivot;
        rf.anchoredPosition = rt.anchoredPosition; rf.sizeDelta = rt.sizeDelta;

        Button enlace = etiqueta.gameObject.AddComponent<Button>();
        enlace.transition = Selectable.Transition.ColorTint;
        enlace.targetGraphic = fondoEnlace;
        UIRapida.EstadosTransparente(enlace);   // el tinte multiplica el blanco del fondo (alfa incluido)
        fondoEnlace.canvasRenderer.SetColor(UIRapida.Transparente);   // arranca invisible (sin esperar la transición)
        enlace.onClick.AddListener(() => MostrarInformacion(TituloTerminos, TextoTerminos, regTerminos));
    }

    private const float LadoCasilla = UIRapida.TamIconoBoton;   // casilla visible (56, token de íconos) (el área táctil es ToqueMinimo)

    /// <summary>
    /// Casilla de términos: caja blanca con contorno BordeCampo (el sprite por defecto tenía ~1,5:1), visible de
    /// al menos 56 × 56, marca "check" en Verde, y área táctil de al menos ToqueMinimo × ToqueMinimo (hijo
    /// transparente "AreaToque" centrado en la casilla). No cambia su lugar en el diseño.
    /// </summary>
    private static void AreaToqueCasilla(Toggle casilla)
    {
        if (casilla == null || casilla.transform.Find("AreaToque") != null) return;
        RectTransform rc = (RectTransform)casilla.transform;

        Image caja = casilla.targetGraphic as Image;
        if (caja != null)
        {
            RectTransform rcaja = caja.rectTransform;
            if (rcaja.rect.width < LadoCasilla || rcaja.rect.height < LadoCasilla)
                rcaja.sizeDelta += new Vector2(Mathf.Max(0f, LadoCasilla - rcaja.rect.width),
                                               Mathf.Max(0f, LadoCasilla - rcaja.rect.height));
            caja.color = UIRapida.SuperficieOpaca;
            UIRapida.Redondear(caja, UIRapida.RadioMinimo);
            UIRapida.BordeDe(caja, UIRapida.RadioMinimo);
        }
        if (casilla.graphic is Image marca)
        {
            marca.sprite = UIRapida.Icono("check");
            marca.color = UIRapida.Verde;
            marca.preserveAspect = true;
            marca.type = Image.Type.Simple;
            if (caja != null && marca.transform.IsChildOf(caja.transform))
            {
                // Centrada en la caja, con margen (la marca no toca el contorno)
                float lado = LadoCasilla * 0.7f;
                UIRapida.Anclar(marca.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                Vector2.zero, new Vector2(lado, lado));
            }
            marca.transform.SetAsLastSibling();   // por encima del contorno
        }

        Canvas.ForceUpdateCanvases();
        float ancho = Mathf.Max(rc.rect.width, UIRapida.ToqueMinimo), alto = Mathf.Max(rc.rect.height, UIRapida.ToqueMinimo);
        Image area = UIRapida.Panel("AreaToque", casilla.transform, UIRapida.Transparente);   // alfa 0: invisible, recibe toques
        area.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Anclar(area.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(ancho, alto));
        area.transform.SetAsLastSibling();   // encima de la casilla: el toque llega al Toggle (padre)
    }

    /// <summary>
    /// Aviso de la base de datos que debe verse una vez (p. ej. una cuenta que no pudo conservarse al actualizar).
    /// BaseDatosLocal.AvisoPendiente se consume al leerlo: se muestra una sola vez como aviso de error.
    /// </summary>
    private void MostrarAvisoPendienteBaseDatos()
    {
        string aviso = BaseDatosLocal.AvisoPendiente;
        if (string.IsNullOrEmpty(aviso)) return;
        if (ui != null) ui.Avisar(aviso, true);
        else Error(loginError, aviso);
    }

    // Al entrar o salir de las pantallas de acceso la contraseña vuelve a ocultarse
    private void OcultarContrasenas()
    {
        foreach (var (campo, ojo) in camposContrasena) MostrarContrasena(campo, ojo, false);
    }
}
