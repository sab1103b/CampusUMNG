using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lee los lugares, sus servicios y la red de caminos de la base de datos LOCAL (SQLite dentro
/// de la app) y registra cada búsqueda para el reporte PDF. Ya no necesita servidor ni PowerShell.
/// Los métodos y eventos públicos son los mismos que cuando usaba la API de Python.
/// </summary>
public class LugaresService : MonoBehaviour
{
    // ----- Datos que usa el resto de la app -----
    [Serializable]
    public class Lugar
    {
        public int id;
        public string nombre;
        public string categoria;
        public string descripcion;
        public string indicacion;
        public string horario;
        public double latitud;
        public double longitud;
    }

    /// <summary>Un salón, oficina o laboratorio que pertenece a un lugar. No tiene coordenadas propias.</summary>
    [Serializable]
    public class Servicio
    {
        public int id;
        public int lugar_id;
        public string nombre;
        public string categoria;
        public string piso;
        public string descripcion;
        public string indicacion;     // cómo llegar dentro del lugar
        public string horario;
    }

    [Serializable]
    public class NodoDto
    {
        public int id;
        public string nombre;
        public double latitud;
        public double longitud;
        public int lugar_id;      // 0 = punto de paso; >0 = entrada de ese lugar
    }

    [Serializable]
    public class ConexionDto
    {
        public int id;
        public int nodo_a;
        public int nodo_b;
    }

    [Serializable]
    public class RedDto
    {
        public NodoDto[] nodos;
        public ConexionDto[] conexiones;
    }

    /// <summary>Edificación 3D agregada por el administrador. Medidas en metros, rotación en grados.</summary>
    [Serializable]
    public class Modelo
    {
        public int id;
        public string tipo;       // cubo | rectangulo | cilindro
        public string nombre;
        public double latitud;
        public double longitud;
        public float rotacion;
        public float ancho;       // eje X (este-oeste antes de rotar)
        public float largo;       // eje Z (norte-sur antes de rotar)
        public float alto;
    }

    public List<Modelo> Modelos { get; private set; } = new List<Modelo>();
    public event Action<List<Modelo>> ModelosCargados;

    public List<Lugar> Lugares { get; private set; } = new List<Lugar>();
    public List<Servicio> Servicios { get; private set; } = new List<Servicio>();
    public RedDto Red { get; private set; }

    public bool Cargado { get; private set; }          // ya hay lugares
    public bool CargaTerminada { get; private set; }   // ya se intentó cargar todo (lugares, servicios y red)

    public event Action<List<Lugar>> LugaresCargados;
    public event Action<List<Servicio>> ServiciosCargados;
    public event Action RedCargada;

    private void Start() => Recargar();

    /// <summary>Vuelve a leer todo (se llama solo después de que el admin hace cambios).</summary>
    public void Recargar() => StartCoroutine(CargarTodo());

    public Lugar BuscarPorId(int id) => Lugares.Find(l => l.id == id);
    public Servicio BuscarServicioPorId(int id) => Servicios.Find(s => s.id == id);
    public List<Servicio> ServiciosDe(int lugarId) => Servicios.FindAll(s => s.lugar_id == lugarId);

    private IEnumerator CargarTodo()
    {
        CargaTerminada = false;
        yield return BaseDatosLocal.Preparar();   // en Android copia la base del APK la primera vez

        if (!BaseDatosLocal.Lista)
        {
            Debug.LogError("No se pudo abrir la base de datos local.");
            CargaTerminada = true;
            yield break;
        }

        Intentar("Lugares", () =>
        {
            Lugares = RepositorioCampus.ListarLugares();
            Cargado = true;
            Debug.Log($"Lugares cargados: {Lugares.Count}");
            LugaresCargados?.Invoke(Lugares);
        });

        Intentar("Servicios", () =>
        {
            Servicios = RepositorioCampus.ListarServicios();
            Debug.Log($"Servicios cargados: {Servicios.Count}");
            ServiciosCargados?.Invoke(Servicios);
        });

        Intentar("Red", () =>
        {
            Red = RepositorioCampus.ObtenerRed();
            Debug.Log($"Red cargada: {Red.nodos.Length} nodos, {Red.conexiones.Length} conexiones");
            RedCargada?.Invoke();
        });

        Intentar("Modelos", () =>
        {
            Modelos = RepositorioCampus.ListarModelos();
            ModelosCargados?.Invoke(Modelos);
        });

        CargaTerminada = true;
    }

    // Un error en una tabla no impide cargar las demás
    private static void Intentar(string que, Action accion)
    {
        try { accion(); }
        catch (Exception e) { Debug.LogError($"{que}: {e.Message}\n{e.StackTrace}"); }
    }

    public void RegistrarBusqueda(int lugarId)
    {
        if (!BaseDatosLocal.Lista) return;
        try { RepositorioCampus.RegistrarBusqueda(lugarId); }
        catch (Exception e) { Debug.LogWarning($"Registrar búsqueda: {e.Message}"); }
    }
}
