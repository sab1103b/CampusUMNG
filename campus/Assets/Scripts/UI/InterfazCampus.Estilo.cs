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

/// <summary> InterfazCampus: estilo de la pantalla de usuario y del menú principal. </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Estilo de la pantalla de usuario: buscador, categorías, barra de ruta y "Explorar campus"
    // (se aplica sobre los objetos del diseño al iniciar)
    // ==================================================================
    private static readonly Color ColorTextoAyuda = UIRapida.Gris;   // texto de ayuda con contraste ≥ 4,5:1
    private const float AlfaDegradadoFoto = 1f;      // tinte del Image (el alfa real lo da el sprite DegradadoLeyenda)
    private const float AlfaMaximoLeyenda = 0.8f;    // abajo nunca opaco del todo: la foto se sigue intuyendo
    private const float AltoDegradadoFoto = 0.75f;   // fracción de la foto que cubre el degradado (desde abajo)
    private const float AlfaMinimoLeyenda = 0.55f;   // alfa mínimo del degradado en su mitad baja (donde va la leyenda)

    // Nombres y textos del diseño (escena Copia-Santiago) que el estilo busca para ajustarlos. La escena no se
    // edita desde código: si alguno cambia de nombre, ese ajuste se omite y queda una advertencia en consola.
    private const string EscenaMenuPrincipal = "MenuPrincipal";       // contenedor del menú principal
    private const string EscenaMarcoFoto = "Image (1)";               // marco de la foto del campus (su hijo 0 es la foto)
    private const string EscenaBarraTitulo = "Barra";                 // línea bajo "Campus Conecta"
    private const string EscenaTituloApp = "Campus Conecta";          // título del menú (texto)
    private const string EscenaLemaViejo1 = "mas cerca";              // lema del diseño sin tildes (texto)
    private const string EscenaLemaViejo2 = "Encuentra tu lugar";
    private const string LemaMenu = "Tu campus, más cerca. Encuentra tu lugar en Cajicá.";
    private const string EscenaClimaTemperatura = "VariableClima";    // textos del ClimaController
    private const string EscenaClimaDescripcion = "ZOnadrecha";

    private static readonly HashSet<string> advertenciasEscena = new HashSet<string>();

    // Advertencia única por elemento del diseño que no se encontró
    private static void AvisarFaltaEnEscena(string nombre, string ajuste)
    {
        if (!advertenciasEscena.Add(nombre)) return;
        Debug.LogWarning($"Estilo: no encontré \"{nombre}\" en la escena; se omite {ajuste}.");
    }
    // Leyenda de la foto del menú: título (TamTitulo) + detalle (TamSecundario) con su separación
    private const float AltoLeyenda = UIRapida.TamTitulo + UIRapida.Esp1 + UIRapida.TamSecundario + UIRapida.Esp3;
    private const float AltoDetalleLeyenda = UIRapida.TamSecundario + UIRapida.Esp2;
    private readonly List<(Button boton, Image fondo, TMP_Text texto, string categoria)> chipsEstilo =
        new List<(Button, Image, TMP_Text, string)>();

    private void EstilizarPantallaUsuario()
    {
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        // ---------- Buscador: píldora blanca ----------
        if (buscador != null)
        {
            Image fondo = buscador.targetGraphic as Image;
            if (fondo != null)
            {
                fondo.color = UIRapida.Superficie;
                buscador.targetGraphic = ElevarConSombra(fondo, UIRapida.RadioPildora);   // flota sobre el mapa
            }
            if (buscador.placeholder is TMP_Text ayuda)
            {
                ayuda.color = ColorTextoAyuda;
                ayuda.fontStyle = FontStyles.Normal; UIRapida.Peso(ayuda, false);
                AjustarTextoCampo((RectTransform)ayuda.transform);
            }
            if (buscador.textComponent != null)
            {
                buscador.textComponent.color = UIRapida.Oscuro;
                AjustarTextoCampo((RectTransform)buscador.textComponent.rectTransform);
            }
            buscador.caretColor = UIRapida.Verde;
            buscador.customCaretColor = true;
            buscador.selectionColor = UIRapida.SeleccionTexto;
        }

        // ---------- Categorías: chips con esquinas redondeadas (RadioEtiqueta, decisión del dueño); la activa en verde ----------
        chipsEstilo.Clear();
        if (chips != null)
        {
            foreach (Button chip in chips)
            {
                if (chip == null) continue;
                Image fondo = chip.targetGraphic as Image;
                TMP_Text texto = chip.GetComponentInChildren<TMP_Text>(true);
                if (fondo == null || texto == null) continue;

                AreaToqueChip((RectTransform)chip.transform);   // antes de elevar: mide el alto real del chip
                fondo = ElevarConSombra(fondo, UIRapida.RadioEtiqueta);   // esquinas redondeadas (no píldora) + sombra suave
                chip.targetGraphic = fondo;
                // El color y el tinte al presionar los pone ActualizarChips (cambian con el chip activo)
                UIRapida.EstadosBoton(chip);
                UIRapida.PesoMedio(texto);   // Medium; solo el chip activo va en Bold (ActualizarChips)
                texto.enableAutoSizing = true;
                texto.fontSizeMin = UIRapida.TamMinimo;
                texto.fontSizeMax = UIRapida.TamSecundario;
                texto.characterSpacing = 0f;
                chipsEstilo.Add((chip, fondo, texto, texto.text.Trim()));
            }
        }
        ActualizarChips();

        // ---------- Lista de resultados: filas claras redondeadas (la plantilla se estiliza antes de clonarla) ----------
        if (plantillaResultado != null && plantillaResultado.targetGraphic is Image fondoFila)
        {
            fondoFila.color = UIRapida.FondoCampo;
            UIRapida.Redondear(fondoFila, UIRapida.RadioControl);
            UIRapida.EstadosBotonClaro(plantillaResultado);   // fila clara: al presionar, PresionadoClaro (los clones lo heredan)
        }
        if (textoSinResultados != null) textoSinResultados.color = UIRapida.Gris;   // contraste ≥ 4,5:1 (la escena daba 4,1:1)

        // ---------- "Explorar campus": píldora verde sin marco ----------
        if (btnExplorarAbajo != null) EstiloBotonPrincipal(btnExplorarAbajo, fuente);

        // ---------- Clima: píldora blanca con sombra (temperatura + descripción) ----------
        PrepararPildoraClima();

        // ---------- Barra de ruta: tarjeta en RutaColor (el azul de la línea de ruta y del halo de tu posición;
        // el pin de destino va en DestinoColor, verde) ----------
        if (barraRuta != null)
        {
            Image fondo = barraRuta.GetComponent<Image>();

            HorizontalLayoutGroup hl = barraRuta.GetComponent<HorizontalLayoutGroup>();
            if (hl != null)
            {
                hl.padding = Relleno(UIRapida.Esp3, UIRapida.Esp3, UIRapida.Esp2, UIRapida.Esp2);
                hl.spacing = UIRapida.Esp2;
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.childForceExpandHeight = false;
            }

            if (barraRuta.transform.Find("IconoRuta") == null)
            {
                Image icono = UIRapida.Panel("IconoRuta", barraRuta.transform, UIRapida.SobreColor);
                icono.sprite = UIRapida.Icono("ruta");
                icono.preserveAspect = true;
                icono.raycastTarget = false;
                LayoutElement le = icono.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = UIRapida.TamIconoGrande;
                icono.transform.SetAsFirstSibling();
            }
            if (fondo != null)
            {
                fondo.color = UIRapida.RutaColor;   // texto e ícono blancos encima: 4,94:1
                ElevarConSombra(fondo, UIRapida.RadioTarjeta);   // cara y sombra detrás del ícono (fuera del layout)
            }

            TMP_Text textoRuta = barraRuta.GetComponentInChildren<TMP_Text>(true);
            if (textoRuta != null && textoRuta.transform.parent == barraRuta.transform)
            {
                textoRuta.enableAutoSizing = true;
                textoRuta.fontSizeMin = UIRapida.TamSecundario;
                textoRuta.fontSizeMax = UIRapida.TamCuerpo;
                textoRuta.lineSpacing = 0f;
                textoRuta.color = UIRapida.SobreColor;
                // Una indicación larga no desborda la barra (alto AltoBarraRuta): como máximo 4 renglones y "…"
                textoRuta.textWrappingMode = TextWrappingModes.Normal;
                textoRuta.overflowMode = TextOverflowModes.Ellipsis;
                textoRuta.maxVisibleLines = 4;
                LayoutElement le = textoRuta.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = UIRapida.ToqueMinimo + 2f * UIRapida.Esp3;   // hasta dos renglones
            }
        }

        if (btnTerminarRuta != null)
        {
            // Píldora blanca con texto RutaColor sobre la barra azul (4,94:1)
            Image fondo = btnTerminarRuta.targetGraphic as Image;
            if (fondo != null) { UIRapida.Redondear(fondo, UIRapida.RadioPildora); fondo.color = UIRapida.SuperficieOpaca; }
            UIRapida.EstadosBotonClaro(btnTerminarRuta);
            LayoutElement le = btnTerminarRuta.GetComponent<LayoutElement>();
            if (le != null) { le.preferredWidth = UIRapida.AnchoBotonTerminar; le.preferredHeight = UIRapida.ToqueMinimo; }
            TMP_Text t = UIRapida.TextoDe(btnTerminarRuta);
            if (t != null)
            {
                t.color = UIRapida.RutaColor;
                UIRapida.Peso(t, true);
                t.enableAutoSizing = true;
                t.fontSizeMin = UIRapida.TamSecundario;
                t.fontSizeMax = UIRapida.TamCuerpo;
                t.characterSpacing = 0f;
            }
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
        while (menu != null && menu.name != EscenaMenuPrincipal) menu = menu.parent;
        if (menu == null) AvisarFaltaEnEscena(EscenaMenuPrincipal, "la foto del menú");
        Canvas canvas = btnExplorar.GetComponentInParent<Canvas>(true);

        // ---------- Textos del encabezado (Panel_Menu) ----------
        if (canvas != null)
        {
            bool lema = false, titulo = false;
            foreach (TMP_Text t in canvas.rootCanvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.text == LemaMenu) lema = true;
                else if (t.text.Contains(EscenaLemaViejo1) || t.text.Contains(EscenaLemaViejo2)) { t.text = LemaMenu; lema = true; }
                if (t.text.Trim() == EscenaTituloApp) { t.color = UIRapida.Verde; titulo = true; }
            }
            if (!lema) AvisarFaltaEnEscena(EscenaLemaViejo2, "el lema con tildes");
            if (!titulo) AvisarFaltaEnEscena(EscenaTituloApp, "el color del título");
            Transform barra = BuscarHijo(canvas.rootCanvas.transform, EscenaBarraTitulo);
            if (barra == null) AvisarFaltaEnEscena(EscenaBarraTitulo, "la línea redondeada del título");
            else if (barra.GetComponentInChildren<Image>(true) is Image linea) UIRapida.Redondear(linea, UIRapida.RadioMinimo);
        }

        // ---------- Foto del campus: redondeada, con sombra y leyenda ----------
        Transform marco = menu != null ? BuscarHijo(menu, EscenaMarcoFoto) : null;
        if (menu != null && marco == null) AvisarFaltaEnEscena(EscenaMarcoFoto, "la portada de la foto");
        Image foto = marco != null && marco.childCount > 0 ? marco.GetChild(0).GetComponent<Image>() : null;
        Image imgMarco = marco != null ? marco.GetComponent<Image>() : null;
        if (imgMarco != null && foto != null && marco.Find("Leyenda") == null)
        {
            // Marco redondeado; la foto lo llena entero. Sin Mask de stencil (dejaba la esquina dentada): al final se
            // le superponen esquinas invertidas antialiasadas del color del fondo (UIRapida.MarcoEsquinas)
            UIRapida.Redondear(imgMarco, UIRapida.RadioTarjeta);
            imgMarco.color = Color.white;
            RectTransform rf = foto.rectTransform;
            rf.anchorMin = Vector2.zero;
            rf.anchorMax = Vector2.one;
            rf.offsetMin = rf.offsetMax = Vector2.zero;
            foto.preserveAspect = false;

            // Sombra detrás del marco: la misma de toda la app (borde difuminado)
            UIRapida.SombraDe(imgMarco);

            // Degradado oscuro abajo (hasta el 75 % de la foto; en su mitad baja, donde va la leyenda, el negro
            // nunca baja de AlfaMinimoLeyenda) + leyenda en blanco opaco
            Image grad = UIRapida.Panel("Degradado", marco, UIRapida.ConAlfa(Color.white, AlfaDegradadoFoto));
            grad.sprite = DegradadoLeyenda();
            grad.raycastTarget = false;
            UIRapida.Anclar(grad.rectTransform, Vector2.zero, new Vector2(1f, AltoDegradadoFoto), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);

            RectTransform leyenda = UIRapida.Rect("Leyenda", marco);
            UIRapida.Anclar(leyenda, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, UIRapida.Esp3),
                            new Vector2(-2f * UIRapida.Esp3, AltoLeyenda));
            TMP_Text l1 = UIRapida.Texto(leyenda, "Sede", "Sede Campus · Cajicá", UIRapida.TamTitulo, UIRapida.SobreColor, fuente, true, TextAlignmentOptions.BottomLeft);
            UIRapida.Estirar((RectTransform)l1.transform, 0, AltoDetalleLeyenda, 0, 0);
            TMP_Text l2 = UIRapida.Texto(leyenda, "Detalle", "Universidad Militar Nueva Granada", UIRapida.TamSecundario, UIRapida.SobreColor, fuente, false, TextAlignmentOptions.BottomLeft);
            UIRapida.Estirar((RectTransform)l2.transform, 0, 0, 0, AltoLeyenda - AltoDetalleLeyenda);

            // Recorte suave: esquinas encima de foto, degradado y leyenda (último hermano), con la sombra horneada
            UIRapida.MarcoEsquinas((RectTransform)marco, ColorFondoDetras(marco), UIRapida.RadioTarjeta, conSombra: true);
        }

        // ---------- Botones ----------
        EstiloBotonPrincipal(btnExplorar, fuente);
        IconoEnBoton(btnExplorar, "pin_contorno", UIRapida.SobreColor);   // contorno en la UI; el relleno solo en el mapa

        if (btnModoAdmin != null)
        {
            EstiloBotonPrincipal(btnModoAdmin, fuente, UIRapida.Menta);
            TMP_Text t = UIRapida.TextoDe(btnModoAdmin);
            if (t != null) t.color = UIRapida.SobreMenta;   // sobre Menta el verde normal no da contraste
            PesoBotonSecundario(btnModoAdmin);
            IconoEnBoton(btnModoAdmin, "llave", UIRapida.SobreMenta);
        }

        if (btnCerrarSesion != null)
        {
            EstiloBotonPrincipal(btnCerrarSesion, fuente, UIRapida.Superficie);
            TMP_Text t = UIRapida.TextoDe(btnCerrarSesion);
            if (t != null) t.color = UIRapida.Rojo;
            PesoBotonSecundario(btnCerrarSesion);
            IconoEnBoton(btnCerrarSesion, "salir", UIRapida.Rojo);
        }
    }

    /// <summary>
    /// Color que se ve detrás del centro de <paramref name="t"/>: mezcla (alfa sobre alfa, en orden de dibujo) de
    /// las imágenes activas del mismo canvas que se dibujan antes y lo cubren (en el menú: el verde menta, el mapa
    /// tenue y el velo oscuro de "Fondo"). Una imagen con textura cuenta como su tinte por el gris medio de la
    /// textura (GrisMedioTextura): el fondo del menú es un mapa claro y casi uniforme a la escala de la esquina.
    /// Sin nada detrás: blanco.
    /// </summary>
    private static Color ColorFondoDetras(Transform t)
    {
        const float GrisMedioTextura = 0.9f;
        RectTransform rt = t as RectTransform;
        Canvas canvas = t.GetComponentInParent<Canvas>();
        if (rt == null || canvas == null) return Color.white;
        Vector3[] esq = new Vector3[4];
        rt.GetWorldCorners(esq);
        Vector3 centro = (esq[0] + esq[2]) * 0.5f;

        Color c = Color.white;
        foreach (Image img in canvas.rootCanvas.GetComponentsInChildren<Image>(false))
        {
            if (img.transform == t || img.transform.IsChildOf(t)) break;   // de aquí en adelante, encima o dentro
            // (los ancestros sí cuentan: se dibujan antes; la sombra propia no: MarcoEsquinas la hornea)
            if (!img.enabled || img.color.a <= 0.01f || img.name == t.name + "_Sombra") continue;
            img.rectTransform.GetWorldCorners(esq);
            if (centro.x < esq[0].x || centro.x > esq[2].x || centro.y < esq[0].y || centro.y > esq[2].y) continue;
            float k = img.sprite != null && img.type == Image.Type.Simple ? GrisMedioTextura : 1f;
            Color capa = new Color(img.color.r * k, img.color.g * k, img.color.b * k);
            c = Color.Lerp(c, capa, img.color.a);
        }
        c.a = 1f;
        return c;
    }

    // Degradado de la foto del menú: negro con a = 0,55 + 0,45·t^1,6 en la mitad baja (t = 0 en la mitad, 1 abajo),
    // limitado a AlfaMaximoLeyenda (0,8)
    // y de 0,55 a 0 en la mitad alta (sin borde visible). Se crea una vez.
    private static Sprite degradadoLeyenda;

    private static Sprite DegradadoLeyenda()
    {
        if (degradadoLeyenda != null) return degradadoLeyenda;
        const int n = 64;
        Texture2D tex = new Texture2D(4, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[4 * n];
        for (int y = 0; y < n; y++)
        {
            float h = 1f - (y + 0.5f) / n;   // 1 abajo, 0 arriba
            float a = h >= 0.5f
                ? AlfaMinimoLeyenda + (1f - AlfaMinimoLeyenda) * Mathf.Pow((h - 0.5f) * 2f, 1.6f)
                : AlfaMinimoLeyenda * Mathf.Pow(h * 2f, 1.6f);
            a = Mathf.Min(a, AlfaMaximoLeyenda);   // máximo 0,8; en la zona de la leyenda el mínimo sigue en 0,55
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(0, 0, 0, b);
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);   // sin mipmaps ni copia en CPU
        degradadoLeyenda = Sprite.Create(tex, new Rect(0, 0, 4, n), new Vector2(0.5f, 0.5f), 100f);
        return degradadoLeyenda;
    }

    // Ícono a la izquierda dentro de un botón (una sola vez); el texto se corre para no quedar debajo del ícono
    private const float RellenoTextoConIcono = UIRapida.Margen + UIRapida.TamIconoBoton + UIRapida.Esp2;

    private static void IconoEnBoton(Button b, string icono, Color color)
    {
        if (b == null || b.transform.Find("Icono_" + icono) != null) return;
        TMP_Text t = UIRapida.TextoDe(b);
        if (t != null) t.margin = new Vector4(RellenoTextoConIcono, t.margin.y, t.margin.z, t.margin.w);
        Image img = UIRapida.Panel("Icono_" + icono, b.transform, color);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(UIRapida.Margen, 0),
                        new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));
    }

    // Margen interno del texto dentro de la píldora del buscador
    private static void AjustarTextoCampo(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(UIRapida.Esp4, UIRapida.Esp1 / 2f);    // dentro de la curva de la píldora
        rt.offsetMax = new Vector2(-UIRapida.Esp3, -UIRapida.Esp1 / 2f);
        TMP_Text t = rt.GetComponent<TMP_Text>();
        if (t != null)
        {
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>
    /// Elevación sobre el mapa (patrón raíz + SombraComoHija): la raíz queda invisible (sigue recibiendo toques),
    /// una "Cara" hija pinta el color y la sombra suave va detrás; ambas fuera del layout y dentro de la animación.
    /// Devuelve la cara (úsala como targetGraphic para el tinte al tocar).
    /// </summary>
    private static Image ElevarConSombra(Image fondo, float radio)
    {
        Transform existente = fondo.transform.Find("Cara");
        if (existente != null) return existente.GetComponent<Image>();
        Image cara = UIRapida.Panel("Cara", fondo.transform, fondo.color);
        UIRapida.Redondear(cara, radio);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        cara.transform.SetAsFirstSibling();
        UIRapida.SombraComoHija(fondo.rectTransform, radio);   // primer hijo: detrás de la cara
        fondo.sprite = null;
        fondo.color = UIRapida.Transparente;   // alfa 0: sigue recibiendo los toques
        return cara;
    }

    /// <summary>
    /// Botón principal del diseño como píldora (verde por defecto): sin marcos internos, texto blanco en negrita.
    /// <paramref name="colorFondo"/> = null o Verde: acción principal (oscurece al presionar);
    /// otro color (Menta, Superficie…): estados claros (PresionadoClaro), para que ningún botón claro se agrise.
    /// </summary>
    private static void EstiloBotonPrincipal(Button b, TMP_FontAsset fuente, Color? colorFondo = null)
    {
        Image fondo = b.targetGraphic as Image;
        if (fondo == null) fondo = b.GetComponent<Image>();
        if (fondo == null) return;

        UIRapida.RedondearPildora(fondo);   // píldora real: radio = mitad del alto actual (también 112 de AltoBotonPrincipal)
        Color c = colorFondo.HasValue ? colorFondo.Value : UIRapida.Verde;
        fondo.color = c;
        if (c == UIRapida.Verde) UIRapida.EstadosBoton(b);   // oscurece al presionar, igual en todos los botones de color sólido
        else UIRapida.EstadosBotonClaro(b);                  // fondo claro: tinte verdoso perceptible al presionar
        foreach (Transform hijo in b.transform)
            if (hijo.GetComponent<TMP_Text>() == null && hijo.GetComponent<Text>() == null)
                hijo.gameObject.SetActive(false);   // marcos e íconos del diseño (el texto, TMP o antiguo, se conserva)

        TMP_Text t = UIRapida.TextoDe(b);
        if (t != null)
        {
            t.color = UIRapida.SobreColor;
            UIRapida.Peso(t, true);   // acción principal: Roboto Bold real
            // Botones en tipo oración: el espaciado solo quedaría para un texto en MAYÚSCULAS
            t.characterSpacing = EsMayusculas(t.text) ? UIRapida.EspaciadoMayusculas : 0f;
            return;
        }
        // Botones del login/registro con Text antiguo (uGUI): mismo aspecto (blanco, Roboto Bold, TamCuerpo)
        Text antiguo = b.GetComponentInChildren<Text>(true);
        if (antiguo != null)
        {
            antiguo.color = UIRapida.SobreColor;
            antiguo.fontStyle = FontStyle.Bold;   // respaldo si Roboto Bold no carga (negrita sintética)
            antiguo.fontSize = Mathf.RoundToInt(UIRapida.TamCuerpo);
            UIRapida.FuenteLegacyEn(antiguo, true);
        }
    }

    /// <summary>Botón secundario con la forma del principal (Menta o blanco): texto en Roboto Medium.</summary>
    private static void PesoBotonSecundario(Button b)
    {
        TMP_Text t = UIRapida.TextoDe(b);
        if (t != null) UIRapida.PesoMedio(t);
    }

    /// <summary>
    /// Área táctil de ToqueMinimo de alto conservando el aspecto del chip (la fila mide ToqueMinimo - 2·Esp2):
    /// un hijo transparente que sobresale arriba y abajo recibe el toque y lo pasa al botón del chip.
    /// La fila (referenciaChips) no cambia de tamaño, así la franja libre se calcula igual.
    /// </summary>
    private static void AreaToqueChip(RectTransform chip)
    {
        if (chip.Find("AreaToque") != null) return;
        Canvas.ForceUpdateCanvases();
        float alto = chip.rect.height > 1f ? chip.rect.height : AltoChipDiseno;
        float extra = Mathf.Max(0f, (UIRapida.ToqueMinimo - alto) / 2f);
        if (extra <= 0f) return;
        Image area = UIRapida.Panel("AreaToque", chip, UIRapida.Transparente);   // alfa 0: invisible, recibe toques
        area.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        RectTransform rt = area.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0f, -extra);
        rt.offsetMax = new Vector2(0f, extra);
        area.transform.SetAsLastSibling();
    }

    // Alto de la fila de chips (respaldo si aún no hay layout): el área táctil mínima menos el margen Esp2 arriba y abajo
    private const float AltoChipDiseno = UIRapida.ToqueMinimo - 2f * UIRapida.Esp2;

    private static bool EsMayusculas(string texto) =>
        !string.IsNullOrEmpty(texto) && texto == texto.ToUpperInvariant() && texto != texto.ToLowerInvariant();

    // Estilo único de selección (UIRapida.MarcarSeleccion): la categoría elegida en SeleccionadoFondo/Texto
    // (Verde, texto Bold); las demás en NoSeleccionadoFondo/Texto (Menta/SobreMenta, Medium)
    private void ActualizarChips()
    {
        string activa = categoriaFiltro == "*" ? "todos" : IndiceBusqueda.Normalizar(categoriaFiltro);
        bool hayLista = panelResultados != null && panelResultados.activeSelf;

        foreach (var (boton, fondo, texto, categoria) in chipsEstilo)
        {
            bool sel = hayLista && categoriaFiltro != null && IndiceBusqueda.Normalizar(categoria) == activa;
            if (boton != null) { UIRapida.MarcarSeleccion(boton, sel); continue; }
            fondo.color = sel ? UIRapida.SeleccionadoFondo : UIRapida.NoSeleccionadoFondo;
            texto.color = sel ? UIRapida.SeleccionadoTexto : UIRapida.NoSeleccionadoTexto;
            if (sel) UIRapida.Peso(texto, true); else UIRapida.PesoMedio(texto);
        }
    }

    // ==================================================================
    // Clima: temperatura + descripción en una píldora Superficie (antes, texto negro directo sobre el mapa).
    // Los textos son los del ClimaController en la escena (VariableClima y ZOnadrecha dentro de ZonaInferior).
    // ==================================================================
    private TMP_Text climaTemperatura, climaDescripcion;
    private LayoutElement leClimaTemperatura, leClimaDescripcion;
    private RectTransform pildoraClima;

    private void PrepararPildoraClima()
    {
        if (referenciaChips == null) return;
        Canvas canvas = referenciaChips.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform raiz = canvas.rootCanvas.transform;
        Transform temp = BuscarHijo(raiz, EscenaClimaTemperatura), desc = BuscarHijo(raiz, EscenaClimaDescripcion);
        if (temp == null) { AvisarFaltaEnEscena(EscenaClimaTemperatura, "la píldora del clima"); return; }
        if (desc == null) { AvisarFaltaEnEscena(EscenaClimaDescripcion, "la píldora del clima"); return; }
        if (temp.parent == null || temp.parent != desc.parent) return;
        climaTemperatura = temp.GetComponentInChildren<TMP_Text>(true);
        climaDescripcion = desc.GetComponentInChildren<TMP_Text>(true);
        if (climaTemperatura == null || climaDescripcion == null) return;

        RectTransform zona = (RectTransform)temp.parent;
        HorizontalLayoutGroup hl = zona.GetComponent<HorizontalLayoutGroup>();
        if (hl != null)
        {
            hl.padding = Relleno(UIRapida.Esp3, UIRapida.Esp3, 0, 0);
            hl.spacing = UIRapida.Esp1;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false;
        }
        leClimaTemperatura = temp.GetComponent<LayoutElement>();
        if (leClimaTemperatura == null) leClimaTemperatura = temp.gameObject.AddComponent<LayoutElement>();
        leClimaDescripcion = desc.GetComponent<LayoutElement>();
        if (leClimaDescripcion == null) leClimaDescripcion = desc.gameObject.AddComponent<LayoutElement>();
        leClimaTemperatura.flexibleWidth = leClimaDescripcion.flexibleWidth = 0f;

        // Temperatura: Oscuro, Bold, TamSubtitulo. Descripción: Gris, TamSecundario (Regular)
        climaTemperatura.enableAutoSizing = false;
        climaTemperatura.fontSize = UIRapida.TamSubtitulo;
        climaTemperatura.color = UIRapida.Oscuro;
        UIRapida.Peso(climaTemperatura, true);
        climaDescripcion.enableAutoSizing = false;
        climaDescripcion.fontSize = UIRapida.TamSecundario;
        climaDescripcion.color = UIRapida.Gris;
        UIRapida.Peso(climaDescripcion, false);
        foreach (TMP_Text t in new[] { climaTemperatura, climaDescripcion })
        {
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Píldora: raíz sin gráfico, sombra suave + cara blanca (fuera del layout, detrás de los textos)
        pildoraClima = UIRapida.Rect("PildoraClima", zona);
        pildoraClima.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Anclar(pildoraClima, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero,
                        new Vector2(0f, UIRapida.TamSubtitulo + 2f * UIRapida.Esp2));
        Image cara = UIRapida.Panel("Cara", pildoraClima, UIRapida.Superficie);
        UIRapida.Redondear(cara, UIRapida.RadioPildora);
        cara.raycastTarget = false;
        UIRapida.Estirar(cara.rectTransform);
        UIRapida.SombraComoHija(pildoraClima, UIRapida.RadioPildora);   // queda como primer hijo, detrás de la cara
        pildoraClima.SetAsFirstSibling();
        AjustarPildoraClima();
    }

    // El ClimaController cambia los textos más tarde: la píldora se ajusta al contenido (evento TextoActualizado)
    private void AjustarPildoraClima()
    {
        if (pildoraClima == null || climaTemperatura == null || climaDescripcion == null) return;
        RectTransform zona = (RectTransform)pildoraClima.parent;
        float disponible = Mathf.Max(0f, zona.rect.width - 2f * UIRapida.Esp3 - UIRapida.Esp1);
        float wTemp = Mathf.Ceil(climaTemperatura.GetPreferredValues(climaTemperatura.text).x);
        float wDesc = string.IsNullOrEmpty(climaDescripcion.text) ? 0f
                    : Mathf.Ceil(climaDescripcion.GetPreferredValues(climaDescripcion.text).x);
        wTemp = Mathf.Min(wTemp, disponible);
        wDesc = Mathf.Min(wDesc, Mathf.Max(0f, disponible - wTemp));   // una descripción larga termina en "…"
        leClimaTemperatura.preferredWidth = wTemp;
        leClimaDescripcion.preferredWidth = wDesc;
        pildoraClima.sizeDelta = new Vector2(wTemp + (wDesc > 0f ? UIRapida.Esp1 + wDesc : 0f) + 2f * UIRapida.Esp3,
                                             pildoraClima.sizeDelta.y);
    }

    // ==================================================================
    // Tipografía y texto seguro
    // ==================================================================
    // Una sola familia (Roboto con pesos reales) en cada canvas raíz: TMP y también los Text/InputField antiguos
    private static void AplicarFuentesEscena()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            UIRapida.AplicarFuentes(c.transform);
            UIRapida.AplicarFuentesLegacy(c.transform);   // login y registro (uGUI): Regular, o Bold real si estaba en negrita
        }
    }

    // El cambio de peso de un texto ya creado se hace con UIRapida.Peso (una sola implementación).

    /// <summary>
    /// Texto de datos (nombres, descripciones) seguro para TMP con texto enriquecido: un nombre con
    /// "&lt;b&gt;" o "&lt;color&gt;" se muestra tal cual en vez de interpretarse como etiqueta.
    /// </summary>
    public static string TextoSeguro(string s) => UIRapida.TextoSeguro(s);   // una sola implementación (UIRapida)
}
