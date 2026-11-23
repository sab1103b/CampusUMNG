using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Formulario de lugar: construcción de la pantalla y listas de opciones (categorías y edificios).</summary>
public partial class FormularioLugarMapa
{
    private void Construir()
    {
        // ---------- Barra superior ----------
        UIRapida.BarraSuperior(transform, "Nuevo lugar", fuente, CerrarConX, out titulo);
        const float altoBarra = UIRapida.AltoBarraSuperior;

        // ---------- Tipo: Edificio / Espacio interior ----------
        const float altoTipos = UIRapida.Esp2 + UIRapida.ToqueMinimo + UIRapida.Esp2;   // 128
        Image tipos = UIRapida.Panel("Tipo", transform, UIRapida.SuperficieOpaca);   // franja fija pegada a la barra
        UIRapida.Anclar(tipos.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -altoBarra), new Vector2(0, altoTipos));
        HorizontalLayoutGroup ht = tipos.gameObject.AddComponent<HorizontalLayoutGroup>();
        ht.padding = new RectOffset((int)UIRapida.Esp3, (int)UIRapida.Esp3, (int)UIRapida.Esp2, (int)UIRapida.Esp2);
        ht.spacing = UIRapida.Esp2;
        ht.childControlWidth = ht.childControlHeight = true;
        ht.childForceExpandWidth = ht.childForceExpandHeight = true;
        tabEdificio = BotonTipo(tipos.transform, "Edificio", "cubo", () => PonerModo(false));
        tabInterior = BotonTipo(tipos.transform, "Espacio interior", "puerta", () => PonerModo(true));

        // ---------- Ayuda sobre el mapa ----------
        Image ayuda = UIRapida.Panel("Ayuda", transform, UIRapida.Superficie);
        UIRapida.Redondear(ayuda, UIRapida.RadioControl);
        ayuda.raycastTarget = false;
        UIRapida.Anclar(ayuda.rectTransform, new Vector2(0.04f, 1), new Vector2(0.96f, 1), new Vector2(0.5f, 1),
                        new Vector2(0, -altoBarra - altoTipos - UIRapida.Esp2), new Vector2(0, AltoAyuda));
        textoAyuda = UIRapida.Texto(ayuda.transform, "Texto", "", UIRapida.TamSecundario, UIRapida.Oscuro, fuente, true, TextAlignmentOptions.TopLeft);
        UIRapida.Estirar((RectTransform)textoAyuda.transform, UIRapida.Esp3, UIRapida.Margen + UIRapida.Esp3, UIRapida.Esp3, UIRapida.Esp2);
        textoAyuda.enableAutoSizing = true;
        textoAyuda.fontSizeMin = UIRapida.TamMinimo;
        textoAyuda.fontSizeMax = UIRapida.TamSecundario;
        textoPuerta = UIRapida.Texto(ayuda.transform, "Puerta", "", UIRapida.TamSecundario, UIRapida.Gris, fuente, false, TextAlignmentOptions.BottomLeft);
        UIRapida.Estirar((RectTransform)textoPuerta.transform, UIRapida.Esp3, UIRapida.Esp2, UIRapida.Esp3, AltoAyuda - AltoLineaPuerta);
        textoPuerta.enableAutoSizing = true;
        textoPuerta.fontSizeMin = UIRapida.TamMinimo;
        textoPuerta.fontSizeMax = UIRapida.TamSecundario;

        // ---------- Zoom ----------
        // Mismo grupo (margen, tamaño y separación) que en Modificar mapa y en el mapa del usuario. La tarjeta del
        // formulario ocupa la mitad de abajo, así que el grupo se centra en el mapa que queda visible (0,65 del alto).
        if (camara != null)
        {
            RectTransform zoom = UIRapida.GrupoControlesMapa(transform, "ControlesMapa", CentroControlesY);
            UIRapida.BotonMapa(zoom, "ZoomMas", "mas", camara.ZoomMas);
            UIRapida.BotonMapa(zoom, "ZoomMenos", "menos", camara.ZoomMenos);
        }

        // ---------- Pin (la punta marca el lugar) ----------
        Image imgPin = UIRapida.Panel("Pin", transform, UIRapida.Verde);
        imgPin.sprite = UIRapida.Icono("pin");
        imgPin.preserveAspect = true;
        pin = imgPin.rectTransform;
        pin.anchorMin = pin.anchorMax = new Vector2(0.5f, 0.5f);
        pin.pivot = new Vector2(0.5f, 0.08f);
        pin.sizeDelta = new Vector2(UIRapida.TamPinMapa, UIRapida.TamPinMapa);
        arrastre = imgPin.gameObject.AddComponent<PinArrastrable>();
        arrastre.Arrastrado += MoverPinA;
        imgPin.gameObject.SetActive(false);

        // ---------- Tarjeta inferior con el formulario ----------
        Image tarjeta = UIRapida.Panel("Formulario", transform, UIRapida.SuperficieOpaca);   // hoja que tapa el mapa
        UIRapida.Redondear(tarjeta, UIRapida.RadioTarjeta);
        // Baja "oculto" bajo el borde: las esquinas redondeadas de abajo no se ven (hoja pegada al borde)
        UIRapida.Anclar(tarjeta.rectTransform, Vector2.zero, new Vector2(1, 0.5f), new Vector2(0.5f, 0), new Vector2(0, -OcultoTarjeta), new Vector2(0, OcultoTarjeta));
        UIRapida.SombraDe(tarjeta, UIRapida.RadioTarjeta);

        // De abajo hacia arriba: Guardar (AltoBotonPrincipal), mensaje (AltoMensaje) y el formulario desplazable
        const float yGuardar = UIRapida.Margen + UIRapida.Esp3, altoGuardar = UIRapida.AltoBotonPrincipal;
        const float yMensaje = yGuardar + altoGuardar + UIRapida.Esp1, altoMensaje = AltoMensaje;
        btnGuardar = UIRapida.Boton(tarjeta.transform, "Guardar", TextoGuardar, UIRapida.TamSubtitulo, UIRapida.Verde, Color.white, fuente, Guardar);
        UIRapida.Redondear((Image)btnGuardar.targetGraphic, UIRapida.RadioPildora);
        UIRapida.Anclar((RectTransform)btnGuardar.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, yGuardar), new Vector2(-2f * UIRapida.Margen, altoGuardar));
        textoBotonGuardar = UIRapida.TextoDe(btnGuardar);

        textoError = UIRapida.Texto(tarjeta.transform, "Mensaje", "", UIRapida.TamSecundario, UIRapida.Rojo, fuente, true, TextAlignmentOptions.MidlineLeft);
        UIRapida.Anclar((RectTransform)textoError.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, yMensaje), new Vector2(-2f * UIRapida.Margen, altoMensaje));

        RectTransform vista = UIRapida.Rect("Vista", tarjeta.transform);
        UIRapida.Estirar(vista, UIRapida.Margen, yMensaje + altoMensaje, UIRapida.Margen, UIRapida.Margen);
        vista.gameObject.AddComponent<RectMask2D>();
        scrollFormulario = vista.gameObject.AddComponent<ScrollRect>();
        RectTransform contenido = UIRapida.Rect("Contenido", vista);
        UIRapida.Anclar(contenido, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vc = contenido.gameObject.AddComponent<VerticalLayoutGroup>();
        vc.spacing = UIRapida.Esp1;
        vc.padding = new RectOffset(0, 0, 0, (int)UIRapida.Esp3);
        vc.childControlWidth = vc.childControlHeight = true;
        vc.childForceExpandWidth = true;
        vc.childForceExpandHeight = false;
        contenido.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollFormulario.viewport = vista;
        scrollFormulario.content = contenido;
        scrollFormulario.horizontal = false;
        scrollFormulario.movementType = ScrollRect.MovementType.Clamped;
        scrollFormulario.scrollSensitivity = 30;

        // Leyenda del asterisco, antes del primer campo
        TMP_Text leyenda = UIRapida.Texto(contenido, "Leyenda", "* Campo obligatorio", UIRapida.TamSecundario, UIRapida.Gris,
                                          fuente, false, TextAlignmentOptions.MidlineLeft);
        leyenda.gameObject.AddComponent<LayoutElement>().minHeight = AltoErrorCampo;

        // Campos (los del espacio interior se muestran u ocultan según el modo)
        filaEdificioPadre = Grupo(contenido, "¿En qué edificio está? *", out Transform ge);
        selectorEdificio = Selector(ge, "Elige el edificio", AbrirEdificios);
        errorEdificio = ErrorCampo(ge);

        Grupo(contenido, "Nombre *", out Transform gn);
        campoNombre = Campo(gn, "CampoNombre", "Ej. Edificio C - Laboratorios", false, NombreMax);
        errorNombre = ErrorCampo(gn);
        campoNombre.onValueChanged.AddListener(_ => OcultarError(errorNombre));

        Grupo(contenido, "Categoría *", out Transform gc);
        selectorCategoria = Selector(gc, "Elige una categoría", AbrirCategorias);
        errorCategoria = ErrorCampo(gc);

        filaPiso = Grupo(contenido, "Piso", out Transform gp);
        campoPiso = Campo(gp, "CampoPiso", "Ej. 2", false, 20);

        Grupo(contenido, "Descripción", out Transform gd);
        campoDescripcion = Campo(gd, "CampoDescripcion", "Qué se encuentra en este lugar", true, 300);

        GameObject gi = Grupo(contenido, "Cómo encontrarlo", out Transform gii);
        etiquetaIndicacion = gi.GetComponentInChildren<TMP_Text>(true);
        campoIndicacion = Campo(gii, "CampoIndicacion", "Ej. Entrada por el costado norte", true, 300);

        Grupo(contenido, "Horario", out Transform gh);
        campoHorario = Campo(gh, "CampoHorario", "Ej. L-V 7:00 - 18:00", false, 120);

        // ---------- Lista de opciones (categorías / edificios) ----------
        // Velo en la raíz del canvas (fuera de la zona segura): cubre también las franjas de cámara y gestos
        Image po = UIRapida.Panel("Opciones", transform, UIRapida.Velo);
        po.gameObject.AddComponent<ZonaSegura.FueraDeZona>();
        UIRapida.Estirar(po.rectTransform);
        panelOpciones = po.gameObject;
        grupoOpciones = panelOpciones.AddComponent<CanvasGroup>();
        Button cierre = po.gameObject.AddComponent<Button>();
        cierre.transition = Selectable.Transition.None;
        cierre.onClick.AddListener(CerrarOpciones);

        // Raíz transparente animada (Aparecer escala la raíz): la sombra es su primer hijo y escala con la cara
        RectTransform raizCaja = UIRapida.Rect("Caja", po.transform);
        UIRapida.Anclar(raizCaja, new Vector2(0.06f, 0.2f), new Vector2(0.94f, 0.8f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        cajaOpciones = raizCaja;
        UIRapida.SombraComoHija(raizCaja, UIRapida.RadioTarjeta);
        Image caja = UIRapida.Panel("Cara", raizCaja, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(caja, UIRapida.RadioTarjeta);
        UIRapida.Estirar(caja.rectTransform);
        Button bloqueo = caja.gameObject.AddComponent<Button>();   // los toques en la caja no llegan al velo (que cierra)
        bloqueo.transition = Selectable.Transition.None;
        bloqueo.navigation = new Navigation { mode = Navigation.Mode.None };
        tituloOpciones = UIRapida.TituloModal(caja.transform, "", TextAlignmentOptions.MidlineLeft);
        UIRapida.Anclar((RectTransform)tituloOpciones.transform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -UIRapida.Esp3),
                        new Vector2(-2f * UIRapida.Margen, UIRapida.AltoTituloModal));

        RectTransform vo = UIRapida.Rect("Vista", caja.transform);
        UIRapida.Estirar(vo, UIRapida.Margen, UIRapida.Margen, UIRapida.Margen, UIRapida.Esp3 + UIRapida.AltoTituloModal + UIRapida.Esp1);
        vo.gameObject.AddComponent<RectMask2D>();
        ScrollRect so = vo.gameObject.AddComponent<ScrollRect>();
        listaOpciones = UIRapida.Rect("Lista", vo);
        UIRapida.Anclar(listaOpciones, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vl = listaOpciones.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = UIRapida.Esp1;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        listaOpciones.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        so.viewport = vo;
        so.content = listaOpciones;
        so.horizontal = false;
        so.movementType = ScrollRect.MovementType.Clamped;
        panelOpciones.SetActive(false);

        ConstruirDescartar();
    }

    // ==================================================================
    // Modal "¿Descartar los cambios?" (como el de confirmar de InterfazCampus)
    // Velo (tocarlo o Atrás = Seguir editando) + tarjeta con título, explicación,
    // "Seguir editando" (secundario) y "Descartar" (destructivo, Rojo).
    // ==================================================================
    private void ConstruirDescartar()
    {
        Image velo = UIRapida.Panel("Descartar", transform, UIRapida.Velo);
        velo.gameObject.AddComponent<ZonaSegura.FueraDeZona>();   // cubre también las franjas de cámara y gestos
        UIRapida.Estirar(velo.rectTransform);
        panelDescartar = velo.gameObject;
        grupoDescartar = panelDescartar.AddComponent<CanvasGroup>();
        Button fuera = panelDescartar.AddComponent<Button>();
        fuera.transition = Selectable.Transition.None;
        fuera.onClick.AddListener(() => CerrarDescartar(true));

        // Tarjeta centrada que crece con el contenido; cara y sombra son hijas estiradas
        Image tarjeta = UIRapida.Panel("Tarjeta", velo.transform, UIRapida.Transparente);
        cajaDescartar = tarjeta.rectTransform;
        Image cara = UIRapida.Panel("Cara", tarjeta.transform, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(cara, UIRapida.RadioTarjeta);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        UIRapida.SombraComoHija(cajaDescartar);
        UIRapida.Anclar(cajaDescartar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(UIRapida.AnchoModal, 0f));
        VerticalLayoutGroup v = tarjeta.gameObject.AddComponent<VerticalLayoutGroup>();
        int m = (int)UIRapida.Margen;
        v.padding = new RectOffset(m, m, m, m);
        v.spacing = UIRapida.Esp3;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tarjeta.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Button bloqueo = tarjeta.gameObject.AddComponent<Button>();   // tocar la tarjeta no cierra
        bloqueo.transition = Selectable.Transition.None;
        bloqueo.navigation = new Navigation { mode = Navigation.Mode.None };

        UIRapida.TituloModal(tarjeta.transform, "¿Descartar los cambios?");
        UIRapida.Texto(tarjeta.transform, "Texto", "Lo que escribiste en este formulario no se guardará.",
                       UIRapida.TamCuerpo, UIRapida.Gris, fuente, false);

        RectTransform fila = UIRapida.Rect("Botones", tarjeta.transform);
        fila.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.ToqueMinimo;
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UIRapida.Esp2;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;

        BotonModalDescartar(UIRapida.BotonSecundario(fila, "SeguirEditando", "Seguir editando", () => CerrarDescartar(true),
                                                     UIRapida.TamCuerpo, fuente));
        Button descartar = UIRapida.Boton(fila, "Descartar", "Descartar", UIRapida.TamCuerpo, UIRapida.Rojo, Color.white, fuente, Descartar);
        UIRapida.Redondear((Image)descartar.targetGraphic, UIRapida.RadioPildora);
        BotonModalDescartar(descartar);
        panelDescartar.SetActive(false);
    }

    private static void BotonModalDescartar(Button b)
    {
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
        le.flexibleWidth = 1f;
    }

    private void AbrirDescartar()
    {
        if (panelDescartar == null) return;
        CerrarOpciones(false);
        if (animDescartar != null) { StopCoroutine(animDescartar); animDescartar = null; }
        panelDescartar.SetActive(true);
        panelDescartar.transform.SetAsLastSibling();
        if (grupoDescartar != null) grupoDescartar.interactable = grupoDescartar.blocksRaycasts = true;
        if (isActiveAndEnabled) animDescartar = StartCoroutine(UIRapida.Aparecer(cajaDescartar, grupoDescartar));
        else UIRapida.TerminarAparicion(cajaDescartar, grupoDescartar);
    }

    // "Seguir editando", tocar el velo o Atrás: cierra el modal sin descartar
    private void CerrarDescartar(bool animar)
    {
        if (animDescartar != null) { StopCoroutine(animDescartar); animDescartar = null; }
        if (panelDescartar == null) return;
        if (animar && panelDescartar.activeSelf && isActiveAndEnabled)
        {
            if (grupoDescartar != null) grupoDescartar.interactable = grupoDescartar.blocksRaycasts = false;
            animDescartar = StartCoroutine(UIRapida.Desaparecer(panelDescartar, cajaDescartar, grupoDescartar));
            return;
        }
        UIRapida.TerminarAparicion(cajaDescartar, grupoDescartar);
        panelDescartar.SetActive(false);
    }

    // "Descartar": se pierde lo escrito y se cierra la pantalla
    private void Descartar()
    {
        if (!DescartarAbierto) return;   // evita un doble toque durante el cierre
        CerrarDescartar(false);
        firmaInicial = null;   // sin cambios pendientes: Atrás ya no vuelve a preguntar
        Cancelar();
    }

    private bool DescartarAbierto => panelDescartar != null && panelDescartar.activeSelf && (grupoDescartar == null || grupoDescartar.interactable);

    // Medidas derivadas de la escala (UIRapida.Tokens): nada de literales sueltos
    private const float AltoAyuda = UIRapida.Esp2 + UIRapida.ToqueMinimo + UIRapida.Esp2;            // 128: tarjeta de ayuda sobre el mapa
    private const float AltoLineaPuerta = UIRapida.AltoEtiquetaCampo + UIRapida.Esp4;                // 72: su línea de puerta (abajo)
    private const float OcultoTarjeta = UIRapida.Esp4;                                               // 32: parte de la tarjeta bajo el borde
    private const float AltoMensaje = 2f * UIRapida.TamSecundario + UIRapida.Esp1;                   // 60: dos líneas sobre Guardar
    private const float AltoErrorCampo = UIRapida.TamSecundario + UIRapida.Esp1;                     // 34: una línea bajo cada campo

    private Button BotonTipo(Transform padre, string texto, string icono, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, "Tipo " + texto, texto, UIRapida.TamCuerpo, UIRapida.Menta, UIRapida.SobreMenta, fuente, accion);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        TMP_Text t = UIRapida.TextoDe(b);
        ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3 + UIRapida.TamIconoBoton + UIRapida.Esp2, UIRapida.Esp1 / 2f);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        Image ic = UIRapida.Panel("Icono", b.transform, UIRapida.SobreMenta);
        ic.sprite = UIRapida.Icono(icono);
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        UIRapida.Anclar(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(UIRapida.Esp3, 0), new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = UIRapida.ToqueMinimo;
        return b;
    }

    // Etiqueta + espacio para el control; devuelve el grupo completo (para mostrarlo u ocultarlo)
    private GameObject Grupo(Transform padre, string etiqueta, out Transform espacio)
    {
        RectTransform g = UIRapida.Rect("Grupo " + etiqueta, padre);
        VerticalLayoutGroup v = g.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        UIRapida.Etiqueta(g, etiqueta, fuente);
        espacio = g;
        return g.gameObject;
    }

    // Campo de texto con alto táctil mínimo
    private TMP_InputField Campo(Transform padre, string nombre, string placeholder, bool multilinea, int limite)
    {
        TMP_InputField c = UIRapida.Campo(padre, nombre, placeholder, fuente, multilinea, limite);
        LayoutElement le = c.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = UIRapida.ToqueMinimo;
            le.preferredHeight = Mathf.Max(le.preferredHeight, UIRapida.ToqueMinimo);
        }
        return c;
    }

    // Mensaje de validación en rojo debajo de un campo (oculto hasta que haga falta)
    private TMP_Text ErrorCampo(Transform padre)
    {
        TMP_Text t = UIRapida.Texto(padre, "Error", "", UIRapida.TamSecundario, UIRapida.Rojo, fuente, false, TextAlignmentOptions.TopLeft);
        t.gameObject.AddComponent<LayoutElement>().minHeight = AltoErrorCampo;
        t.gameObject.SetActive(false);
        return t;
    }

    // Botón que muestra la opción elegida y abre la lista
    private Button Selector(Transform padre, string vacio, UnityEngine.Events.UnityAction abrir)
    {
        Button b = UIRapida.Boton(padre, "Selector", vacio, UIRapida.TamCuerpo, UIRapida.FondoCampo, UIRapida.Gris, fuente, abrir);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        UIRapida.EstadosBotonClaro(b);              // FondoCampo: al presionar, PresionadoClaro
        UIRapida.BordeDe((Image)b.targetGraphic);   // mismo contorno que los campos de texto (WCAG 1.4.11)
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
        TMP_Text t = UIRapida.TextoDe(b);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        UIRapida.Peso(t, false);   // el valor elegido va en peso normal
        const float tamFlecha = UIRapida.TamIconoChico;
        ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3, UIRapida.Esp1 / 2f);
        ((RectTransform)t.transform).offsetMax = new Vector2(-(UIRapida.Esp3 + tamFlecha + UIRapida.Esp1), -UIRapida.Esp1 / 2f);
        Image flecha = UIRapida.Panel("Flecha", b.transform, UIRapida.Verde);
        flecha.sprite = UIRapida.Icono("abajo");
        flecha.preserveAspect = true;
        flecha.raycastTarget = false;
        UIRapida.Anclar(flecha.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-UIRapida.Esp3, 0), new Vector2(tamFlecha, tamFlecha));
        return b;
    }

    private static void TextoSelector(Button b, string texto, bool elegido)
    {
        TMP_Text t = UIRapida.TextoDe(b);
        t.text = texto;
        t.color = elegido ? UIRapida.Oscuro : UIRapida.Gris;
    }

    // ==================================================================
    // Listas de opciones
    // ==================================================================
    private void AbrirCategorias()
    {
        string[] lista = esInterior ? CategoriasInterior : CategoriasEdificio;
        MostrarOpciones("Categoría", lista, Array.IndexOf(lista, categoria), i =>
        {
            categoria = lista[i];
            TextoSelector(selectorCategoria, UIRapida.TextoSeguro(categoria), true);
            OcultarError(errorCategoria);
        });
    }

    // Cada opción se identifica por su posición en la lista (y de ahí por el lugar y su id), no por el nombre:
    // dos edificios con el mismo nombre ya no se confunden
    private void AbrirEdificios()
    {
        List<LugaresService.Lugar> lista = new List<LugaresService.Lugar>(lugares.Lugares);
        lista.Sort((a, b) => string.Compare(a.nombre, b.nombre, StringComparison.CurrentCultureIgnoreCase));
        string[] nombres = lista.ConvertAll(l => l.nombre).ToArray();
        int actual = lista.FindIndex(x => x.id == edificioPadre);

        MostrarOpciones("¿En qué edificio está?", nombres, actual, i =>
        {
            LugaresService.Lugar l = lista[i];
            if (l == null) return;
            edificioPadre = l.id;
            TextoSelector(selectorEdificio, UIRapida.TextoSeguro(l.nombre), true);
            OcultarError(errorEdificio);
            ubicacion = new Vector2d(l.latitud, l.longitud);
            hayUbicacion = true;
            ActualizarCamara();
            ActualizarPuerta();
        });
    }

    // "actual": posición de la opción elegida (-1 = ninguna). "elegir" recibe la posición tocada.
    private void MostrarOpciones(string tituloLista, string[] opciones, int actual, Action<int> elegir)
    {
        tituloOpciones.text = tituloLista;
        for (int i = listaOpciones.childCount - 1; i >= 0; i--)
        {
            GameObject hijo = listaOpciones.GetChild(i).gameObject;
            hijo.SetActive(false);   // Destroy es diferido: que no cuente en el layout de este frame
            Destroy(hijo);
        }

        for (int i = 0; i < opciones.Length; i++)
        {
            int indice = i;
            bool sel = i == actual;
            Button b = UIRapida.Boton(listaOpciones, "Opcion " + i, UIRapida.TextoSeguro(opciones[i]), UIRapida.TamCuerpo,
                                      sel ? UIRapida.Verde : UIRapida.FondoCampo, sel ? Color.white : UIRapida.Oscuro, fuente, () =>
                                      {
                                          CerrarOpciones();
                                          elegir(indice);
                                      });
            UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
            TMP_Text t = UIRapida.TextoDe(b);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            UIRapida.Peso(t, sel);
            ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3, UIRapida.Esp1 / 2f);
            LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
        }
        listaOpciones.anchoredPosition = Vector2.zero;   // la lista empieza arriba
        panelOpciones.SetActive(true);
        panelOpciones.transform.SetAsLastSibling();
        if (grupoOpciones != null) grupoOpciones.interactable = grupoOpciones.blocksRaycasts = true;
        if (animOpciones != null) StopCoroutine(animOpciones);
        animOpciones = StartCoroutine(UIRapida.Aparecer(cajaOpciones, grupoOpciones));
    }

    // Cierra la lista con fundido (UIRapida.Desaparecer); si la pantalla está apagada o se va a cerrar, al instante
    private void CerrarOpciones() => CerrarOpciones(true);

    private void CerrarOpciones(bool animar)
    {
        if (animOpciones != null) { StopCoroutine(animOpciones); animOpciones = null; }
        if (panelOpciones == null) return;
        if (animar && panelOpciones.activeSelf && isActiveAndEnabled)
        {
            if (grupoOpciones != null) grupoOpciones.interactable = grupoOpciones.blocksRaycasts = false;   // sin toques dobles mientras se va
            animOpciones = StartCoroutine(UIRapida.Desaparecer(panelOpciones, cajaOpciones, grupoOpciones));
            return;
        }
        UIRapida.TerminarAparicion(cajaOpciones, grupoOpciones);
        panelOpciones.SetActive(false);
    }

    // La lista está abierta y no se está cerrando
    private bool OpcionesAbiertas => panelOpciones != null && panelOpciones.activeSelf && (grupoOpciones == null || grupoOpciones.interactable);
}
