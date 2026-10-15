using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Se comunica con la API de Python: descarga los lugares, sus servicios y la red de caminos,
/// y registra cada búsqueda para el reporte PDF.
/// </summary>
public class LugaresService : MonoBehaviour
{
    [Header("API")]
    [Tooltip("En el editor: http://127.0.0.1:8000  |  En el celular: http://IP_DE_TU_PC:8000")]
    [SerializeField] private string urlBase = "http://127.0.0.1:8000";

    // ----- Reflejan el JSON de la API -----
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

    [Serializable] private class ListaLugares { public Lugar[] items; }
    [Serializable] private class ListaServicios { public Servicio[] items; }
    [Serializable] private class BusquedaDto { public int lugar_id; }

    public List<Lugar> Lugares { get; private set; } = new List<Lugar>();
    public List<Servicio> Servicios { get; private set; } = new List<Servicio>();
    public RedDto Red { get; private set; }

    public bool Cargado { get; private set; }          // ya hay lugares
    public bool CargaTerminada { get; private set; }   // ya se intentó cargar todo (lugares, servicios y red)
    public string UrlBase => urlBase;

    public event Action<List<Lugar>> LugaresCargados;
    public event Action<List<Servicio>> ServiciosCargados;
    public event Action RedCargada;

    private void Start() => Recargar();

    /// <summary>Vuelve a descargar todo (úsalo después de que el admin haga cambios).</summary>
    public void Recargar() => StartCoroutine(DescargarTodo());

    public Lugar BuscarPorId(int id) => Lugares.Find(l => l.id == id);
    public Servicio BuscarServicioPorId(int id) => Servicios.Find(s => s.id == id);
    public List<Servicio> ServiciosDe(int lugarId) => Servicios.FindAll(s => s.lugar_id == lugarId);

    private IEnumerator DescargarTodo()
    {
        CargaTerminada = false;
        yield return DescargarLugares();
        yield return DescargarServicios();
        yield return DescargarRed();
        CargaTerminada = true;
    }

    private IEnumerator DescargarLugares()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(urlBase + "/api/lugares"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Lugares: error {req.responseCode} - {req.error}  (URL: {urlBase})");
                yield break;
            }

            // JsonUtility no lee listas en la raíz del JSON, así que la envolvemos en un objeto
            string envuelto = "{\"items\":" + req.downloadHandler.text + "}";
            ListaLugares datos = JsonUtility.FromJson<ListaLugares>(envuelto);

            Lugares = new List<Lugar>(datos.items);
            Cargado = true;
            Debug.Log($"Lugares cargados: {Lugares.Count}");
            LugaresCargados?.Invoke(Lugares);
        }
    }

    private IEnumerator DescargarServicios()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(urlBase + "/api/servicios"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Servicios: error {req.responseCode} - {req.error}  (¿ejecutaste servicios.sql?)");
                yield break;
            }

            string envuelto = "{\"items\":" + req.downloadHandler.text + "}";
            ListaServicios datos = JsonUtility.FromJson<ListaServicios>(envuelto);

            Servicios = new List<Servicio>(datos.items);
            Debug.Log($"Servicios cargados: {Servicios.Count}");
            ServiciosCargados?.Invoke(Servicios);
        }
    }

    private IEnumerator DescargarRed()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(urlBase + "/api/red"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Red: error {req.responseCode} - {req.error}");
                yield break;
            }

            Red = JsonUtility.FromJson<RedDto>(req.downloadHandler.text);
            int n = Red.nodos != null ? Red.nodos.Length : 0;
            int c = Red.conexiones != null ? Red.conexiones.Length : 0;
            Debug.Log($"Red descargada: {n} nodos, {c} conexiones");
            RedCargada?.Invoke();
        }
    }

    public void RegistrarBusqueda(int lugarId) => StartCoroutine(EnviarBusqueda(lugarId));

    private IEnumerator EnviarBusqueda(int lugarId)
    {
        string json = JsonUtility.ToJson(new BusquedaDto { lugar_id = lugarId });

        using (UnityWebRequest req = new UnityWebRequest(urlBase + "/api/busquedas/registrar", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"Registrar búsqueda: error {req.responseCode} - {req.error}");
        }
    }
}