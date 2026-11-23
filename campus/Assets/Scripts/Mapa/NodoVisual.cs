using UnityEngine;

/// <summary>
/// Esfera que representa un nodo de la red en la pantalla "Modificar mapa".
/// Guarda su id para saber qué nodo se tocó.
/// </summary>
public class NodoVisual : MonoBehaviour
{
    public int id;
    public int lugarId;   // > 0 = puerta (entrada) de ese lugar

    private Renderer render;
    private bool tieneColor;
    private Color colorActual;

    public void PonerColor(Color c)
    {
        if (tieneColor && colorActual == c) return;
        if (render == null) render = GetComponent<Renderer>();
        if (render == null) return;
        // Material compartido por color (GeoMapa lo guarda en caché): sin copias por nodo
        render.sharedMaterial = GeoMapa.Material(c);
        colorActual = c;
        tieneColor = true;
    }
}
