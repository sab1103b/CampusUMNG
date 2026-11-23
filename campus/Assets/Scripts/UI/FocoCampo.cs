using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Indicador de foco y de error de un campo de texto (TMP_InputField o InputField legacy), sobre su contorno "Borde"
/// (<see cref="UIRapida.BordeDe"/>): en reposo BordeCampo y GrosorBordeCampo; con foco Verde y GrosorFocoCampo;
/// con error Rojo y GrosorFocoCampo (el error manda sobre el foco). El cambio de grosor hace que el estado no
/// dependa solo del color. Se agrega con <see cref="UIRapida.IndicadorFoco"/>; el error con <see cref="UIRapida.MarcarErrorCampo"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class FocoCampo : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerDownHandler
{
    /// <summary>Contorno que se pinta (hijo "Borde" del fondo del campo).</summary>
    public Image borde;
    /// <summary>Radio de esquina del fondo del campo (el mismo que se pasó a BordeDe).</summary>
    public float radio = UIRapida.RadioControl;
    /// <summary>false = solo indica error (selectores que son botones: su "foco" sería el toque).</summary>
    public bool conFoco = true;

    private Selectable campo;
    private bool enfocado, error, suscrito;

    /// <summary>true mientras el campo tiene foco.</summary>
    public bool Enfocado => enfocado;

    /// <summary>Marca o quita el error (borde Rojo mientras sea true).</summary>
    public bool Error
    {
        get => error;
        set { if (error == value) return; error = value; Pintar(); }
    }

    private void Awake() => Suscribir();

    private void OnEnable() => Pintar();

    private void OnDisable()
    {
        enfocado = false;   // al volver, el campo no tiene foco
        Pintar();
    }

    private void OnDestroy()
    {
        if (!suscrito || campo == null) return;
        if (campo is TMP_InputField tmp) tmp.onEndEdit.RemoveListener(AlTerminar);
        else if (campo is InputField legado) legado.onEndEdit.RemoveListener(AlTerminar);
    }

    // Al cerrar el teclado ("Listo") el campo deja de editar aunque siga seleccionado: se quita el foco
    private void Suscribir()
    {
        if (suscrito) return;
        campo = GetComponent<Selectable>();
        if (campo is TMP_InputField tmp) tmp.onEndEdit.AddListener(AlTerminar);
        else if (campo is InputField legado) legado.onEndEdit.AddListener(AlTerminar);
        suscrito = true;
    }

    private void AlTerminar(string _) => Fijar(false);

    public void OnSelect(BaseEventData _) => Fijar(true);
    public void OnDeselect(BaseEventData _) => Fijar(false);
    // Tocar otra vez un campo que seguía seleccionado (teclado cerrado) lo vuelve a activar
    public void OnPointerDown(PointerEventData _) => Fijar(true);

    private void Fijar(bool valor)
    {
        if (valor && (!conFoco || (campo != null && !campo.IsInteractable()))) valor = false;
        if (enfocado == valor) return;
        enfocado = valor;
        Pintar();
    }

    /// <summary>Vuelve a pintar el contorno según el estado (foco / error / reposo).</summary>
    public void Pintar()
    {
        if (borde == null) return;
        bool marcado = error || enfocado;
        borde.color = error ? UIRapida.Rojo : enfocado ? UIRapida.Verde : UIRapida.BordeCampo;
        Sprite s = marcado
            ? UIRapida.ContornoRedondeado(UIRapida.TrazoContornoPx(UIRapida.GrosorFocoCampo, radio))
            : UIRapida.ContornoRedondeado();
        if (borde.sprite != s) borde.sprite = s;
    }
}
