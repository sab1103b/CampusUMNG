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

    private void Awake()
    {
        // Logs: todo visible en el Editor y en builds de desarrollo; en release solo advertencias y errores
        if (!Application.isEditor)
            Debug.unityLogger.filterLogType = Debug.isDebugBuild ? LogType.Log : LogType.Warning;
    }

    private void Start()
    {
        iniciado = true;
        recargaPendiente = false;
        Recargar();
    }

    // Recargas coalescidas: si piden otra mientras una corre, se repite UNA sola vez al terminar
    private bool recargando;
    private bool recargaPendiente;

    /// <summary>Vuelve a leer todo (se llama solo después de que el admin hace cambios).</summary>
    public void Recargar()
    {
        if (recargando || !isActiveAndEnabled)
        {
            // Corriendo: se repite al terminar. Desactivado: se lanza en OnEnable (no se pierde).
            recargaPendiente = true;
            return;
        }
        StartCoroutine(CicloRecarga());
    }

    public Lugar BuscarPorId(int id) => Lugares.Find(l => l.id == id);
    public Servicio BuscarServicioPorId(int id) => Servicios.Find(s => s.id == id);
    public List<Servicio> ServiciosDe(int lugarId) => Servicios.FindAll(s => s.lugar_id == lugarId);

    private bool iniciado;   // Start ya pidió la primera carga (antes de eso OnEnable no relanza nada)

    private void OnEnable()
    {
        if (iniciado && recargaPendiente) Recargar();
    }

    private void OnDisable()
    {
        // Las corrutinas se detienen al desactivar: si una recarga quedó a medias, se repite en OnEnable.
        // Si se detuvo mientras preparaba la base, se libera la marca (su finally nunca correrá).
        if (recargando)
        {
            recargaPendiente = true;
            BaseDatosLocal.CancelarPreparacion();
        }
        recargando = false;
    }

    private IEnumerator CicloRecarga()
    {
        recargando = true;
        do
        {
            recargaPendiente = false;
            yield return CargarTodo();
        } while (recargaPendiente);
        recargando = false;
    }

    private IEnumerator CargarTodo()
    {
        yield return BaseDatosLocal.Preparar();   // en Android copia la base del APK la primera vez

        if (!BaseDatosLocal.Lista)
        {
            Debug.LogError("No se pudo abrir la base de datos local.");
            CargaTerminada = true;
            yield break;
        }

        // 1) Leer todo en variables locales (si una tabla falla se conserva lo anterior)
        List<Lugar> nuevosLugares = null;
        List<Servicio> nuevosServicios = null;
        RedDto nuevaRed = null;
        List<Modelo> nuevosModelos = null;

        Intentar("Lugares", () => nuevosLugares = RepositorioCampus.ListarLugares());
        Intentar("Servicios", () => nuevosServicios = RepositorioCampus.ListarServicios());
        Intentar("Red", () => nuevaRed = RepositorioCampus.ObtenerRed());
        Intentar("Modelos", () => nuevosModelos = RepositorioCampus.ListarModelos());

        // 2) Reemplazar TODAS las listas antes de avisar a nadie
        if (nuevosLugares != null) { Lugares = nuevosLugares; Cargado = true; }
        if (nuevosServicios != null) Servicios = nuevosServicios;
        if (nuevaRed != null) Red = nuevaRed;
        if (nuevosModelos != null) Modelos = nuevosModelos;
        CargaTerminada = true;

        Registro.Info($"Datos cargados: {Lugares.Count} lugares, {Servicios.Count} servicios, " +
                  $"{(Red != null && Red.nodos != null ? Red.nodos.Length : 0)} nodos, {Modelos.Count} modelos");

        // 3) Eventos (mismo orden de siempre), ya con todos los datos coherentes
        if (nuevosLugares != null) Intentar("Evento lugares", () => LugaresCargados?.Invoke(Lugares));
        if (nuevosServicios != null) Intentar("Evento servicios", () => ServiciosCargados?.Invoke(Servicios));
        if (nuevaRed != null) Intentar("Evento red", () => RedCargada?.Invoke());
        if (nuevosModelos != null) Intentar("Evento modelos", () => ModelosCargados?.Invoke(Modelos));
    }

    // Un error en una tabla no impide cargar las demás
    private static void Intentar(string que, Action accion)
    {
        try { accion(); }
        catch (Exception e) { Debug.LogError($"{que}: {e.Message}\n{e.StackTrace}"); }
    }

    // Último registro de cada lugar (Time.realtimeSinceStartup): cada búsqueda cuenta; solo se evita el doble conteo
    // inmediato de una misma acción (por ejemplo, la ficha y "Cómo llegar" del mismo lugar en pocos segundos)
    private const float SegundosEntreBusquedas = 5f;
    private readonly Dictionary<int, float> ultimaBusqueda = new Dictionary<int, float>();

    public void RegistrarBusqueda(int lugarId)
    {
        // También cuenta en Play del Editor (decisión del dueño, para probar el reporte). La base del Editor es la
        // maestra que viaja en el APK: antes de compilar, Unity ofrece limpiar estas búsquedas de prueba.
        if (!BaseDatosLocal.Lista) return;

        float ahora = Time.realtimeSinceStartup;
        if (ultimaBusqueda.TryGetValue(lugarId, out float antes) && ahora - antes < SegundosEntreBusquedas) return;

        try
        {
            RepositorioCampus.RegistrarBusqueda(lugarId);
            ultimaBusqueda[lugarId] = ahora;
        }
        catch (Exception e) { Debug.LogWarning($"Registrar búsqueda: {e.Message}"); }
    }
}
