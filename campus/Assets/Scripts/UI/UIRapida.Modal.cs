using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// UIRapida (modal de confirmación): velo FueraDeZona (tocarlo = cancelar) + tarjeta centrada con TituloModal,
/// texto Gris, botón secundario (Menta) y botón de acción (Rojo si es destructiva, Verde si no).
/// Mismo diseño que el "¿Descartar los cambios?" del formulario y la confirmación de InterfazCampus.
/// </summary>
public static partial class UIRapida
{
    /// <summary>
    /// Crea un modal de confirmación apagado, como hijo directo de "padre" (la raíz del canvas de la pantalla,
    /// antes de ZonaSegura.En: el velo queda fuera de la zona segura y cubre también las franjas).
    /// Se muestra con <see cref="ModalConfirmacion.Mostrar"/>. Confirmar cierra al instante y llama a "alConfirmar";
    /// Cancelar, tocar el velo o Atrás (<see cref="ModalConfirmacion.ManejarAtras"/>) cierran con fundido y llaman a "alCancelar".
    /// "soloInformar" = aviso de solo lectura (p. ej. términos de uso): sin Cancelar y con un único botón Verde
    /// ("Entendido" si textoConfirmar es null). Se puede cambiar después con <see cref="ModalConfirmacion.Configurar"/>.
    /// </summary>
    public static ModalConfirmacion ModalConfirmar(Transform padre, string titulo, string texto,
                                                   string textoCancelar, string textoConfirmar, bool destructivo,
                                                   UnityAction alConfirmar, UnityAction alCancelar = null,
                                                   TMP_FontAsset fuente = null, string nombre = "ModalConfirmar",
                                                   bool soloInformar = false)
    {
        Image velo = Panel(nombre, padre, Velo);
        velo.gameObject.AddComponent<ZonaSegura.FueraDeZona>();
        Estirar(velo.rectTransform);
        GameObject go = velo.gameObject;
        go.SetActive(false);   // se configura apagado (el componente no corre OnEnable/OnDisable aún)
        ModalConfirmacion m = go.AddComponent<ModalConfirmacion>();
        m.grupo = go.AddComponent<CanvasGroup>();
        Button fuera = go.AddComponent<Button>();
        fuera.transition = Selectable.Transition.None;
        fuera.onClick.AddListener(m.Cancelar);

        // Tarjeta centrada que crece con el contenido; cara y sombra son hijas estiradas
        Image tarjeta = Panel("Tarjeta", velo.transform, Transparente);
        m.caja = tarjeta.rectTransform;
        Image cara = Panel("Cara", tarjeta.transform, SuperficieOpaca);
        Redondear(cara, RadioTarjeta);
        cara.raycastTarget = false;
        cara.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Estirar(cara.rectTransform);
        SombraComoHija(m.caja);
        Anclar(m.caja, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(AnchoModal, 0f));
        VerticalLayoutGroup v = tarjeta.gameObject.AddComponent<VerticalLayoutGroup>();
        int mg = (int)Margen;
        v.padding = new RectOffset(mg, mg, mg, mg);
        v.spacing = Esp3;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tarjeta.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Button bloqueo = tarjeta.gameObject.AddComponent<Button>();   // tocar la tarjeta no cancela
        bloqueo.transition = Selectable.Transition.None;
        bloqueo.navigation = new Navigation { mode = Navigation.Mode.None };

        m.Titulo = TituloModal(tarjeta.transform, titulo ?? "");
        m.Texto = Texto(tarjeta.transform, "Texto", texto ?? "", TamCuerpo, Gris, fuente, false);
        m.Texto.gameObject.SetActive(!string.IsNullOrEmpty(texto));

        RectTransform fila = Rect("Botones", tarjeta.transform);
        fila.gameObject.AddComponent<LayoutElement>().preferredHeight = ToqueMinimo;
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = Esp2;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;

        m.BotonCancelar = BotonSecundario(fila, "Cancelar", string.IsNullOrEmpty(textoCancelar) ? "Cancelar" : textoCancelar,
                                          m.Cancelar, TamCuerpo, fuente);
        m.BotonConfirmar = Boton(fila, "Confirmar", "", TamCuerpo, Verde, SobreColor, fuente, m.Confirmar);
        RedondearPildora((Image)m.BotonConfirmar.targetGraphic);
        foreach (Button b in new[] { m.BotonCancelar, m.BotonConfirmar })
        {
            LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = ToqueMinimo;
            le.flexibleWidth = 1f;
        }
        m.alConfirmar = alConfirmar;
        m.alCancelar = alCancelar;
        m.Configurar(textoCancelar, textoConfirmar, destructivo, soloInformar);
        return m;
    }

    /// <summary>
    /// Modal de lista de opciones (categorías, edificios, lugar de una puerta): UN solo diseño para toda la app.
    /// Velo FueraDeZona (tocarlo llama a "alCerrar") como hijo directo de "padre" (raíz del canvas de la pantalla),
    /// caja centrada de ancho <see cref="AnchoModal"/> y alto <see cref="AltoRelModalLista"/> del alto de la pantalla,
    /// con sombra, cara blanca redondeada (se traga los toques), <see cref="TituloModal"/> arriba (franja AltoTituloModal),
    /// lista desplazable (VerticalLayoutGroup, separación Esp1) y, si "textoBoton" no es null, un botón secundario
    /// abajo que también llama a "alCerrar". Se crea apagado. El dueño lo muestra/oculta con
    /// <see cref="Aparecer"/> / <see cref="Desaparecer"/> sobre <see cref="ModalDeLista.Caja"/> y <see cref="ModalDeLista.Grupo"/>.
    /// </summary>
    public static ModalDeLista ModalLista(Transform padre, string titulo, UnityAction alCerrar,
                                          TMP_FontAsset fuente = null, string textoBoton = null, string nombre = "ModalLista")
    {
        Image velo = Panel(nombre, padre, Velo);
        velo.gameObject.AddComponent<ZonaSegura.FueraDeZona>();
        Estirar(velo.rectTransform);
        GameObject go = velo.gameObject;
        go.SetActive(false);
        ModalDeLista m = go.AddComponent<ModalDeLista>();
        m.Grupo = go.AddComponent<CanvasGroup>();
        Button fuera = go.AddComponent<Button>();
        fuera.transition = Selectable.Transition.None;
        if (alCerrar != null) fuera.onClick.AddListener(alCerrar);

        // Raíz transparente animada (Aparecer la escala): la sombra es su primer hijo y escala con la cara
        float medio = AltoRelModalLista * 0.5f;
        RectTransform caja = Rect("Caja", velo.transform);
        Anclar(caja, new Vector2(0.5f, 0.5f - medio), new Vector2(0.5f, 0.5f + medio), new Vector2(0.5f, 0.5f),
               Vector2.zero, new Vector2(AnchoModal, 0f));
        m.Caja = caja;
        SombraComoHija(caja, RadioTarjeta);
        Image cara = Panel("Cara", caja, SuperficieOpaca);
        Redondear(cara, RadioTarjeta);
        Estirar(cara.rectTransform);
        Button bloqueo = cara.gameObject.AddComponent<Button>();   // los toques en la caja no llegan al velo
        bloqueo.transition = Selectable.Transition.None;
        bloqueo.navigation = new Navigation { mode = Navigation.Mode.None };
        m.Cara = cara;

        m.Titulo = TituloModal(cara.transform, titulo ?? "", TextAlignmentOptions.MidlineLeft);
        Anclar((RectTransform)m.Titulo.transform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -Esp3),
               new Vector2(-2f * Margen, AltoTituloModal));

        bool conBoton = textoBoton != null;
        RectTransform vista = Rect("Vista", cara.transform);
        Estirar(vista, Margen, conBoton ? Esp3 + ToqueMinimo + Esp2 : Margen, Margen, Esp3 + AltoTituloModal + Esp1);
        vista.gameObject.AddComponent<RectMask2D>();
        ScrollRect sr = vista.gameObject.AddComponent<ScrollRect>();
        RectTransform lista = Rect("Lista", vista);
        Anclar(lista, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup vl = lista.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = Esp1;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        lista.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport = vista;
        sr.content = lista;
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        m.Lista = lista;
        m.Desplazamiento = sr;

        if (conBoton)
        {
            m.Boton = BotonSecundario(cara.transform, "Cancelar", textoBoton, alCerrar, TamCuerpo, fuente);
            Anclar((RectTransform)m.Boton.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, Esp3),
                   new Vector2(-2f * Margen, ToqueMinimo));
        }
        return m;
    }

    /// <summary>Partes de un modal creado con <see cref="ModalLista"/>.</summary>
    public sealed class ModalDeLista : MonoBehaviour
    {
        public CanvasGroup Grupo;
        /// <summary>Raíz animada (sombra + cara): pásala a Aparecer/Desaparecer.</summary>
        public RectTransform Caja;
        public Image Cara;
        public TMP_Text Titulo;
        /// <summary>Contenido de la lista: agrega aquí las filas (alto mínimo ToqueMinimo).</summary>
        public RectTransform Lista;
        public ScrollRect Desplazamiento;
        /// <summary>Botón secundario de abajo (null si se creó sin "textoBoton").</summary>
        public Button Boton;

        /// <summary>
        /// Quita las filas (las apaga primero: Destroy es diferido y no deben contar en el layout), frena la inercia
        /// del desplazamiento y vuelve arriba. Quien lo llama no necesita tocar Lista.anchoredPosition.
        /// </summary>
        public void Vaciar()
        {
            if (Desplazamiento != null) Desplazamiento.StopMovement();   // sin inercia que mueva la lista nueva
            if (Lista == null) return;
            for (int i = Lista.childCount - 1; i >= 0; i--)
            {
                GameObject hijo = Lista.GetChild(i).gameObject;
                hijo.SetActive(false);
                Destroy(hijo);
            }
            Lista.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>Modal creado con <see cref="ModalConfirmar"/>: mostrar/ocultar con Aparecer/Desaparecer y callbacks.</summary>
    public sealed class ModalConfirmacion : MonoBehaviour
    {
        public TMP_Text Titulo, Texto;
        public Button BotonCancelar, BotonConfirmar;
        internal CanvasGroup grupo;
        internal RectTransform caja;
        internal UnityAction alConfirmar, alCancelar;
        private Coroutine anim;

        /// <summary>true = aviso de solo lectura: sin Cancelar y con un único botón Verde.</summary>
        public bool SoloInformar { get; private set; }

        /// <summary>
        /// Cambia los botones: textos (null = se conserva), color de la acción (Rojo si "destructivo", si no Verde) y el
        /// modo "soloInformar" (oculta Cancelar; el botón queda Verde y dice "Entendido" si "textoConfirmar" es null).
        /// </summary>
        public void Configurar(string textoCancelar, string textoConfirmar, bool destructivo, bool soloInformar)
        {
            SoloInformar = soloInformar;
            if (BotonCancelar != null)
            {
                BotonCancelar.gameObject.SetActive(!soloInformar);
                TMP_Text tc = TextoDe(BotonCancelar);
                if (tc != null && !string.IsNullOrEmpty(textoCancelar)) tc.text = textoCancelar;
            }
            if (BotonConfirmar == null) return;
            TMP_Text t = TextoDe(BotonConfirmar);
            if (t != null)
            {
                if (textoConfirmar != null) t.text = textoConfirmar;
                else if (soloInformar) t.text = "Entendido";
                else if (string.IsNullOrEmpty(t.text)) t.text = "Aceptar";
                t.color = SobreColor;
            }
            if (BotonConfirmar.targetGraphic != null)
                BotonConfirmar.targetGraphic.color = destructivo && !soloInformar ? Rojo : Verde;
        }

        /// <summary>Visible (aunque se esté cerrando).</summary>
        public bool Visible => gameObject.activeSelf;
        /// <summary>Visible y no se está cerrando (acepta toques).</summary>
        public bool Abierto => gameObject.activeSelf && (grupo == null || grupo.interactable);

        /// <summary>
        /// Muestra el modal encima de todo (último hermano). "titulo"/"texto" null = se conservan; "texto" vacío lo oculta.
        /// "alConfirmar"/"alCancelar" no null reemplazan los callbacks de esta vez en adelante.
        /// </summary>
        public void Mostrar(string titulo = null, string texto = null, UnityAction alConfirmar = null, UnityAction alCancelar = null)
        {
            if (titulo != null && Titulo != null) Titulo.text = titulo;
            if (texto != null && Texto != null)
            {
                Texto.text = texto;
                Texto.gameObject.SetActive(texto.Length > 0);
            }
            if (alConfirmar != null) this.alConfirmar = alConfirmar;
            if (alCancelar != null) this.alCancelar = alCancelar;
            Parar();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (grupo != null) grupo.interactable = grupo.blocksRaycasts = true;
            if (isActiveAndEnabled) anim = StartCoroutine(Aparecer(caja, grupo));
            else TerminarAparicion(caja, grupo);   // la pantalla está apagada: queda listo al encenderla
        }

        /// <summary>Cierra sin llamar a ningún callback (con fundido si "animar" y está en pantalla).</summary>
        public void Ocultar(bool animar = true)
        {
            Parar();
            if (animar && gameObject.activeSelf && isActiveAndEnabled)
            {
                if (grupo != null) grupo.interactable = grupo.blocksRaycasts = false;   // sin toques dobles mientras se va
                anim = StartCoroutine(Desaparecer(gameObject, caja, grupo));
                return;
            }
            TerminarAparicion(caja, grupo);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Botón secundario, velo o Atrás: cierra con fundido y llama a alCancelar.</summary>
        public void Cancelar()
        {
            if (!Abierto) return;
            Ocultar(true);
            alCancelar?.Invoke();
        }

        /// <summary>Botón de acción: cierra al instante (sin doble toque) y llama a alConfirmar.</summary>
        public void Confirmar()
        {
            if (!Abierto) return;
            Ocultar(false);
            alConfirmar?.Invoke();
        }

        /// <summary>Para UIManager.RegistrarManejadorAtras: true si el modal estaba visible (Atrás = cancelar).</summary>
        public bool ManejarAtras()
        {
            if (!Visible) return false;
            Cancelar();   // si ya se está cerrando, solo se consume el Atrás
            return true;
        }

        private void Parar()
        {
            if (anim != null) { StopCoroutine(anim); anim = null; }
        }

        // La pantalla se apagó a mitad de una animación: queda en su estado final (el dueño la oculta al volver)
        private void OnDisable()
        {
            anim = null;
            TerminarAparicion(caja, grupo);
        }
    }
}
