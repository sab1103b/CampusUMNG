using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Mantiene una imagen como píldora (radio = mitad del lado corto REAL) aunque su alto lo decida un LayoutGroup
/// o cambie con la pantalla. Recalcula solo cuando cambia el tamaño. Ajusta también su sombra estándar:
/// hermana "&lt;nombre&gt;_Sombra" (<see cref="UIRapida.SombraDe(Graphic, float)"/>) o, si la imagen es la "Cara"
/// de una raíz con sombra hija (<see cref="UIRapida.SombraComoHija"/>), la "Sombra" de esa raíz.
/// Se agrega con <see cref="UIRapida.RedondearPildora"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class RadioPildoraAuto : UIBehaviour
{
    [System.NonSerialized] private Image img;
    [System.NonSerialized] private float ultimoLado = -1f;

    protected override void OnEnable()
    {
        base.OnEnable();
        ultimoLado = -1f;   // el sprite generado por código no se guarda: se vuelve a aplicar al encender
        Aplicar();
    }

    protected override void OnRectTransformDimensionsChange() => Aplicar();

    /// <summary>Aplica la forma con el tamaño actual (no hace nada si el lado corto no cambió).</summary>
    public void Aplicar()
    {
        if (img == null) img = GetComponent<Image>();
        if (img == null) return;
        Rect r = ((RectTransform)transform).rect;
        float lado = Mathf.Min(r.width, r.height);
        if (lado <= 0f) lado = Mathf.Max(r.width, r.height);   // ancho aún sin layout: usa el alto
        if (lado <= 0f || Mathf.Abs(lado - ultimoLado) < 0.5f) return;
        ultimoLado = lado;
        UIRapida.AplicarPildora(img, lado);

        Transform padre = transform.parent;
        if (padre == null) return;
        Transform sombra = padre.Find(name + "_Sombra");
        if (sombra == null && name == "Cara") sombra = padre.Find("Sombra");
        if (sombra != null) UIRapida.EstiloSombra(sombra.GetComponent<Image>(), lado * 0.5f);
    }
}
