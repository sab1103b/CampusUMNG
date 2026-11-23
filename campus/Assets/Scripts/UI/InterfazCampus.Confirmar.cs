using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InterfazCampus: modal de confirmación para acciones irreversibles (eliminar lugar o espacio interior).
/// Velo oscuro + tarjeta con título, explicación y dos botones: "Cancelar" (Menta) y "Eliminar" (Rojo).
/// Tocar el velo o el botón Atrás de Android = Cancelar (AtrasEnCapas).
/// La misma tarjeta sirve como aviso de solo lectura con un único botón "Entendido" (MostrarInformacion).
/// </summary>
public partial class InterfazCampus
{
    private GameObject modalConfirmar;
    private RectTransform tarjetaConfirmar;
    private CanvasGroup grupoConfirmar;
    private TMP_Text tituloConfirmar, textoConfirmar;
    private GameObject botonCancelar, botonEliminar, botonEntendido;
    private Action accionConfirmar;
    private Coroutine animacionConfirmar;
    private bool confirmarCerrando;

    private bool ConfirmacionAbierta => modalConfirmar != null && modalConfirmar.activeSelf && !confirmarCerrando;

    private void PedirConfirmacion(string titulo, string texto, Action confirmar)
    {
        if (!ConstruirConfirmacion()) { confirmar?.Invoke(); return; }   // sin canvas no hay modal (no debería pasar)
        AbrirTarjeta(titulo, texto, confirmar, informativa: false, ancla: btnSalirAdmin);
    }

    /// <summary>Misma tarjeta con un solo botón "Entendido" (texto de solo lectura, p. ej. los términos de uso).</summary>
    private void MostrarInformacion(string titulo, string texto, Component ancla)
    {
        if (!ConstruirConfirmacion()) return;
        AbrirTarjeta(titulo, texto, null, informativa: true, ancla: ancla);
    }

    private void AbrirTarjeta(string titulo, string texto, Action confirmar, bool informativa, Component ancla)
    {
        // El modal se muestra en el canvas de la pantalla que lo pide (administración o registro)
        Canvas canvas = ancla != null ? ancla.GetComponentInParent<Canvas>(true) : null;
        if (canvas != null && modalConfirmar.transform.parent != canvas.rootCanvas.transform)
            modalConfirmar.transform.SetParent(canvas.rootCanvas.transform, false);
        botonCancelar.SetActive(!informativa);
        botonEliminar.SetActive(!informativa);
        botonEntendido.SetActive(informativa);

        if (animacionConfirmar != null) StopCoroutine(animacionConfirmar);
        animacionConfirmar = null;
        confirmarCerrando = false;
        accionConfirmar = confirmar;
        tituloConfirmar.text = titulo;
        textoConfirmar.text = texto;
        modalConfirmar.transform.SetAsLastSibling();   // encima de todo, incluso de las franjas de la zona segura
        modalConfirmar.SetActive(true);
        if (isActiveAndEnabled) animacionConfirmar = StartCoroutine(UIRapida.Aparecer(tarjetaConfirmar, grupoConfirmar));
        else UIRapida.TerminarAparicion(tarjetaConfirmar, grupoConfirmar);
    }

    /// <summary>Cierra el modal sin ejecutar la acción (Cancelar, Atrás, cambio de pantalla).</summary>
    private void CerrarConfirmacion(bool animar = true)
    {
        accionConfirmar = null;
        if (modalConfirmar == null || !modalConfirmar.activeSelf) return;
        if (animacionConfirmar != null) StopCoroutine(animacionConfirmar);
        animacionConfirmar = null;

        if (animar && isActiveAndEnabled && modalConfirmar.activeInHierarchy)
        {
            confirmarCerrando = true;
            animacionConfirmar = StartCoroutine(CerrarConfirmacionLuego());
            return;
        }
        confirmarCerrando = false;
        modalConfirmar.SetActive(false);
        UIRapida.TerminarAparicion(tarjetaConfirmar, grupoConfirmar);
    }

    private IEnumerator CerrarConfirmacionLuego()
    {
        // El grupo está en la raíz del modal: velo y tarjeta se desvanecen juntos; la tarjeta además se achica
        yield return UIRapida.Desaparecer(modalConfirmar, tarjetaConfirmar, grupoConfirmar);
        confirmarCerrando = false;
        animacionConfirmar = null;
    }

    private void Confirmar()
    {
        if (!ConfirmacionAbierta) return;   // evita un doble toque durante el cierre
        Action accion = accionConfirmar;
        CerrarConfirmacion();
        accion?.Invoke();
    }

    private bool ConstruirConfirmacion()
    {
        if (modalConfirmar != null) return true;
        Component ancla = btnSalirAdmin != null ? (Component)btnSalirAdmin
                        : contenedorLugares != null ? (Component)contenedorLugares : regTerminos;
        Canvas canvas = ancla != null ? ancla.GetComponentInParent<Canvas>(true) : null;
        if (canvas == null) return false;
        Transform raiz = canvas.rootCanvas.transform;

        // Velo a pantalla completa (fuera de la zona segura): tocarlo = Cancelar
        Image velo = UIRapida.Panel("ConfirmarEliminar", raiz, UIRapida.Velo);
        velo.gameObject.AddComponent<ZonaSegura.FueraDeZona>();
        UIRapida.Estirar(velo.rectTransform);
        Button fuera = velo.gameObject.AddComponent<Button>();
        fuera.transition = Selectable.Transition.None;
        fuera.onClick.AddListener(() => CerrarConfirmacion());
        modalConfirmar = velo.gameObject;
        grupoConfirmar = modalConfirmar.AddComponent<CanvasGroup>();

        // Tarjeta centrada: raíz invisible que crece con el contenido; la cara opaca y la sombra de la app
        // son hijas estiradas (así siguen al alto que calcula el layout)
        Image tarjeta = UIRapida.Panel("Tarjeta", velo.transform, UIRapida.Transparente);
        tarjetaConfirmar = tarjeta.rectTransform;
        Image cara = UIRapida.Panel("Cara", tarjeta.transform, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(cara, UIRapida.RadioTarjeta);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        UIRapida.Estirar(cara.rectTransform);
        UIRapida.SombraComoHija(tarjetaConfirmar);
        UIRapida.Anclar(tarjetaConfirmar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(UIRapida.AnchoModal, 0f));
        VerticalLayoutGroup v = tarjeta.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = Relleno(UIRapida.Margen, UIRapida.Margen, UIRapida.Margen, UIRapida.Margen);
        v.spacing = UIRapida.Esp3;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tarjeta.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        tarjeta.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // tocar la tarjeta no cancela

        tituloConfirmar = UIRapida.TituloModal(tarjeta.transform, "");   // mismo título que Formulario y Editor
        textoConfirmar = UIRapida.Texto(tarjeta.transform, "Texto", "", UIRapida.TamCuerpo, UIRapida.Gris,
                                        UIRapida.FuenteTexto, false);

        // Botones: Cancelar (secundario) a la izquierda, Eliminar (destructivo) a la derecha
        RectTransform fila = UIRapida.Rect("Botones", tarjeta.transform);
        fila.gameObject.AddComponent<LayoutElement>().preferredHeight = UIRapida.ToqueMinimo;
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UIRapida.Esp2;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;

        botonCancelar = AjustarBotonModal(UIRapida.BotonSecundario(fila, "BotonCancelar", "Cancelar", () => CerrarConfirmacion()));
        botonEliminar = BotonModal(fila, "Eliminar", UIRapida.Rojo, Color.white, Confirmar);
        botonEntendido = BotonModal(fila, "Entendido", UIRapida.Verde, Color.white, () => CerrarConfirmacion());   // modo informativo

        UIRapida.AplicarFuentes(modalConfirmar.transform);
        modalConfirmar.SetActive(false);
        return true;
    }

    private static GameObject BotonModal(Transform fila, string texto, Color fondo, Color colorTexto, UnityEngine.Events.UnityAction accion)
    {
        // UIRapida.Boton ya aplica EstadosBoton
        Button b = UIRapida.Boton(fila, "Boton" + texto, texto, UIRapida.TamCuerpo, fondo, colorTexto, null, accion);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioPildora);
        return AjustarBotonModal(b);
    }

    private static GameObject AjustarBotonModal(Button b)
    {
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
        le.flexibleWidth = 1f;
        return b.gameObject;
    }
}
