using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Modo administrador. TODA edición de lugares, servicios, nodos y caminos pasa por aquí,
/// y ninguna operación se ejecuta si no hay sesión de administrador iniciada.
/// Trabaja contra la base de datos local (SQLite); los métodos públicos son los mismos
/// que cuando usaba la API, así que los paneles existentes no cambian.
/// Las ediciones hechas en el celular duran hasta que se instale una versión nueva de la app: entonces
/// se reemplazan por los datos del APK (solo se conservan cuentas y búsquedas; ver BaseDatosLocal).
/// El reporte PDF (generación, MediaStore e Intent de Android) está en AdminService.Reportes.cs.
/// </summary>
public partial class AdminService : MonoBehaviour
{
    [SerializeField] private LugaresService servicio;

    [Tooltip("Minutos que dura la sesión de administrador (tope, aunque se use sin parar)")]
    [SerializeField] private float minutosSesion = 120f;
    [Tooltip("Minutos sin usar el modo administrador tras los que la sesión se cierra sola")]
    [SerializeField] private float minutosInactividad = 15f;

    private string nicknameActual;
    // Vencimiento de la sesión con dos relojes; vence con el PRIMERO que llegue:
    //  - DateTime.UtcNow (ticks): sigue corriendo con la app en segundo plano o el celular suspendido.
    //  - Time.realtimeSinceStartup: no se puede adelantar ni atrasar cambiando la hora del celular.
    private long expiraTicksUtc;
    private float expiraRealtime;
    // Última actividad de administrador (login, cambios, estadísticas, RegistrarActividad), con los mismos dos relojes
    private long actividadTicksUtc;
    private float actividadRealtime;
    private const string MsgInactividad = "Tu sesión se cerró por inactividad.";

    // Segundos sin actividad; cuenta el reloj que más haya avanzado (igual que el vencimiento: vence con el primero)
    private double SegundosInactiva()
    {
        double porReloj = (DateTime.UtcNow.Ticks - actividadTicksUtc) / (double)TimeSpan.TicksPerSecond;
        double porSesion = Time.realtimeSinceStartup - actividadRealtime;
        return Math.Max(porReloj, porSesion);
    }

    private bool Inactiva => minutosInactividad > 0f && SegundosInactiva() >= minutosInactividad * 60.0;

    /// <summary>
    /// Marca actividad del administrador (reinicia la cuenta de inactividad). Escribir y las estadísticas ya
    /// la marcan; la interfaz puede llamarlo además al navegar las pantallas de admin. Sin sesión no hace nada.
    /// </summary>
    public void RegistrarActividad()
    {
        if (string.IsNullOrEmpty(nicknameActual)) return;
        actividadTicksUtc = DateTime.UtcNow.Ticks;
        actividadRealtime = Time.realtimeSinceStartup;
    }

    private double SegundosRestantesSesion()
    {
        double porReloj = (expiraTicksUtc - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond;
        double porSesion = expiraRealtime - Time.realtimeSinceStartup;
        return Math.Min(porReloj, porSesion);
    }

    /// <summary>Verdadero solo si hay sesión de administrador vigente (sin vencer ni inactiva).</summary>
    public bool EstaAutenticado => !string.IsNullOrEmpty(nicknameActual) && SegundosRestantesSesion() > 0 && !Inactiva;

    /// <summary>Verdadero si la sesión vence en menos de 5 minutos.</summary>
    public bool SesionPorVencer => EstaAutenticado && SegundosRestantesSesion() < 300;

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
    // Cada 5 fallos seguidos se bloquea: 60 s, luego 5 min y desde ahí 30 min (máximo).
    // Se guarda en PlayerPrefs para que cerrar y abrir la app no reinicie el bloqueo.
    // Además, mientras la app siga abierta, se cuenta con Time.realtimeSinceStartup (no depende del
    // reloj del celular): adelantar la hora no acorta un bloqueo en curso.
    // Límite conocido (app sin conexión, sin servidor): cerrar la app y adelantar el reloj, o borrar
    // los datos de la app, reinicia el bloqueo. Lo compensa el costo de PBKDF2 y que cada código de
    // registro es de un solo uso.
    private const int MaxIntentos = 5;
    private static readonly double[] SegundosBloqueoPorNivel = { 60, 300, 1800 };
    private const string PrefIntentos = "admin_intentos_fallidos";
    private const string PrefBloqueo = "admin_bloqueado_hasta";     // ticks UTC en que termina
    private const string PrefNivel = "admin_nivel_bloqueo";         // cuántos bloqueos seguidos van
    private const string PrefDuracion = "admin_duracion_bloqueo";   // segundos del bloqueo actual
    private const string PrefUltimoFallo = "admin_ultimo_fallo";    // ticks UTC del último fallo
    // Los fallos sueltos caducan: si el último fue hace más de esto, la cuenta vuelve a empezar en 0
    private const double HorasCaducidadFallos = 24;

    // Fin del bloqueo según el reloj interno de esta ejecución (0 = sin bloqueo en esta ejecución)
    private static float bloqueoHastaRealtime;

    private static int IntentosFallidos
    {
        get
        {
            int n = PlayerPrefs.GetInt(PrefIntentos, 0);
            if (n <= 0) return 0;
            // Fallos viejos (más de HorasCaducidadFallos) ya no cuentan; un reloj que retrocedió tampoco los borra
            if (long.TryParse(PlayerPrefs.GetString(PrefUltimoFallo, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
                && ticks > 0 && (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalHours > HorasCaducidadFallos)
                return 0;
            return n;
        }
        set
        {
            PlayerPrefs.SetInt(PrefIntentos, value);
            if (value > 0) PlayerPrefs.SetString(PrefUltimoFallo, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            else PlayerPrefs.DeleteKey(PrefUltimoFallo);
            PlayerPrefs.Save();
        }
    }

    private static double SegundosBloqueoRestantes()
    {
        // Bloqueo medido con el reloj interno (no se puede adelantar) mientras la app siga abierta
        double porSesion = bloqueoHastaRealtime - Time.realtimeSinceStartup;
        if (!long.TryParse(PlayerPrefs.GetString(PrefBloqueo, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0)
            return porSesion;

        double resta = (new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
        // Si el reloj del celular retrocedió, nunca bloquear más que la duración de este bloqueo (ni más de 30 min)
        double duracion = PlayerPrefs.GetFloat(PrefDuracion, (float)SegundosBloqueoPorNivel[0]);
        double maximo = SegundosBloqueoPorNivel[SegundosBloqueoPorNivel.Length - 1];
        double porReloj = Math.Min(resta, Math.Min(duracion, maximo));
        return Math.Max(porReloj, porSesion);
    }

    private bool EstaBloqueado(Action<bool, string> cb)
    {
        double resta = SegundosBloqueoRestantes();
        if (resta <= 0) return false;
        cb?.Invoke(false, TextoBloqueo(resta));
        return true;
    }

    // "Demasiados intentos. Espera 1 min." / "… Espera 40 s."
    private static string TextoBloqueo(double resta)
    {
        string espera = resta >= 60 ? $"{Math.Ceiling(resta / 60)} min" : $"{Math.Ceiling(resta)} s";
        return $"Demasiados intentos. Espera {espera}.";
    }

    /// <summary>
    /// Cuenta un fallo. Devuelve el mensaje del bloqueo si ESTE fallo lo activó (para anunciarlo de
    /// inmediato, no recién en el siguiente intento); null si todavía no hay bloqueo.
    /// </summary>
    private string ContarFallo()
    {
        int n = IntentosFallidos + 1;
        if (n < MaxIntentos) { IntentosFallidos = n; return null; }

        int nivel = Mathf.Clamp(PlayerPrefs.GetInt(PrefNivel, 0), 0, SegundosBloqueoPorNivel.Length - 1);
        double segundos = SegundosBloqueoPorNivel[nivel];
        PlayerPrefs.SetString(PrefBloqueo, DateTime.UtcNow.AddSeconds(segundos).Ticks.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.SetFloat(PrefDuracion, (float)segundos);
        PlayerPrefs.SetInt(PrefNivel, Math.Min(nivel + 1, SegundosBloqueoPorNivel.Length - 1));
        bloqueoHastaRealtime = Time.realtimeSinceStartup + (float)segundos;
        IntentosFallidos = 0;   // también guarda
        return TextoBloqueo(segundos);
    }

    private static void ReiniciarIntentos()
    {
        PlayerPrefs.DeleteKey(PrefBloqueo);
        PlayerPrefs.DeleteKey(PrefNivel);
        PlayerPrefs.DeleteKey(PrefDuracion);
        bloqueoHastaRealtime = 0f;
        IntentosFallidos = 0;
    }

    // Vencimiento de sesión: se revisa una vez por segundo y se cierra sola
    private float proximaRevision;

    private void Update()
    {
        if (string.IsNullOrEmpty(nicknameActual) || Time.unscaledTime < proximaRevision) return;
        proximaRevision = Time.unscaledTime + 1f;
        RevisarVencimiento();
    }

    // Al volver a la app (tras horas en segundo plano) se revisa de inmediato, sin esperar al Update
    private void OnApplicationPause(bool pausa)
    {
        if (!pausa) RevisarVencimiento();
    }

    private void RevisarVencimiento()
    {
        if (string.IsNullOrEmpty(nicknameActual)) return;
        if (SegundosRestantesSesion() <= 0) CerrarSesion();   // dispara SesionCambiada(false)
        else if (Inactiva) CerrarPorInactividad();
    }

    // Cierre por inactividad: se anuncia solo si el usuario está en las pantallas de administrador
    // (en el mapa no le importa); el aviso genérico de cierre del UIManager se silencia para no taparlo
    private void CerrarPorInactividad()
    {
        UIManager ui = FindFirstObjectByType<UIManager>();
        if (ui != null && ui.AnunciaCierreDeSesion)
        {
            ui.Avisar(MsgInactividad);
            ui.SilenciarProximoCierreDeSesion();
        }
        CerrarSesion();
    }

    // ----- Sesión -----

    // Operaciones en curso (login y registro calculan PBKDF2 en otro hilo). Si el objeto se desactiva
    // a mitad de camino, OnDisable detiene la corrutina y llama el callback pendiente con
    // MensajeInterrumpida, para que la pantalla no quede esperando para siempre.
    private const string MensajeInterrumpida = "Operación interrumpida. Intenta de nuevo.";
    private bool iniciando;
    private bool registrando;
    private Action<bool, string> loginPendiente;
    private Action<bool, string> registroPendiente;
    private Coroutine rutinaLogin;
    private Coroutine rutinaRegistro;

    /// <summary>Verdadero mientras se verifica un inicio de sesión.</summary>
    public bool IniciandoSesion => iniciando;

    /// <summary>Verdadero mientras se crea una cuenta.</summary>
    public bool Registrando => registrando;

    /// <summary>
    /// Inicia sesión de administrador. ASÍNCRONO: el PBKDF2 corre en otro hilo; la base de datos,
    /// PlayerPrefs y la sesión se manejan solo en el hilo principal.
    /// Contrato de 'alTerminar(ok, mensaje)':
    ///  - Se llama EXACTAMENTE UNA VEZ y SIEMPRE en el hilo principal.
    ///  - Puede llamarse antes de que este método retorne (base cargando, bloqueo por intentos, otro login
    ///    en curso) o varios fotogramas después (verificación de la contraseña). Quien llama debe mantener
    ///    "Verificando…" y el botón deshabilitado hasta recibirlo, sin límite de tiempo.
    ///  - Si el objeto se desactiva antes de terminar: (false, "Operación interrumpida. Intenta de nuevo.").
    ///  - ok = true: la sesión ya está abierta y SesionCambiada(true) ya se disparó.
    /// </summary>
    public void IniciarSesion(string nickname, string llave, Action<bool, string> alTerminar)
    {
        if (!BaseDatosLocal.Lista)
        {
            alTerminar?.Invoke(false, "La base de datos todavía está cargando. Intenta de nuevo.");
            return;
        }
        if (iniciando)
        {
            alTerminar?.Invoke(false, "Ya se está verificando el usuario. Espera un momento.");
            return;
        }
        if (EstaBloqueado(alTerminar)) return;

        RepositorioCampus.CuentaLogin cuenta;
        try { cuenta = RepositorioCampus.LeerCuentaParaLogin(nickname); }
        catch (Exception e)
        {
            Debug.LogError("Login: " + e);
            alTerminar?.Invoke(false, "No se pudo verificar el usuario.");
            return;
        }

        // Sin objeto activo no hay corrutina: se verifica aquí (mismo resultado, solo bloquea un momento)
        if (!isActiveAndEnabled)
        {
            bool ok;
            try { ok = RepositorioCampus.VerificarLlave(cuenta, llave); }
            catch (Exception e)
            {
                Debug.LogError("Login: " + e);
                alTerminar?.Invoke(false, "No se pudo verificar el usuario.");
                return;
            }
            TerminarLogin(ok, cuenta, alTerminar);
            return;
        }

        iniciando = true;
        loginPendiente = alTerminar;
        rutinaLogin = StartCoroutine(IniciarSesionEnSegundoPlano(cuenta, llave));
    }

    private IEnumerator IniciarSesionEnSegundoPlano(RepositorioCampus.CuentaLogin cuenta, string llave)
    {
        // Solo PBKDF2 en el otro hilo (sin SQLite, PlayerPrefs ni APIs de Unity)
        Task<bool> tarea = Task.Run(() => RepositorioCampus.VerificarLlave(cuenta, llave));
        while (!tarea.IsCompleted) yield return null;

        // De vuelta en el hilo principal
        Action<bool, string> alTerminar = loginPendiente;
        iniciando = false;
        loginPendiente = null;
        rutinaLogin = null;

        if (tarea.IsFaulted || tarea.IsCanceled)
        {
            Debug.LogError("Login: " + (tarea.Exception != null ? tarea.Exception.GetBaseException().ToString() : "cancelado"));
            alTerminar?.Invoke(false, "No se pudo verificar el usuario.");
            yield break;
        }
        TerminarLogin(tarea.Result, cuenta, alTerminar);
    }

    private void TerminarLogin(bool ok, RepositorioCampus.CuentaLogin cuenta, Action<bool, string> alTerminar)
    {
        // Mismo mensaje para nickname o llave incorrectos (no revela cuál falló)
        if (!ok)
        {
            string bloqueo = ContarFallo();
            alTerminar?.Invoke(false, bloqueo ?? "Usuario o contraseña incorrectos.");
            return;
        }

        ReiniciarIntentos();
        nicknameActual = cuenta.nickname;   // tal como quedó registrado (el login no distingue mayúsculas)
        expiraRealtime = Time.realtimeSinceStartup + minutosSesion * 60f;
        expiraTicksUtc = DateTime.UtcNow.AddMinutes(minutosSesion).Ticks;
        RegistrarActividad();
        SesionCambiada?.Invoke(true);
        alTerminar?.Invoke(true, "Sesión iniciada.");
    }

    /// <summary>
    /// Registra una cuenta de administrador con el código de verificación entregado.
    /// No requiere sesión. Una vez registrada, la cuenta no se puede modificar.
    /// El cálculo pesado (PBKDF2 contra cada espacio) corre en otro hilo; la base de datos solo se usa
    /// en el hilo principal. Mismo contrato de callback que IniciarSesion: exactamente una vez, en el hilo
    /// principal, de inmediato (errores de validación) o fotogramas después; si el objeto se desactiva:
    /// (false, "Operación interrumpida. Intenta de nuevo.") y la cuenta NO queda creada.
    /// </summary>
    public void Registrar(string nickname, string llave, string codigo, Action<bool, string> alTerminar)
    {
        if (!BaseDatosLocal.Lista)
        {
            alTerminar?.Invoke(false, "La base de datos todavía está cargando. Intenta de nuevo.");
            return;
        }
        if (registrando)
        {
            alTerminar?.Invoke(false, "Ya se está creando la cuenta. Espera un momento.");
            return;
        }
        if (EstaBloqueado(alTerminar)) return;

        string usuario;
        List<RepositorioCampus.EspacioRegistro> espacios;
        try
        {
            usuario = RepositorioCampus.ValidarRegistro(codigo, nickname, llave);
            espacios = RepositorioCampus.LeerEspaciosParaRegistro();
        }
        catch (Exception e)
        {
            TerminarRegistro(e, alTerminar);
            return;
        }

        // Sin objeto activo no hay corrutina: se hace todo aquí (mismo resultado, solo bloquea un momento)
        if (!isActiveAndEnabled)
        {
            try { RepositorioCampus.CompletarRegistro(RepositorioCampus.PrepararRegistro(codigo, llave, espacios), usuario); }
            catch (Exception e) { TerminarRegistro(e, alTerminar); return; }
            TerminarRegistro(null, alTerminar);
            return;
        }

        registrando = true;
        registroPendiente = alTerminar;
        rutinaRegistro = StartCoroutine(RegistrarEnSegundoPlano(usuario, llave, codigo, espacios));
    }

    private IEnumerator RegistrarEnSegundoPlano(string usuario, string llave, string codigo,
                                                List<RepositorioCampus.EspacioRegistro> espacios)
    {
        // Solo PBKDF2 en el otro hilo (sin SQLite, PlayerPrefs ni APIs de Unity)
        Task<RepositorioCampus.PreparacionRegistro> tarea =
            Task.Run(() => RepositorioCampus.PrepararRegistro(codigo, llave, espacios));
        while (!tarea.IsCompleted) yield return null;

        // De vuelta en el hilo principal: aquí se escribe en la base y se llama el callback
        Action<bool, string> alTerminar = registroPendiente;
        registrando = false;
        registroPendiente = null;
        rutinaRegistro = null;

        Exception error = null;
        if (tarea.IsFaulted || tarea.IsCanceled)
            error = tarea.Exception != null ? tarea.Exception.GetBaseException() : new Exception("Registro cancelado.");
        else if (!BaseDatosLocal.Lista)
            error = new ErrorCampus("La base de datos todavía está cargando. Intenta de nuevo.", TipoErrorCampus.BaseDatos);
        else
        {
            try { RepositorioCampus.CompletarRegistro(tarea.Result, usuario); }
            catch (Exception e) { error = e; }
        }
        TerminarRegistro(error, alTerminar);
    }

    private void TerminarRegistro(Exception error, Action<bool, string> alTerminar)
    {
        if (error == null)
        {
            ReiniciarIntentos();
            alTerminar?.Invoke(true, "Cuenta registrada. Ya puedes iniciar sesión.");
            return;
        }
        if (error is ErrorCampus ec)
        {
            // Solo cuentan los códigos errados; si este fallo activa el bloqueo, se anuncia ya
            string bloqueo = ec.Tipo == TipoErrorCampus.CodigoInvalido ? ContarFallo() : null;
            alTerminar?.Invoke(false, bloqueo ?? ec.Message);
            return;
        }
        Debug.LogError("Registro: " + error);
        alTerminar?.Invoke(false, "No se pudo registrar la cuenta. Intenta de nuevo.");
    }

    /// <summary>
    /// Al desactivarse el objeto, Unity detiene sus corrutinas (y al desactivar solo el componente
    /// seguirían): en ambos casos se detienen aquí, se reinician las banderas y se avisa a quien esperaba.
    /// La tarea en el otro hilo termina sola y su resultado se descarta (no escribe nada).
    /// </summary>
    private void OnDisable()
    {
        if (rutinaLogin != null) StopCoroutine(rutinaLogin);
        if (rutinaRegistro != null) StopCoroutine(rutinaRegistro);
        rutinaLogin = rutinaRegistro = null;

        Action<bool, string> login = loginPendiente, registro = registroPendiente;
        iniciando = registrando = false;
        loginPendiente = registroPendiente = null;

        AvisarInterrupcion(login);
        AvisarInterrupcion(registro);
    }

    private static void AvisarInterrupcion(Action<bool, string> alTerminar)
    {
        if (alTerminar == null) return;
        try { alTerminar(false, MensajeInterrumpida); }
        catch (Exception e) { Debug.LogError("Admin (operación interrumpida): " + e); }
    }

    public void CerrarSesion()
    {
        bool habiaSesion = !string.IsNullOrEmpty(nicknameActual);
        nicknameActual = null;
        expiraRealtime = 0f;
        expiraTicksUtc = 0;
        actividadRealtime = 0f;
        actividadTicksUtc = 0;
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

    // ----- Reporte PDF: ver AdminService.Reportes.cs -----

    // ----- Interno -----
    private const string Ok = "{\"ok\":true}";

    // Exige sesión vigente y, si la hay, cuenta como actividad (reinicia la cuenta de inactividad)
    private bool VerificarSesion(Action<bool, string> cb)
    {
        if (EstaAutenticado) { RegistrarActividad(); return true; }
        bool porInactividad = !string.IsNullOrEmpty(nicknameActual) && SegundosRestantesSesion() > 0 && Inactiva;
        if (porInactividad) CerrarPorInactividad();
        else CerrarSesion();   // limpia una sesión vencida
        cb?.Invoke(false, porInactividad ? MsgInactividad : "Debes iniciar sesión como administrador.");
        return false;
    }

    // ----- Estadísticas (pestaña "Reportes de uso") -----

    /// <summary>
    /// Búsquedas por lugar del mes de 'hoy' y del anterior. Exige sesión de administrador (cuenta como
    /// actividad). Devuelve null sin sesión o si falla la lectura (el detalle va al log).
    /// </summary>
    public List<RepositorioCampus.EstadisticaLugar> EstadisticasDelMes(DateTime hoy)
    {
        if (!VerificarSesion(null)) return null;
        try { return RepositorioCampus.EstadisticasDelMes(hoy); }
        catch (Exception e)
        {
            Debug.LogError("Estadísticas: " + e.Message);
            return null;
        }
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
            Debug.LogError("Admin: " + e);   // el detalle técnico solo va al log
            alTerminar?.Invoke(false, "No se pudo guardar. Intenta de nuevo.");
            return;
        }

        if (servicio != null) servicio.Recargar();
        alTerminar?.Invoke(true, respuesta);
    }
}
