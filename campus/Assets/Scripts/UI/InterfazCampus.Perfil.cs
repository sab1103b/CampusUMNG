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

/// <summary> InterfazCampus: menú de perfil de la pantalla de usuario. </summary>
public partial class InterfazCampus
{
    private void MostrarMenuPerfil(bool ver)
    {
        if (ver)
        {
            CerrarCapasExcepto(Capa.MenuPerfil);
            CancelarCierreMenu();
        }
        else if (CerrarMenuAnimado()) return;   // se desvanece y se apaga solo
        if (menuPerfil != null) menuPerfil.SetActive(ver);
        if (zonaCerrarMenu != null) zonaCerrarMenu.SetActive(ver);
        if (!ver || tarjetaPerfil == null) return;

        // Menú nuevo: datos de la sesión, debajo del botón de perfil, encima de todo, con animación
        ActualizarMenuPerfil();
        zonaCerrarMenu.transform.SetAsLastSibling();
        tarjetaPerfil.SetAsLastSibling();
        ColocarBajoPerfil();
        animacionMenu = Aparecer(tarjetaPerfil, animacionMenu);   // misma animación que la ficha
    }

    // ==================================================================
    // Menú de perfil (reemplaza el recuadro gris del diseño)
    // ==================================================================
    private RectTransform tarjetaPerfil;
    private TMP_Text perfilNombre, perfilRol, textoItemAdmin;
    // Ancho del menú de perfil: dos tercios del modal (cabe "Panel de administración" a TamCuerpo con su ícono)
    private const float AnchoMenuPerfil = UIRapida.AnchoModal * 2f / 3f;
    private GameObject itemSalir;
    private Coroutine animacionMenu, cierreMenu;

    // ----- Cierre animado del menú (misma salida que la ficha y los modales) -----
    private bool CerrarMenuAnimado()
    {
        if (cierreMenu != null) return true;   // ya se está cerrando
        if (tarjetaPerfil == null || menuPerfil == null || !menuPerfil.activeSelf ||
            !isActiveAndEnabled || !menuPerfil.activeInHierarchy) return false;
        if (animacionMenu != null) { StopCoroutine(animacionMenu); animacionMenu = null; }
        cierreMenu = StartCoroutine(CerrarMenuLuego());
        return true;
    }

    private IEnumerator CerrarMenuLuego()
    {
        CanvasGroup gTarjeta = GrupoDe(tarjetaPerfil);
        CanvasGroup gVelo = zonaCerrarMenu != null ? GrupoDe((RectTransform)zonaCerrarMenu.transform) : null;
        if (gVelo != null) gVelo.blocksRaycasts = false;   // el mapa vuelve a recibir toques enseguida
        IEnumerator salida = UIRapida.Desaparecer(menuPerfil, tarjetaPerfil, gTarjeta);
        while (salida.MoveNext())
        {
            if (gVelo != null) gVelo.alpha = gTarjeta.alpha;   // el velo se desvanece junto con la tarjeta
            yield return salida.Current;
        }
        cierreMenu = null;
        TerminarCierreMenu();
    }

    private void TerminarCierreMenu()
    {
        if (menuPerfil != null) menuPerfil.SetActive(false);
        if (zonaCerrarMenu != null)
        {
            zonaCerrarMenu.SetActive(false);
            RestaurarVelo();
        }
        if (tarjetaPerfil != null) UIRapida.TerminarAparicion(tarjetaPerfil, tarjetaPerfil.GetComponent<CanvasGroup>());
    }

    private void CancelarCierreMenu()
    {
        if (cierreMenu == null) return;
        StopCoroutine(cierreMenu);
        cierreMenu = null;
        RestaurarVelo();
        if (tarjetaPerfil != null) UIRapida.TerminarAparicion(tarjetaPerfil, tarjetaPerfil.GetComponent<CanvasGroup>());
    }

    private void RestaurarVelo()
    {
        CanvasGroup g = zonaCerrarMenu != null ? zonaCerrarMenu.GetComponent<CanvasGroup>() : null;
        if (g == null) return;
        g.alpha = 1f;
        g.blocksRaycasts = true;
    }

    /// <summary>Al desactivar el objeto las corrutinas se detienen: cada capa queda en su estado final.</summary>
    private void TerminarCierresAnimados()
    {
        if (cierreMenu != null) { cierreMenu = null; TerminarCierreMenu(); }
        if (cierreFicha != null) { cierreFicha = null; TerminarCierreFicha(); }
        animacionMenu = animacionFicha = animacionBarraRuta = null;
        if (tarjetaPerfil != null) UIRapida.TerminarAparicion(tarjetaPerfil, tarjetaPerfil.GetComponent<CanvasGroup>());
        if (fichaRaiz != null) UIRapida.TerminarAparicion(fichaRaiz, fichaRaiz.GetComponent<CanvasGroup>());
        if (barraRuta != null) UIRapida.TerminarAparicion((RectTransform)barraRuta.transform, barraRuta.GetComponent<CanvasGroup>());
    }

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
        Image fondo = UIRapida.Panel("CierreMenuPerfil", raiz, UIRapida.Velo);
        fondo.gameObject.AddComponent<ZonaSegura.FueraDeZona>();   // el velo cubre también muesca y barra de gestos
        UIRapida.Estirar(fondo.rectTransform);
        Button cerrar = fondo.gameObject.AddComponent<Button>();
        cerrar.transition = Selectable.Transition.None;
        cerrar.onClick.AddListener(() => MostrarMenuPerfil(false));

        // Tarjeta
        Image tarjeta = UIRapida.Panel("MenuPerfil", raiz, UIRapida.SuperficieOpaca);
        // Hermana del velo en la raíz (también fuera de la zona): así queda siempre encima de él al mostrarse.
        // Su posición sigue al botón de perfil, que sí está dentro de la zona segura.
        tarjeta.gameObject.AddComponent<ZonaSegura.FueraDeZona>();
        UIRapida.Redondear(tarjeta, UIRapida.RadioTarjeta);
        tarjetaPerfil = tarjeta.rectTransform;
        tarjetaPerfil.anchorMin = tarjetaPerfil.anchorMax = new Vector2(0.5f, 0.5f);
        tarjetaPerfil.pivot = new Vector2(1f, 1f);
        tarjetaPerfil.sizeDelta = new Vector2(AnchoMenuPerfil, 0f);
        tarjeta.gameObject.AddComponent<CanvasGroup>();
        VerticalLayoutGroup v = tarjeta.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = Relleno(UIRapida.Margen, UIRapida.Margen, UIRapida.Margen, UIRapida.Esp3);   // mismo relleno que la ficha
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tarjeta.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // La tarjeta blanca va encima de su propia sombra: se pinta con un fondo hijo (hoja opaca)
        Image cara = UIRapida.Panel("Cara", tarjeta.transform, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(cara, UIRapida.RadioTarjeta);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        cara.transform.SetAsFirstSibling();
        UIRapida.SombraComoHija(tarjetaPerfil);   // sombra única de la app, detrás de la cara (fuera del layout)
        tarjeta.color = UIRapida.Transparente;   // la tarjeta en sí es invisible (solo recibe toques)

        // Encabezado: avatar + nombre + rol
        RectTransform encabezado = UIRapida.Rect("Encabezado", tarjeta.transform);
        encabezado.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.AltoEncabezadoTarjeta;
        HorizontalLayoutGroup he = encabezado.gameObject.AddComponent<HorizontalLayoutGroup>();
        he.spacing = UIRapida.Esp3;
        he.childAlignment = TextAnchor.MiddleLeft;
        he.childControlWidth = he.childControlHeight = true;
        he.childForceExpandWidth = he.childForceExpandHeight = false;

        Image avatar = UIRapida.Panel("Avatar", encabezado, UIRapida.Menta);
        avatar.sprite = UIRapida.Circulo();
        avatar.preserveAspect = true;
        LayoutElement la = avatar.gameObject.AddComponent<LayoutElement>();
        la.preferredWidth = la.preferredHeight = UIRapida.TamAvatar;
        IconoEnAvatar(avatar.transform, "persona");

        RectTransform textos = UIRapida.Rect("Textos", encabezado);
        textos.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup vt = textos.gameObject.AddComponent<VerticalLayoutGroup>();
        vt.childAlignment = TextAnchor.MiddleLeft;
        vt.childControlWidth = vt.childControlHeight = true;
        vt.childForceExpandHeight = false;
        perfilNombre = UIRapida.Texto(textos, "Nombre", "Visitante", UIRapida.TamSubtitulo, UIRapida.Oscuro, fuente, true);
        // Un apodo largo se achica hasta TamSecundario y, si aún no cabe, termina en "…" (un solo renglón)
        perfilNombre.textWrappingMode = TextWrappingModes.NoWrap;
        perfilNombre.overflowMode = TextOverflowModes.Ellipsis;
        perfilNombre.enableAutoSizing = true;
        perfilNombre.fontSizeMin = UIRapida.TamSecundario;
        perfilNombre.fontSizeMax = UIRapida.TamSubtitulo;
        perfilRol = UIRapida.Texto(textos, "Rol", "Explorando el campus", UIRapida.TamSecundario, UIRapida.Gris, fuente);

        // Separador
        Image linea = UIRapida.Panel("Separador", tarjeta.transform, UIRapida.Separador);
        linea.raycastTarget = false;
        linea.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.AltoSeparador;

        // Opciones
        textoItemAdmin = ItemMenu(tarjeta.transform, "llave", "Modo administrador", UIRapida.Verde, UIRapida.Oscuro, fuente,
                                  () => { MostrarMenuPerfil(false); AbrirModoAdmin(); });
        ItemMenu(tarjeta.transform, "inicio", "Menú principal", UIRapida.Verde, UIRapida.Oscuro, fuente,
                 () => { CerrarCapasExcepto(Capa.Ninguna); ui.IrRaiz(PMenu); });
        itemSalir = ItemMenu(tarjeta.transform, "salir", "Cerrar sesión", UIRapida.Rojo, UIRapida.Rojo, fuente, () =>
        {
            MostrarMenuPerfil(false);
            CerrarSesionPedida();   // el usuario lo pidió: se confirma con un aviso (una sola vez)
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
        Image fondo = UIRapida.Panel("Opcion " + texto, padre, Color.white);   // el tinte lo decide EstadosTransparente
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        fondo.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.AltoFilaMenu;
        Button b = fondo.gameObject.AddComponent<Button>();
        b.targetGraphic = fondo;
        UIRapida.EstadosTransparente(b);   // PresionadoClaro al tocar: se nota sobre la tarjeta blanca
        b.onClick.AddListener(accion);

        Image img = UIRapida.Panel("Icono", fondo.transform, colorIcono);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(UIRapida.Esp2, 0), new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));

        TMP_Text t = UIRapida.Texto(fondo.transform, "Texto", texto, UIRapida.TamCuerpo, colorTexto, fuente, false, TextAlignmentOptions.MidlineLeft);
        UIRapida.Estirar((RectTransform)t.transform, UIRapida.Esp2 + UIRapida.TamIconoBoton + UIRapida.Esp3, 0, UIRapida.Esp2, 0);
        return t;
    }

    private void ActualizarMenuPerfil()
    {
        bool sesion = admin != null && admin.EstaAutenticado;
        if (perfilNombre != null) perfilNombre.text = sesion ? TextoSeguro(admin.NicknameActual) : "Visitante";
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

        Vector3[] c = esquinas;   // arreglo compartido (sin asignaciones)
        ((RectTransform)btnPerfil.transform).GetWorldCorners(c);          // 3 = abajo-derecha
        Vector3 local = padre.InverseTransformPoint(c[3]);

        float margen = UIRapida.Esp3;
        float x = Mathf.Clamp(local.x, padre.rect.xMin + margen + tarjetaPerfil.rect.width, padre.rect.xMax - margen);
        tarjetaPerfil.anchoredPosition = new Vector2(x, local.y - UIRapida.Esp2);
    }
}
