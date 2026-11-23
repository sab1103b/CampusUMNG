using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
///    OJO: al instalar una versión nueva de la app, las ediciones hechas en el celular con el modo
///    administrador (lugares, servicios, red, modelos) se REEMPLAZAN por las de la maestra; solo se
///    conservan las cuentas de administrador y las búsquedas (ver BaseDatosLocal.Instalacion.cs).
///
/// Partes: este archivo (apertura, esquema, migraciones, semilla) y BaseDatosLocal.Instalacion.cs
/// (copia desde el APK y paso de los datos del celular).
///
/// Si no existe ninguna base, se crea con las tablas y (solo en el Editor) se llena con
/// Assets/BaseDatos/semilla.sql.
/// </summary>
public static partial class BaseDatosLocal
{
    public const string NombreArchivo = "campus.db";

    private static SQLiteConnection conexion;
    private static bool preparando;
    private static int turnoPreparacion;   // sube al cancelar: el finally de una preparación vieja no toca la nueva

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
        // Si otro script ya la está preparando, solo espera (si esa preparación se cancela, esta la retoma)
        while (preparando && conexion == null) yield return null;
        if (conexion != null) yield break;

        preparando = true;
        int turno = ++turnoPreparacion;
        try
        {
            if (!Application.isEditor)
                yield return CopiarDesdeApkSiHaceFalta();

            if (turno == turnoPreparacion) AbrirSincrono();
        }
        finally
        {
            // Solo si sigue siendo la preparación vigente (si una corrutina detenida se recolecta tarde, no
            // apaga la marca de otra que ya empezó)
            if (turno == turnoPreparacion) preparando = false;
        }
    }

    /// <summary>
    /// Llamar si la corrutina que ejecutaba Preparar se detuvo (objeto desactivado o destruido): una corrutina
    /// detenida nunca llega a su finally y 'preparando' quedaría en true para siempre. La siguiente llamada a
    /// Preparar vuelve a empezar (la copia desde el APK es idempotente: usa un archivo temporal).
    /// </summary>
    public static void CancelarPreparacion()
    {
        if (!preparando || conexion != null) return;
        preparando = false;
        turnoPreparacion++;
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
                Registro.Info($"Base de datos creada desde semilla.sql ({n} instrucciones): {ruta}");
            }
            catch (Exception e)
            {
                // La transacción se revierte: la base queda vacía y se reintenta en el próximo Play
                Debug.LogError($"No se pudo cargar semilla.sql: {e.Message}\n{e.StackTrace}");
            }
        }
        else if (esNueva)
        {
            Registro.Info($"Base de datos creada vacía: {ruta}");
        }

#if UNITY_EDITOR
        // Primera vez: 5 espacios de administrador con su código de registro
        try { RepositorioCampus.AsegurarEspaciosIniciales(); }
        catch (Exception e) { Debug.LogError("Espacios de administrador: " + e.Message); }

        // Completa la huella de los códigos migrados a PBKDF2 (una sola vez; sin pendientes no hace nada)
        try { RepositorioCampus.CompletarHuellasMigradas(); }
        catch (Exception e) { Debug.LogWarning("Códigos migrados de administrador: " + e.Message); }

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


    /// <summary>
    /// Sube PRAGMA user_version (versión de los DATOS). Solo en el Editor: así el próximo APK sabe que
    /// trae datos nuevos y reemplaza la copia del celular.
    /// La versión nunca baja: es la mayor entre (actual + 1) y la marca de tiempo yyyyMMddHH
    /// (p. ej. 2026112218, cabe en int32 hasta el año 2147). Por eso reconstruir la base desde la
    /// semilla tampoco la reinicia: los celulares siempre reciben la base nueva.
    /// </summary>
    public static void MarcarCambio()
    {
        if (!Application.isEditor || conexion == null) return;
        int v = conexion.ExecuteScalar<int>("PRAGMA user_version");
        FijarVersionDatos(conexion, SiguienteVersion(v));
    }

    /// <summary>Siguiente versión de datos: mayor que 'actual' y al menos la marca de tiempo actual.</summary>
    public static int SiguienteVersion(int actual)
    {
        long marca = long.Parse(DateTime.Now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        long siguiente = Math.Max((long)actual + 1, marca);
        return (int)Math.Min(siguiente, int.MaxValue);
    }

    // PRAGMA no acepta parámetros: el número se escribe en el texto (es un int, no viene del usuario)
    private static void FijarVersionDatos(SQLiteConnection c, int version) =>
        c.Execute("PRAGMA user_version = " + version.ToString(CultureInfo.InvariantCulture));

    // ------------------------------------------------------------------
    // Esquema (equivalente a schema.sql + red_caminos.sql + servicios.sql)
    // Los CHECK de largo (descripción ≤ 500, indicación ≤ 300, horario ≤ 120, piso ≤ 20) son los mismos
    // límites que validan RepositorioCampus y el prebuild; solo aplican a bases NUEVAS (CREATE IF NOT
    // EXISTS no altera tablas existentes; SQLite no permite agregar un CHECK a una tabla ya creada).
    // length(NULL) es NULL: un campo vacío (NULL) pasa el CHECK.
    // ------------------------------------------------------------------
    /// <summary>Largo máximo de los textos de lugares y servicios (mismos límites que el CHECK y RepositorioCampus).</summary>
    public const int MaxDescripcion = 500, MaxIndicacion = 300, MaxHorario = 120, MaxPiso = 20;

    private static readonly string[] Esquema =
    {
        @"CREATE TABLE IF NOT EXISTS lugares (
            id          INTEGER PRIMARY KEY,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT NOT NULL CHECK (length(categoria) BETWEEN 1 AND 50),
            descripcion TEXT CHECK (length(descripcion) <= 500),
            indicacion  TEXT CHECK (length(indicacion) <= 300),
            horario     TEXT CHECK (length(horario) <= 120),
            latitud     REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud    REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180)
        )",
        TablaAdministradores,
        @"CREATE TABLE IF NOT EXISTS busquedas_log (
            id         INTEGER PRIMARY KEY,
            lugar_id   INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            fecha_hora TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        )",
        // idx_busquedas_lugar_fecha (lugar_id, fecha_hora) lo crea la migración 3 (ver Migrar)
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
        // (nodo_a ya está cubierto por el índice de UNIQUE (nodo_a, nodo_b); idx_conexiones_a sobraba)
        "CREATE INDEX IF NOT EXISTS idx_conexiones_b ON conexiones(nodo_b)",
        @"CREATE TABLE IF NOT EXISTS servicios (
            id          INTEGER PRIMARY KEY,
            lugar_id    INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT,
            piso        TEXT CHECK (length(piso) <= 20),
            descripcion TEXT CHECK (length(descripcion) <= 500),
            indicacion  TEXT CHECK (length(indicacion) <= 300),
            horario     TEXT CHECK (length(horario) <= 120)
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
        // Datos de mantenimiento: versión del esquema y marcas de migraciones
        @"CREATE TABLE IF NOT EXISTS meta (
            clave TEXT PRIMARY KEY,
            valor TEXT
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

    // Una cuenta ya registrada no se puede modificar: ni usuario ni llave.
    //  - Tampoco su codigo_hash: el espacio 1 conserva el hash de código viejo (SHA-256) porque se
    //    registró antes de PBKDF2. Es intencional y compatible: SeguridadLlaves.VerificarCodigo acepta
    //    los dos formatos, y ese código ya está usado.
    //  - NO impide DELETE (intencional): el dueño quita el acceso desde el Editor
    //    (Campus > Base de datos > Administradores); la app no tiene ninguna opción para borrar cuentas.
    private const string TriggerAdminInmutable =
        @"CREATE TRIGGER IF NOT EXISTS admin_inmutable
          BEFORE UPDATE ON administradores
          WHEN OLD.nickname IS NOT NULL
          BEGIN
              SELECT RAISE(ABORT, 'La cuenta de administrador ya está registrada y no se puede modificar');
          END";

    // Usuario único sin distinguir mayúsculas (migración 4). NOCASE solo pliega ASCII, igual que lower():
    // por eso el registro solo admite usuarios ASCII (RepositorioCampus.ValidarRegistro).
    private const string IndiceNicknameSinMayusculas =
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_admin_nick_ci ON administradores(nickname COLLATE NOCASE)";

    private class InfoColumna { public string name { get; set; } }
    private class AdminViejo
    {
        public string nickname { get; set; }
        public string llave_hash { get; set; }
    }

    /// <summary>
    /// Versión del esquema guardada en meta.version_esquema (no confundir con PRAGMA user_version = versión de datos).
    /// Historial: 1 = tablas originales · 2 = administradores con código de registro (MigrarAdministradores)
    ///            3 = índice compuesto busquedas_log(lugar_id, fecha_hora) para los reportes por mes.
    ///            4 = índice único idx_admin_nick_ci (nickname COLLATE NOCASE): "Ana" y "ana" no pueden
    ///                coexistir ni aunque dos registros lleguen a la vez. El índice NO lo crea la migración:
    ///                lo intenta CrearTablas en cada apertura (tolerante), así una base vieja con duplicados
    ///                lo recibe en cuanto el dueño los resuelva; mientras tanto vale la revisión con lower().
    /// Para un cambio nuevo: sube este número y agrega un bloque "if (v &lt; N)" en Migrar.
    /// </summary>
    public const int VersionEsquema = 4;

    private static void CrearTablas(SQLiteConnection c)
    {
        foreach (string sql in Esquema)
        {
            // Un índice que no se pueda crear (p. ej. único con duplicados en una base vieja) no debe impedir abrir la app
            if (sql.TrimStart().StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) ||
                sql.TrimStart().StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
            {
                try { c.Execute(sql); }
                catch (Exception e) { Debug.LogWarning("No se pudo crear un índice: " + e.Message); }
            }
            else c.Execute(sql);
        }
        MigrarAdministradores(c);
        c.Execute(TriggerAdminInmutable);
        // Después de MigrarAdministradores (que reconstruye la tabla). Se intenta en cada apertura: IF NOT EXISTS
        // lo hace gratis cuando ya existe, y si hay usuarios repetidos solo se avisa (la app abre igual).
        try { c.Execute(IndiceNicknameSinMayusculas); }
        catch (Exception e) { Debug.LogWarning("No se pudo crear el índice único de usuarios (¿usuarios repetidos?): " + e.Message); }
        Migrar(c);
    }

    private static int LeerVersionEsquema(SQLiteConnection c)
    {
        string valor = c.ExecuteScalar<string>("SELECT valor FROM meta WHERE clave = 'version_esquema'");
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }

    /// <summary>
    /// Migraciones por versión de esquema (meta.version_esquema). Todas van en UNA transacción junto con
    /// la nueva versión: o queda todo aplicado o nada (y se reintenta la próxima vez que se abra).
    /// Si la base ya está al día no escribe nada (abrir la base no modifica el archivo).
    /// Nunca baja la versión (una app vieja que abra una base más nueva no la toca).
    /// </summary>
    private static void Migrar(SQLiteConnection c)
    {
        int v;
        try { v = LeerVersionEsquema(c); }
        catch (Exception e) { Debug.LogWarning("No se pudo leer la versión del esquema: " + e.Message); return; }
        if (v >= VersionEsquema) return;

        try
        {
            c.RunInTransaction(() =>
            {
                // v < 2: el formato de administradores lo resuelve MigrarAdministradores (detecta la columna)
                if (v < 3)
                {
                    // Los reportes filtran por lugar y por rango de fechas; el compuesto también cubre lugar_id
                    c.Execute("CREATE INDEX IF NOT EXISTS idx_busquedas_lugar_fecha ON busquedas_log(lugar_id, fecha_hora)");
                    c.Execute("DROP INDEX IF EXISTS idx_busquedas_lugar");
                }
                // v < 4: el índice idx_admin_nick_ci lo intenta CrearTablas en cada apertura (no depende de
                // esta marca: si falla por duplicados, se vuelve a intentar la próxima vez)
                c.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES ('version_esquema', ?)",
                          VersionEsquema.ToString(CultureInfo.InvariantCulture));
            });
            Registro.Info($"Esquema de la base actualizado de la versión {v} a la {VersionEsquema}.");
        }
        catch (Exception e)
        {
            // La transacción se revierte; la app sigue funcionando con el esquema anterior
            Debug.LogWarning($"No se pudo actualizar el esquema a la versión {VersionEsquema}: {e.Message}");
        }
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
        Registro.Info("Tabla administradores actualizada al formato con códigos de registro.");
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
    /// <summary>
    /// Borra la base maestra y la vuelve a crear desde semilla.sql (solo Editor).
    /// La versión de datos queda MAYOR que la de la base borrada, para que los celulares la reciban.
    /// </summary>
    public static void ReconstruirDesdeSemilla()
    {
        Cerrar();
        int vieja = File.Exists(RutaMaestra) ? LeerVersion(RutaMaestra) : 0;
        if (File.Exists(RutaMaestra)) File.Delete(RutaMaestra);
        AbrirSincrono();

        // Solo si la semilla entró: una base vacía debe quedar en versión 0 para reintentar la siembra
        if (conexion != null && conexion.ExecuteScalar<int>("SELECT COUNT(*) FROM lugares") > 0 &&
            conexion.ExecuteScalar<int>("PRAGMA user_version") <= vieja)
            FijarVersionDatos(conexion, SiguienteVersion(vieja));
    }
#endif
}
