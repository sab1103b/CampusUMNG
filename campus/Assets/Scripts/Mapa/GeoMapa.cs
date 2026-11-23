using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

/// <summary>
/// Utilidades compartidas para trabajar sobre el mapa: escala metros↔unidades, punto del suelo
/// bajo un toque y materiales que funcionan en URP (también en el APK).
/// </summary>
public static class GeoMapa
{
    // Colores del mapa en hex (misma notación que la paleta de UIRapida). Equivalen a los floats
    // anteriores redondeados a 8 bits: (0,95; 0,93; 0,88), (0,99; 0,98; 0,95) y (0,90; 0,94; 0,86).

    /// <summary>Color de las edificaciones (el mismo tono claro low-poly de los edificios de Mapbox).</summary>
    public static readonly Color ColorEdificio = UIRapida.Hex("#f2ede0");

    /// <summary>Color de las calles 3D de Mapbox (crema muy claro, se distingue del fondo verde).</summary>
    public static readonly Color ColorCalles = UIRapida.Hex("#fcfaf2");

    /// <summary>Fondo de la cámara: lo que se ve fuera del mapa cargado (verde grisáceo del fondo).</summary>
    public static readonly Color ColorFondoCamara = UIRapida.Hex("#e6f0db");

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

    // Un material por color (se reutiliza): antes se creaba uno nuevo en cada llamada y se acumulaban
    private static readonly Dictionary<Color32, Material> cache = new Dictionary<Color32, Material>();

    /// <summary>
    /// Material de color sólido, compartido por todos los objetos de ese color (no lo modifiques:
    /// para cambiar el color de un objeto pide otro con este método y asígnalo a sharedMaterial).
    /// Se copia de un objeto que ya está en la escena (por ejemplo el marcador), así el shader de URP
    /// viene incluido en el APK y no aparece en rosado.
    /// Devuelve null solo si no existe ningún shader de respaldo (asignar null a sharedMaterial es seguro).
    /// </summary>
    public static Material Material(Color color)
    {
        Color32 clave = color;
        if (cache.TryGetValue(clave, out Material existente) && existente != null) return existente;

        // Dueño único de la liberación: al cerrar la app (y al salir de Play en el Editor)
        if (!liberacionRegistrada)
        {
            Application.quitting += Liberar;
            liberacionRegistrada = true;
        }

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
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (s == null) s = Shader.Find("Standard");
                // Sprites/Default está en "Always Included Shaders": existe en cualquier APK
                if (s == null) s = Shader.Find("Sprites/Default");
                if (s == null)
                {
                    // new Material(null) lanzaría excepción: sin shader no hay material (quien llama acepta null)
                    Debug.LogWarning("GeoMapa: no se encontró ningún shader para los materiales del mapa.");
                    return null;
                }
                materialBase = new Material(s);
            }

            materialBase.mainTexture = null;
        }

        Material nuevo = new Material(materialBase) { name = "GeoMapa_" + ColorUtility.ToHtmlStringRGBA(color) };
        PonerColor(nuevo, color);
        cache[clave] = nuevo;
        return nuevo;
    }

    private static bool liberacionRegistrada;

    /// <summary>
    /// Destruye los materiales creados. Lo llama GeoMapa solo, al cerrar la app (Application.quitting);
    /// ningún componente debe llamarlo (otros podrían seguir usando los materiales compartidos).
    /// Si después se pide un material, se vuelve a crear.
    /// </summary>
    public static void Liberar()
    {
        Application.quitting -= Liberar;
        liberacionRegistrada = false;
        foreach (Material m in cache.Values) if (m != null) Object.Destroy(m);
        cache.Clear();
        if (materialBase != null) Object.Destroy(materialBase);
        materialBase = null;
    }

    public static void PonerColor(Material m, Color color)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
    }
}
