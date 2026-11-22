using System;
using System.Collections.Generic;
using SQLite4Unity3d;

/// <summary>Error con un mensaje listo para mostrar al usuario.</summary>
public class ErrorCampus : Exception
{
    public ErrorCampus(string mensaje) : base(mensaje) { }
}

/// <summary>
/// Todas las consultas a la base local. Es el equivalente a los endpoints de backend/main.py,
/// con las mismas validaciones y los mismos mensajes de error.
/// </summary>
public static class RepositorioCampus
{
    private static SQLiteConnection Db => BaseDatosLocal.Conexion;

    // ----- Filas tal como salen de SQLite (sqlite4unity3d mapea a propiedades, no a campos) -----
    private class FilaLugar
    {
        public int id { get; set; }
        public string nombre { get; set; }
        public string categoria { get; set; }
        public string descripcion { get; set; }
        public string indicacion { get; set; }
        public string horario { get; set; }
        public double latitud { get; set; }
        public double longitud { get; set; }
    }

    private class FilaServicio
    {
        public int id { get; set; }
        public int lugar_id { get; set; }
        public string nombre { get; set; }
        public string categoria { get; set; }
        public string piso { get; set; }
        public string descripcion { get; set; }
        public string indicacion { get; set; }
        public string horario { get; set; }
    }

    private class FilaNodo
    {
        public int id { get; set; }
        public string nombre { get; set; }
        public double latitud { get; set; }
        public double longitud { get; set; }
        public int lugar_id { get; set; }
    }

    private class FilaConexion
    {
        public int id { get; set; }
        public int nodo_a { get; set; }
        public int nodo_b { get; set; }
    }

    private class FilaRanking
    {
        public string nombre { get; set; }
        public string categoria { get; set; }
        public int total { get; set; }
    }

    private class FilaAdmin
    {
        public string nickname { get; set; }
        public string llave_hash { get; set; }
    }

    private class FilaEspacio
    {
        public int id { get; set; }
        public string nickname { get; set; }
        public string registrado_en { get; set; }
    }

    private class FilaModelo
    {
        public int id { get; set; }
        public string tipo { get; set; }
        public string nombre { get; set; }
        public double latitud { get; set; }
        public double longitud { get; set; }
        public double rotacion { get; set; }
        public double ancho { get; set; }
        public double largo { get; set; }
        public double alto { get; set; }
    }

    private class FilaEstadistica
    {
        public string nombre { get; set; }
        public string categoria { get; set; }
        public int mes_actual { get; set; }
        public int mes_anterior { get; set; }
    }

    public struct LugarRanking
    {
        public string nombre;
        public string categoria;
        public int total;
    }

    // ==================================================================
    // Público (modo usuario)
    // ==================================================================
    public static List<LugaresService.Lugar> ListarLugares()
    {
        List<FilaLugar> filas = Db.Query<FilaLugar>(
            @"SELECT id, nombre, categoria,
                     COALESCE(descripcion, '') AS descripcion,
                     COALESCE(indicacion, '') AS indicacion,
                     COALESCE(horario, '') AS horario,
                     latitud, longitud
              FROM lugares
              ORDER BY nombre COLLATE NOCASE");

        return filas.ConvertAll(f => new LugaresService.Lugar
        {
            id = f.id, nombre = f.nombre, categoria = f.categoria, descripcion = f.descripcion,
            indicacion = f.indicacion, horario = f.horario, latitud = f.latitud, longitud = f.longitud,
        });
    }

    public static List<LugaresService.Servicio> ListarServicios()
    {
        List<FilaServicio> filas = Db.Query<FilaServicio>(
            @"SELECT id, lugar_id, nombre,
                     COALESCE(categoria, '') AS categoria,
                     COALESCE(piso, '') AS piso,
                     COALESCE(descripcion, '') AS descripcion,
                     COALESCE(indicacion, '') AS indicacion,
                     COALESCE(horario, '') AS horario
              FROM servicios
              ORDER BY lugar_id, nombre COLLATE NOCASE");

        return filas.ConvertAll(f => new LugaresService.Servicio
        {
            id = f.id, lugar_id = f.lugar_id, nombre = f.nombre, categoria = f.categoria, piso = f.piso,
            descripcion = f.descripcion, indicacion = f.indicacion, horario = f.horario,
        });
    }

    public static LugaresService.RedDto ObtenerRed()
    {
        List<FilaNodo> nodos = Db.Query<FilaNodo>(
            @"SELECT id, COALESCE(nombre, '') AS nombre, latitud, longitud,
                     COALESCE(lugar_id, 0) AS lugar_id
              FROM nodos ORDER BY id");
        List<FilaConexion> conexiones = Db.Query<FilaConexion>(
            "SELECT id, nodo_a, nodo_b FROM conexiones ORDER BY id");

        return new LugaresService.RedDto
        {
            nodos = nodos.ConvertAll(n => new LugaresService.NodoDto
            {
                id = n.id, nombre = n.nombre, latitud = n.latitud, longitud = n.longitud, lugar_id = n.lugar_id,
            }).ToArray(),
            conexiones = conexiones.ConvertAll(c => new LugaresService.ConexionDto
            {
                id = c.id, nodo_a = c.nodo_a, nodo_b = c.nodo_b,
            }).ToArray(),
        };
    }

    public static List<LugaresService.Modelo> ListarModelos()
    {
        return Db.Query<FilaModelo>(
            @"SELECT id, tipo, COALESCE(nombre, '') AS nombre, latitud, longitud, rotacion, ancho, largo, alto
              FROM modelos ORDER BY id")
            .ConvertAll(f => new LugaresService.Modelo
            {
                id = f.id, tipo = f.tipo, nombre = f.nombre, latitud = f.latitud, longitud = f.longitud,
                rotacion = (float)f.rotacion, ancho = (float)f.ancho, largo = (float)f.largo, alto = (float)f.alto,
            });
    }

    public static void RegistrarBusqueda(int lugarId)
    {
        if (!Existe("lugares", lugarId)) throw new ErrorCampus("Lugar no encontrado");
        Db.Execute("INSERT INTO busquedas_log (lugar_id) VALUES (?)", lugarId);
    }

    // ==================================================================
    // Administradores
    // ==================================================================
    public static bool VerificarAdmin(string nickname, string llave)
    {
        List<FilaAdmin> filas = Db.Query<FilaAdmin>(
            "SELECT nickname, llave_hash FROM administradores WHERE nickname = ? AND llave_hash IS NOT NULL",
            (nickname ?? "").Trim());

        // Aunque el nickname no exista se calcula un hash, para no revelar qué falló por el tiempo de respuesta
        string guardado = filas.Count > 0 ? filas[0].llave_hash : SeguridadLlaves.Hashear("x");
        return SeguridadLlaves.Verificar(llave, guardado) && filas.Count > 0;
    }

    /// <summary>Un espacio de administrador: libre (sin usuario) o registrado.</summary>
    public struct EspacioAdmin
    {
        public int id;
        public string nickname;       // null = libre
        public string registradoEn;
        public bool Libre => string.IsNullOrEmpty(nickname);
    }

    public static List<EspacioAdmin> ListarEspaciosAdmin()
    {
        return Db.Query<FilaEspacio>("SELECT id, nickname, registrado_en FROM administradores ORDER BY id")
                 .ConvertAll(f => new EspacioAdmin { id = f.id, nickname = f.nickname, registradoEn = f.registrado_en });
    }

    public static int ContarAdminsRegistrados() =>
        Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname IS NOT NULL");

    /// <summary>
    /// Registro desde la app: el código debe existir y su espacio estar libre.
    /// Después de esto el usuario y la llave de ese espacio no se pueden cambiar (trigger en la base).
    /// </summary>
    public static void RegistrarAdmin(string codigo, string nickname, string llave)
    {
        nickname = (nickname ?? "").Trim();
        if (SeguridadLlaves.NormalizarCodigo(codigo).Length == 0) throw new ErrorCampus("Escribe el código de verificación.");
        if (nickname.Length < 3 || nickname.Length > 50) throw new ErrorCampus("El usuario debe tener entre 3 y 50 caracteres.");
        foreach (char ch in nickname)
            if (!char.IsLetterOrDigit(ch) && ch != '.' && ch != '_' && ch != '-')
                throw new ErrorCampus("El usuario solo puede tener letras, números, punto, guion y guion bajo.");
        if (string.IsNullOrEmpty(llave) || llave.Length < 6) throw new ErrorCampus("La contraseña debe tener al menos 6 caracteres.");

        string hashCodigo = SeguridadLlaves.HashCodigo(codigo);
        List<FilaEspacio> espacio = Db.Query<FilaEspacio>(
            "SELECT id, nickname, registrado_en FROM administradores WHERE codigo_hash = ?", hashCodigo);

        if (espacio.Count == 0) throw new ErrorCampus("El código de verificación no es válido.");
        if (!string.IsNullOrEmpty(espacio[0].nickname)) throw new ErrorCampus("Este código ya fue usado para registrar una cuenta.");
        if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname = ?", nickname) > 0)
            throw new ErrorCampus("Ese usuario ya existe. Elige otro.");

        string hashLlave = SeguridadLlaves.Hashear(llave);
        int n = Db.Execute(
            @"UPDATE administradores
              SET nickname = ?, llave_hash = ?, registrado_en = datetime('now', 'localtime')
              WHERE id = ? AND nickname IS NULL",
            nickname, hashLlave, espacio[0].id);
        if (n == 0) throw new ErrorCampus("Este código ya fue usado para registrar una cuenta.");

        BaseDatosLocal.MarcarCambio();
    }

    /// <summary>
    /// Crea 'cantidad' espacios libres y devuelve sus códigos (solo se guardan como hash).
    /// En el Editor los códigos se anotan en CodigosRegistroAdmin.txt, fuera de Assets (no viaja en el APK).
    /// </summary>
    public static List<(int id, string codigo)> CrearEspaciosAdmin(int cantidad)
    {
        List<(int, string)> creados = new List<(int, string)>();
        Db.RunInTransaction(() =>
        {
            for (int i = 0; i < cantidad; i++)
            {
                string codigo = SeguridadLlaves.GenerarCodigo();
                Db.Execute("INSERT INTO administradores (codigo_hash) VALUES (?)", SeguridadLlaves.HashCodigo(codigo));
                creados.Add((UltimoId(), codigo));
            }
        });
        BaseDatosLocal.MarcarCambio();

#if UNITY_EDITOR
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        List<string> lineas = creados.ConvertAll(c => $"{c.Item1}|{c.Item2}|{fecha}");
        System.IO.File.AppendAllLines(RutaCodigos, lineas, new System.Text.UTF8Encoding(false));
#endif
        return creados;
    }

    /// <summary>Elimina un espacio (libre o registrado): esa cuenta pierde el acceso. Solo desde el Editor.</summary>
    public static void EliminarEspacioAdmin(int id)
    {
        if (Db.Execute("DELETE FROM administradores WHERE id = ?", id) == 0)
            throw new ErrorCampus("Espacio no encontrado");
        BaseDatosLocal.MarcarCambio();
    }

#if UNITY_EDITOR
    /// <summary>Archivo con los códigos generados (fuera de Assets: no se empaqueta).</summary>
    public static string RutaCodigos =>
        System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "CodigosRegistroAdmin.txt");

    /// <summary>id del espacio -> código, leído del archivo de códigos.</summary>
    public static Dictionary<int, string> LeerCodigos()
    {
        Dictionary<int, string> mapa = new Dictionary<int, string>();
        if (!System.IO.File.Exists(RutaCodigos)) return mapa;
        foreach (string linea in System.IO.File.ReadAllLines(RutaCodigos))
        {
            string[] p = linea.Split('|');
            if (p.Length >= 2 && int.TryParse(p[0], out int id)) mapa[id] = p[1];
        }
        return mapa;
    }

    /// <summary>La primera vez (sin archivo de códigos ni espacios libres) crea los 5 espacios iniciales.</summary>
    public static void AsegurarEspaciosIniciales()
    {
        if (System.IO.File.Exists(RutaCodigos)) return;
        if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname IS NULL") > 0) return;

        CrearEspaciosAdmin(5);
        UnityEngine.Debug.Log("Se crearon 5 espacios de administrador. Sus códigos están en: " + RutaCodigos);
    }
#endif

    // ==================================================================
    // Estadísticas mensuales (pestaña "Reportes de uso")
    // ==================================================================
    public struct EstadisticaLugar
    {
        public string nombre;
        public string categoria;
        public int mesActual;
        public int mesAnterior;
    }

    public static List<EstadisticaLugar> EstadisticasDelMes(DateTime hoy)
    {
        DateTime inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        DateTime inicioAnterior = inicioMes.AddMonths(-1);
        string f(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss");

        List<FilaEstadistica> filas = Db.Query<FilaEstadistica>(
            @"SELECT l.nombre, l.categoria,
                     SUM(CASE WHEN b.fecha_hora >= ? THEN 1 ELSE 0 END) AS mes_actual,
                     SUM(CASE WHEN b.fecha_hora >= ? AND b.fecha_hora < ? THEN 1 ELSE 0 END) AS mes_anterior
              FROM lugares l
              LEFT JOIN busquedas_log b ON b.lugar_id = l.id
              GROUP BY l.id, l.nombre, l.categoria
              ORDER BY mes_actual DESC, l.nombre COLLATE NOCASE",
            f(inicioMes), f(inicioAnterior), f(inicioMes));

        return filas.ConvertAll(x => new EstadisticaLugar
        {
            nombre = x.nombre, categoria = x.categoria, mesActual = x.mes_actual, mesAnterior = x.mes_anterior,
        });
    }

    // ==================================================================
    // Reporte
    // ==================================================================
    public static List<LugarRanking> RankingBusquedas(int limite, out int totalBusquedas)
    {
        List<FilaRanking> filas = Db.Query<FilaRanking>(
            @"SELECT l.nombre, l.categoria, COUNT(b.id) AS total
              FROM lugares l
              LEFT JOIN busquedas_log b ON b.lugar_id = l.id
              GROUP BY l.id, l.nombre, l.categoria
              ORDER BY total DESC, l.nombre COLLATE NOCASE
              LIMIT ?", limite);

        totalBusquedas = Db.ExecuteScalar<int>("SELECT COUNT(*) FROM busquedas_log");
        return filas.ConvertAll(f => new LugarRanking { nombre = f.nombre, categoria = f.categoria, total = f.total });
    }

    // ==================================================================
    // Lugares
    // ==================================================================
    public static string CrearLugar(AdminService.LugarEditable d)
    {
        ValidarLugar(d);
        int lugarId = 0;
        int? nodoId = null;

        Escribir(() =>
        {
            Db.Execute(
                @"INSERT INTO lugares (nombre, categoria, descripcion, indicacion, horario, latitud, longitud)
                  VALUES (?, ?, ?, ?, ?, ?, ?)",
                d.nombre.Trim(), d.categoria.Trim(), d.descripcion, d.indicacion, d.horario, d.latitud, d.longitud);
            lugarId = UltimoId();

            if (d.conectar_con_nodo > 0)
            {
                if (!Existe("nodos", d.conectar_con_nodo)) throw new ErrorCampus("El nodo a conectar no existe");

                Db.Execute("INSERT INTO nodos (nombre, latitud, longitud, lugar_id) VALUES (?, ?, ?, ?)",
                           d.nombre.Trim(), d.latitud, d.longitud, lugarId);
                nodoId = UltimoId();

                (int a, int b) = Par(nodoId.Value, d.conectar_con_nodo);
                Db.Execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", a, b);
            }
        });

        return $"{{\"id\":{lugarId},\"nodo_id\":{(nodoId.HasValue ? nodoId.Value.ToString() : "null")}}}";
    }

    public static void ActualizarLugar(int id, AdminService.LugarEditable d)
    {
        ValidarLugar(d);
        Escribir(() =>
        {
            int n = Db.Execute(
                @"UPDATE lugares
                  SET nombre = ?, categoria = ?, descripcion = ?, indicacion = ?, horario = ?, latitud = ?, longitud = ?
                  WHERE id = ?",
                d.nombre.Trim(), d.categoria.Trim(), d.descripcion, d.indicacion, d.horario, d.latitud, d.longitud, id);
            if (n == 0) throw new ErrorCampus("Lugar no encontrado");
        });
    }

    /// <summary>Su nodo-puerta queda como nodo de paso; sus servicios y su historial de búsquedas se borran.</summary>
    public static void EliminarLugar(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM lugares WHERE id = ?", id) == 0) throw new ErrorCampus("Lugar no encontrado");
        });
    }

    // ==================================================================
    // Servicios
    // ==================================================================
    public static string CrearServicio(AdminService.ServicioEditable d)
    {
        ValidarServicio(d);
        int id = 0;
        Escribir(() =>
        {
            if (!Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe");
            Db.Execute(
                @"INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
                  VALUES (?, ?, ?, ?, ?, ?, ?)",
                d.lugar_id, d.nombre.Trim(), d.categoria, d.piso, d.descripcion, d.indicacion, d.horario);
            id = UltimoId();
        });
        return $"{{\"id\":{id}}}";
    }

    public static void ActualizarServicio(int id, AdminService.ServicioEditable d)
    {
        ValidarServicio(d);
        Escribir(() =>
        {
            if (!Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe");
            int n = Db.Execute(
                @"UPDATE servicios
                  SET lugar_id = ?, nombre = ?, categoria = ?, piso = ?, descripcion = ?, indicacion = ?, horario = ?
                  WHERE id = ?",
                d.lugar_id, d.nombre.Trim(), d.categoria, d.piso, d.descripcion, d.indicacion, d.horario, id);
            if (n == 0) throw new ErrorCampus("Servicio no encontrado");
        });
    }

    public static void EliminarServicio(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM servicios WHERE id = ?", id) == 0) throw new ErrorCampus("Servicio no encontrado");
        });
    }

    // ==================================================================
    // Nodos y conexiones
    // ==================================================================
    public static string CrearNodo(AdminService.NodoEditable d)
    {
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, 80, "El nombre del nodo");
        int id = 0;

        Escribir(() =>
        {
            if (d.lugar_id > 0 && !Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar o el nodo indicado no existe");
            if (d.conectar_con_nodo > 0 && !Existe("nodos", d.conectar_con_nodo)) throw new ErrorCampus("El lugar o el nodo indicado no existe");

            Db.Execute("INSERT INTO nodos (nombre, latitud, longitud, lugar_id) VALUES (?, ?, ?, ?)",
                       d.nombre, d.latitud, d.longitud, d.lugar_id > 0 ? (object)d.lugar_id : null);
            id = UltimoId();

            if (d.conectar_con_nodo > 0)
            {
                (int a, int b) = Par(id, d.conectar_con_nodo);
                Db.Execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", a, b);
            }
        });
        return $"{{\"id\":{id}}}";
    }

    public static void ActualizarNodo(int id, AdminService.NodoEditable d)
    {
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, 80, "El nombre del nodo");

        Escribir(() =>
        {
            if (d.lugar_id > 0 && !Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe");
            int n = Db.Execute("UPDATE nodos SET nombre = ?, latitud = ?, longitud = ?, lugar_id = ? WHERE id = ?",
                               d.nombre, d.latitud, d.longitud, d.lugar_id > 0 ? (object)d.lugar_id : null, id);
            if (n == 0) throw new ErrorCampus("Nodo no encontrado");
        });
    }

    /// <summary>Sus conexiones se borran solas. Ojo: puede dejar partes de la red desconectadas.</summary>
    public static void EliminarNodo(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM nodos WHERE id = ?", id) == 0) throw new ErrorCampus("Nodo no encontrado");
        });
    }

    public static void CrearConexion(int nodoA, int nodoB)
    {
        (int a, int b) = Par(nodoA, nodoB);
        Escribir(() =>
        {
            if (!Existe("nodos", a) || !Existe("nodos", b)) throw new ErrorCampus("Alguno de los nodos no existe");
            if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM conexiones WHERE nodo_a = ? AND nodo_b = ?", a, b) > 0)
                throw new ErrorCampus("Esos nodos ya están conectados");
            Db.Execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", a, b);
        });
    }

    public static void EliminarConexion(int nodoA, int nodoB)
    {
        (int a, int b) = Par(nodoA, nodoB);
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM conexiones WHERE nodo_a = ? AND nodo_b = ?", a, b) == 0)
                throw new ErrorCampus("Esa conexión no existe");
        });
    }

    // ==================================================================
    // Modelos 3D (edificaciones agregadas por el administrador)
    // ==================================================================
    public static string CrearModelo(AdminService.ModeloEditable d)
    {
        ValidarModelo(d);
        int id = 0;
        Escribir(() =>
        {
            Db.Execute(
                @"INSERT INTO modelos (tipo, nombre, latitud, longitud, rotacion, ancho, largo, alto)
                  VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                d.tipo, d.nombre, d.latitud, d.longitud, d.rotacion, d.ancho, d.largo, d.alto);
            id = UltimoId();
        });
        return $"{{\"id\":{id}}}";
    }

    public static void ActualizarModelo(int id, AdminService.ModeloEditable d)
    {
        ValidarModelo(d);
        Escribir(() =>
        {
            int n = Db.Execute(
                @"UPDATE modelos SET tipo = ?, nombre = ?, latitud = ?, longitud = ?, rotacion = ?, ancho = ?, largo = ?, alto = ?
                  WHERE id = ?",
                d.tipo, d.nombre, d.latitud, d.longitud, d.rotacion, d.ancho, d.largo, d.alto, id);
            if (n == 0) throw new ErrorCampus("Modelo no encontrado");
        });
    }

    public static void EliminarModelo(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM modelos WHERE id = ?", id) == 0) throw new ErrorCampus("Modelo no encontrado");
        });
    }

    private static void ValidarModelo(AdminService.ModeloEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del modelo");
        if (d.tipo != "cubo" && d.tipo != "rectangulo" && d.tipo != "cilindro")
            throw new ErrorCampus("Tipo de modelo no válido (cubo, rectangulo o cilindro).");
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, 120, "El nombre del modelo");
        if (d.ancho < 1 || d.ancho > 300 || d.largo < 1 || d.largo > 300) throw new ErrorCampus("El ancho y el largo deben estar entre 1 y 300 m.");
        if (d.alto < 1 || d.alto > 200) throw new ErrorCampus("La altura debe estar entre 1 y 200 m.");
    }

    /// <summary>
    /// Reemplaza TODA la red (lo usa el menú Campus > Guardar red en la base de datos).
    /// Los nodos que apuntan a un lugar inexistente quedan como nodos de paso.
    /// </summary>
    public static int ReemplazarRed(IList<LugaresService.NodoDto> nodos, IList<(int a, int b)> caminos, out int entradasSinLugar)
    {
        int sinLugar = 0;
        Escribir(() =>
        {
            Db.Execute("DELETE FROM conexiones");
            Db.Execute("DELETE FROM nodos");

            foreach (LugaresService.NodoDto n in nodos)
            {
                object lugar = null;
                if (n.lugar_id > 0)
                {
                    if (Existe("lugares", n.lugar_id)) lugar = n.lugar_id;
                    else sinLugar++;
                }
                Db.Execute("INSERT INTO nodos (id, nombre, latitud, longitud, lugar_id) VALUES (?, ?, ?, ?, ?)",
                           n.id, n.nombre, n.latitud, n.longitud, lugar);
            }

            foreach ((int a, int b) in caminos)
                Db.Execute("INSERT OR IGNORE INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", Math.Min(a, b), Math.Max(a, b));
        });
        entradasSinLugar = sinLugar;
        return nodos.Count;
    }

    // ==================================================================
    // Utilidades
    // ==================================================================

    /// <summary>Ejecuta un cambio en una transacción y traduce los errores de SQLite a mensajes claros.</summary>
    private static void Escribir(Action cambio)
    {
        try
        {
            Db.RunInTransaction(cambio);
        }
        catch (ErrorCampus) { throw; }
        catch (SQLiteException e)
        {
            string m = e.Message ?? "";
            if (m.Contains("UNIQUE")) throw new ErrorCampus("Registro duplicado");
            if (m.Contains("FOREIGN KEY")) throw new ErrorCampus("El registro relacionado no existe");
            if (m.Contains("CHECK")) throw new ErrorCampus("Algún dato está fuera del rango permitido");
            if (m.Contains("NOT NULL")) throw new ErrorCampus("Falta un dato obligatorio");
            throw new ErrorCampus("Error de base de datos: " + m);
        }
        BaseDatosLocal.MarcarCambio();
    }

    private static int UltimoId() => (int)Db.ExecuteScalar<long>("SELECT last_insert_rowid()");

    private static bool Existe(string tabla, int id) =>
        Db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {tabla} WHERE id = ?", id) > 0;   // 'tabla' nunca viene del usuario

    private static (int, int) Par(int a, int b)
    {
        if (a == b) throw new ErrorCampus("Un nodo no puede conectarse consigo mismo");
        return a < b ? (a, b) : (b, a);
    }

    private static void ValidarLugar(AdminService.LugarEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del lugar");
        ValidarObligatorio(d.nombre, 120, "El nombre");
        ValidarObligatorio(d.categoria, 50, "La categoría");
        ValidarLargo(d.horario, 120, "El horario");
        ValidarCoordenadas(d.latitud, d.longitud);
    }

    private static void ValidarServicio(AdminService.ServicioEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del servicio");
        if (d.lugar_id < 1) throw new ErrorCampus("Elige el lugar al que pertenece el servicio");
        ValidarObligatorio(d.nombre, 120, "El nombre");
        ValidarLargo(d.categoria, 50, "La categoría");
        ValidarLargo(d.piso, 20, "El piso");
        ValidarLargo(d.horario, 120, "El horario");
    }

    private static void ValidarObligatorio(string valor, int max, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) throw new ErrorCampus($"{campo} es obligatorio.");
        ValidarLargo(valor.Trim(), max, campo);
    }

    private static void ValidarLargo(string valor, int max, string campo)
    {
        if (valor != null && valor.Length > max) throw new ErrorCampus($"{campo} admite máximo {max} caracteres.");
    }

    private static void ValidarCoordenadas(double lat, double lon)
    {
        if (lat < -90 || lat > 90) throw new ErrorCampus("La latitud debe estar entre -90 y 90.");
        if (lon < -180 || lon > 180) throw new ErrorCampus("La longitud debe estar entre -180 y 180.");
    }
}
