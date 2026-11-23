using System;
using System.Collections.Generic;
using SQLite4Unity3d;

/// <summary>Tipo de error, para decidir qué hacer sin depender del texto del mensaje.</summary>
public enum TipoErrorCampus
{
    General,
    CodigoInvalido,      // código de registro errado (cuenta como intento fallido)
    CodigoUsado,
    UsuarioExiste,
    DatoInvalido,
    NoEncontrado,
    BaseDatos,
}

/// <summary>Error con un mensaje listo para mostrar al usuario.</summary>
public class ErrorCampus : Exception
{
    public TipoErrorCampus Tipo { get; }

    public ErrorCampus(string mensaje) : this(mensaje, TipoErrorCampus.General) { }
    public ErrorCampus(string mensaje, TipoErrorCampus tipo) : base(mensaje) { Tipo = tipo; }
}

/// <summary>
/// Todas las consultas a la base local. Es el equivalente a los endpoints de backend/main.py,
/// con las mismas validaciones y los mismos mensajes de error.
/// Partes: este archivo (filas, consultas públicas y utilidades), .Admin.cs (cuentas y espacios de
/// administrador), .Lugares.cs (lugares, servicios, red y modelos) y .Reportes.cs (búsquedas y estadísticas).
/// Las escrituras (crear/editar/borrar lugares, servicios, red y modelos, registrar búsquedas y cuentas)
/// son 'internal': desde fuera de Assembly-CSharp no se pueden llamar, y dentro de la app solo las usa
/// AdminService (que exige sesión), LugaresService y ExportadorRed. Lo que usan las herramientas del
/// Editor (otro ensamblado) sigue 'public' y existe solo con UNITY_EDITOR.
/// </summary>
public static partial class RepositorioCampus
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

    // Espacio con el hash de su código (solo para el registro)
    private class FilaCodigo
    {
        public int id { get; set; }
        public string nickname { get; set; }
        public string codigo_hash { get; set; }
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

    internal static void RegistrarBusqueda(int lugarId)
    {
        if (!Existe("lugares", lugarId)) throw new ErrorCampus("Lugar no encontrado.", TipoErrorCampus.NoEncontrado);
        Db.Execute("INSERT INTO busquedas_log (lugar_id) VALUES (?)", lugarId);
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
            throw TraducirError(e);
        }
        BaseDatosLocal.MarcarCambio();
    }

    // Mensaje genérico para UNIQUE (ReemplazarRed lo cambia por uno sobre nodos repetidos)
    private const string MsgUnico = "Ya existe un elemento con ese nombre. Usa otro.";

    /// <summary>
    /// Mensajes de SQLite moderno ("UNIQUE constraint failed") y del 3.6 del Editor
    /// ("column x is not unique", "constraint failed", "x may not be NULL").
    /// </summary>
    private static ErrorCampus TraducirError(SQLiteException e)
    {
        string m = (e.Message ?? "").ToLowerInvariant();
        // Mensaje propio del trigger admin_inmutable (ya está en español y es claro): se devuelve tal cual
        if (m.Contains("no se puede modificar")) return new ErrorCampus(e.Message.Trim(), TipoErrorCampus.DatoInvalido);
        if (m.Contains("unique")) return new ErrorCampus(MsgUnico, TipoErrorCampus.DatoInvalido);
        if (m.Contains("foreign key")) return new ErrorCampus("El lugar o nodo elegido ya no existe. Actualiza la lista.", TipoErrorCampus.DatoInvalido);
        if (m.Contains("not null") || m.Contains("may not be null")) return new ErrorCampus("Falta un dato obligatorio.", TipoErrorCampus.DatoInvalido);
        if (m.Contains("check") || m.Contains("constraint failed") || e.Result == SQLite3.Result.Constraint)
            return new ErrorCampus("Algún dato está fuera del rango permitido.", TipoErrorCampus.DatoInvalido);
        // El detalle técnico solo va al log; al usuario, un mensaje claro
        UnityEngine.Debug.LogError("Base de datos: " + e);
        return new ErrorCampus("No se pudo guardar. Intenta de nuevo.", TipoErrorCampus.BaseDatos);
    }

    private static int UltimoId() => (int)Db.ExecuteScalar<long>("SELECT last_insert_rowid()");

    private static bool Existe(string tabla, int id) =>
        Db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {tabla} WHERE id = ?", id) > 0;   // 'tabla' nunca viene del usuario

    private static (int, int) Par(int a, int b)
    {
        if (a == b) throw new ErrorCampus("Un nodo no puede conectarse consigo mismo.", TipoErrorCampus.DatoInvalido);
        return a < b ? (a, b) : (b, a);
    }
}
