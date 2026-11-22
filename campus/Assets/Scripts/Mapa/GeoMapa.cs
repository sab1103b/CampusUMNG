using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

/// <summary>
/// Utilidades compartidas para trabajar sobre el mapa: escala metros↔unidades, punto del suelo
/// bajo un toque y materiales que funcionan en URP (también en el APK).
/// </summary>
public static class GeoMapa
{
    /// <summary>Color de las edificaciones (el mismo tono claro low-poly de los edificios de Mapbox).</summary>
    public static readonly Color ColorEdificio = new Color(0.95f, 0.93f, 0.88f, 1f);

    private static Material materialBase;

    /// <summary>Cuántas unidades de Unity mide un metro en el mapa actual.</summary>
    public static float UnidadesPorMetro(AbstractMap mapa)
    {
        if (mapa == null) return 1f;
        Vector2d c = mapa.CenterLatitudeLongitude;
        Vector3 a = mapa.GeoToWorldPosition(c, false);
        Vector3 b = mapa.GeoToWorldPosition(new Vector2d(c.x + 0.001, c.y), false);
        float unidades = Vector3.Distance(a, b);
        return unidades > 0.0001f ? unidades / 111.32f : 1f;   // 0.001° de latitud ≈ 111.32 m
    }

    /// <summary>Punto del suelo (y = 0) que hay bajo una posición de la pantalla.</summary>
    public static bool PuntoEnSuelo(Camera cam, Vector2 pantalla, out Vector3 punto)
    {
        punto = Vector3.zero;
        if (cam == null) return false;
        Ray rayo = cam.ScreenPointToRay(pantalla);
        if (Mathf.Abs(rayo.direction.y) < 1e-4f) return false;
        float t = -rayo.origin.y / rayo.direction.y;
        if (t <= 0f) return false;
        punto = rayo.origin + rayo.direction * t;
        return true;
    }

    /// <summary>
    /// Material de color sólido. Se copia de un objeto que ya está en la escena (por ejemplo el
    /// marcador), así el shader de URP viene incluido en el APK y no aparece en rosado.
    /// </summary>
    public static Material Material(Color color)
    {
        if (materialBase == null)
        {
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name.Contains("Universal Render Pipeline") &&
                    (m.HasProperty("_BaseColor") || m.HasProperty("_Color")))
                {
                    materialBase = new Material(m);
                    break;
                }
            }

            if (materialBase == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                           ?? Shader.Find("Standard");
                materialBase = new Material(s);
            }

            materialBase.mainTexture = null;
        }

        Material nuevo = new Material(materialBase);
        PonerColor(nuevo, color);
        return nuevo;
    }

    public static void PonerColor(Material m, Color color)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
    }
}
