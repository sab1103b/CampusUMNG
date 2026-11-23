using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Atajos para construir interfaz por código con el estilo de Campus Conecta.</summary>
public static partial class UIRapida
{
    // Paleta, escala y medidas: UIRapida.Tokens.cs · Tipografía (Roboto): UIRapida.Tipografia.cs · Íconos: UIRapida.Iconos.cs

    /// <summary>
    /// Texto de datos (nombres, descripciones) seguro para TMP con texto enriquecido: un nombre con
    /// "&lt;b&gt;" o "&lt;color&gt;" se muestra tal cual en vez de interpretarse como etiqueta.
    /// </summary>
    public static string TextoSeguro(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        if (s.IndexOf('<') < 0) return s;
        // Dentro de <noparse> solo "</noparse>" cierra: si el dato lo contiene, se parte en dos bloques
        return "<noparse>" + s.Replace("</noparse>", "</no</noparse><noparse>parse>") + "</noparse>";
    }

    /// <summary>Colores estándar de un botón de color sólido (verde, rojo…): oscurece al presionar.</summary>
    public static void EstadosBoton(Button b)
    {
        if (b == null) return;
        ColorBlock cb = b.colors;
        cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white;
        cb.pressedColor = Presionado;
        cb.disabledColor = Deshabilitado;
        cb.fadeDuration = DuracionFundido;
        b.colors = cb;
    }

    /// <summary>
    /// Estados para botones blancos o transparentes sobre fondo claro (chips, "Eliminar" en filas):
    /// al presionar toman <see cref="PresionadoClaro"/> (verdoso, perceptible sobre blanco) en vez del gris de
    /// <see cref="Presionado"/>. Un chip verde (activo) usa <see cref="EstadosBoton"/>.
    /// </summary>
    public static void EstadosBotonClaro(Button b)
    {
        if (b == null) return;
        EstadosBoton(b);
        ColorBlock cb = b.colors;
        cb.pressedColor = PresionadoClaro;
        b.colors = cb;
    }

    /// <summary>
    /// Estados de un botón SIN fondo propio (filas de lista, enlaces, "Ver contraseña"): normal, resaltado y
    /// seleccionado <see cref="Transparente"/>; al presionar <see cref="PresionadoClaro"/>; multiplicador 1 y
    /// fundido <see cref="DuracionFundido"/>. El tinte multiplica el color del targetGraphic, así que este se deja
    /// blanco (si no hay targetGraphic, se usa o se crea una Image transparente en el mismo objeto).
    /// </summary>
    public static void EstadosTransparente(Button b)
    {
        if (b == null) return;
        if (b.targetGraphic == null)
        {
            Image img = b.GetComponent<Image>();
            if (img == null) img = b.gameObject.AddComponent<Image>();
            b.targetGraphic = img;
        }
        b.targetGraphic.color = Color.white;
        b.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = b.colors;
        cb.normalColor = cb.highlightedColor = cb.selectedColor = Transparente;
        cb.pressedColor = PresionadoClaro;
        cb.disabledColor = Transparente;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = DuracionFundido;
        b.colors = cb;
    }

    /// <summary>
    /// Estados de un campo de texto (TMP_InputField o InputField legacy): blanco sin tinte en reposo, resaltado y
    /// seleccionado (el foco lo marca el contorno, ver <see cref="IndicadorFoco"/>), PresionadoClaro al tocar.
    /// </summary>
    public static void EstadosCampo(Selectable campo)
    {
        if (campo == null) return;
        campo.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = campo.colors;
        cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white;
        cb.pressedColor = PresionadoClaro;
        cb.disabledColor = Deshabilitado;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = DuracionFundido;
        campo.colors = cb;
    }

    /// <summary>
    /// Indicador de foco del campo (TMP o legacy): agrega (o devuelve) su <see cref="FocoCampo"/>. Con foco el contorno
    /// pasa a Verde y GrosorFocoCampo; al salir vuelve a BordeCampo. "borde" null = el contorno "Borde" del fondo del campo
    /// (targetGraphic), creado con <see cref="BordeDe"/> si no existe. "radio" = radio de esquina del fondo del campo.
    /// "conFoco" = false para selectores (botones con contorno de campo): solo muestran el error, no el foco.
    /// </summary>
    public static FocoCampo IndicadorFoco(Selectable campo, Image borde = null, float radio = RadioControl, bool conFoco = true)
    {
        if (campo == null) return null;
        if (borde == null) borde = BordeDe(campo.targetGraphic as Image, radio);
        FocoCampo f = campo.GetComponent<FocoCampo>();
        if (f == null) f = campo.gameObject.AddComponent<FocoCampo>();
        f.borde = borde;
        f.radio = radio;
        f.conFoco = conFoco;
        f.Pintar();
        return f;
    }

    /// <summary>
    /// Marca (true) o quita (false) el error de un campo: contorno Rojo y GrosorFocoCampo mientras haya error.
    /// Si el campo aún no tiene indicador, se le agrega con <see cref="IndicadorFoco"/> (radio RadioControl).
    /// </summary>
    public static void MarcarErrorCampo(Selectable campo, bool error)
    {
        if (campo == null) return;
        FocoCampo f = campo.GetComponent<FocoCampo>();
        if (f == null)
        {
            if (!error) return;   // nada que quitar
            f = IndicadorFoco(campo, null, RadioControl, !(campo is Button));   // un selector solo muestra el error
            if (f == null) return;
        }
        f.Error = error;
    }

    /// <summary>
    /// Forma de píldora REAL para un botón (radio = mitad de su alto actual, aunque lo cambie un LayoutGroup o la
    /// pantalla): agrega <see cref="RadioPildoraAuto"/>, que también ajusta la sombra estándar del botón.
    /// Reemplaza a Redondear(img, RadioPildora), que solo era píldora con alto ToqueMinimo.
    /// </summary>
    public static RadioPildoraAuto RedondearPildora(Image img)
    {
        if (img == null) return null;
        RadioPildoraAuto r = img.GetComponent<RadioPildoraAuto>();
        if (r == null) r = img.gameObject.AddComponent<RadioPildoraAuto>();
        // Hasta que el layout le dé tamaño: sprite de píldora con el alto declarado (o ToqueMinimo)
        Rect rc = img.rectTransform.rect;
        float lado = Mathf.Min(rc.width, rc.height);
        if (lado <= 0f) lado = rc.height > 0f ? rc.height : ToqueMinimo;
        AplicarPildora(img, lado);
        r.Aplicar();
        return r;
    }

    /// <summary>
    /// Estilo único de selección (chips de categoría, pestañas de tipo, opciones de lista):
    /// elegido = <see cref="SeleccionadoFondo"/> / <see cref="SeleccionadoTexto"/> y texto Bold;
    /// no elegido = <see cref="NoSeleccionadoFondo"/> / <see cref="NoSeleccionadoTexto"/> y texto Medium.
    /// Tiñe el fondo (targetGraphic), el texto y los íconos hijos llamados "Icono…"; ajusta el tinte al presionar.
    /// </summary>
    public static void MarcarSeleccion(Button b, bool seleccionado)
    {
        if (b == null) return;
        Color fondo = seleccionado ? SeleccionadoFondo : NoSeleccionadoFondo;
        Color texto = seleccionado ? SeleccionadoTexto : NoSeleccionadoTexto;
        if (b.targetGraphic != null) b.targetGraphic.color = fondo;
        TMP_Text t = TextoDe(b);
        if (t != null)
        {
            t.color = texto;
            if (seleccionado) Peso(t, true);
            else if (Application.isPlaying) PesoMedio(t);   // en el Editor no hay Medium: peso normal
            else Peso(t, false);
        }
        foreach (Image img in b.GetComponentsInChildren<Image>(true))
            if (img != b.targetGraphic && img.name.StartsWith("Icono", System.StringComparison.Ordinal)) img.color = texto;
        // Verde: oscurece al presionar (Presionado); Menta: tinte claro (PresionadoClaro, texto 5,3:1)
        if (seleccionado) EstadosBoton(b); else EstadosBotonClaro(b);
    }

    /// <summary>Recuerda los colores de un botón mientras está desactivado con <see cref="EstadoDeshabilitado"/>.</summary>
    public class MemoriaDeshabilitado : MonoBehaviour
    {
        public bool guardado;
        public Color fondo, texto, tinte;
        public Graphic[] iconos;
        public Color[] coloresIconos;
    }

    /// <summary>
    /// Activa o desactiva un botón con el estado deshabilitado de la app: fondo plano <see cref="FondoDeshabilitado"/>
    /// y texto/íconos <see cref="TextoDeshabilitado"/> (≥4,5:1), sin el tinte multiplicador del ColorBlock.
    /// Al reactivarlo devuelve los colores que tenía. Cambia también <c>interactable</c>.
    /// </summary>
    public static void EstadoDeshabilitado(Button b, bool deshabilitado)
    {
        if (b == null) return;
        MemoriaDeshabilitado m = b.GetComponent<MemoriaDeshabilitado>();
        if (m == null) m = b.gameObject.AddComponent<MemoriaDeshabilitado>();
        Graphic fondo = b.targetGraphic;
        TMP_Text t = TextoDe(b);

        if (deshabilitado)
        {
            if (!m.guardado)
            {
                m.fondo = fondo != null ? fondo.color : Color.white;
                m.texto = t != null ? t.color : Oscuro;
                m.tinte = b.colors.disabledColor;
                // Íconos dibujados dentro del botón (Image hijas que no son el fondo ni sombras)
                var lista = new System.Collections.Generic.List<Graphic>();
                foreach (Image img in b.GetComponentsInChildren<Image>(true))
                    if (img != fondo && !img.name.Contains("Sombra")) lista.Add(img);
                m.iconos = lista.ToArray();
                m.coloresIconos = new Color[m.iconos.Length];
                for (int i = 0; i < m.iconos.Length; i++) m.coloresIconos[i] = m.iconos[i].color;
                m.guardado = true;
            }
            ColorBlock cb = b.colors;
            cb.disabledColor = Color.white;   // el color plano ya marca el estado; sin tinte encima
            b.colors = cb;
            if (fondo != null) fondo.color = FondoDeshabilitado;
            if (t != null) t.color = TextoDeshabilitado;
            if (m.iconos != null)
                foreach (Graphic g in m.iconos) if (g != null) g.color = TextoDeshabilitado;
        }
        else if (m.guardado)
        {
            if (fondo != null) fondo.color = m.fondo;
            if (t != null) t.color = m.texto;
            if (m.iconos != null)
                for (int i = 0; i < m.iconos.Length; i++) if (m.iconos[i] != null) m.iconos[i].color = m.coloresIconos[i];
            ColorBlock cb = b.colors;
            cb.disabledColor = m.tinte;
            b.colors = cb;
            m.guardado = false;
        }
        b.interactable = !deshabilitado;
    }

    // ==================================================================
    // Sombras de tarjeta: un solo estilo en toda la app (borde difuminado de SombraExtra, bajada SombraY)
    // ==================================================================
    /// <summary>
    /// Sombra suave bajo una tarjeta (una sola vez por objeto): hermana justo detrás de ella, con su mismo
    /// anclaje, SombraExtra más grande por lado, SombraY más abajo y borde difuminado (sprite SombraSuave).
    /// Sirve para cualquier Graphic (Image, RawImage…). Si la tarjeta la ubica un LayoutGroup, usa
    /// <see cref="SombraComoHija"/> (la sombra hermana no seguiría al layout).
    /// </summary>
    public static Image SombraDe(Graphic tarjeta, float radio = RadioTarjeta) =>
        tarjeta != null ? SombraDe(tarjeta.rectTransform, radio) : null;

    /// <summary>Igual que <see cref="SombraDe(Graphic, float)"/> para una tarjeta sin Graphic propio.</summary>
    public static Image SombraDe(RectTransform tarjeta, float radio = RadioTarjeta)
    {
        if (tarjeta == null) return null;
        Transform padre = tarjeta.parent;
        string nombre = tarjeta.name + "_Sombra";
        Transform existente = padre != null ? padre.Find(nombre) : null;
        if (existente != null) return existente.GetComponent<Image>();
        Image s = Panel(nombre, padre, Sombra);
        RectTransform rs = s.rectTransform;
        rs.anchorMin = tarjeta.anchorMin; rs.anchorMax = tarjeta.anchorMax; rs.pivot = tarjeta.pivot;
        // El crecimiento se reparte alrededor del pivote para que quede centrada sobre la tarjeta
        Vector2 crece = new Vector2(2f * SombraExtra, 2f * SombraExtra);
        Vector2 centrar = new Vector2((tarjeta.pivot.x - 0.5f) * crece.x, (tarjeta.pivot.y - 0.5f) * crece.y);
        rs.sizeDelta = tarjeta.sizeDelta + crece;
        rs.anchoredPosition = tarjeta.anchoredPosition + centrar + new Vector2(0f, -SombraY);
        rs.localScale = tarjeta.localScale;
        EstiloSombra(s, radio);
        s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        s.transform.SetSiblingIndex(tarjeta.GetSiblingIndex());   // justo detrás de la tarjeta
        return s;
    }

    /// <summary>
    /// Sombra como PRIMER hijo de una raíz transparente que pinta su cara en otro hijo (ficha, menú de perfil):
    /// se estira con la raíz (sigue al layout y a la animación) y queda fuera del layout.
    /// </summary>
    public static Image SombraComoHija(RectTransform raiz, float radio = RadioTarjeta)
    {
        if (raiz == null) return null;
        Transform existente = raiz.Find("Sombra");
        if (existente != null) return existente.GetComponent<Image>();
        Image s = Panel("Sombra", raiz, Sombra);
        Estirar(s.rectTransform, -SombraExtra, -SombraExtra - SombraY, -SombraExtra, -SombraExtra + SombraY);
        EstiloSombra(s, radio);
        s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        s.transform.SetAsFirstSibling();
        return s;
    }

    /// <summary>Aplica el sprite difuminado a una imagen de sombra ya colocada (su rect = tarjeta + SombraExtra por lado).</summary>
    public static void EstiloSombra(Image s, float radio = RadioTarjeta)
    {
        if (s == null) return;
        s.color = Sombra;
        s.sprite = SombraSuave(radio);
        s.type = Image.Type.Sliced;
        s.pixelsPerUnitMultiplier = EscalaSombra;   // 1 px del sprite = 2 unidades de canvas
        s.raycastTarget = false;
    }

    /// <summary>
    /// Recorte suave de una foto (o cualquier tarjeta) SIN Mask de stencil: agrega/actualiza el hijo "Esquinas" de "marco",
    /// estirado y como ÚLTIMO hermano, con esquinas invertidas antialiasadas del color "colorFondo" (el fondo liso
    /// que hay detrás del marco). No recibe toques y queda fuera del layout. Idempotente: si ya existe, solo actualiza
    /// color/radio y lo vuelve a poner encima (llámalo después de agregar degradados o leyendas al marco).
    /// "conSombra" = el marco tiene la sombra estándar (<see cref="SombraDe(Graphic, float)"/>): las esquinas la
    /// llevan horneada para que no se corte. Úsalo en vez de Mask: quita el Mask y deja la foto estirada en el marco.
    /// </summary>
    public static Image MarcoEsquinas(RectTransform marco, Color colorFondo, float radio = RadioTarjeta, bool conSombra = true)
    {
        if (marco == null) return null;
        Transform existente = marco.Find("Esquinas");
        Image e = existente != null ? existente.GetComponent<Image>() : null;
        if (e == null)
        {
            e = Panel("Esquinas", marco, colorFondo);
            Estirar(e.rectTransform);
            e.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        e.color = colorFondo;
        e.sprite = EsquinasInvertidas(radio, conSombra);
        e.type = Image.Type.Sliced;
        e.fillCenter = false;                       // el centro es transparente: no se dibuja (menos relleno de GPU)
        e.pixelsPerUnitMultiplier = EscalaEsquinas;  // 2 px del sprite por unidad de canvas
        e.raycastTarget = false;
        e.transform.SetAsLastSibling();
        return e;
    }

    /// <summary>Igual que <see cref="MarcoEsquinas(RectTransform, Color, float, bool)"/> sobre el rect de una imagen (p. ej. la foto).</summary>
    public static Image MarcoEsquinas(Graphic foto, Color colorFondo, float radio = RadioTarjeta, bool conSombra = true) =>
        foto != null ? MarcoEsquinas(foto.rectTransform, colorFondo, radio, conSombra) : null;

    public static RectTransform Rect(string nombre, Transform padre)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(padre, false);
        return (RectTransform)go.transform;
    }

    public static Image Panel(string nombre, Transform padre, Color color)
    {
        RectTransform rt = Rect(nombre, padre);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static void Anclar(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivote, Vector2 posicion, Vector2 tamano)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;
    }

    public static void Estirar(RectTransform rt, float izq = 0, float abajo = 0, float der = 0, float arriba = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(izq, abajo);
        rt.offsetMax = new Vector2(-der, -arriba);
    }

    public static TMP_Text Texto(Transform padre, string nombre, string texto, float tam, Color color,
                                 TMP_FontAsset fuente, bool negrita = false,
                                 TextAlignmentOptions alineacion = TextAlignmentOptions.Left)
    {
        RectTransform rt = Rect(nombre, padre);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = FuenteDe(fuente, negrita);
        if (f != null) t.font = f;
        t.text = texto;
        t.fontSize = tam;
        t.color = color;
        t.fontStyle = negrita ? FontStyles.Bold : FontStyles.Normal;
        t.alignment = alineacion;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    public static Button Boton(Transform padre, string nombre, string texto, float tam, Color fondo, Color colorTexto,
                               TMP_FontAsset fuente, UnityAction accion)
    {
        Image img = Panel(nombre, padre, fondo);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        EstadosBoton(b);
        if (accion != null) b.onClick.AddListener(accion);

        TMP_Text t = Texto(img.transform, "Texto", texto, tam, colorTexto, fuente, true, TextAlignmentOptions.Center);
        Estirar((RectTransform)t.transform, Esp1, Esp1 / 2f, Esp1, Esp1 / 2f);
        t.enableAutoSizing = true;
        t.fontSizeMin = TamMinimo;
        t.fontSizeMax = tam;
        return b;
    }

    /// <summary>
    /// Botón secundario (Cancelar, Descartar...): SIEMPRE Menta con texto SobreMenta y forma de píldora.
    /// El gris queda solo para texto. Alto mínimo ToqueMinimo (lo pone quien lo coloca o su LayoutGroup).
    /// </summary>
    public static Button BotonSecundario(Transform padre, string nombre, string texto, UnityAction accion,
                                         float tam = TamCuerpo, TMP_FontAsset fuente = null)
    {
        Button b = Boton(padre, nombre, texto, tam, Menta, SobreMenta, fuente, accion);
        RedondearPildora((Image)b.targetGraphic);
        EstadosBotonClaro(b);   // sobre Menta, Presionado (#d9d9d9) bajaba el texto a 3,6:1; PresionadoClaro lo mantiene
        if (Application.isPlaying && EsFuenteReemplazable(fuente)) PesoMedio(TextoDe(b));   // secundario: Medium (la principal, Bold)
        return b;
    }

    /// <summary>
    /// Botón de cerrar (X) único en toda la app: círculo de ToqueMinimo con el ícono "eliminar" (TamIconoChico).
    /// sobreClaro = true: fondo FondoCampo e ícono Gris (tarjetas y hojas blancas);
    /// false: fondo blanco e ícono SobreMenta (barras y fondos de color). Quien lo llama lo ancla.
    /// </summary>
    public static Button BotonCerrar(Transform padre, UnityAction accion, bool sobreClaro = true)
    {
        Image img = Panel("BotonCerrar", padre, sobreClaro ? FondoCampo : SuperficieOpaca);
        img.sprite = Circulo();
        img.preserveAspect = true;
        img.rectTransform.sizeDelta = new Vector2(ToqueMinimo, ToqueMinimo);
        LayoutElement le = img.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = ToqueMinimo;

        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        EstadosBotonClaro(b);   // fondo claro (FondoCampo o blanco): al presionar, PresionadoClaro en ambos casos
        if (accion != null) b.onClick.AddListener(accion);

        Image ic = Panel("Icono", img.transform, sobreClaro ? Gris : SobreMenta);
        ic.sprite = Icono("eliminar");
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        Anclar(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
               new Vector2(TamIconoChico, TamIconoChico));
        return b;
    }

    /// <summary>Título de modal (formulario, editor, confirmar): Oscuro, TamSubtitulo y Bold (Roboto Bold real).</summary>
    public static TMP_Text TituloModal(Transform padre, string texto,
                                       TextAlignmentOptions alineacion = TextAlignmentOptions.Left, string nombre = "Titulo")
    {
        TMP_Text t = Texto(padre, nombre, texto, TamSubtitulo, Oscuro, null, true, alineacion);
        t.enableAutoSizing = true;
        t.fontSizeMin = TamCuerpo;
        t.fontSizeMax = TamSubtitulo;
        return t;
    }

    public static TMP_Text TextoDe(Button b) => b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;

    /// <summary>
    /// Campo de texto TextMeshPro con fondo redondeado (una línea o varias), contorno BordeCampo e indicador de foco
    /// (<see cref="IndicadorFoco"/>). Para marcar un error usa <see cref="MarcarErrorCampo"/>.
    /// </summary>
    public static TMP_InputField Campo(Transform padre, string nombre, string placeholder, TMP_FontAsset fuente,
                                       bool multilinea = false, int limite = 200)
    {
        Image fondo = Panel(nombre, padre, FondoCampo);
        Redondear(fondo, RadioControl);
        GameObject go = fondo.gameObject;
        go.SetActive(false);   // se configura apagado para que TMP_InputField arranque completo

        RectTransform area = Rect("Area", go.transform);
        Estirar(area, Esp3, Esp1, Esp3, Esp1);
        area.gameObject.AddComponent<RectMask2D>();

        // Placeholder en estilo normal y Gris, igual que en el resto de campos de la app
        TMP_Text ph = Texto(area, "Placeholder", placeholder, TamCuerpo, Gris, fuente, false,
                            multilinea ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
        Estirar((RectTransform)ph.transform);
        TMP_Text txt = Texto(area, "Texto", "", TamCuerpo, Oscuro, fuente, false,
                             multilinea ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
        Estirar((RectTransform)txt.transform);
        if (!multilinea) { ph.textWrappingMode = TextWrappingModes.NoWrap; txt.textWrappingMode = TextWrappingModes.NoWrap; }

        TMP_InputField campo = go.AddComponent<TMP_InputField>();
        campo.textViewport = area;
        campo.textComponent = txt;
        campo.placeholder = ph;
        campo.targetGraphic = fondo;
        campo.fontAsset = FuenteDe(fuente, false);
        campo.pointSize = TamCuerpo;
        campo.characterLimit = limite;
        campo.lineType = multilinea ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        campo.caretColor = Verde;
        campo.customCaretColor = true;
        campo.selectionColor = SeleccionTexto;
        EstadosCampo(campo);   // blanco sin tinte y PresionadoClaro al tocar; el foco lo marca el contorno

        go.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = multilinea ? AltoCampoMultilinea : ToqueMinimo;
        IndicadorFoco(campo, BordeDe(fondo), RadioControl);   // contorno BordeCampo; Verde y más grueso con foco
        go.SetActive(true);
        return campo;
    }

    /// <summary>
    /// Contorno de campo de texto (<see cref="BordeCampo"/>, ≈ <see cref="GrosorBordeCampo"/> unidades): hijo "Borde"
    /// estirado sobre el campo, fuera del layout y sin recibir toques. Sirve también para los campos legacy
    /// (login, registro) que no se crean con <see cref="Campo"/>. Idempotente: devuelve el borde existente.
    /// "radio" debe coincidir con el radio de esquina del fondo del campo.
    /// </summary>
    public static Image BordeDe(Image campo, float radio = RadioControl)
    {
        if (campo == null) return null;
        Transform existente = campo.transform.Find("Borde");
        if (existente != null) return existente.GetComponent<Image>();
        Image borde = Panel("Borde", campo.transform, BordeCampo);
        Estirar(borde.rectTransform);
        borde.sprite = ContornoRedondeado();
        borde.type = Image.Type.Sliced;
        borde.pixelsPerUnitMultiplier = 22f / Mathf.Max(1f, radio);
        borde.raycastTarget = false;
        borde.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return borde;
    }

    /// <summary>
    /// Texto de error ÚNICO de la app (bajo un campo o al pie de un formulario): Rojo, TamSecundario y peso Medium
    /// (en el Editor, sin Medium, queda Regular). Alineado arriba a la izquierda y vacío; quien lo llama lo ancla
    /// o le pone LayoutElement, y puede cambiar la alineación.
    /// </summary>
    public static TMP_Text TextoError(Transform padre, string nombre)
    {
        TMP_Text t = Texto(padre, nombre, "", TamSecundario, Rojo, null, false, TextAlignmentOptions.TopLeft);
        if (Application.isPlaying) PesoMedio(t);
        return t;
    }

    /// <summary>Etiqueta pequeña en peso medio (Roboto Medium) encima de un campo.</summary>
    public static TMP_Text Etiqueta(Transform padre, string texto, TMP_FontAsset fuente)
    {
        TMP_Text t = Texto(padre, "Etiqueta " + texto, texto, TamSecundario, Oscuro, fuente, true, TextAlignmentOptions.BottomLeft);
        if (Application.isPlaying) PesoMedio(t);   // en el Editor queda en negrita (LiberationSans no tiene Medium)
        t.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = AltoEtiquetaCampo;
        return t;
    }

    // ==================================================================
    // Botón redondo blanco (+, -, Yo, 3D): el mismo en el mapa del usuario y en "Modificar mapa".
    // Estructura: raíz con el Button (sin imagen) > "Sombra" (SombraComoHija, sigue al layout) > "Cara" (círculo
    // Superficie, targetGraphic) > texto o ícono. Así la sombra queda debajo aunque el botón lo ubique un LayoutGroup.
    // ==================================================================
    private static Button RedondoBase(Transform padre, string nombre, UnityAction accion, out Image cara)
    {
        RectTransform raiz = Rect(nombre, padre);
        raiz.sizeDelta = new Vector2(ToqueMinimo, ToqueMinimo);
        SombraComoHija(raiz, ToqueMinimo / 2f);   // radio = medio lado: sombra circular (TamanoRedondo la ajusta)

        cara = Panel("Cara", raiz, Superficie);
        cara.sprite = Circulo();
        cara.preserveAspect = true;
        Estirar(cara.rectTransform);

        Button b = raiz.gameObject.AddComponent<Button>();
        b.targetGraphic = cara;
        EstadosBotonClaro(b);   // cara blanca: al presionar, PresionadoClaro (perceptible sobre blanco)
        if (accion != null) b.onClick.AddListener(accion);
        return b;
    }

    public static Button BotonRedondo(Transform padre, string nombre, string texto, float tamano,
                                      TMP_FontAsset fuente, UnityAction accion)
    {
        Button b = RedondoBase(padre, nombre, accion, out Image cara);
        TMP_Text t = Texto(cara.transform, "Texto", texto, tamano, Verde, fuente, true, TextAlignmentOptions.Center);
        Estirar((RectTransform)t.transform);
        return b;
    }

    /// <summary>
    /// Botón redondo blanco con un ícono dibujado (Icono(nombre)) centrado y de tamaño fijo:
    /// así "+" y "-" del zoom se ven iguales en todas las pantallas.
    /// </summary>
    public static Button BotonRedondo(Transform padre, string nombre, string icono, UnityAction accion,
                                      float tamIcono = TamIconoBoton)
    {
        Button b = RedondoBase(padre, nombre, accion, out Image cara);
        Image ic = Panel("Icono", cara.transform, Verde);
        ic.sprite = Icono(icono);
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        Anclar(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
               new Vector2(tamIcono, tamIcono));
        return b;
    }

    /// <summary>Lado de un botón redondo (BotonRedondo) con su sombra circular a juego.</summary>
    public static void TamanoRedondo(Button b, float lado)
    {
        if (b == null) return;
        RectTransform rt = (RectTransform)b.transform;
        rt.sizeDelta = new Vector2(lado, lado);
        Transform s = rt.Find("Sombra");
        if (s != null) EstiloSombra(s.GetComponent<Image>(), lado / 2f);
    }

    // ==================================================================
    // Controles sobre el mapa (zoom, ubicación, 2D/3D): mismo lugar y tamaño en todas las pantallas
    // ==================================================================
    /// <summary>
    /// Columna vertical anclada a la derecha y centrada en alto (margen MargenControlesMapa, separación Esp3).
    /// Su tamaño se ajusta solo a los hijos. Crea los botones con <see cref="BotonMapa"/> / <see cref="BotonMapaTexto"/>.
    /// "centroY" (0-1 del alto) solo se cambia si una hoja fija tapa la mitad de abajo del mapa.
    /// </summary>
    public static RectTransform GrupoControlesMapa(Transform padre, string nombre = "ControlesMapa", float centroY = 0.5f)
    {
        RectTransform grupo = Rect(nombre, padre);
        Anclar(grupo, new Vector2(1f, centroY), new Vector2(1f, centroY), new Vector2(1f, 0.5f),
               new Vector2(-MargenControlesMapa, 0f), new Vector2(TamBotonMapa, TamBotonMapa));
        VerticalLayoutGroup v = grupo.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = Esp3;
        v.childAlignment = TextAnchor.MiddleCenter;
        v.childControlWidth = v.childControlHeight = false;       // cada botón conserva su tamaño (TamBotonMapa)
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        ContentSizeFitter f = grupo.gameObject.AddComponent<ContentSizeFitter>();
        f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return grupo;
    }

    /// <summary>Botón redondo con ícono para el grupo de controles del mapa (TamBotonMapa x TamBotonMapa).</summary>
    public static Button BotonMapa(Transform grupo, string nombre, string icono, UnityAction accion)
    {
        Button b = BotonRedondo(grupo, nombre, icono, accion);
        TamanoRedondo(b, TamBotonMapa);
        return b;
    }

    /// <summary>Botón redondo con texto (2D/3D) para el grupo de controles del mapa; letra TamSubtitulo.</summary>
    public static Button BotonMapaTexto(Transform grupo, string nombre, string texto, TMP_FontAsset fuente, UnityAction accion)
    {
        Button b = BotonRedondo(grupo, nombre, texto, TamSubtitulo, fuente, accion);
        TamanoRedondo(b, TamBotonMapa);
        return b;
    }

    // ==================================================================
    // Barra superior de las pantallas de administración (mismo alto, título y cierre en todas)
    // ==================================================================
    /// <summary>
    /// Barra verde arriba (AltoBarraSuperior) con el título a la izquierda y, si "cerrar" no es null,
    /// el botón de cerrar estándar (<see cref="BotonCerrar"/>, blanco sobre la barra) a la derecha.
    /// </summary>
    public static Image BarraSuperior(Transform padre, string titulo, TMP_FontAsset fuente, UnityAction cerrar,
                                      out TMP_Text textoTitulo)
    {
        Image barra = Panel("BarraSuperior", padre, Verde);
        Anclar(barra.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, AltoBarraSuperior));

        textoTitulo = Texto(barra.transform, "Titulo", titulo, TamTitulo, SobreColor, fuente, true, TextAlignmentOptions.MidlineLeft);
        textoTitulo.textWrappingMode = TextWrappingModes.NoWrap;
        textoTitulo.overflowMode = TextOverflowModes.Ellipsis;
        textoTitulo.enableAutoSizing = true;
        textoTitulo.fontSizeMin = TamSubtitulo;
        textoTitulo.fontSizeMax = TamTitulo;
        Estirar((RectTransform)textoTitulo.transform, Margen, Esp1, cerrar != null ? Margen + ToqueMinimo + Esp2 : Margen, Esp1);

        if (cerrar != null)
        {
            Button b = BotonCerrar(barra.transform, cerrar, false);   // fondo blanco sobre la barra verde
            b.name = "Cerrar";
            Anclar((RectTransform)b.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                   new Vector2(-Margen + Esp1, 0), new Vector2(ToqueMinimo, ToqueMinimo));
        }
        return barra;
    }

    // ==================================================================
    // Aparición suave de modales: escala 0,96 -> 1 y alfa 0 -> 1 (tiempo real, no depende de timeScale)
    // ==================================================================
    /// <summary>Corrutina: el dueño la lanza con StartCoroutine al mostrar el modal.</summary>
    public static IEnumerator Aparecer(RectTransform caja, CanvasGroup grupo, float duracion = DuracionAparicion)
    {
        float t0 = Time.unscaledTime;
        while (true)
        {
            CamaraMapa.Despertar();   // la animación necesita cuadros aunque la cámara esté en reposo
            float k = duracion > 0f ? Mathf.Clamp01((Time.unscaledTime - t0) / duracion) : 1f;
            float e = 1f - (1f - k) * (1f - k) * (1f - k);   // desaceleración suave
            if (grupo != null) grupo.alpha = e;
            if (caja != null) caja.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
            if (k >= 1f) yield break;
            yield return null;
        }
    }

    /// <summary>Animación de cierre: alfa 1→0 y escala 1→0,98; al terminar desactiva el objeto.</summary>
    public static IEnumerator Desaparecer(GameObject objeto, RectTransform caja, CanvasGroup grupo, float duracion = DuracionDesaparicion)
    {
        float t0 = Time.unscaledTime;
        while (objeto != null)
        {
            CamaraMapa.Despertar();
            float k = duracion > 0f ? Mathf.Clamp01((Time.unscaledTime - t0) / duracion) : 1f;
            float e = k * k;   // acelera al salir
            if (grupo != null) grupo.alpha = 1f - e;
            if (caja != null) caja.localScale = Vector3.one * Mathf.Lerp(1f, 0.98f, e);
            if (k >= 1f) break;
            yield return null;
        }
        if (objeto != null) objeto.SetActive(false);
        TerminarAparicion(caja, grupo);   // listo para la próxima vez que se muestre
    }

    /// <summary>Deja el modal en su estado final (por si se oculta a mitad de la animación).</summary>
    public static void TerminarAparicion(RectTransform caja, CanvasGroup grupo)
    {
        if (grupo != null) grupo.alpha = 1f;
        if (caja != null) caja.localScale = Vector3.one;
    }
}
