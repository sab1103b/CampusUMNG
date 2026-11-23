using Mapbox.Unity.Map;
using UnityEngine;

/// <summary>
/// Aspecto visual del mapa del campus: fondo con zonas verdes, edificios low-poly y calles claras.
/// Se aplica antes de que el mapa cargue (Awake). Si cambias el estilo en el Inspector durante Play,
/// el mapa se redibuja con el nuevo estilo (solo en el Editor). Los colores salen de GeoMapa.
///
/// Si la escena no lo tiene, InterfazCampus lo agrega solo al mapa con los valores por defecto.
/// </summary>
public class EstiloMapa : MonoBehaviour
{
    public enum Fondo
    {
        [Tooltip("Parques, prados y canchas en verde, agua en azul, relieve suave")]
        ZonasVerdes,
        [Tooltip("Mapa urbano clásico, más énfasis en las vías")]
        Calles,
        [Tooltip("Foto satelital con nombres de calles")]
        SateliteConCalles,
        [Tooltip("Foto satelital sola")]
        Satelite,
        [Tooltip("Gris claro (el estilo anterior)")]
        Claro,
    }

    [SerializeField] private AbstractMap mapa;

    [Header("Fondo del mapa (imagen de Mapbox)")]
    [SerializeField] private Fondo fondo = Fondo.ZonasVerdes;
    [Tooltip("Imágenes en alta resolución (@2x): más nítidas en celular")]
    [SerializeField] private bool altaResolucion = true;

    [Header("Edificios 3D (low-poly)")]
    [Tooltip("Usa GeoMapa.ColorEdificio: el mismo tono que las edificaciones agregadas por el administrador")]
    [SerializeField] private bool colorearEdificios = true;

    [Header("Calles 3D")]
    [Tooltip("Usa GeoMapa.ColorCalles")]
    [SerializeField] private bool colorearCalles = true;

    [Header("Fondo de la cámara (lo que se ve fuera del mapa cargado)")]
    [Tooltip("Usa GeoMapa.ColorFondoCamara")]
    [SerializeField] private bool cambiarFondoCamara = true;

    private Fondo fondoAplicado;

    public static string IdDeEstilo(Fondo f)
    {
        switch (f)
        {
            case Fondo.Calles: return "mapbox://styles/mapbox/streets-v12";
            case Fondo.SateliteConCalles: return "mapbox://styles/mapbox/satellite-streets-v12";
            case Fondo.Satelite: return "mapbox://styles/mapbox/satellite-v9";
            case Fondo.Claro: return "mapbox://styles/mapbox/light-v11";
            default: return "mapbox://styles/mapbox/outdoors-v12";
        }
    }

    private void Awake()
    {
        if (mapa == null) mapa = GetComponent<AbstractMap>();
        if (mapa == null) mapa = FindFirstObjectByType<AbstractMap>();
        Aplicar();
    }

#if UNITY_EDITOR
    // Permite probar estilos en Play desde el Inspector (solo en el Editor: en el APK no hay Update)
    private void Update()
    {
        if (fondo != fondoAplicado) AplicarFondo();
    }
#endif

    /// <summary>Configuración por código (cuando el componente se agrega al iniciar).</summary>
    public void Configurar(AbstractMap mapaCampus)
    {
        mapa = mapaCampus;
        Aplicar();
    }

    public void Aplicar()
    {
        if (mapa == null) return;

        AplicarFondo();

        if (colorearEdificios) ColorearSubcapa("building", GeoMapa.ColorEdificio);
        if (colorearCalles) ColorearSubcapa("road", GeoMapa.ColorCalles);

        if (cambiarFondoCamara)
        {
            foreach (Camera c in Camera.allCameras)
                if (c.clearFlags == CameraClearFlags.SolidColor && c.targetTexture == null)
                    c.backgroundColor = GeoMapa.ColorFondoCamara;
        }
    }

    private void AplicarFondo()
    {
        fondoAplicado = fondo;
        if (mapa == null || mapa.ImageLayer == null) return;

        if (mapa.ImageLayer is ImageryLayer capa)
        {
            capa.SetLayerSource(IdDeEstilo(fondo));
            capa.UseRetina(altaResolucion);
        }
    }

    // Las subcapas de la escena se llaman "Untitled": se buscan por la capa de datos (building, road...)
    private void ColorearSubcapa(string capaDatos, Color color)
    {
        if (mapa.VectorData == null) return;
        foreach (VectorSubLayerProperties sub in mapa.VectorData.GetAllFeatureSubLayers())
        {
            if (sub == null || sub.coreOptions == null || sub.coreOptions.layerName != capaDatos) continue;
            sub.Texturing.ColorStyle.SetAsStyle(color);
        }
    }
}
