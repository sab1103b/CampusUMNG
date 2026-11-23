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

            if (turno == turnoPreparacion)
            {
                AbrirSincrono();
                // Base recién instalada desde el APK: la huella se guarda con el contenido ya migrado
                if (huellaTrasAbrir && conexion != null)
                {
                    huellaTrasAbrir = false;
                    GuardarHuellaInstalada(RutaActiva);
                }
            }
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
    // Los CHECK de largo (descripción ≤ 500, indicación ≤ 300, horario ≤ 120, piso ≤ 20, nombre del nodo ≤ 80,
    // categoría del servicio ≤ 50, nombre del modelo ≤ 120) y la rotación de los modelos (0–360) son los mismos
    // límites que validan RepositorioCampus y el prebuild. CREATE IF NOT EXISTS no altera tablas existentes
    // y SQLite no permite agregar un CHECK a una tabla ya creada: las bases viejas los reciben con las
    // migraciones 5 y 6, que reconstruyen la tabla (ver ReconstruirConChecks).
    // length(NULL) es NULL: un campo vacío (NULL) pasa el CHECK.
    // ------------------------------------------------------------------
    /// <summary>Largo máximo de los textos de lugares y servicios (mismos límites que el CHECK y RepositorioCampus).</summary>
    public const int MaxDescripcion = 500, MaxIndicacion = 300, MaxHorario = 120, MaxPiso = 20;

    /// <summary>Largo máximo del nombre de un nodo (CHECK de nodos.nombre).</summary>
    public const int MaxNombreNodo = 80;

    // Definiciones con un marcador {0} en el nombre: la misma definición crea la tabla nueva (Esquema) y la
    // copia temporal de las migraciones 5 y 6 (p. ej. lugares_nueva)
    private const string DefLugares =
        @"CREATE TABLE {0} (
            id          INTEGER PRIMARY KEY,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT NOT NULL CHECK (length(categoria) BETWEEN 1 AND 50),
            descripcion TEXT CHECK (length(descripcion) <= 500),
            indicacion  TEXT CHECK (length(indicacion) <= 300),
            horario     TEXT CHECK (length(horario) <= 120),
            latitud     REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud    REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180)
        )";

    private const string DefServicios =
        @"CREATE TABLE {0} (
            id          INTEGER PRIMARY KEY,
            lugar_id    INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            nombre      TEXT NOT NULL CHECK (length(nombre) BETWEEN 1 AND 120),
            categoria   TEXT CHECK (length(categoria) <= 50),
            piso        TEXT CHECK (length(piso) <= 20),
            descripcion TEXT CHECK (length(descripcion) <= 500),
            indicacion  TEXT CHECK (length(indicacion) <= 300),
            horario     TEXT CHECK (length(horario) <= 120)
        )";

    private const string IndiceServiciosLugar = "CREATE INDEX IF NOT EXISTS idx_servicios_lugar ON servicios(lugar_id)";

    private const string DefNodos =
        @"CREATE TABLE {0} (
            id        INTEGER PRIMARY KEY,
            nombre    TEXT CHECK (length(nombre) <= 80),
            latitud   REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud  REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180),
            lugar_id  INTEGER REFERENCES lugares(id) ON DELETE SET NULL
        )";

    private const string IndiceNodosLugar = "CREATE INDEX IF NOT EXISTS idx_nodos_lugar ON nodos(lugar_id)";

    // Edificaciones 3D agregadas por el administrador (medidas en metros, rotación en grados)
    private const string DefModelos =
        @"CREATE TABLE {0} (
            id        INTEGER PRIMARY KEY,
            tipo      TEXT NOT NULL CHECK (tipo IN ('cubo', 'rectangulo', 'cilindro')),
            nombre    TEXT CHECK (length(nombre) <= 120),
            latitud   REAL NOT NULL CHECK (latitud BETWEEN -90 AND 90),
            longitud  REAL NOT NULL CHECK (longitud BETWEEN -180 AND 180),
            rotacion  REAL NOT NULL DEFAULT 0 CHECK (rotacion BETWEEN 0 AND 360),
            ancho     REAL NOT NULL DEFAULT 12 CHECK (ancho BETWEEN 1 AND 300),
            largo     REAL NOT NULL DEFAULT 12 CHECK (largo BETWEEN 1 AND 300),
            alto      REAL NOT NULL DEFAULT 10 CHECK (alto BETWEEN 1 AND 200)
        )";

    private static readonly string[] Esquema =
    {
        string.Format(DefLugares, "IF NOT EXISTS lugares"),
        TablaAdministradores,
        @"CREATE TABLE IF NOT EXISTS busquedas_log (
            id         INTEGER PRIMARY KEY,
            lugar_id   INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
            fecha_hora TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        )",
        // idx_busquedas_lugar_fecha (lugar_id, fecha_hora) lo crea la migración 3 (ver Migrar)
        "CREATE INDEX IF NOT EXISTS idx_busquedas_fecha ON busquedas_log(fecha_hora)",
        string.Format(DefNodos, "IF NOT EXISTS nodos"),
        IndiceNodosLugar,
        @"CREATE TABLE IF NOT EXISTS conexiones (
            id      INTEGER PRIMARY KEY,
            nodo_a  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
            nodo_b  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
            CHECK (nodo_a < nodo_b),
            UNIQUE (nodo_a, nodo_b)
        )",
        // (nodo_a ya está cubierto por el índice de UNIQUE (nodo_a, nodo_b); idx_conexiones_a sobraba)
        "CREATE INDEX IF NOT EXISTS idx_conexiones_b ON conexiones(nodo_b)",
        string.Format(DefServicios, "IF NOT EXISTS servicios"),
        IndiceServiciosLugar,
        string.Format(DefModelos, "IF NOT EXISTS modelos"),
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
    ///            5 = CHECK de largo en lugares y servicios también en bases creadas antes de esos CHECK
    ///                (se reconstruyen las dos tablas conservando ids y datos; ver ReconstruirConChecks).
    ///            6 = CHECK de nodos.nombre ≤ 80, servicios.categoria ≤ 50, modelos.nombre ≤ 120 y
    ///                modelos.rotacion entre 0 y 360 (misma reconstrucción). Al copiar, los textos más largos
    ///                se recortan al límite y la rotación se lleva a [0, 360): un dato viejo nunca bloquea la
    ///                migración (se registra cuántas filas se ajustaron).
    /// Para un cambio nuevo: sube este número y agrega un bloque "if (v &lt; N)" en Migrar.
    /// </summary>
    public const int VersionEsquema = 6;

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

        // Migraciones 5 y 6: reconstruir una tabla exige las claves foráneas APAGADAS (con ellas, DROP TABLE lugares
        // borraría en cascada búsquedas y servicios y dejaría los nodos sin lugar; DROP TABLE nodos borraría las
        // conexiones). El PRAGMA no tiene efecto dentro de una transacción: se apaga antes y se restaura después.
        // Cada tabla se reconstruye una sola vez, con su definición final, si le falta el CHECK más nuevo.
        bool reconstruirLugares = false, reconstruirServicios = false, reconstruirNodos = false, reconstruirModelos = false;
        bool apagarClaves = false;
        try
        {
            if (v < 6)
            {
                reconstruirLugares = FaltaCheck(c, "lugares", "length(descripcion)");
                reconstruirServicios = FaltaCheck(c, "servicios", "length(categoria)");
                reconstruirNodos = FaltaCheck(c, "nodos", "length(nombre)");
                reconstruirModelos = FaltaCheck(c, "modelos", "rotacion BETWEEN");
            }
            bool alguna = reconstruirLugares || reconstruirServicios || reconstruirNodos || reconstruirModelos;
            apagarClaves = alguna && c.ExecuteScalar<int>("PRAGMA foreign_keys") == 1;
        }
        catch (Exception e)
        {
            // Sin poder revisar no se reconstruye nada (y la versión no sube: se reintenta en la próxima apertura)
            Debug.LogWarning("No se pudo revisar el esquema de las tablas: " + e.Message);
            return;
        }

        try
        {
            if (apagarClaves) c.Execute("PRAGMA foreign_keys = OFF");
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
                // v < 5 y v < 6: CHECK de largo y de rotación (solo si la tabla no los tiene ya). Al copiar se
                // recorta cada texto a su límite: un dato viejo demasiado largo no bloquea la migración.
                if (reconstruirLugares) ReconstruirConChecks(c, "lugares", DefLugares,
                    "id, nombre, categoria, descripcion, indicacion, horario, latitud, longitud",
                    "id, " + NoVacio("nombre", 120, "'Lugar ' || id") + ", " + NoVacio("categoria", 50, "'Otro'") +
                    ", substr(descripcion, 1, 500), substr(indicacion, 1, 300), substr(horario, 1, 120), latitud, longitud",
                    "length(nombre) > 120 OR length(trim(nombre)) = 0 OR length(categoria) > 50 OR length(trim(categoria)) = 0 OR " +
                    "length(descripcion) > 500 OR length(indicacion) > 300 OR length(horario) > 120",
                    null);
                if (reconstruirServicios) ReconstruirConChecks(c, "servicios", DefServicios,
                    "id, lugar_id, nombre, categoria, piso, descripcion, indicacion, horario",
                    "id, lugar_id, " + NoVacio("nombre", 120, "'Espacio interior ' || id") + ", substr(categoria, 1, 50), substr(piso, 1, 20), " +
                    "substr(descripcion, 1, 500), substr(indicacion, 1, 300), substr(horario, 1, 120)",
                    "length(nombre) > 120 OR length(trim(nombre)) = 0 OR length(categoria) > 50 OR length(piso) > 20 OR " +
                    "length(descripcion) > 500 OR length(indicacion) > 300 OR length(horario) > 120",
                    IndiceServiciosLugar);
                if (reconstruirNodos) ReconstruirConChecks(c, "nodos", DefNodos,
                    "id, nombre, latitud, longitud, lugar_id",
                    "id, substr(nombre, 1, 80), latitud, longitud, lugar_id",
                    "length(nombre) > 80",
                    IndiceNodosLugar);
                if (reconstruirModelos) ReconstruirConChecks(c, "modelos", DefModelos,
                    "id, tipo, nombre, latitud, longitud, rotacion, ancho, largo, alto",
                    "id, tipo, substr(nombre, 1, 120), latitud, longitud, " + RotacionNormalizada + ", ancho, largo, alto",
                    "length(nombre) > 120 OR NOT (rotacion BETWEEN 0 AND 360)",
                    null);
                // Con las claves foráneas apagadas pudieron quedar (o venir de antes) filas que apuntan a algo que
                // ya no existe: se limpian aquí, dentro de la misma transacción, antes de anotar la versión
                LimpiarHuerfanos(c);
                c.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES ('version_esquema', ?)",
                          VersionEsquema.ToString(CultureInfo.InvariantCulture));
            });
            Registro.Info($"Esquema de la base actualizado de la versión {v} a la {VersionEsquema}.");
        }
        catch (Exception e)
        {
            // La transacción se revierte; la app sigue funcionando con el esquema anterior y se reintenta en la
            // próxima apertura. (Los textos largos ya no la bloquean: se recortan al copiar.)
            Debug.LogWarning($"No se pudo actualizar el esquema a la versión {VersionEsquema}: {e.Message}");
        }
        finally
        {
            if (apagarClaves)
            {
                try { c.Execute("PRAGMA foreign_keys = ON"); }
                catch (Exception e) { Debug.LogWarning("No se pudieron volver a activar las claves foráneas: " + e.Message); }
            }
        }
    }

    // ------------------------------------------------------------------
    // Huérfanos: filas que apuntan a un lugar o nodo que ya no existe
    // ------------------------------------------------------------------
    // Las mismas reglas cuentan (prebuild) y limpian (migración y menú del Editor). Condición y acción son
    // textos SQL fijos del código. Orden: primero los nodos (no se borran) y luego las conexiones.
    private static readonly (string tabla, string condicion, string accion, string descripcion)[] ReglasHuerfanos =
    {
        ("servicios", "lugar_id IS NULL OR lugar_id NOT IN (SELECT id FROM lugares)",
         "DELETE FROM servicios WHERE {0}", "espacios interiores sin lugar (se borran)"),
        ("nodos", "lugar_id IS NOT NULL AND lugar_id NOT IN (SELECT id FROM lugares)",
         "UPDATE nodos SET lugar_id = NULL WHERE {0}", "nodos con un lugar inexistente (quedan como nodos de paso)"),
        ("conexiones", "nodo_a NOT IN (SELECT id FROM nodos) OR nodo_b NOT IN (SELECT id FROM nodos)",
         "DELETE FROM conexiones WHERE {0}", "conexiones con nodos inexistentes (se borran)"),
        ("busquedas_log", "lugar_id IS NULL OR lugar_id NOT IN (SELECT id FROM lugares)",
         "DELETE FROM busquedas_log WHERE {0}", "búsquedas de un lugar inexistente (se borran)"),
    };

    /// <summary>
    /// Cuenta las filas huérfanas (servicios o búsquedas sin lugar, nodos con un lugar inexistente, conexiones
    /// con nodos inexistentes). 'detalle' = una línea por regla con huérfanos ("" si no hay). No modifica nada.
    /// </summary>
    public static int ContarHuerfanos(SQLiteConnection c, out string detalle)
    {
        int total = 0;
        StringBuilder sb = new StringBuilder();
        foreach (var r in ReglasHuerfanos)
        {
            int n = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {r.tabla} WHERE {r.condicion}");
            if (n == 0) continue;
            total += n;
            sb.Append($"\n  {n} {r.descripcion}");
        }
        detalle = sb.ToString();
        return total;
    }

    /// <summary>
    /// Limpia las filas huérfanas (ver ContarHuerfanos) y registra cuántas por regla. Devuelve el total.
    /// Debe llamarse dentro de una transacción (Migrar) o abrir la suya propia (menú del Editor).
    /// </summary>
    public static int LimpiarHuerfanos(SQLiteConnection c)
    {
        int total = 0;
        StringBuilder sb = new StringBuilder();
        foreach (var r in ReglasHuerfanos)
        {
            int n = c.Execute(string.Format(r.accion, r.condicion));
            if (n == 0) continue;
            total += n;
            sb.Append($"\n  {n} {r.descripcion}");
        }
        if (total > 0) Registro.Info($"Datos huérfanos limpiados: {total}." + sb);
        return total;
    }

    // ¿La tabla se creó antes de un CHECK? (su definición guardada no contiene 'marca')
    private static bool FaltaCheck(SQLiteConnection c, string tabla, string marca)
    {
        string sql = c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?", tabla);
        return sql != null && sql.IndexOf(marca, StringComparison.OrdinalIgnoreCase) < 0;
    }

    // Texto obligatorio recortado a 'max'; si queda vacío, 'respaldo' (expresión SQL fija del código)
    private static string NoVacio(string columna, int max, string respaldo) =>
        $"CASE WHEN length(trim({columna})) > 0 THEN substr({columna}, 1, {max}) ELSE {respaldo} END";

    // Rotación llevada a [0, 360) sin floor() (no existe en el SQLite 3.6 del Editor): r - 360·trunc(r/360),
    // y +360 si quedó negativa. Los valores ya válidos (0–360) se copian tal cual.
    private const string RotacionNormalizada =
        @"CASE WHEN rotacion BETWEEN 0 AND 360 THEN rotacion
               WHEN rotacion - 360.0 * CAST(rotacion / 360.0 AS INTEGER) < 0
                    THEN rotacion - 360.0 * CAST(rotacion / 360.0 AS INTEGER) + 360.0
               ELSE rotacion - 360.0 * CAST(rotacion / 360.0 AS INTEGER) END";

    /// <summary>
    /// Migraciones 5 y 6: reconstruye 'tabla' con su definición actual (con CHECK). Se llama dentro de la
    /// transacción de Migrar y con las claves foráneas apagadas. Orden seguro también en SQLite moderno: se crea
    /// la copia nueva, se pasan los datos con sus ids ('seleccion' recorta cada texto a su límite), se borra la
    /// vieja y la copia toma el nombre (renombrar la VIEJA haría que SQLite 3.26+ apuntara las claves foráneas
    /// de las otras tablas a la copia vieja). 'ajustadas' = condición SQL de las filas que se recortan (solo
    /// para el registro). Los índices de la tabla se borran con ella: 'indice' los vuelve a crear.
    /// No hay triggers sobre estas tablas. Todos los textos SQL son fijos del código, nunca del usuario.
    /// </summary>
    private static void ReconstruirConChecks(SQLiteConnection c, string tabla, string definicion, string columnas,
                                             string seleccion, string ajustadas, string indice)
    {
        string nueva = tabla + "_nueva";
        int recortadas = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {tabla} WHERE {ajustadas}");
        c.Execute("DROP TABLE IF EXISTS " + nueva);
        c.Execute(string.Format(definicion, nueva));
        c.Execute($"INSERT INTO {nueva} ({columnas}) SELECT {seleccion} FROM {tabla}");
        c.Execute("DROP TABLE " + tabla);
        c.Execute($"ALTER TABLE {nueva} RENAME TO {tabla}");
        if (indice != null) c.Execute(indice);
        Registro.Info($"Tabla {tabla} reconstruida con los límites (CHECK); filas ajustadas al límite: {recortadas}.");
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
                if (t.StartsWith("select setval", StringComparison.Ordinal) || t.StartsWith("\\", StringComparison.Ordinal)) continue;

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
