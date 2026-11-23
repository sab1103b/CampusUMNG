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
        // Ancho completo menos Margen por lado (mismo borde que la tarjeta y los modales), no un % de la pantalla
        UIRapida.Anclar(ayuda.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1),
                        new Vector2(0, -altoBarra - altoTipos - UIRapida.Esp2), new Vector2(-2f * UIRapida.Margen, AltoAyuda));
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
        btnGuardar = UIRapida.Boton(tarjeta.transform, "Guardar", TextoGuardar, UIRapida.TamSubtitulo, UIRapida.Verde, UIRapida.SobreColor, fuente, Guardar);
        UIRapida.RedondearPildora((Image)btnGuardar.targetGraphic);   // AltoBotonPrincipal: radio 56, píldora real
        UIRapida.Anclar((RectTransform)btnGuardar.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, yGuardar), new Vector2(-2f * UIRapida.Margen, altoGuardar));
        textoBotonGuardar = UIRapida.TextoDe(btnGuardar);

        textoError = UIRapida.TextoError(tarjeta.transform, "Mensaje");   // mismo estilo que los errores de campo (Medium)
        textoError.alignment = TextAlignmentOptions.MidlineLeft;
        UIRapida.Anclar((RectTransform)textoError.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, yMensaje), new Vector2(-2f * UIRapida.Margen, altoMensaje));
        // Mensajes largos (respuestas del servicio): bajan hasta TamMinimo y, si aun así no caben, dos líneas con "…"
        textoError.enableAutoSizing = true;
        textoError.fontSizeMin = UIRapida.TamMinimo;
        textoError.fontSizeMax = UIRapida.TamSecundario;
        textoError.overflowMode = TextOverflowModes.Ellipsis;
        textoError.maxVisibleLines = 2;

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
        // Modal de lista compartido (UIRapida.ModalLista): mismo ancho, alto y cara que el de puertas de Modificar mapa.
        // Velo en la raíz del canvas (fuera de la zona segura): cubre también las franjas de cámara y gestos
        modalOpciones = UIRapida.ModalLista(transform, "", CerrarOpciones, fuente, null, "Opciones");
        panelOpciones = modalOpciones.gameObject;
        grupoOpciones = modalOpciones.Grupo;
        cajaOpciones = modalOpciones.Caja;
        tituloOpciones = modalOpciones.Titulo;
        listaOpciones = modalOpciones.Lista;
        panelOpciones.SetActive(false);

        ConstruirDescartar();
    }

    // ==================================================================
    // Modal "¿Descartar los cambios?": el modal compartido de la app (UIRapida.ModalConfirmar), igual que
    // InterfazCampus y Modificar mapa. Velo (tocarlo o Atrás = Seguir editando) + título, explicación,
    // "Seguir editando" (secundario) y "Descartar" (destructivo, Rojo).
    // ==================================================================
    private void ConstruirDescartar()
    {
        modalDescartar = UIRapida.ModalConfirmar(transform, "¿Descartar los cambios?",
                                                 "Lo que escribiste en este formulario no se guardará.",
                                                 "Seguir editando", "Descartar", true, Descartar, null, fuente, "Descartar");
    }

    private void AbrirDescartar()
    {
        if (modalDescartar == null) return;
        CerrarOpciones(false);
        modalDescartar.Mostrar();
    }

    // "Seguir editando", tocar el velo o Atrás: cierra el modal sin descartar
    private void CerrarDescartar(bool animar)
    {
        if (modalDescartar != null) modalDescartar.Ocultar(animar);
    }

    // "Descartar" (el modal ya se cerró al confirmar): se pierde lo escrito y se cierra la pantalla
    private void Descartar()
    {
        firmaInicial = null;   // sin cambios pendientes: Atrás ya no vuelve a preguntar
        Cancelar();
    }

    // Medidas derivadas de la escala (UIRapida.Tokens): nada de literales sueltos
    private const float AltoAyuda = UIRapida.Esp2 + UIRapida.ToqueMinimo + UIRapida.Esp2;            // 128: tarjeta de ayuda sobre el mapa
    private const float AltoLineaPuerta = UIRapida.AltoEtiquetaCampo + UIRapida.Esp4;                // 72: su línea de puerta (abajo)
    private const float OcultoTarjeta = UIRapida.Esp4;                                               // 32: parte de la tarjeta bajo el borde
    private const float AltoMensaje = 2f * UIRapida.TamSecundario * 1.2f + UIRapida.Esp1;            // ≈70: dos líneas (interlineado 1,2) sobre Guardar
    private const float AltoErrorCampo = UIRapida.TamSecundario + UIRapida.Esp1;                     // 34: una línea bajo cada campo

    // Pestaña de tipo: el color de elegida / no elegida lo pone Marcar (UIRapida.MarcarSeleccion)
    private Button BotonTipo(Transform padre, string texto, string icono, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, "Tipo " + texto, texto, UIRapida.TamCuerpo, UIRapida.NoSeleccionadoFondo,
                                  UIRapida.NoSeleccionadoTexto, fuente, accion);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        TMP_Text t = UIRapida.TextoDe(b);
        ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3 + UIRapida.TamIconoBoton + UIRapida.Esp2, UIRapida.Esp1 / 2f);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        Image ic = UIRapida.Panel("Icono", b.transform, UIRapida.NoSeleccionadoTexto);
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
        TMP_Text t = UIRapida.TextoError(padre, "Error");
        t.gameObject.AddComponent<LayoutElement>().minHeight = AltoErrorCampo;
        t.gameObject.SetActive(false);
        return t;
    }

    // Botón que muestra la opción elegida y abre la lista
    private Button Selector(Transform padre, string vacio, UnityEngine.Events.UnityAction abrir)
    {
        Button b = UIRapida.Boton(padre, "Selector", vacio, UIRapida.TamCuerpo, UIRapida.FondoCampo, UIRapida.Gris, fuente, abrir);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        UIRapida.EstadosBotonClaro(b);              // FondoCampo: al presionar, PresionadoClaro (Gris 4,6:1, Oscuro 6,8:1)
        // Mismo contorno que los campos de texto (WCAG 1.4.11); sin indicador de foco (es un botón), solo de error
        UIRapida.IndicadorFoco(b, UIRapida.BordeDe((Image)b.targetGraphic), UIRapida.RadioControl, false);
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
        modalOpciones.Vaciar();   // apaga y destruye las filas anteriores, frena la inercia y vuelve arriba

        for (int i = 0; i < opciones.Length; i++)
        {
            int indice = i;
            Button b = UIRapida.Boton(listaOpciones, "Opcion " + i, UIRapida.TextoSeguro(opciones[i]), UIRapida.TamCuerpo,
                                      UIRapida.NoSeleccionadoFondo, UIRapida.NoSeleccionadoTexto, fuente, () =>
                                      {
                                          CerrarOpciones();
                                          elegir(indice);
                                      });
            UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
            UIRapida.MarcarSeleccion(b, i == actual);   // mismo estilo de selección que chips y pestañas
            TMP_Text t = UIRapida.TextoDe(b);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3, UIRapida.Esp1 / 2f);
            LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
        }
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
