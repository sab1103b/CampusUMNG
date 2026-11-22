using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Modo administrador. TODA edición de lugares, servicios, nodos y caminos pasa por aquí,
/// y ninguna operación se ejecuta si no hay sesión de administrador iniciada.
/// Trabaja contra la base de datos local (SQLite); los métodos públicos son los mismos
/// que cuando usaba la API, así que los paneles existentes no cambian.
/// </summary>
public class AdminService : MonoBehaviour
{
    [SerializeField] private LugaresService servicio;

    [Tooltip("Minutos que dura la sesión de administrador")]
    [SerializeField] private float minutosSesion = 120f;

    private string nicknameActual;
    private float expiraEn;   // Time.realtimeSinceStartup en que vence la sesión

    /// <summary>Verdadero solo si hay sesión de administrador vigente.</summary>
    public bool EstaAutenticado => !string.IsNullOrEmpty(nicknameActual) && Time.realtimeSinceStartup < expiraEn;

    /// <summary>Nickname del administrador con sesión abierta (null si no hay).</summary>
    public string NicknameActual => EstaAutenticado ? nicknameActual : null;

    /// <summary>Se dispara al iniciar sesión (true) o al cerrarla / vencer (false). Úsalo para mostrar u ocultar el panel admin.</summary>
    public event Action<bool> SesionCambiada;

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
    public class ModeloEditable
    {
        public string tipo;             // cubo | rectangulo | cilindro
        public string nombre;
        public double latitud;
        public double longitud;
        public float rotacion;          // grados
        public float ancho;             // metros
        public float largo;             // metros
        public float alto;              // metros
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

    // ----- Protección contra intentos repetidos (login y registro) -----
    private const int MaxIntentos = 5;
    private const float SegundosBloqueo = 60f;
    private int intentosFallidos;
    private float bloqueadoHasta;

    private bool EstaBloqueado(Action<bool, string> cb)
    {
        float resta = bloqueadoHasta - Time.realtimeSinceStartup;
        if (resta <= 0f) return false;
        cb?.Invoke(false, $"Demasiados intentos. Espera {Mathf.CeilToInt(resta)} s.");
        return true;
    }

    private void ContarFallo()
    {
        intentosFallidos++;
        if (intentosFallidos < MaxIntentos) return;
        intentosFallidos = 0;
        bloqueadoHasta = Time.realtimeSinceStartup + SegundosBloqueo;
    }

    // ----- Sesión -----
    public void IniciarSesion(string nickname, string llave, Action<bool, string> alTerminar)
    {
        if (!BaseDatosLocal.Lista)
        {
            alTerminar?.Invoke(false, "La base de datos todavía está cargando. Intenta de nuevo.");
            return;
        }
        if (EstaBloqueado(alTerminar)) return;

        bool ok;
        try { ok = RepositorioCampus.VerificarAdmin(nickname, llave); }
        catch (Exception e)
        {
            Debug.LogError("Login: " + e);
            alTerminar?.Invoke(false, "No se pudo verificar el usuario.");
            return;
        }

        // Mismo mensaje para nickname o llave incorrectos (no revela cuál falló)
        if (!ok) { ContarFallo(); alTerminar?.Invoke(false, "Usuario o contraseña incorrectos."); return; }

        intentosFallidos = 0;
        nicknameActual = nickname.Trim();
        expiraEn = Time.realtimeSinceStartup + minutosSesion * 60f;
        SesionCambiada?.Invoke(true);
        alTerminar?.Invoke(true, "Sesión iniciada");
    }

    /// <summary>
    /// Registra una cuenta de administrador con el código de verificación entregado.
    /// No requiere sesión. Una vez registrada, la cuenta no se puede modificar.
    /// </summary>
    public void Registrar(string nickname, string llave, string codigo, Action<bool, string> alTerminar)
    {
        if (!BaseDatosLocal.Lista)
        {
            alTerminar?.Invoke(false, "La base de datos todavía está cargando. Intenta de nuevo.");
            return;
        }
        if (EstaBloqueado(alTerminar)) return;

        try
        {
            RepositorioCampus.RegistrarAdmin(codigo, nickname, llave);
        }
        catch (ErrorCampus e)
        {
            if (e.Message.Contains("código")) ContarFallo();   // solo cuentan los códigos errados
            alTerminar?.Invoke(false, e.Message);
            return;
        }
        catch (Exception e)
        {
            Debug.LogError("Registro: " + e);
            alTerminar?.Invoke(false, "No se pudo registrar la cuenta.");
            return;
        }

        intentosFallidos = 0;
        alTerminar?.Invoke(true, "Cuenta registrada. Ya puedes iniciar sesión.");
    }

    public void CerrarSesion()
    {
        bool habiaSesion = !string.IsNullOrEmpty(nicknameActual);
        nicknameActual = null;
        expiraEn = 0f;
        if (habiaSesion) SesionCambiada?.Invoke(false);
    }

    // ----- Lugares -----
    public void CrearLugar(LugarEditable l, Action<bool, string> cb) =>
        Escribir(() => RepositorioCampus.CrearLugar(l), cb);

    public void ActualizarLugar(int id, LugarEditable l, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.ActualizarLugar(id, l); return Ok; }, cb);

    public void EliminarLugar(int id, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.EliminarLugar(id); return Ok; }, cb);

    // ----- Servicios (salones, oficinas... de un lugar) -----
    public void CrearServicio(ServicioEditable sv, Action<bool, string> cb) =>
        Escribir(() => RepositorioCampus.CrearServicio(sv), cb);

    public void ActualizarServicio(int id, ServicioEditable sv, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.ActualizarServicio(id, sv); return Ok; }, cb);

    public void EliminarServicio(int id, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.EliminarServicio(id); return Ok; }, cb);

    // ----- Nodos -----
    public void CrearNodo(NodoEditable n, Action<bool, string> cb) =>
        Escribir(() => RepositorioCampus.CrearNodo(n), cb);

    public void ActualizarNodo(int id, NodoEditable n, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.ActualizarNodo(id, n); return Ok; }, cb);

    public void EliminarNodo(int id, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.EliminarNodo(id); return Ok; }, cb);

    // ----- Caminos (conexiones) -----
    public void CrearConexion(int nodoA, int nodoB, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.CrearConexion(nodoA, nodoB); return Ok; }, cb);

    public void EliminarConexion(int nodoA, int nodoB, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.EliminarConexion(nodoA, nodoB); return Ok; }, cb);

    // ----- Modelos 3D (edificaciones) -----
    public void CrearModelo(ModeloEditable m, Action<bool, string> cb) =>
        Escribir(() => RepositorioCampus.CrearModelo(m), cb);

    public void ActualizarModelo(int id, ModeloEditable m, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.ActualizarModelo(id, m); return Ok; }, cb);

    public void EliminarModelo(int id, Action<bool, string> cb) =>
        Escribir(() => { RepositorioCampus.EliminarModelo(id); return Ok; }, cb);

    // ----- Reporte PDF -----

    /// <summary>
    /// Genera el PDF con el ranking de los 10 lugares más buscados.
    /// Si sale bien, el callback recibe la ruta completa del archivo.
    /// Editor: carpeta "Reportes" junto a Assets. Android: Android/data/&lt;paquete&gt;/files/Reportes.
    /// </summary>
    public void GenerarReportePdf(Action<bool, string> alTerminar)
    {
        if (!VerificarSesion(alTerminar)) return;

        try
        {
            alTerminar?.Invoke(true, GuardarReportePdf());
        }
        catch (Exception e)
        {
            Debug.LogError("Reporte PDF: " + e);
            alTerminar?.Invoke(false, "No se pudo generar el reporte: " + e.Message);
        }
    }

    /// <summary>
    /// Crea el PDF con el ranking actual y devuelve su ruta. No verifica sesión:
    /// úsalo solo desde GenerarReportePdf o desde herramientas del Editor.
    /// </summary>
    public static string GuardarReportePdf()
    {
        var ranking = RepositorioCampus.RankingBusquedas(10, out int total);
        DateTime ahora = DateTime.Now;
        byte[] pdf = ReportePdf.Generar(ranking, total, ahora);

        string carpeta = Application.isEditor
            ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reportes")
            : Path.Combine(Application.persistentDataPath, "Reportes");
        Directory.CreateDirectory(carpeta);

        string ruta = Path.Combine(carpeta, $"reporte_busquedas_{ahora:yyyyMMdd_HHmmss}.pdf");
        File.WriteAllBytes(ruta, pdf);
        Debug.Log("Reporte PDF generado: " + ruta);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(ruta);
#endif
        return ruta;
    }

    // ----- Interno -----
    private const string Ok = "{\"ok\":true}";

    private bool VerificarSesion(Action<bool, string> cb)
    {
        if (EstaAutenticado) return true;
        CerrarSesion();   // limpia una sesión vencida
        cb?.Invoke(false, "Debes iniciar sesión como administrador.");
        return false;
    }

    // Cualquier cambio exige sesión de administrador y, si sale bien, recarga lugares y red
    private void Escribir(Func<string> cambio, Action<bool, string> alTerminar)
    {
        if (!VerificarSesion(alTerminar)) return;

        string respuesta;
        try
        {
            respuesta = cambio();
        }
        catch (ErrorCampus e)
        {
            alTerminar?.Invoke(false, e.Message);
            return;
        }
        catch (Exception e)
        {
            Debug.LogError("Admin: " + e);
            alTerminar?.Invoke(false, "Error inesperado: " + e.Message);
            return;
        }

        if (servicio != null) servicio.Recargar();
        alTerminar?.Invoke(true, respuesta);
    }
}
