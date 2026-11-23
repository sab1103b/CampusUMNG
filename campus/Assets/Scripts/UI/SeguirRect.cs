using UnityEngine;

/// <summary>
/// Mantiene este RectTransform igual al de un hermano (<see cref="objetivo"/>): mismas anclas, pivote, posición y
/// tamaño. Sirve para un fondo que debe ir DETRÁS de un elemento (un hijo se dibujaría encima). Ignora el layout
/// del padre y solo copia cuando algo cambió.
/// </summary>
[DisallowMultipleComponent]
public class SeguirRect : MonoBehaviour
{
    public RectTransform objetivo;

    private RectTransform propio;

    private void Awake() { propio = (RectTransform)transform; }

    private void LateUpdate()
    {
        if (objetivo == null || propio == null) return;
        if (propio.anchorMin != objetivo.anchorMin) propio.anchorMin = objetivo.anchorMin;
        if (propio.anchorMax != objetivo.anchorMax) propio.anchorMax = objetivo.anchorMax;
        if (propio.pivot != objetivo.pivot) propio.pivot = objetivo.pivot;
        if (propio.anchoredPosition != objetivo.anchoredPosition) propio.anchoredPosition = objetivo.anchoredPosition;
        if (propio.sizeDelta != objetivo.sizeDelta) propio.sizeDelta = objetivo.sizeDelta;
    }
}
