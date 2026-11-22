using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SQLite4Unity3d;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Base de datos SQLite embebida en la app (reemplaza PostgreSQL + API de Python).
///
/// Dónde vive el archivo:
///  - En el Editor: Assets/StreamingAssets/campus.db  → es la base "maestra" del proyecto.
///    Lo que edites en Play (modo administrador) o con el menú Campus queda guardado ahí
///    y viaja dentro del APK al compilar.
///  - En Android: la primera vez se copia del APK a Application.persistentDataPath y desde
///    ahí se lee y se edita. Si compilas un APK con una base maestra más nueva
///    (PRAGMA user_version mayor), la copia del celular se reemplaza.
///
/// Si no existe ninguna base, se crea con las tablas y (solo en el Editor) se llena con
/// Assets/BaseDatos/semilla.sql.
/// </summary>
public static class BaseDatosLocal
{
    public const string NombreArchivo = "campus.db";

    private static SQLiteConnection conexion;
    private static bool preparando;

    /// <summary>Verdadero cuando la conexión está abierta y lista para consultas.</summary>
    public static bool Lista => conexion != null;

    /// <summary>Ruta del archivo que usa la app en este dispositivo.</summary>
    public static string RutaActiva =>
        Application.isEditor ? RutaMaestra : Path.Combine(Application.persistentDataPath, NombreArchivo);

    /// <summary>Base maestra dentro del proyecto (la que se empaqueta en el APK).</summary>
    public static string RutaMaestra => Path.Combine(Application.streamingAssetsPath, NombreArchivo);

    /// <summary>Archivo SQL con los datos iniciales (solo se lee en el Editor).</summary>
    public static string RutaSemilla => Path.Combine(Application.dataPath, "BaseDatos", "semilla.sql");

    /// <summary>Conexión abierta. Lanza excepción si todavía no se llamó Preparar.</summary>
    public static SQLiteConnection Conexion
    {
        get
        {
            if (conexion == null)
            {
                if (Application.isEditor) AbrirSincrono();   // en el Editor no hay que copiar nada
                else throw new InvalidOperationException("La base de datos local aún no está lista.");
            }
            return conexion;
        }
    }

    // ------------------------------------------------------------------
    // Apertura
    // ------------------------------------------------------------------

    /// <summary>
    /// Deja la base lista. En Android puede tardar unos milisegundos la primera vez
    /// (copia el archivo desde el APK), por eso es una corrutina.
    /// </summary>
    public static IEnumerator Preparar()
    {
        if (conexion != null) yield break;

        // Si otro script ya la está preparando, solo espera
        if (preparando)
        {
            while (preparando) yield return null;
            yield break;
        }

        preparando = true;
        try
        {
            if (!Application.isEditor)
                yield return CopiarDesdeApkSiHaceFalta();

            AbrirSincrono();
        }
        finally
        {
            preparando = false;
        }
    }

    /// <summary>Abre (o crea) la base en RutaActiva. Usado por el Editor y tras la copia en Android.</summary>
    public static void AbrirSincrono()
    {
        if (conexion != null) return;

        string ruta = RutaActiva;
        bool esNueva = !File.Exists(ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta));

        conexion = new SQLiteConnection(ruta, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
        conexion.Execute("PRAGMA foreign_keys = ON");   // activa ON DELETE CASCADE / SET NULL

        CrearTablas(conexion);

        // Se siembra si es nueva o si quedó vacía porque una siembra anterior falló
        if ((esNueva || NuncaSembrada(conexion)) && Application.isEditor && File.Exists(RutaSemilla))
        {
            try
            {
                int n = EjecutarScript(conexion, File.ReadAllText(RutaSemilla, Encoding.UTF8));
                MarcarCambio();
                Debug.Log($"Base de datos creada desde semilla.sql ({n} instrucciones): {ruta}");
            }
            catch (Exception e)
            {
                // La transacción se revierte: la base queda vacía y se reintenta en el próximo Play
                Debug.LogError($"No se pudo cargar semilla.sql: {e.Message}\n{e.StackTrace}");
            }
        }
        else if (esNueva)
        {
            Debug.Log($"Base de datos creada vacía: {ruta}");
        }

#if UNITY_EDITOR
        // Primera vez: 5 espacios de administrador con su código de registro
        try { RepositorioCampus.AsegurarEspaciosIniciales(); }
        catch (Exception e) { Debug.LogError("Espacios de administrador: " + e.Message); }


        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Cerrar;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Cerrar;
#endif
        Application.quitting -= Cerrar;
        Application.quitting += Cerrar;
    }

    // Versión 0 y sin datos = nunca se cargó la semilla (o la carga falló)
    private static bool NuncaSembrada(SQLiteConnection c)
    {
        if (c.ExecuteScalar<int>("PRAGMA user_version") != 0) return false;
        return c.ExecuteScalar<int>(
            "SELECT (SELECT COUNT(*) FROM lugares) + (SELECT COUNT(*) FROM nodos) + (SELECT COUNT(*) FROM administradores)") == 0;
    }

    public static void Cerrar()
    {
        if (conexion == null) return;
        conexion.Close();
        conexion = null;
    }

    // ------------------------------------------------------------------
    // Copia desde el APK (Android)
    // ------------------------------------------------------------------
    private static IEnumerator CopiarDesdeApkSiHaceFalta()
    {
        string destino = RutaActiva;
        string temporal = Path.Combine(Application.temporaryCachePath, "campus_apk.db");

        // En Android StreamingAssets está dentro del APK: solo se puede leer con UnityWebRequest
        string origen = RutaMaestra;
        if (!origen.Contains("://")) origen = "file://" + origen;

        using (UnityWebRequest req = UnityWebRequest.Get(origen))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"No hay base de datos dentro del APK ({req.error}). Se usará la local o una vacía.");
                yield break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(temporal));
            File.WriteAllBytes(temporal, req.downloadHandler.data);
        }

        if (!File.Exists(destino))
        {
            File.Copy(temporal, destino);
            Debug.Log("Base de datos copiada desde el APK.");
        }
        else if (LeerVersion(temporal) > LeerVersion(destino))
        {
            ConservarDatosDelCelular(temporal, destino);
            File.Copy(temporal, destino, true);
            Debug.Log("Base de datos actualizada con la versión del APK (más nueva).");
        }

        File.Delete(temporal);
    }

    /// <summary>
    /// Antes de reemplazar la base del celular por la del APK nuevo, pasa a la nueva lo que
    /// solo existe en el celular: las cuentas de administrador registradas ahí (por su código)
    /// y las búsquedas hechas después de la última búsqueda que trae el APK.
    /// </summary>
    private static void ConservarDatosDelCelular(string baseNueva, string baseCelular)
    {
        try
        {
            using (var c = new SQLiteConnection(baseNueva, SQLiteOpenFlags.ReadWrite))
            {
                CrearTablas(c);   // por si el APK trae una base de formato anterior
                c.Execute("ATTACH DATABASE ? AS viejo", baseCelular);
                try
                {
                    c.Execute(
                        @"UPDATE administradores SET
                              nickname      = (SELECT v.nickname      FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash),
                              llave_hash    = (SELECT v.llave_hash    FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash),
                              registrado_en = (SELECT v.registrado_en FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash)
                          WHERE nickname IS NULL
                            AND codigo_hash IN (SELECT codigo_hash FROM viejo.administradores WHERE nickname IS NOT NULL)");
                }
                catch (Exception e) { Debug.LogWarning("No se pudieron conservar las cuentas del celular: " + e.Message); }

                try
                {
                    c.Execute(
                        @"INSERT INTO busquedas_log (lugar_id, fecha_hora)
                          SELECT lugar_id, fecha_hora FROM viejo.busquedas_log
                          WHERE lugar_id IN (SELECT id FROM lugares)
                            AND fecha_hora > (SELECT COALESCE(MAX(fecha_hora), '') FROM busquedas_log)");
                }
                catch (Exception e) { Debug.LogWarning("No se pudieron conservar las búsquedas del celular: " + e.Message); }

                c.Execute("DETACH DATABASE viejo");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("No se pudieron conservar los datos del celular: " + e.Message);
        }
    }

    private static int LeerVersion(string ruta)
    {
        try
        {
            using (var c = new SQLiteConnection(ruta, SQLiteOpenFlags.ReadOnly))
                return c.ExecuteScalar<int>("PRAGMA user_version");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"No se pudo leer la versión de {ruta}: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Sube PRAGMA user_version. Solo en el Editor: así el próximo APK sabe que trae datos
    /// nuevos y reemplaza la copia del celular.
    /// </summary>
    public static void MarcarCambio()
    {
        if (!Application.isEditor || conexion == null) return;
        int v = conexion.ExecuteScalar<int>("PRAGMA user_version");
        conexion.Execute($"PRAGMA user_version = {v + 1}");
    }

    // ------------------------------------------------------------------
    // Esquema (equivalente a schema.sql + red_caminos.sql + servicios.sql)
    // ------------------------------------------------------------------
    private static readonly string[] Esquema =
    {
        @"CREATE TABLE IF NOT EXISTS lugares (
            id          INTEGER PRIMARY KEY,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT NOT NULL CHECK (length(categoria) BETWEEN 1 AND 50),
            descripcion TEXT,
            indicacion  TEXT,
            horario     TEXT,
            latitud     REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud    REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180)
        )",
        TablaAdministradores,
        @"CREATE TABLE IF NOT EXISTS busquedas_log (
            id         INTEGER PRIMARY KEY,
            lugar_id   INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            fecha_hora TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        )",
        "CREATE INDEX IF NOT EXISTS idx_busquedas_lugar ON busquedas_log(lugar_id)",
        "CREATE INDEX IF NOT EXISTS idx_busquedas_fecha ON busquedas_log(fecha_hora)",
        @"CREATE TABLE IF NOT EXISTS nodos (
            id        INTEGER PRIMARY KEY,
            nombre    TEXT,
            latitud   REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud  REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180),
            lugar_id  INTEGER REFERENCES lugares(id) ON DELETE SET NULL
        )",
        "CREATE INDEX IF NOT EXISTS idx_nodos_lugar ON nodos(lugar_id)",
        @"CREATE TABLE IF NOT EXISTS conexiones (
            id      INTEGER PRIMARY KEY,
            nodo_a  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
            nodo_b  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
            CHECK (nodo_a < nodo_b),
            UNIQUE (nodo_a, nodo_b)
        )",
        "CREATE INDEX IF NOT EXISTS idx_conexiones_a ON conexiones(nodo_a)",
        "CREATE INDEX IF NOT EXISTS idx_conexiones_b ON conexiones(nodo_b)",
        @"CREATE TABLE IF NOT EXISTS servicios (
            id          INTEGER PRIMARY KEY,
            lugar_id    INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT,
            piso        TEXT,
            descripcion TEXT,
            indicacion  TEXT,
            horario     TEXT
        )",
        "CREATE INDEX IF NOT EXISTS idx_servicios_lugar ON servicios(lugar_id)",
        // Edificaciones 3D agregadas por el administrador (medidas en metros, rotación en grados)
        @"CREATE TABLE IF NOT EXISTS modelos (
            id        INTEGER PRIMARY KEY,
            tipo      TEXT NOT NULL CHECK (tipo IN ('cubo', 'rectangulo', 'cilindro')),
            nombre    TEXT,
            latitud   REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud  REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180),
            rotacion  REAL NOT NULL DEFAULT 0,
            ancho     REAL NOT NULL DEFAULT 12 CHECK (ancho BETWEEN 1 AND 300),
            largo     REAL NOT NULL DEFAULT 12 CHECK (largo BETWEEN 1 AND 300),
            alto      REAL NOT NULL DEFAULT 10 CHECK (alto BETWEEN 1 AND 200)
        )",
    };

    /// <summary>
    /// Cuentas de administrador. Cada fila es un "espacio" con un código de registro único
    /// (solo se guarda su hash). Mientras nickname y llave_hash están vacíos, el espacio está libre;
    /// al registrarse con el código se llenan y desde ahí no se pueden modificar (trigger).
    /// </summary>
    private const string TablaAdministradores =
        @"CREATE TABLE IF NOT EXISTS administradores (
            id            INTEGER PRIMARY KEY,
            codigo_hash   TEXT UNIQUE NOT NULL,
            nickname      TEXT UNIQUE,
            llave_hash    TEXT,
            registrado_en TEXT
        )";

    // Una cuenta ya registrada no se puede modificar: ni usuario ni llave
    private const string TriggerAdminInmutable =
        @"CREATE TRIGGER IF NOT EXISTS admin_inmutable
          BEFORE UPDATE ON administradores
          WHEN OLD.nickname IS NOT NULL
          BEGIN
              SELECT RAISE(ABORT, 'La cuenta de administrador ya está registrada y no se puede modificar');
          END";

    private class InfoColumna { public string name { get; set; } }
    private class AdminViejo
    {
        public string nickname { get; set; }
        public string llave_hash { get; set; }
    }

    private static void CrearTablas(SQLiteConnection c)
    {
        foreach (string sql in Esquema) c.Execute(sql);
        MigrarAdministradores(c);
        c.Execute(TriggerAdminInmutable);
    }

    /// <summary>
    /// Bases creadas antes de los códigos de registro: la tabla administradores no tiene
    /// codigo_hash. Se reconstruye conservando las cuentas existentes (quedan registradas).
    /// </summary>
    private static void MigrarAdministradores(SQLiteConnection c)
    {
        List<InfoColumna> columnas = c.Query<InfoColumna>("PRAGMA table_info(administradores)");
        if (columnas.Exists(x => x.name == "codigo_hash")) return;

        c.RunInTransaction(() =>
        {
            List<AdminViejo> viejos = c.Query<AdminViejo>("SELECT nickname, llave_hash FROM administradores");
            c.Execute("ALTER TABLE administradores RENAME TO administradores_viejo");
            c.Execute(TablaAdministradores);
            foreach (AdminViejo a in viejos)
            {
                // Las cuentas antiguas reciben un código aleatorio que nadie conoce (ya están registradas)
                c.Execute(
                    @"INSERT INTO administradores (codigo_hash, nickname, llave_hash, registrado_en)
                      VALUES (?, ?, ?, datetime('now', 'localtime'))",
                    SeguridadLlaves.HashCodigo(SeguridadLlaves.GenerarCodigo()), a.nickname, a.llave_hash);
            }
            c.Execute("DROP TABLE administradores_viejo");
        });
        Debug.Log("Tabla administradores actualizada al formato con códigos de registro.");
    }

    // ------------------------------------------------------------------
    // Scripts SQL (semilla)
    // ------------------------------------------------------------------

    /// <summary>
    /// Ejecuta un archivo SQL con varias instrucciones separadas por ';'.
    /// Ignora comentarios '--' y las instrucciones propias de PostgreSQL (setval, \c, \i).
    /// Todo va en una transacción: o entra completo o no entra nada.
    /// </summary>
    public static int EjecutarScript(SQLiteConnection c, string sql)
    {
        List<string> instrucciones = DividirScript(sql);
        int ejecutadas = 0;

        c.RunInTransaction(() =>
        {
            foreach (string inst in instrucciones)
            {
                string t = inst.TrimStart().ToLowerInvariant();
                if (t.StartsWith("select setval") || t.StartsWith("\\")) continue;

                // El SQLite del Editor (Mapbox, v3.6) no acepta INSERT con varias filas: se insertan de a una
                foreach (string fila in SepararInsertMultiple(inst))
                {
                    try { c.Execute(fila); }
                    catch (Exception e)
                    {
                        string corta = fila.Length > 160 ? fila.Substring(0, 160) + "..." : fila;
                        throw new Exception($"{e.Message}  →  {corta}", e);
                    }
                }
                ejecutadas++;
            }
        });
        return ejecutadas;
    }

    /// <summary>
    /// "INSERT INTO t (a, b) VALUES (1, 'x'), (2, 'y')"  →  dos INSERT de una fila.
    /// Cualquier otra instrucción se devuelve tal cual.
    /// </summary>
    private static List<string> SepararInsertMultiple(string inst)
    {
        List<string> resultado = new List<string>();
        var m = System.Text.RegularExpressions.Regex.Match(inst,
            @"^\s*(INSERT\s+(?:OR\s+\w+\s+)?INTO\s+[^(]+\([^)]*\)\s*VALUES)\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) { resultado.Add(inst); return resultado; }

        string cabecera = m.Groups[1].Value;
        string resto = inst.Substring(m.Length);

        int nivel = 0;
        bool enTexto = false;
        int inicio = -1;

        for (int i = 0; i < resto.Length; i++)
        {
            char ch = resto[i];
            if (ch == '\'') { enTexto = !enTexto; continue; }
            if (enTexto) continue;

            if (ch == '(')
            {
                if (nivel == 0) inicio = i;
                nivel++;
            }
            else if (ch == ')')
            {
                nivel--;
                if (nivel == 0 && inicio >= 0)
                {
                    resultado.Add(cabecera + " " + resto.Substring(inicio, i - inicio + 1));
                    inicio = -1;
                }
            }
        }

        if (resultado.Count == 0) resultado.Add(inst);   // formato inesperado: que SQLite dé el error
        return resultado;
    }

    private static List<string> DividirScript(string sql)
    {
        List<string> lista = new List<string>();
        StringBuilder actual = new StringBuilder();
        bool enTexto = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char ch = sql[i];

            // Comentario de línea fuera de un texto
            if (!enTexto && ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                actual.Append('\n');
                continue;
            }

            if (ch == '\'') enTexto = !enTexto;   // '' dentro de un texto se alterna dos veces: queda igual

            if (ch == ';' && !enTexto)
            {
                if (actual.ToString().Trim().Length > 0) lista.Add(actual.ToString().Trim());
                actual.Clear();
                continue;
            }
            actual.Append(ch);
        }

        if (actual.ToString().Trim().Length > 0) lista.Add(actual.ToString().Trim());
        return lista;
    }

#if UNITY_EDITOR
    /// <summary>Borra la base maestra y la vuelve a crear desde semilla.sql (solo Editor).</summary>
    public static void ReconstruirDesdeSemilla()
    {
        Cerrar();
        if (File.Exists(RutaMaestra)) File.Delete(RutaMaestra);
        AbrirSincrono();
    }
#endif
}
