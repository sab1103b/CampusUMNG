using UnityEngine;

/// <summary>
/// Una edificación 3D agregada por el administrador (cubo, rectángulo o cilindro).
/// La crea ModelosMapa a partir de la base de datos; guarda sus datos para poder editarla.
/// </summary>
public class ModeloCampus : MonoBehaviour
{
    public LugaresService.Modelo datos;

    private Renderer render;

    private void Awake() => render = GetComponent<Renderer>();

    /// <summary>Resalta el modelo (seleccionado) o le devuelve su color normal.</summary>
    public void Resaltar(bool activo, Color colorResaltado)
    {
        if (render == null) render = GetComponent<Renderer>();
        // Material compartido por color (GeoMapa lo guarda en caché): sin copias por modelo
        if (render != null) render.sharedMaterial = GeoMapa.Material(activo ? colorResaltado : GeoMapa.ColorEdificio);
    }
}
