using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Modo administrador. TODA edición de lugares, nodos y caminos pasa por aquí,
/// y ninguna operación se envía si no hay sesión de administrador iniciada.
/// (El servidor también lo exige: sin token válido responde 401.)
/// </summary>
public class AdminService : MonoBehaviour
{
    [SerializeField] private LugaresService servicio;

    private string token;
    private float expiraEn;   // Time.realtimeSinceStartup en que vence el token

    /// <summary>Verdadero solo si hay sesión de administrador vigente.</summary>
    public bool EstaAutenticado => !string.IsNullOrEmpty(token) && Time.realtimeSinceStartup < expiraEn;

    /// <summary>Se dispara al iniciar sesión (true) o al cerrarla / vencer (false). Úsalo para mostrar u ocultar el panel admin.</summary>
    public event Action<bool> SesionCambiada;

    // ----- Datos que se envían a la API -----
    [Serializable] private class LoginIn { public string nickname; public string llave; }
    [Serializable] private class LoginOut { public string token; public int expira_en_segundos; }
    [Serializable] private class ErrorApi { public string detail; }
    [Serializable] private class ConexionIn { public int nodo_a; public int nodo_b; }

    [Serializable]
    public class LugarEditable
    {
        public string nombre;
        public string categoria;
        public string descripcion;
        public string indicacion;
        public string horario;
        public double latitud;
        public double longitud;
        [Tooltip("ID de un nodo existente al que se une el lugar nuevo (0 = no crear nodo-puerta)")]
        public int conectar_con_nodo;
    }

    [Serializable]
    public class NodoEditable
    {
        public string nombre;
        public double latitud;
        public double longitud;
        public int lugar_id;            // 0 = no es la puerta de ningún lugar
        public int conectar_con_nodo;   // 0 = no conectar al crear
    }

    [Serializable]
    public class ServicioEditable
    {
        public int lugar_id;            // lugar al que pertenece (obligatorio)
        public string nombre;
        public string categoria;
        public string piso;
        public string descripcion;
        public string indicacion;       // cómo llegar dentro del lugar
        public string horario;
    }

    // ----- Sesión -----
    public void IniciarSesion(string nickname, string llave, Action<bool, string> alTerminar)
    {
        string json = JsonUtility.ToJson(new LoginIn { nickname = nickname, llave = llave });

        StartCoroutine(Enviar("POST", "/api/admin/login", json, false, false, (ok, texto) =>
        {
            if (!ok) { alTerminar?.Invoke(false, texto); return; }

            LoginOut r = JsonUtility.FromJson<LoginOut>(texto);
            token = r.token;
            expiraEn = Time.realtimeSinceStartup + r.expira_en_segundos - 30f;   // margen de 30 s
            SesionCambiada?.Invoke(true);
            alTerminar?.Invoke(true, "Sesión iniciada");
        }));
    }

    public void CerrarSesion()
    {
        bool habiaSesion = !string.IsNullOrEmpty(token);
        token = null;
        expiraEn = 0f;
        if (habiaSesion) SesionCambiada?.Invoke(false);
    }

    // ----- Lugares -----
    public void CrearLugar(LugarEditable l, Action<bool, string> cb) =>
        Escribir("POST", "/api/admin/lugares", JsonUtility.ToJson(l), cb);

    public void ActualizarLugar(int id, LugarEditable l, Action<bool, string> cb) =>
        Escribir("PUT", $"/api/admin/lugares/{id}", JsonUtility.ToJson(l), cb);

    public void EliminarLugar(int id, Action<bool, string> cb) =>
        Escribir("DELETE", $"/api/admin/lugares/{id}", null, cb);

    // ----- Servicios (salones, oficinas... de un lugar) -----
    public void CrearServicio(ServicioEditable sv, Action<bool, string> cb) =>
        Escribir("POST", "/api/admin/servicios", JsonUtility.ToJson(sv), cb);

    public void ActualizarServicio(int id, ServicioEditable sv, Action<bool, string> cb) =>
        Escribir("PUT", $"/api/admin/servicios/{id}", JsonUtility.ToJson(sv), cb);

    public void EliminarServicio(int id, Action<bool, string> cb) =>
        Escribir("DELETE", $"/api/admin/servicios/{id}", null, cb);

    // ----- Nodos -----
    public void CrearNodo(NodoEditable n, Action<bool, string> cb) =>
        Escribir("POST", "/api/admin/nodos", JsonUtility.ToJson(n), cb);

    public void ActualizarNodo(int id, NodoEditable n, Action<bool, string> cb) =>
        Escribir("PUT", $"/api/admin/nodos/{id}", JsonUtility.ToJson(n), cb);

    public void EliminarNodo(int id, Action<bool, string> cb) =>
        Escribir("DELETE", $"/api/admin/nodos/{id}", null, cb);

    // ----- Caminos (conexiones) -----
    public void CrearConexion(int nodoA, int nodoB, Action<bool, string> cb) =>
        Escribir("POST", "/api/admin/conexiones",
                 JsonUtility.ToJson(new ConexionIn { nodo_a = nodoA, nodo_b = nodoB }), cb);

    public void EliminarConexion(int nodoA, int nodoB, Action<bool, string> cb) =>
        Escribir("DELETE", $"/api/admin/conexiones/{nodoA}/{nodoB}", null, cb);

    // Cualquier cambio exige sesión de administrador y, si sale bien, recarga lugares y red
    private void Escribir(string metodo, string ruta, string json, Action<bool, string> cb)
    {
        StartCoroutine(Enviar(metodo, ruta, json, true, true, cb));
    }

    private IEnumerator Enviar(string metodo, string ruta, string json, bool requiereToken,
                               bool recargarAlTerminar, Action<bool, string> alTerminar)
    {
        if (requiereToken && !EstaAutenticado)
        {
            CerrarSesion();   // limpia una sesión vencida
            alTerminar?.Invoke(false, "Debes iniciar sesión como administrador.");
            yield break;
        }

        using (UnityWebRequest req = new UnityWebRequest(servicio.UrlBase + ruta, metodo))
        {
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (requiereToken) req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 10;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (recargarAlTerminar) servicio.Recargar();
                alTerminar?.Invoke(true, req.downloadHandler.text);
                yield break;
            }

            // 401 = el servidor no reconoce la sesión (venció o se reinició el servidor)
            if (req.responseCode == 401 && requiereToken)
            {
                CerrarSesion();
                alTerminar?.Invoke(false, "La sesión de administrador venció. Inicia sesión de nuevo.");
                yield break;
            }

            alTerminar?.Invoke(false, MensajeDeError(req));
        }
    }

    private static string MensajeDeError(UnityWebRequest req)
    {
        if (req.responseCode == 401) return "Credenciales incorrectas.";
        try
        {
            ErrorApi e = JsonUtility.FromJson<ErrorApi>(req.downloadHandler.text);
            if (!string.IsNullOrEmpty(e.detail)) return e.detail;
        }
        catch (Exception) { /* la respuesta no tenía el formato esperado */ }

        return string.IsNullOrEmpty(req.error) ? "Error desconocido." : req.error;
    }
}