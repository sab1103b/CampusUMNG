using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Utilidades de entrada compartidas por la cámara del mapa, el editor del mapa y el formulario de lugar.</summary>
public static class EntradaMapa
{
    private static readonly List<RaycastResult> impactos = new List<RaycastResult>();
    private static PointerEventData datos;
    private static EventSystem sistema;

    /// <summary>¿Esa posición de pantalla está sobre algún elemento de la interfaz que recibe toques?</summary>
    public static bool SobreUI(Vector2 posicionPantalla)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;
        if (datos == null || sistema != es)
        {
            datos = new PointerEventData(es);
            sistema = es;
        }
        datos.position = posicionPantalla;
        impactos.Clear();
        es.RaycastAll(datos, impactos);
        return impactos.Count > 0;
    }

    /// <summary>Distancia máxima (píxeles) para que un toque cuente como "toque" y no como arrastre.</summary>
    public static float ToleranciaToque => Mathf.Max(12f, Screen.dpi > 0 ? Screen.dpi * 0.08f : 15f);
}
