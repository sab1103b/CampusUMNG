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

/// <summary> InterfazCampus: ficha del lugar (contenido y diseño). </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Usuario: ficha
    // ==================================================================
    private int fichaLugarId, fichaServicioId;   // lo que muestra la ficha (para refrescarla al recargar datos)
    // Mismo texto que la etiqueta del formulario de espacio interior
    private const string TituloIndicacionInterior = "Cómo llegar dentro del edificio";

    private void MostrarFicha(LugaresService.Lugar l, LugaresService.Servicio s)
    {
        if (l == null) return;
        fichaLugarId = l.id;
        fichaServicioId = s != null ? s.id : 0;
        if (fichaNombre != null) fichaNombre.text = TextoSeguro(s != null ? s.nombre : l.nombre);
        if (fichaCategoria != null)
        {
            // Servicio sin categoría: solo el nombre del lugar (nada de " · Lugar")
            string cat = s == null ? l.categoria
                       : string.IsNullOrEmpty(s.categoria) ? l.nombre
                       : $"{s.categoria} · {l.nombre}";
            fichaCategoria.text = TextoSeguro((cat ?? "").ToUpperInvariant());
            if (fichaFilas != null) fichaCategoria.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(cat));   // etiqueta menta
        }

        List<LugaresService.Servicio> servicios = lugares != null ? lugares.ServiciosDe(l.id) : new List<LugaresService.Servicio>();
        string descripcion = s != null && !string.IsNullOrEmpty(s.descripcion) ? s.descripcion : l.descripcion;
        string horario = s != null && !string.IsNullOrEmpty(s.horario) ? s.horario : l.horario;
        string encontrarlo = !string.IsNullOrEmpty(l.indicacion) && l.indicacion != "--" ? l.indicacion : null;

        if (fichaFilas != null)
        {
            // Ficha con filas: cada dato con su ícono y título; los datos vacíos no aparecen
            if (fichaDetalle != null)
            {
                fichaDetalle.text = TextoSeguro(descripcion);
                fichaDetalle.gameObject.SetActive(!string.IsNullOrEmpty(descripcion));
            }
            for (int k = fichaFilas.childCount - 1; k >= 0; k--) Destroy(fichaFilas.GetChild(k).gameObject);

            if (s != null && !string.IsNullOrEmpty(s.piso)) FilaFicha("capas", "Piso", s.piso);
            if (s != null && !string.IsNullOrEmpty(s.indicacion)) FilaFicha("puerta", TituloIndicacionInterior, s.indicacion);
            if (!string.IsNullOrEmpty(horario)) FilaFicha("reloj", "Horario", horario);
            if (encontrarlo != null) FilaFicha("puerta", s != null ? $"Cómo encontrar {l.nombre}" : "Cómo encontrarlo", encontrarlo);
            if (s == null && servicios.Count > 0)
                FilaFicha("lista", "Aquí encuentras", string.Join(", ", servicios.ConvertAll(x => x.nombre)));

            if (fichaScroll != null) fichaScroll.verticalNormalizedPosition = 1f;   // siempre desde arriba
        }
        else if (fichaDetalle != null)
        {
            // Respaldo: todo en un solo texto (si la ficha no se pudo reorganizar)
            StringBuilder d = new StringBuilder();
            if (!string.IsNullOrEmpty(descripcion)) d.AppendLine(TextoSeguro(descripcion));
            if (s != null && !string.IsNullOrEmpty(s.piso)) d.AppendLine($"<b>Piso:</b> {TextoSeguro(s.piso)}");
            if (s != null && !string.IsNullOrEmpty(s.indicacion)) d.AppendLine($"<b>{TituloIndicacionInterior}:</b> {TextoSeguro(s.indicacion)}");
            if (!string.IsNullOrEmpty(horario)) d.AppendLine($"<b>Horario:</b> {TextoSeguro(horario)}");
            if (encontrarlo != null)
                d.AppendLine($"<b>{(s != null ? $"Cómo encontrar {TextoSeguro(l.nombre)}" : "Cómo encontrarlo")}:</b> {TextoSeguro(encontrarlo)}");
            if (s == null && servicios.Count > 0) d.AppendLine($"<b>Aquí encuentras:</b> {TextoSeguro(string.Join(", ", servicios.ConvertAll(x => x.nombre)))}");
            fichaDetalle.text = d.ToString().Trim();
        }

        CerrarCapasExcepto(Capa.Ficha);
        bool seCerraba = CancelarCierreFicha();   // se eligió otro lugar mientras la ficha se cerraba
        if (fichaRaiz != null)
        {
            if (!fichaRaiz.gameObject.activeSelf) fichaRaiz.gameObject.SetActive(true);
            AcomodarEnFranja(fichaRaiz, UIRapida.AltoMaxFicha, pegadoArriba: false);
        }
        if (ui != null && !ui.EstaVisible(PFicha))
        {
            ui.Ir(PFicha);   // si ya estaba abierta, solo se actualiza su contenido
            if (fichaRaiz != null) animacionFicha = Aparecer(fichaRaiz, animacionFicha);
        }
        else if (seCerraba && fichaRaiz != null) animacionFicha = Aparecer(fichaRaiz, animacionFicha);
    }

    // ----- Cierre animado de la ficha -----
    // La ficha es una pantalla superpuesta del UIManager: primero se desvanece (UIRapida.Desaparecer)
    // y después se cierra con ui.Cerrar. El Atrás también pasa por aquí (AtrasEnCapas).
    private Coroutine cierreFicha;

    private void CerrarFichaAnimada()
    {
        if (ui == null || !ui.EstaVisible(PFicha) || cierreFicha != null) return;
        if (comoLlegar != null) comoLlegar.OcultarDestino();   // sin ruta, el pin del lugar se va con la ficha
        if (fichaRaiz == null || !isActiveAndEnabled || !fichaRaiz.gameObject.activeInHierarchy)
        {
            ui.Cerrar(PFicha);
            return;
        }
        if (animacionFicha != null) { StopCoroutine(animacionFicha); animacionFicha = null; }
        cierreFicha = StartCoroutine(CerrarFichaLuego());
    }

    private IEnumerator CerrarFichaLuego()
    {
        yield return UIRapida.Desaparecer(fichaRaiz.gameObject, fichaRaiz, GrupoDe(fichaRaiz));
        cierreFicha = null;
        TerminarCierreFicha();
    }

    // Deja la ficha lista para la próxima vez y la cierra en el UIManager (si sigue arriba)
    private void TerminarCierreFicha()
    {
        if (fichaRaiz != null)
        {
            UIRapida.TerminarAparicion(fichaRaiz, GrupoDe(fichaRaiz));
            // Solo se reactiva si la ficha sigue arriba: si IrRaiz/Ir la sacó de la pila no queda "huérfana" encima.
            // Si la raíz es un hijo del panel, se deja encendida (el panel se apaga en Quitar; nunca queda vacío).
            bool esPanel = fichaRaiz.gameObject.name == PFicha;
            bool sigueArriba = ui != null && ui.PantallaActual == PFicha;
            fichaRaiz.gameObject.SetActive(sigueArriba || !esPanel);
        }
        if (ui == null) return;
        if (ui.PantallaActual == PFicha) ui.Cerrar(PFicha);
        else ui.Quitar(PFicha);   // otra pantalla la tapó durante el fundido: sale del historial (Atrás no la muestra vacía)
    }

    private bool CancelarCierreFicha()
    {
        if (cierreFicha == null) return false;
        StopCoroutine(cierreFicha);
        cierreFicha = null;
        if (fichaRaiz != null) UIRapida.TerminarAparicion(fichaRaiz, GrupoDe(fichaRaiz));
        return true;
    }

    private static CanvasGroup GrupoDe(RectTransform rt)
    {
        CanvasGroup g = rt.GetComponent<CanvasGroup>();
        return g != null ? g : rt.gameObject.AddComponent<CanvasGroup>();
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
        if (raiz != null) raiz.color = UIRapida.Transparente;
        fichaRaiz.sizeDelta = new Vector2(-UIRapida.Margen, fichaRaiz.sizeDelta.y);   // margen a los lados

        Image cara = UIRapida.Panel("Cara", fichaRaiz, UIRapida.SuperficieOpaca);   // hoja que tapa el mapa
        UIRapida.Redondear(cara, UIRapida.RadioTarjeta);
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        cara.transform.SetAsFirstSibling();
        UIRapida.SombraComoHija(fichaRaiz);   // sombra única de la app (queda como primer hijo, detrás de la cara)

        Image asa = UIRapida.Panel("Asa", fichaRaiz, UIRapida.Separador);
        UIRapida.Redondear(asa, UIRapida.RadioMinimo);
        asa.raycastTarget = false;
        asa.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Anclar(asa.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -UIRapida.Esp2),
                        new Vector2(UIRapida.AnchoAsa, UIRapida.AltoAsa));
        asa.transform.SetSiblingIndex(2);

        VerticalLayoutGroup v = fichaRaiz.GetComponent<VerticalLayoutGroup>();
        if (v != null)
        {
            v.padding = Relleno(UIRapida.Margen, UIRapida.Margen, UIRapida.Margen, UIRapida.Esp3);
            v.spacing = UIRapida.Esp2;
        }

        // ---------- Encabezado: ícono · nombre + etiqueta de categoría · botón cerrar ----------
        RectTransform encabezado = UIRapida.Rect("Encabezado", fichaRaiz);
        encabezado.SetSiblingIndex(3);
        encabezado.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.AltoEncabezadoTarjeta;
        HorizontalLayoutGroup he = encabezado.gameObject.AddComponent<HorizontalLayoutGroup>();
        he.spacing = UIRapida.Esp3;
        he.childAlignment = TextAnchor.MiddleLeft;
        he.childControlWidth = he.childControlHeight = true;
        he.childForceExpandWidth = he.childForceExpandHeight = false;

        Image circulo = UIRapida.Panel("IconoLugar", encabezado, UIRapida.Menta);
        circulo.sprite = UIRapida.Circulo();
        circulo.preserveAspect = true;
        circulo.raycastTarget = false;
        LayoutElement lc = circulo.gameObject.AddComponent<LayoutElement>();
        lc.preferredWidth = lc.preferredHeight = UIRapida.TamAvatar;
        IconoEnAvatar(circulo.transform, "pin");

        RectTransform textos = UIRapida.Rect("Textos", encabezado);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup vt = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        vt.spacing = UIRapida.Esp1;
        vt.childAlignment = TextAnchor.MiddleLeft;
        vt.childControlWidth = vt.childControlHeight = true;
        vt.childForceExpandWidth = false;
        vt.childForceExpandHeight = false;

        fichaNombre.transform.SetParent(textos, false);
        QuitarAlto(fichaNombre.gameObject);
        fichaNombre.enableAutoSizing = true;
        fichaNombre.fontSizeMin = UIRapida.TamNombreFicha;   // el nombre manda sobre la descripción (TamSubtitulo)
        fichaNombre.fontSizeMax = UIRapida.TamTitulo;
        fichaNombre.textWrappingMode = TextWrappingModes.Normal;
        fichaNombre.overflowMode = TextOverflowModes.Ellipsis;
        fichaNombre.maxVisibleLines = 2;

        // Etiqueta de categoría: fondo menta redondeado que se ajusta al texto
        Image etiqueta = UIRapida.Panel("Etiqueta", textos, UIRapida.Menta);
        UIRapida.Redondear(etiqueta, UIRapida.RadioEtiqueta);
        etiqueta.raycastTarget = false;
        HorizontalLayoutGroup hl = etiqueta.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = Relleno(UIRapida.Esp2, UIRapida.Esp2, UIRapida.Esp1, UIRapida.Esp1);
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        etiqueta.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fichaCategoria.transform.SetParent(etiqueta.transform, false);
        QuitarAlto(fichaCategoria.gameObject);
        fichaCategoria.enableAutoSizing = false;
        fichaCategoria.fontSize = UIRapida.TamSecundario;   // etiquetas en MAYÚSCULAS: TamSecundario + EspaciadoMayusculas
        fichaCategoria.fontStyle = FontStyles.Bold;
        fichaCategoria.color = UIRapida.SobreMenta;   // sobre fondo Menta (5:1)
        fichaCategoria.characterSpacing = UIRapida.EspaciadoMayusculas;   // etiqueta en MAYÚSCULAS
        fichaCategoria.textWrappingMode = TextWrappingModes.NoWrap;

        // Cerrar: el botón de cerrar único de la app (círculo claro de ToqueMinimo con X gris)
        UIRapida.BotonCerrar(encabezado, CerrarFicha);

        // ---------- Separador ----------
        Image linea = UIRapida.Panel("Separador", fichaRaiz, UIRapida.Separador);
        linea.raycastTarget = false;
        linea.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.AltoSeparador;
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
        vi.spacing = UIRapida.Esp3;
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
        fichaDetalle.fontSize = UIRapida.TamSubtitulo;   // textos grandes en la ficha (decisión del dueño)
        fichaDetalle.lineSpacing = 0f;
        fichaDetalle.color = UIRapida.Oscuro;
        fichaDetalle.overflowMode = TextOverflowModes.Overflow;
        fichaDetalle.alignment = TextAlignmentOptions.TopLeft;

        fichaFilas = UIRapida.Rect("Filas", info);
        VerticalLayoutGroup vf = fichaFilas.gameObject.AddComponent<VerticalLayoutGroup>();
        vf.spacing = UIRapida.Esp3;
        vf.childControlWidth = vf.childControlHeight = true;
        vf.childForceExpandWidth = true;
        vf.childForceExpandHeight = false;

        // ---------- Botón principal ----------
        if (btnCerrarFicha != null) btnCerrarFicha.gameObject.SetActive(false);   // ahora cierra la X de arriba
        if (btnComoLlegar != null)
        {
            EstiloBotonPrincipal(btnComoLlegar, fichaNombre.font);
            IconoEnBoton(btnComoLlegar, "ruta", Color.white);

            LayoutElement fila = btnComoLlegar.transform.parent.GetComponent<LayoutElement>();
            if (fila != null) fila.preferredHeight = fila.minHeight = UIRapida.AltoBotonPrincipal;
        }
    }

    // Fila de dato: ícono verde + título pequeño en mayúsculas + valor
    private void FilaFicha(string icono, string titulo, string valor)
    {
        RectTransform fila = UIRapida.Rect("Fila " + titulo, fichaFilas);
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UIRapida.Esp3;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        Image img = UIRapida.Panel("Icono", fila, UIRapida.Verde);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        LayoutElement li = img.gameObject.AddComponent<LayoutElement>();
        li.preferredWidth = li.preferredHeight = li.minWidth = UIRapida.TamIconoGrande;

        RectTransform textos = UIRapida.Rect("Textos", fila);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup v = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        TMP_Text t = UIRapida.Texto(textos, "Titulo", TextoSeguro(titulo.ToUpperInvariant()), UIRapida.TamSecundario, UIRapida.Gris, fuenteFicha, true);
        t.characterSpacing = UIRapida.EspaciadoMayusculas;   // título en MAYÚSCULAS
        TMP_Text textoValor = UIRapida.Texto(textos, "Valor", TextoSeguro(valor), UIRapida.TamSubtitulo, UIRapida.Oscuro, fuenteFicha);
        UIRapida.AplicarFuentes(fila);   // fila creada después del Start
        // Ficha grande (decisión del dueño): mismo tamaño que la descripción, pero en Medium para distinguirse de ella
        UIRapida.PesoMedio(textoValor);
    }

    private void CerrarFicha() => CerrarCapasExcepto(Capa.Ninguna);

    // Ícono SobreMenta centrado dentro de un avatar Menta (ficha y menú de perfil): mismo tamaño en ambos
    private static void IconoEnAvatar(Transform avatar, string icono)
    {
        Image img = UIRapida.Panel("Icono", avatar, UIRapida.SobreMenta);   // sobre Menta (5:1)
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));
    }

    // Quita las alturas fijas que la integración había puesto (el texto decide su alto)
    private static void QuitarAlto(GameObject go)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) return;
        le.minHeight = le.preferredHeight = -1;
        le.flexibleHeight = -1;
    }
}
