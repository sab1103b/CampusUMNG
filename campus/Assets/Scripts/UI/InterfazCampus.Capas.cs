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

/// <summary> InterfazCampus: controles del mapa y franja libre donde se acomodan las capas. </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Controles del mapa: mover y zoom (se crean solos si la escena no los tiene)
    // ==================================================================

    private CamaraMapa camaraControles;
    private TMP_Text etiquetaPerspectiva;

    private void AlCambiarPerspectiva(bool tresD)
    {
        if (etiquetaPerspectiva != null) etiquetaPerspectiva.text = tresD ? "2D" : "3D";
    }

    private void AvisarSiUbicacionAproximada()
    {
        if (ui == null || ubicacion == null || !ubicacion.UsandoUbicacionAproximada) return;
        if (!string.IsNullOrEmpty(ubicacion.MensajeEstado)) ui.Avisar(ubicacion.MensajeEstado);
    }

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
            Registro.Info($"Controles del mapa agregados a '{cam.name}'.");
        }

        // 2. Botones +, −, ubicación y 2D/3D en la pantalla de usuario (mismo grupo que el editor y el formulario)
        if (referenciaChips == null) return;
        Canvas canvas = referenciaChips.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform usuario = canvas.rootCanvas.transform;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        Transform grupo = usuario.Find("ControlesMapa");   // puede existir si se creó con la herramienta del Editor
        if (grupo == null)
        {
            grupo = UIRapida.GrupoControlesMapa(usuario);

            // Por debajo de la lista, la ficha y la barra: cuando se abren, quedan encima
            if (panelResultados != null) grupo.SetSiblingIndex(panelResultados.transform.GetSiblingIndex());

            UIRapida.BotonMapa(grupo, "BotonZoomMas", "mas", control.ZoomMas);
            UIRapida.BotonMapa(grupo, "BotonZoomMenos", "menos", control.ZoomMenos);
            UIRapida.BotonMapa(grupo, "BotonMiUbicacion", "ubicacion", control.CentrarEnUsuario);
        }

        // Sin GPS, "Mi ubicación" centra en el punto aproximado: se explica por qué (mensaje del UbicacionController)
        Transform miUbicacion = grupo.Find("BotonMiUbicacion");
        Button botonMiUbicacion = miUbicacion != null ? miUbicacion.GetComponent<Button>() : null;
        if (botonMiUbicacion != null) botonMiUbicacion.onClick.AddListener(AvisarSiUbicacionAproximada);

        // Botón de perspectiva: dice "3D" en la vista de arriba y "2D" en la isométrica
        Transform perspectiva = grupo.Find("BotonPerspectiva");
        Button botonPerspectiva = perspectiva != null
            ? perspectiva.GetComponent<Button>()
            : UIRapida.BotonMapaTexto(grupo, "BotonPerspectiva", "3D", fuente, null);
        if (perspectiva == null && botonPerspectiva != null) botonPerspectiva.onClick.AddListener(control.AlternarPerspectiva);

        TMP_Text etiqueta = botonPerspectiva != null ? botonPerspectiva.GetComponentInChildren<TMP_Text>(true) : null;
        if (etiqueta != null)
        {
            etiqueta.text = control.Es3D ? "2D" : "3D";
            etiqueta.fontSize = UIRapida.TamSubtitulo;
            etiquetaPerspectiva = etiqueta;
            camaraControles = control;
            control.PerspectivaCambiada += AlCambiarPerspectiva;   // se quita en OnDestroy
        }

        // Grupo antiguo (creado en el Editor, sin ajuste automático): crece según cuántos botones tenga
        if (grupo.GetComponent<ContentSizeFitter>() == null)
        {
            int n = grupo.childCount;
            ((RectTransform)grupo).sizeDelta = new Vector2(UIRapida.TamBotonMapa,
                n * UIRapida.TamBotonMapa + Mathf.Max(0, n - 1) * UIRapida.Esp3);
        }
    }

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
    // Espacio libre del mapa
    // Todo lo que se abre sobre el mapa (resultados, ficha, barra de ruta, avisos) se acomoda SOLO
    // en la franja libre: debajo de las categorías y encima de la zona inferior del diseño.
    // Se calcula con la pantalla real, así no tapa el encabezado ni los botones en ningún celular.
    // ==================================================================
    private const float MargenFranja = UIRapida.Esp2;      // aire entre la franja libre y lo que se acomoda en ella
    private const float AltoMinimoFranja = 80f;            // un panel nunca queda más bajo que esto (pantallas muy cortas)

    // Límites de la franja libre en coordenadas de mundo (en un Canvas Overlay = píxeles de pantalla)
    private static readonly Vector3[] esquinas = new Vector3[4];

    private bool FranjaLibre(out float yArriba, out float yAbajo)
    {
        yArriba = float.PositiveInfinity;
        yAbajo = float.NegativeInfinity;
        Canvas.ForceUpdateCanvases();
        Vector3[] c = esquinas;   // 0 = abajo-izquierda, 1 = arriba-izquierda

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
        float arriba = padre.InverseTransformPoint(new Vector3(0, yArribaMundo, 0)).y - MargenFranja;
        float abajo = padre.InverseTransformPoint(new Vector3(0, yAbajoMundo, 0)).y + MargenFranja;
        float alto = Mathf.Max(AltoMinimoFranja, Mathf.Min(altoMaximo, arriba - abajo));

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
    // en el formulario y el editor del mapa, arriba bajo la barra superior (abajo están "Guardar" y la barra del editor);
    // en las demás pantallas, abajo, lejos de títulos y botones.
    private void AcomodarAviso()
    {
        if (fondoAviso == null) return;
        RectTransform rt = (RectTransform)fondoAviso.transform;
        string p = ui != null ? ui.PantallaActual : null;

        // A lo ancho con el Margen de la app a cada lado (antes: 6 % del ancho, distinto en cada celular)
        rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
        rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
        rt.sizeDelta = new Vector2(-2f * UIRapida.Margen, rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);

        if ((p == PUsuario || p == PFicha) && FranjaLibre(out _, out _))
        {
            AcomodarEnFranja(rt, UIRapida.AltoAviso, pegadoArriba: true);
            return;
        }

        if (p == PFormulario || p == EditorMapa.NombrePantalla)
        {
            rt.anchorMin = new Vector2(rt.anchorMin.x, 1);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                                              -(UIRapida.AltoBarraSuperior + UIRapida.Esp2) - RecorteSuperior(rt));
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, UIRapida.AltoAviso);
            return;
        }

        rt.anchorMin = new Vector2(rt.anchorMin.x, 0);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, UIRapida.AltoBotonPrincipal);   // por encima de los botones de abajo
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, UIRapida.AltoAviso);
    }

    /// <summary>
    /// Cuánto del borde superior del padre del aviso queda fuera de Screen.safeArea (cámara frontal), en unidades
    /// del canvas del aviso. Es 0 si el padre ya está dentro de la zona segura (ZonaSegura lo movió ahí).
    /// </summary>
    private static float RecorteSuperior(RectTransform rt)
    {
        RectTransform padre = rt.parent as RectTransform;
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        if (padre == null || canvas == null) return 0f;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        padre.GetWorldCorners(esquinas);   // 1 = arriba-izquierda
        float topePadre = RectTransformUtility.WorldToScreenPoint(cam, esquinas[1]).y;
        float pixeles = topePadre - Screen.safeArea.yMax;
        float escala = canvas.rootCanvas.scaleFactor;
        return pixeles > 0f && escala > 0f ? pixeles / escala : 0f;
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
}
