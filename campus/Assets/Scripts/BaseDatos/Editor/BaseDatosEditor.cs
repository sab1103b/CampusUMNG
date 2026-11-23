using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Herramientas del Editor para la base de datos local (menú Campus > Base de datos).
///
/// Administradores: cada cuenta es un "espacio" con un código de registro único.
/// Tú (dueño del proyecto) ves los códigos aquí y se los entregas a la persona; ella se registra
/// en la app con usuario + contraseña + código. Una vez registrada, nadie puede cambiar su usuario
/// ni su contraseña (la base lo impide). Solo se puede eliminar el espacio para quitarle el acceso.
/// </summary>
public class BaseDatosEditor : EditorWindow
{
    private List<RepositorioCampus.EspacioAdmin> espacios = new List<RepositorioCampus.EspacioAdmin>();
    private Dictionary<int, string> codigos = new Dictionary<int, string>();
    private bool mostrarCodigos;
    private string mensaje = "";
    private MessageType tipoMensaje = MessageType.None;
    private Vector2 scroll;

    // ------------------------------------------------------------------
    // Menús
    // ------------------------------------------------------------------
    [MenuItem("Campus/Base de datos/Administradores...", false, 1)]
    private static void AbrirVentana()
    {
        BaseDatosEditor w = GetWindow<BaseDatosEditor>(true, "Administradores", true);
        w.minSize = new Vector2(440, 420);
        w.Refrescar();
    }

    [MenuItem("Campus/Base de datos/Mostrar archivo campus.db", false, 20)]
    private static void Mostrar()
    {
        BaseDatosLocal.AbrirSincrono();   // la crea si todavía no existe
        EditorUtility.RevealInFinder(BaseDatosLocal.RutaMaestra);
        Registro.Info("Base de datos maestra: " + BaseDatosLocal.RutaMaestra);
    }

    [MenuItem("Campus/Base de datos/Validar lugares sin puerta", false, 21)]
    private static void ValidarPuertas()
    {
        int n = AvisarLugaresSinPuerta();
        if (n == 0) Registro.Info("Validación: todos los lugares tienen al menos un nodo-puerta.");
    }

    /// <summary>
    /// Borra las búsquedas de la base maestra (las que se registran al probar en Play en el Editor),
    /// para que no viajen en el APK ni salgan en los reportes. Nunca se borran solas.
    /// </summary>
    [MenuItem("Campus/Base de datos/Limpiar búsquedas de prueba", false, 22)]
    private static void LimpiarBusquedas()
    {
        try
        {
            BaseDatosLocal.AbrirSincrono();
            int n = RepositorioCampus.ContarBusquedas();
            if (n == 0)
            {
                EditorUtility.DisplayDialog("Campus", "La base maestra no tiene búsquedas registradas.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Campus",
                    $"La base maestra tiene {n} búsqueda(s) registrada(s). Normalmente son de pruebas hechas en el Editor.\n\n" +
                    "Se borrarán TODAS (los lugares, servicios, red y cuentas no se tocan). Esta acción no se puede deshacer.",
                    "Borrar búsquedas", "Cancelar"))
                return;

            int borradas = RepositorioCampus.BorrarBusquedas();
            Registro.Info($"Se borraron {borradas} búsquedas de la base maestra.");
            EditorUtility.DisplayDialog("Campus", $"Se borraron {borradas} búsqueda(s).", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Limpiar búsquedas: " + e);
            EditorUtility.DisplayDialog("Campus", "No se pudieron borrar las búsquedas:\n" + e.Message, "OK");
        }
    }

    /// <summary>
    /// Limpia las filas huérfanas de la base maestra (las mismas reglas que revisa el prebuild y que aplica la
    /// migración): espacios interiores y búsquedas sin lugar se borran, nodos con un lugar inexistente quedan
    /// como nodos de paso y las conexiones con nodos inexistentes se borran.
    /// </summary>
    [MenuItem("Campus/Base de datos/Limpiar datos huérfanos", false, 23)]
    private static void LimpiarHuerfanos()
    {
        try
        {
            BaseDatosLocal.AbrirSincrono();
            var c = BaseDatosLocal.Conexion;
            int n = BaseDatosLocal.ContarHuerfanos(c, out string detalle);
            if (n == 0)
            {
                EditorUtility.DisplayDialog("Campus", "La base maestra no tiene datos huérfanos.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Campus",
                    $"La base maestra tiene {n} fila(s) huérfana(s):{detalle}\n\nEsta acción no se puede deshacer.",
                    "Limpiar", "Cancelar"))
                return;

            int limpiadas = 0;
            c.RunInTransaction(() => limpiadas = BaseDatosLocal.LimpiarHuerfanos(c));
            BaseDatosLocal.MarcarCambio();   // los celulares deben recibir la base corregida
            EditorUtility.DisplayDialog("Campus", $"Se limpiaron {limpiadas} fila(s) huérfana(s).", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Limpiar datos huérfanos: " + e);
            EditorUtility.DisplayDialog("Campus", "No se pudieron limpiar los datos huérfanos:\n" + e.Message, "OK");
        }
    }

    /// <summary>Advierte en consola (sin borrar nada) si la base maestra trae búsquedas. Requiere la base abierta.</summary>
    public static int AvisarBusquedasEnMaestra()
    {
        try
        {
            int n = RepositorioCampus.ContarBusquedas();
            if (n > 0)
                Debug.LogWarning($"La base maestra tiene {n} búsqueda(s) registrada(s) (probablemente de pruebas en el Editor): " +
                                 "viajarán en el APK y contarán en los reportes. Si son de prueba, bórralas con " +
                                 "Campus > Base de datos > Limpiar búsquedas de prueba y vuelve a compilar.");
            return n;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("No se pudo revisar las búsquedas de la base maestra: " + e.Message);
            return 0;
        }
    }

    /// <summary>
    /// Escribe en consola (solo advertencia) los lugares sin nodo-puerta: su ruta termina en el punto
    /// más cercano del camino. Devuelve cuántos hay. Requiere la base abierta.
    /// </summary>
    public static int AvisarLugaresSinPuerta()
    {
        try
        {
            var lista = RepositorioCampus.LugaresSinPuerta();
            if (lista.Count == 0) return 0;
            var lineas = lista.ConvertAll(l => $"  #{l.id} {l.nombre}");
            Debug.LogWarning($"{lista.Count} lugares sin puerta (sin nodo de entrada en la red de caminos); " +
                             "la ruta llega al punto más cercano del camino:\n" + string.Join("\n", lineas));
            return lista.Count;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("No se pudo validar los lugares sin puerta: " + e.Message);
            return 0;
        }
    }

    [MenuItem("Campus/Base de datos/Reconstruir desde semilla.sql", false, 40)]
    private static void Reconstruir()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Campus", "Detén el modo Play antes de reconstruir la base.", "OK");
            return;
        }
        if (!File.Exists(BaseDatosLocal.RutaSemilla))
        {
            EditorUtility.DisplayDialog("Campus", "No existe el archivo:\n" + BaseDatosLocal.RutaSemilla, "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Campus",
                "Se BORRARÁ la base actual (lugares, servicios, red, búsquedas y cuentas de administrador) " +
                "y se volverá a crear con Assets/BaseDatos/semilla.sql.\n\n" +
                "Los espacios de administrador se pierden; se crean 5 nuevos con códigos nuevos.\n\n" +
                "Esta acción no se puede deshacer.",
                "Borrar y reconstruir", "Cancelar"))
            return;

        try
        {
            // Los códigos viejos ya no sirven: se archiva el archivo para que se generen 5 nuevos
            if (File.Exists(RepositorioCampus.RutaCodigos))
                File.Move(RepositorioCampus.RutaCodigos,
                          RepositorioCampus.RutaCodigos.Replace(".txt", "_anterior_" +
                              System.DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt"));

            BaseDatosLocal.ReconstruirDesdeSemilla();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Campus",
                "Base reconstruida.\n\nLos códigos de registro nuevos están en Campus > Base de datos > Administradores.", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Reconstruir base: " + e);
            EditorUtility.DisplayDialog("Campus", "Error al reconstruir:\n" + e.Message, "OK");
        }
    }

    // ------------------------------------------------------------------
    // Ventana de administradores
    // ------------------------------------------------------------------
    private void Refrescar()
    {
        try
        {
            espacios = RepositorioCampus.ListarEspaciosAdmin();
            codigos = RepositorioCampus.LeerCodigos();
        }
        catch (System.Exception e) { Avisar("No se pudo leer la base: " + e.Message, MessageType.Error); }
    }

    private void Avisar(string texto, MessageType tipo) { mensaje = texto; tipoMensaje = tipo; }

    private void OnFocus() => Refrescar();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Entrega a cada persona el código de un espacio LIBRE. En la app, en Registro, escribe usuario, " +
            "contraseña y ese código. Después nadie puede cambiar ese usuario ni su contraseña.",
            MessageType.Info);

        int libres = espacios.FindAll(e => e.Libre).Count;
        EditorGUILayout.LabelField($"Espacios: {espacios.Count}   ·   Libres: {libres}   ·   Registrados: {espacios.Count - libres}",
                                   EditorStyles.boldLabel);

        mostrarCodigos = EditorGUILayout.ToggleLeft("Mostrar códigos de los espacios libres", mostrarCodigos);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (RepositorioCampus.EspacioAdmin e in espacios.ToArray())
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"#{e.id}", GUILayout.Width(36));

            if (e.Libre)
            {
                string codigo = codigos.TryGetValue(e.id, out string c) ? c : "(código no disponible)";
                EditorGUILayout.LabelField("LIBRE", GUILayout.Width(70));
                if (mostrarCodigos) EditorGUILayout.SelectableLabel(codigo, GUILayout.Height(18));
                else EditorGUILayout.LabelField("•••••-•••••");

                GUI.enabled = mostrarCodigos && codigos.ContainsKey(e.id);
                if (GUILayout.Button("Copiar", GUILayout.Width(60)))
                {
                    EditorGUIUtility.systemCopyBuffer = codigo;
                    Avisar($"Código del espacio #{e.id} copiado.", MessageType.Info);
                }
                GUI.enabled = true;
            }
            else
            {
                EditorGUILayout.LabelField("REGISTRADO", GUILayout.Width(90));
                EditorGUILayout.LabelField($"{e.nickname}   ({e.registradoEn})");
            }

            string accion = e.Libre ? "Eliminar" : "Quitar acceso";
            if (GUILayout.Button(accion, GUILayout.Width(95)) &&
                EditorUtility.DisplayDialog("Campus",
                    e.Libre
                        ? $"¿Eliminar el espacio libre #{e.id}? Su código dejará de funcionar."
                        : $"¿Quitar el acceso a '{e.nickname}'? Se elimina su cuenta y no podrá volver a entrar.",
                    accion, "Cancelar"))
            {
                try { RepositorioCampus.EliminarEspacioAdmin(e.id); Avisar($"Espacio #{e.id} eliminado.", MessageType.Info); }
                catch (System.Exception ex) { Avisar(ex.Message, MessageType.Error); }
                Refrescar();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Agregar 5 espacios", GUILayout.Height(26)))
        {
            try
            {
                var nuevos = RepositorioCampus.CrearEspaciosAdmin(5);
                Avisar($"Se crearon {nuevos.Count} espacios nuevos.", MessageType.Info);
            }
            catch (System.Exception ex) { Avisar(ex.Message, MessageType.Error); }
            Refrescar();
        }
        if (GUILayout.Button("Abrir archivo de códigos", GUILayout.Height(26)))
        {
            if (File.Exists(RepositorioCampus.RutaCodigos)) EditorUtility.RevealInFinder(RepositorioCampus.RutaCodigos);
            else Avisar("Todavía no hay archivo de códigos.", MessageType.Warning);
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(mensaje)) EditorGUILayout.HelpBox(mensaje, tipoMensaje);

        EditorGUILayout.LabelField(
            "Los códigos se guardan en CodigosRegistroAdmin.txt (fuera de Assets: no viaja en el APK ni se sube a git). " +
            "En la base solo queda su huella (hash).", EditorStyles.wordWrappedMiniLabel);
    }
}

/// <summary>Antes de compilar el APK se asegura de que la base maestra exista y esté cerrada.</summary>
public class BaseDatosPreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // Se cierra y se vuelve a abrir: abrir aplica las migraciones pendientes (p. ej. la 6) con el SQLite
        // del Editor. Si la maestra no quedó en la versión de esquema de la app o no está sana, el build falla.
        BaseDatosLocal.Cerrar();
        BaseDatosLocal.AbrirSincrono();   // si no existe, la crea desde semilla.sql
        RevisarEsquemaEIntegridad();
        RevisarHuerfanos();                         // filas que apuntan a algo inexistente: el build falla
        int libres = RepositorioCampus.ListarEspaciosAdmin().FindAll(e => e.Libre).Count;
        int registrados = RepositorioCampus.ContarAdminsRegistrados();
        BaseDatosEditor.AvisarLugaresSinPuerta();   // solo advertencia, no detiene el build
        RevisarIndiceUsuarios();                    // idx_admin_nick_ci (usuario único sin mayúsculas)
        RevisarLargos();                            // límites de texto de lugares y servicios
        RevisarBusquedasDePrueba();                 // pregunta qué hacer si la maestra trae búsquedas
        BaseDatosLocal.Cerrar();          // libera el archivo para que se empaquete completo

        if (libres == 0 && registrados == 0)
            Debug.LogWarning("La base de datos no tiene espacios de administrador: nadie podrá registrarse ni entrar al modo administrador. " +
                             "Créalos en Campus > Base de datos > Administradores.");
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// La base que viaja en el APK debe estar migrada (meta.version_esquema ≥ BaseDatosLocal.VersionEsquema) y
    /// sana (PRAGMA quick_check = ok). Si no, BuildFailedException: un APK con la maestra a medio migrar
    /// obligaría a cada celular a migrar por su cuenta (y una base dañada no se podría instalar).
    /// </summary>
    private static void RevisarEsquemaEIntegridad()
    {
        string error = null;
        try
        {
            var c = BaseDatosLocal.Conexion;
            string valor = c.ExecuteScalar<string>("SELECT valor FROM meta WHERE clave = 'version_esquema'");
            int version = int.TryParse(valor, System.Globalization.NumberStyles.Integer,
                                       System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : 0;
            if (version < BaseDatosLocal.VersionEsquema)
                error = $"La base maestra está en la versión de esquema {version} y la app necesita la " +
                        $"{BaseDatosLocal.VersionEsquema}: la migración no se pudo aplicar (revisa la consola). " +
                        "Corrige el problema, abre la base (Campus > Base de datos > Mostrar archivo campus.db) y vuelve a compilar.";
            else
            {
                string revision = c.ExecuteScalar<string>("PRAGMA quick_check");
                if (!string.Equals(revision, "ok", System.StringComparison.OrdinalIgnoreCase))
                    error = "La base maestra no pasó PRAGMA quick_check (" + revision + "). Repárala o reconstrúyela " +
                            "desde semilla.sql antes de compilar.";
            }
        }
        catch (System.Exception e) { error = "No se pudo revisar la base maestra antes de compilar: " + e.Message; }

        if (error == null) return;
        BaseDatosLocal.Cerrar();
        throw new BuildFailedException(error);
    }

    /// <summary>
    /// Cuenta las filas huérfanas con las mismas reglas que limpia la migración (BaseDatosLocal.ContarHuerfanos).
    /// Si hay alguna, BuildFailedException: el APK no debe llevar servicios o búsquedas sin lugar, nodos con un
    /// lugar inexistente ni conexiones rotas. Se corrigen con Campus > Base de datos > Limpiar datos huérfanos.
    /// </summary>
    private static void RevisarHuerfanos()
    {
        string error = null;
        try
        {
            int n = BaseDatosLocal.ContarHuerfanos(BaseDatosLocal.Conexion, out string detalle);
            if (n > 0)
                error = $"La base maestra tiene {n} fila(s) huérfana(s):{detalle}\nCorrígelas con " +
                        "Campus > Base de datos > Limpiar datos huérfanos y vuelve a compilar.";
        }
        catch (System.Exception e) { error = "No se pudo revisar si hay datos huérfanos: " + e.Message; }

        if (error == null) return;
        BaseDatosLocal.Cerrar();
        throw new BuildFailedException(error);
    }

    /// <summary>
    /// Avisa si falta el índice único idx_admin_nick_ci (lo crea BaseDatosLocal.CrearTablas; si no existe,
    /// casi siempre es porque hay usuarios repetidos sin distinguir mayúsculas, p. ej. "Ana" y "ana").
    /// </summary>
    private static void RevisarIndiceUsuarios()
    {
        try
        {
            int n = BaseDatosLocal.Conexion.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_admin_nick_ci'");
            if (n == 0)
                Debug.LogWarning("La base maestra no tiene el índice único idx_admin_nick_ci (usuarios sin distinguir " +
                                 "mayúsculas). Probablemente hay usuarios repetidos (p. ej. \"Ana\" y \"ana\"): quita uno en " +
                                 "Campus > Base de datos > Administradores y vuelve a abrir la base para que se cree.");
        }
        catch (System.Exception e) { Debug.LogWarning("No se pudo revisar el índice de usuarios: " + e.Message); }
    }

    /// <summary>
    /// Avisa de lugares o servicios con textos más largos que los límites de la app (los mismos CHECK del
    /// esquema de bases nuevas): una base creada desde semilla.sql con esos datos no se podría sembrar, y el
    /// administrador no podría guardarlos sin recortarlos. También cuenta (solo informativo) los servicios sin
    /// piso: no se inventan, se completan cuando la universidad confirme.
    /// </summary>
    private static void RevisarLargos()
    {
        try
        {
            var c = BaseDatosLocal.Conexion;
            string lugares = c.ExecuteScalar<string>(
                "SELECT group_concat('#' || id || ' ' || nombre, ', ') FROM lugares " +
                "WHERE length(descripcion) > ? OR length(indicacion) > ? OR length(horario) > ?",
                BaseDatosLocal.MaxDescripcion, BaseDatosLocal.MaxIndicacion, BaseDatosLocal.MaxHorario);
            string servicios = c.ExecuteScalar<string>(
                "SELECT group_concat('#' || id || ' ' || nombre, ', ') FROM servicios " +
                "WHERE length(descripcion) > ? OR length(indicacion) > ? OR length(horario) > ? OR length(piso) > ?",
                BaseDatosLocal.MaxDescripcion, BaseDatosLocal.MaxIndicacion, BaseDatosLocal.MaxHorario, BaseDatosLocal.MaxPiso);
            if (!string.IsNullOrEmpty(lugares) || !string.IsNullOrEmpty(servicios))
                Debug.LogWarning($"Textos más largos que lo permitido (descripción ≤ {BaseDatosLocal.MaxDescripcion}, " +
                                 $"indicación ≤ {BaseDatosLocal.MaxIndicacion}, horario ≤ {BaseDatosLocal.MaxHorario}, " +
                                 $"piso ≤ {BaseDatosLocal.MaxPiso}). Recórtalos en la base y en semilla.sql:" +
                                 (string.IsNullOrEmpty(lugares) ? "" : "\n  Lugares: " + lugares) +
                                 (string.IsNullOrEmpty(servicios) ? "" : "\n  Servicios: " + servicios));

            int sinPiso = c.ExecuteScalar<int>("SELECT COUNT(*) FROM servicios WHERE piso IS NULL OR trim(piso) = ''");
            if (sinPiso > 0)
                Registro.Info($"{sinPiso} servicio(s) sin piso (se muestran sin piso hasta que la universidad lo confirme).");
        }
        catch (System.Exception e) { Debug.LogWarning("No se pudo revisar el largo de los textos: " + e.Message); }
    }

    /// <summary>
    /// Si la base maestra trae búsquedas (casi siempre de pruebas en Play), pregunta: Limpiar / Compilar
    /// igual / Cancelar. Importa porque viajan en el APK y cuentan en los reportes. Al actualizar la app se
    /// conservan las búsquedas del celular que no estén ya en la base nueva (mismo lugar y misma fecha_hora).
    /// Límite conocido (no se marca qué búsquedas del celular vinieron del APK anterior): si se borran
    /// búsquedas de la maestra que ya habían viajado en un APK, los celulares con esa versión las vuelven a
    /// traer al actualizar. Por eso conviene limpiar las búsquedas de prueba ANTES del primer APK.
    /// En compilaciones sin ventana (batch mode) no hay a quién preguntar: el build FALLA para que las
    /// búsquedas de prueba nunca lleguen al APK sin que alguien lo decida (límpialas y vuelve a compilar).
    /// </summary>
    private static void RevisarBusquedasDePrueba()
    {
        int n = BaseDatosEditor.AvisarBusquedasEnMaestra();
        if (n == 0) return;
        if (Application.isBatchMode)
        {
            BaseDatosLocal.Cerrar();
            throw new BuildFailedException($"La base maestra tiene {n} búsqueda(s) registrada(s). Bórralas con " +
                                           "Campus > Base de datos > Limpiar búsquedas de prueba y vuelve a compilar.");
        }

        int opcion = EditorUtility.DisplayDialogComplex("Campus",
            $"La base maestra tiene {n} búsqueda(s) registrada(s), probablemente de pruebas en el Editor.\n\n" +
            "Si se compilan, viajan en el APK y cuentan en los reportes. Al actualizar la app se conservarán " +
            "las búsquedas del celular que no estén ya en la base nueva.\n\n" +
            "Limpiar: borra todas las búsquedas de la maestra (lugares, red y cuentas no se tocan) y compila.",
            "Limpiar y compilar", "Cancelar", "Compilar igual");

        if (opcion == 1)
        {
            BaseDatosLocal.Cerrar();
            throw new BuildFailedException("Compilación cancelada: revisa las búsquedas de la base maestra.");
        }
        if (opcion == 0)
        {
            int borradas = RepositorioCampus.BorrarBusquedas();
            Registro.Info($"Antes de compilar se borraron {borradas} búsqueda(s) de la base maestra.");
        }
    }
}
