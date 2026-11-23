using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pin de la interfaz que se puede arrastrar con el dedo o el mouse.
/// Avisa la posición de pantalla mientras se arrastra (quien lo usa la convierte a lat/lon).
/// Como es parte de la interfaz, arrastrarlo no mueve el mapa.
/// No asigna memoria por frame: solo guarda vectores y dispara eventos.
/// </summary>
public class PinArrastrable : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>Posición de pantalla de la punta del pin mientras se arrastra.</summary>
    public event Action<Vector2> Arrastrado;
    public event Action Soltado;

    private bool activo = true;

    /// <summary>Si es falso, el pin no se puede tomar (y se suelta si se estaba arrastrando).</summary>
    public bool Activo
    {
        get => activo;
        set
        {
            activo = value;
            if (!activo) Cancelar();
        }
    }

    public bool Arrastrando { get; private set; }

    private Vector2 desfase;        // del dedo a la punta del pin, para que no salte al tomarlo
    private Vector2 inicio;         // donde se tocó el pin
    private bool superoTolerancia;  // movimientos menores a EntradaMapa.ToleranciaToque no lo mueven

    public void OnPointerDown(PointerEventData e)
    {
        if (!activo) return;
        RectTransform rt = (RectTransform)transform;
        Vector2 punta = RectTransformUtility.WorldToScreenPoint(e.pressEventCamera, rt.position);
        desfase = punta - e.position;
        inicio = e.position;
        superoTolerancia = false;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!activo) return;
        Arrastrando = true;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!activo || !Arrastrando) return;
        if (!superoTolerancia)
        {
            // Misma tolerancia que usa el mapa para distinguir toque de arrastre
            if ((e.position - inicio).sqrMagnitude < EntradaMapa.ToleranciaToque * EntradaMapa.ToleranciaToque) return;
            superoTolerancia = true;
        }
        Arrastrado?.Invoke(e.position + desfase);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!Arrastrando) return;
        Arrastrando = false;
        superoTolerancia = false;
        Soltado?.Invoke();
    }

    // Si el pin se oculta a mitad de un arrastre, Unity no envía OnEndDrag: se suelta aquí
    private void OnDisable() => Cancelar();

    private void Cancelar()
    {
        if (!Arrastrando) return;
        Arrastrando = false;
        superoTolerancia = false;
        Soltado?.Invoke();
    }
}
