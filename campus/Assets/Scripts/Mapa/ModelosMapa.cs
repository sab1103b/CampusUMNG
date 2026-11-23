using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

/// <summary>
/// Dibuja en el mapa las edificaciones 3D guardadas en la base de datos (tabla modelos).
/// Se ven en el modo usuario y en "Modificar mapa". Cada vez que la base cambia, se redibujan.
/// Si la escena no lo tiene, InterfazCampus lo agrega solo.
/// </summary>
public class ModelosMapa : MonoBehaviour
{
    [SerializeField] private AbstractMap mapa;
    [SerializeField] private LugaresService lugares;

    private readonly Dictionary<int, ModeloCampus> creados = new Dictionary<int, ModeloCampus>();
    private bool mapaListo;
    private float unidadesPorMetro = 1f;

    /// <summary>Se dispara después de redibujar todos los modelos.</summary>
    public event System.Action Redibujados;

    public float UnidadesPorMetro => unidadesPorMetro;

    public void Configurar(AbstractMap mapaCampus, LugaresService servicio, bool mapaYaIniciado)
    {
        Desuscribir();   // si se configura otra vez, no se duplican los eventos
        mapa = mapaCampus;
        lugares = servicio;
        if (mapa != null)
        {
            mapa.OnInitialized += AlIniciarMapa;
            mapa.OnUpdated += Reubicar;
        }
        if (lugares != null) lugares.ModelosCargados += AlCargarModelos;
        mapaListo |= mapaYaIniciado;
        Redibujar();
    }

    private void AlIniciarMapa() { mapaListo = true; Redibujar(); }
    private void AlCargarModelos(List<LugaresService.Modelo> _) => Redibujar();

    private void Desuscribir()
    {
        if (mapa != null)
        {
            mapa.OnInitialized -= AlIniciarMapa;
            mapa.OnUpdated -= Reubicar;
        }
        if (lugares != null) lugares.ModelosCargados -= AlCargarModelos;
    }

    private void OnDestroy()
    {
        Desuscribir();
        // Los materiales compartidos de GeoMapa NO se liberan aquí: los usan también la ruta, el editor y
        // el halo. Su único dueño es GeoMapa (los libera al cerrar la app, Application.quitting).
    }

    public ModeloCampus Buscar(int id) => creados.TryGetValue(id, out ModeloCampus m) ? m : null;

    /// <summary>Vuelve a crear todos los modelos desde los datos cargados.</summary>
    public void Redibujar()
    {
        if (!mapaListo || lugares == null) return;

        foreach (ModeloCampus m in creados.Values) if (m != null) Destroy(m.gameObject);
        creados.Clear();

        unidadesPorMetro = GeoMapa.UnidadesPorMetro(mapa);
        foreach (LugaresService.Modelo datos in lugares.Modelos)
        {
            GameObject go = CrearPrimitiva(datos.tipo);
            go.name = $"Modelo_{datos.id}_{datos.tipo}";
            go.transform.SetParent(transform, false);

            ModeloCampus mc = go.AddComponent<ModeloCampus>();
            mc.datos = datos;
            go.GetComponent<Renderer>().sharedMaterial = GeoMapa.Material(GeoMapa.ColorEdificio);
            Ubicar(mc);
            creados[datos.id] = mc;
        }
        Redibujados?.Invoke();
    }

    /// <summary>Pone el modelo en su latitud/longitud con su tamaño y giro (también sirve para la vista previa al editar).</summary>
    public void Ubicar(ModeloCampus mc)
    {
        if (mc == null || mapa == null) return;
        LugaresService.Modelo d = mc.datos;
        float u = unidadesPorMetro;

        Vector3 suelo = mapa.GeoToWorldPosition(new Vector2d(d.latitud, d.longitud), false);
        float alto = d.alto * u;

        mc.transform.rotation = Quaternion.Euler(0f, d.rotacion, 0f);
        mc.transform.position = new Vector3(suelo.x, alto * 0.5f, suelo.z);   // apoyado en el suelo

        // El cilindro de Unity mide 2 de alto; el cubo, 1
        float escalaY = d.tipo == "cilindro" ? alto * 0.5f : alto;
        mc.transform.localScale = new Vector3(d.ancho * u, escalaY, d.largo * u);
    }

    private void Reubicar()
    {
        if (!mapaListo) return;
        unidadesPorMetro = GeoMapa.UnidadesPorMetro(mapa);
        foreach (ModeloCampus m in creados.Values) Ubicar(m);
    }

    private static GameObject CrearPrimitiva(string tipo)
    {
        // Cilindro: primitiva de Unity. Cubo y rectángulo: caja con distintas medidas.
        return GameObject.CreatePrimitive(tipo == "cilindro" ? PrimitiveType.Cylinder : PrimitiveType.Cube);
    }
}
