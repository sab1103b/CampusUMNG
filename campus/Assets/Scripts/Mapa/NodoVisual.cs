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

    public void PonerColor(Color c)
    {
        if (render == null) render = GetComponent<Renderer>();
        if (render != null) GeoMapa.PonerColor(render.material, c);
    }
}
