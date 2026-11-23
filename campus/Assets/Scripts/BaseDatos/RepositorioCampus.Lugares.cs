using System;
using System.Collections.Generic;
using SQLite4Unity3d;

/// <summary>Lugares, servicios, nodos, caminos y modelos 3D (con sus validaciones).</summary>
public static partial class RepositorioCampus
{
    // ==================================================================
    // Lugares
    // ==================================================================
    internal static string CrearLugar(AdminService.LugarEditable d)
    {
        ValidarLugar(d);
        int lugarId = 0;
        int? nodoId = null;

        Escribir(() =>
        {
            ExigirNombreLibre(d.nombre, 0);
            // El id nunca se reutiliza (meta.ultimo_id_lugar, como ultimo_id_admin): un lugar borrado no "renace"
            // con su id, así el historial de búsquedas (p. ej. el que pasa del celular al actualizar) no cae en otro lugar
            lugarId = Math.Max(Db.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM lugares"),
                               LeerEnteroMeta(ClaveUltimoIdLugar)) + 1;
            Db.Execute(
                @"INSERT INTO lugares (id, nombre, categoria, descripcion, indicacion, horario, latitud, longitud)
                  VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                lugarId, d.nombre.Trim(), d.categoria.Trim(), d.descripcion, d.indicacion, d.horario, d.latitud, d.longitud);
            GuardarUltimoIdLugar(lugarId);

            if (d.conectar_con_nodo > 0)
            {
                if (!Existe("nodos", d.conectar_con_nodo)) throw new ErrorCampus("El nodo a conectar no existe.", TipoErrorCampus.NoEncontrado);

                // El nodo-puerta lleva el nombre del lugar (hasta 120) recortado al límite del nodo (CHECK ≤ 80)
                Db.Execute("INSERT INTO nodos (nombre, latitud, longitud, lugar_id) VALUES (?, ?, ?, ?)",
                           Recortar(d.nombre.Trim(), BaseDatosLocal.MaxNombreNodo), d.latitud, d.longitud, lugarId);
                nodoId = UltimoId();

                (int a, int b) = Par(nodoId.Value, d.conectar_con_nodo);
                Db.Execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", a, b);
            }
        });

        return $"{{\"id\":{lugarId},\"nodo_id\":{(nodoId.HasValue ? nodoId.Value.ToString() : "null")}}}";
    }

    internal static void ActualizarLugar(int id, AdminService.LugarEditable d)
    {
        ValidarLugar(d);
        Escribir(() =>
        {
            ExigirNombreLibre(d.nombre, id);
            // Nombre anterior (leído en la misma transacción): solo se renombran las puertas que lo llevaban
            string anterior = Db.ExecuteScalar<string>("SELECT nombre FROM lugares WHERE id = ?", id);
            int n = Db.Execute(
                @"UPDATE lugares
                  SET nombre = ?, categoria = ?, descripcion = ?, indicacion = ?, horario = ?, latitud = ?, longitud = ?
                  WHERE id = ?",
                d.nombre.Trim(), d.categoria.Trim(), d.descripcion, d.indicacion, d.horario, d.latitud, d.longitud, id);
            if (n == 0) throw new ErrorCampus("Lugar no encontrado.", TipoErrorCampus.NoEncontrado);
            // Sus nodos-puerta llevan el nombre del lugar (como al crearlo): solo cambia el nombre, no la posición
            // ni los caminos. Una puerta con nombre propio (p. ej. "Entrada norte") lo conserva: solo se renombran
            // las que tenían el nombre anterior (recortado como al crearlas) o ninguno.
            Db.Execute("UPDATE nodos SET nombre = ? WHERE lugar_id = ? AND (nombre IS NULL OR trim(nombre) = '' OR nombre = ?)",
                       Recortar(d.nombre.Trim(), BaseDatosLocal.MaxNombreNodo), id,
                       Recortar((anterior ?? "").Trim(), BaseDatosLocal.MaxNombreNodo));
        });
    }

    /// <summary>Su nodo-puerta queda como nodo de paso; sus servicios y su historial de búsquedas se borran.</summary>
    internal static void EliminarLugar(int id)
    {
        Escribir(() =>
        {
            // Se anota el id más alto antes de borrar: si se borra el último, su id no se vuelve a usar
            int maximo = Math.Max(Db.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM lugares"),
                                  LeerEnteroMeta(ClaveUltimoIdLugar));
            if (Db.Execute("DELETE FROM lugares WHERE id = ?", id) == 0) throw new ErrorCampus("Lugar no encontrado.", TipoErrorCampus.NoEncontrado);
            GuardarUltimoIdLugar(maximo);
        });
    }

    /// <summary>
    /// Dos lugares no pueden llamarse igual: la búsqueda y el reporte los mostrarían repetidos. Se comparan sin
    /// distinguir mayúsculas ni tildes ("Ágora" = "agora") y sin espacios sobrantes, también entre palabras
    /// ("Aula  1" = "Aula 1"). 'idPropio' = el lugar que se edita (0 al crear). Solo nombres equivalentes: los
    /// lugares muy cercanos con nombre distinto los sigue avisando el formulario. Se compara en C# porque
    /// lower() de SQLite solo pliega ASCII.
    /// </summary>
    private static void ExigirNombreLibre(string nombre, int idPropio)
    {
        string buscado = ClaveNombre(nombre);
        foreach (FilaLugar f in Db.Query<FilaLugar>("SELECT id, nombre FROM lugares WHERE id <> ?", idPropio))
            if (ClaveNombre(f.nombre) == buscado)
                throw new ErrorCampus("Ya existe un lugar con ese nombre.", TipoErrorCampus.DatoInvalido);
    }

    private static readonly System.Text.RegularExpressions.Regex Espacios = new System.Text.RegularExpressions.Regex(@"\s+");

    /// <summary>Nombre para comparar: sin espacios sobrantes (también internos), en minúsculas y sin tildes.</summary>
    internal static string ClaveNombre(string nombre)
    {
        string t = Espacios.Replace((nombre ?? "").Trim(), " ").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        System.Text.StringBuilder sb = new System.Text.StringBuilder(t.Length);
        foreach (char ch in t)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static string Recortar(string texto, int max) =>
        texto != null && texto.Length > max ? texto.Substring(0, max).TrimEnd() : texto;

    private const string ClaveUltimoIdLugar = "ultimo_id_lugar";

    private static void GuardarUltimoIdLugar(int id) =>
        Db.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES (?, ?)",
                   ClaveUltimoIdLugar, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // ==================================================================
    // Servicios
    // ==================================================================
    internal static string CrearServicio(AdminService.ServicioEditable d)
    {
        ValidarServicio(d);
        int id = 0;
        Escribir(() =>
        {
            if (!Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe.", TipoErrorCampus.NoEncontrado);
            ExigirServicioLibre(d.lugar_id, d.nombre, 0);
            Db.Execute(
                @"INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
                  VALUES (?, ?, ?, ?, ?, ?, ?)",
                d.lugar_id, d.nombre.Trim(), d.categoria, d.piso, d.descripcion, d.indicacion, d.horario);
            id = UltimoId();
        });
        return $"{{\"id\":{id}}}";
    }

    internal static void ActualizarServicio(int id, AdminService.ServicioEditable d)
    {
        ValidarServicio(d);
        Escribir(() =>
        {
            if (!Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe.", TipoErrorCampus.NoEncontrado);
            ExigirServicioLibre(d.lugar_id, d.nombre, id);
            int n = Db.Execute(
                @"UPDATE servicios
                  SET lugar_id = ?, nombre = ?, categoria = ?, piso = ?, descripcion = ?, indicacion = ?, horario = ?
                  WHERE id = ?",
                d.lugar_id, d.nombre.Trim(), d.categoria, d.piso, d.descripcion, d.indicacion, d.horario, id);
            if (n == 0) throw new ErrorCampus("No encontramos ese espacio interior. Actualiza la lista.", TipoErrorCampus.NoEncontrado);
        });
    }

    /// <summary>
    /// Dentro de un mismo lugar no puede haber dos espacios interiores con el mismo nombre (misma comparación
    /// que los lugares: ClaveNombre). En lugares distintos sí ("Baños" en varios edificios). 'idPropio' = el
    /// servicio que se edita (0 al crear).
    /// </summary>
    private static void ExigirServicioLibre(int lugarId, string nombre, int idPropio)
    {
        string buscado = ClaveNombre(nombre);
        foreach (FilaLugar f in Db.Query<FilaLugar>("SELECT id, nombre FROM servicios WHERE lugar_id = ? AND id <> ?", lugarId, idPropio))
            if (ClaveNombre(f.nombre) == buscado)
                throw new ErrorCampus("Ya existe un espacio interior con ese nombre en este lugar.", TipoErrorCampus.DatoInvalido);
    }

    internal static void EliminarServicio(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM servicios WHERE id = ?", id) == 0) throw new ErrorCampus("No encontramos ese espacio interior. Actualiza la lista.", TipoErrorCampus.NoEncontrado);
        });
    }

    // ==================================================================
    // Nodos y conexiones
    // ==================================================================
    internal static string CrearNodo(AdminService.NodoEditable d)
    {
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, BaseDatosLocal.MaxNombreNodo, "El nombre del nodo");
        int id = 0;

        Escribir(() =>
        {
            if (d.lugar_id > 0 && !Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar o el nodo indicado no existe.", TipoErrorCampus.NoEncontrado);
            if (d.conectar_con_nodo > 0 && !Existe("nodos", d.conectar_con_nodo)) throw new ErrorCampus("El lugar o el nodo indicado no existe.", TipoErrorCampus.NoEncontrado);

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

    internal static void ActualizarNodo(int id, AdminService.NodoEditable d)
    {
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, BaseDatosLocal.MaxNombreNodo, "El nombre del nodo");

        Escribir(() =>
        {
            if (d.lugar_id > 0 && !Existe("lugares", d.lugar_id)) throw new ErrorCampus("El lugar indicado no existe.", TipoErrorCampus.NoEncontrado);
            int n = Db.Execute("UPDATE nodos SET nombre = ?, latitud = ?, longitud = ?, lugar_id = ? WHERE id = ?",
                               d.nombre, d.latitud, d.longitud, d.lugar_id > 0 ? (object)d.lugar_id : null, id);
            if (n == 0) throw new ErrorCampus("Nodo no encontrado.", TipoErrorCampus.NoEncontrado);
        });
    }

    /// <summary>Sus conexiones se borran solas. Ojo: puede dejar partes de la red desconectadas.</summary>
    internal static void EliminarNodo(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM nodos WHERE id = ?", id) == 0) throw new ErrorCampus("Nodo no encontrado.", TipoErrorCampus.NoEncontrado);
        });
    }

    internal static void CrearConexion(int nodoA, int nodoB)
    {
        (int a, int b) = Par(nodoA, nodoB);
        Escribir(() =>
        {
            if (!Existe("nodos", a) || !Existe("nodos", b)) throw new ErrorCampus("Alguno de los nodos no existe.", TipoErrorCampus.NoEncontrado);
            if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM conexiones WHERE nodo_a = ? AND nodo_b = ?", a, b) > 0)
                throw new ErrorCampus("Esos nodos ya están conectados.", TipoErrorCampus.DatoInvalido);
            Db.Execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", a, b);
        });
    }

    internal static void EliminarConexion(int nodoA, int nodoB)
    {
        (int a, int b) = Par(nodoA, nodoB);
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM conexiones WHERE nodo_a = ? AND nodo_b = ?", a, b) == 0)
                throw new ErrorCampus("Esa conexión no existe.", TipoErrorCampus.NoEncontrado);
        });
    }

    // ==================================================================
    // Modelos 3D (edificaciones agregadas por el administrador)
    // ==================================================================
    internal static string CrearModelo(AdminService.ModeloEditable d)
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

    internal static void ActualizarModelo(int id, AdminService.ModeloEditable d)
    {
        ValidarModelo(d);
        Escribir(() =>
        {
            int n = Db.Execute(
                @"UPDATE modelos SET tipo = ?, nombre = ?, latitud = ?, longitud = ?, rotacion = ?, ancho = ?, largo = ?, alto = ?
                  WHERE id = ?",
                d.tipo, d.nombre, d.latitud, d.longitud, d.rotacion, d.ancho, d.largo, d.alto, id);
            if (n == 0) throw new ErrorCampus("Modelo no encontrado.", TipoErrorCampus.NoEncontrado);
        });
    }

    internal static void EliminarModelo(int id)
    {
        Escribir(() =>
        {
            if (Db.Execute("DELETE FROM modelos WHERE id = ?", id) == 0) throw new ErrorCampus("Modelo no encontrado.", TipoErrorCampus.NoEncontrado);
        });
    }

    private static void ValidarModelo(AdminService.ModeloEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del modelo.", TipoErrorCampus.DatoInvalido);
        if (d.tipo != "cubo" && d.tipo != "rectangulo" && d.tipo != "cilindro")
            throw new ErrorCampus("Tipo de modelo no válido (cubo, rectángulo o cilindro).", TipoErrorCampus.DatoInvalido);
        ValidarCoordenadas(d.latitud, d.longitud);
        ValidarLargo(d.nombre, 120, "El nombre del modelo");
        if (!Finito(d.ancho) || !Finito(d.largo) || !Finito(d.alto) || !Finito(d.rotacion))
            throw new ErrorCampus("Las medidas o el giro del modelo no son válidos.", TipoErrorCampus.DatoInvalido);
        d.rotacion = NormalizarGrados(d.rotacion);   // siempre en [0, 360)
        if (d.ancho < 1 || d.ancho > 300 || d.largo < 1 || d.largo > 300) throw new ErrorCampus("El ancho y el largo deben estar entre 1 y 300 m.", TipoErrorCampus.DatoInvalido);
        if (d.alto < 1 || d.alto > 200) throw new ErrorCampus("La altura debe estar entre 1 y 200 m.", TipoErrorCampus.DatoInvalido);
    }

    private static bool Finito(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

    /// <summary>Lleva un ángulo a [0, 360): -90 → 270, 450 → 90.</summary>
    private static float NormalizarGrados(float grados)
    {
        float r = grados % 360f;
        if (r < 0f) r += 360f;
        return r >= 360f ? 0f : r;   // -1e-8 % 360 + 360 puede redondear a 360
    }

    /// <summary>
    /// Reemplaza TODA la red (lo usa el menú Campus > Guardar red en la base de datos).
    /// Los nodos que apuntan a un lugar inexistente quedan como nodos de paso.
    /// </summary>
    internal static int ReemplazarRed(IList<LugaresService.NodoDto> nodos, IList<(int a, int b)> caminos, out int entradasSinLugar)
    {
        int sinLugar = 0;
        try { ReemplazarRedEnTransaccion(nodos, caminos, ref sinLugar); }
        catch (ErrorCampus e) when (e.Message == MsgUnico)
        {
            // Aquí no hay nombres únicos: lo repetido es el número (id) de un nodo
            throw new ErrorCampus("Hay un dato repetido (por ejemplo, un nodo con el mismo número).", TipoErrorCampus.DatoInvalido);
        }
        entradasSinLugar = sinLugar;
        return nodos.Count;
    }

    private static void ReemplazarRedEnTransaccion(IList<LugaresService.NodoDto> nodos, IList<(int a, int b)> caminos, ref int sinLugarTotal)
    {
        int sinLugar = 0;
        Escribir(() =>
        {
            sinLugar = 0;   // si la transacción se reintentara, no se cuenta dos veces
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
                           n.id, Recortar(n.nombre, BaseDatosLocal.MaxNombreNodo), n.latitud, n.longitud, lugar);
            }

            foreach ((int a, int b) in caminos)
                Db.Execute("INSERT OR IGNORE INTO conexiones (nodo_a, nodo_b) VALUES (?, ?)", Math.Min(a, b), Math.Max(a, b));
        });
        sinLugarTotal = sinLugar;
    }

    // ==================================================================
    // Validaciones
    // ==================================================================
    private static void ValidarLugar(AdminService.LugarEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del lugar.", TipoErrorCampus.DatoInvalido);
        ValidarObligatorio(d.nombre, 120, "El nombre");
        ValidarObligatorio(d.categoria, 50, "La categoría");
        ValidarLargo(d.descripcion, 500, "La descripción");
        ValidarLargo(d.indicacion, 300, "La indicación");
        ValidarLargo(d.horario, 120, "El horario");
        ValidarCoordenadas(d.latitud, d.longitud);
    }

    private static void ValidarServicio(AdminService.ServicioEditable d)
    {
        if (d == null) throw new ErrorCampus("Faltan los datos del espacio interior.", TipoErrorCampus.DatoInvalido);
        if (d.lugar_id < 1) throw new ErrorCampus("Elige el edificio al que pertenece el espacio interior.", TipoErrorCampus.DatoInvalido);
        ValidarObligatorio(d.nombre, 120, "El nombre");
        ValidarLargo(d.categoria, 50, "La categoría");
        ValidarLargo(d.piso, 20, "El piso");
        ValidarLargo(d.descripcion, 500, "La descripción");
        ValidarLargo(d.indicacion, 300, "La indicación");
        ValidarLargo(d.horario, 120, "El horario");
    }

    private static void ValidarObligatorio(string valor, int max, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) throw new ErrorCampus($"{campo} es obligatorio.", TipoErrorCampus.DatoInvalido);
        ValidarLargo(valor.Trim(), max, campo);
    }

    private static void ValidarLargo(string valor, int max, string campo)
    {
        if (valor != null && valor.Length > max) throw new ErrorCampus($"{campo} admite máximo {max} caracteres.", TipoErrorCampus.DatoInvalido);
    }

    private static void ValidarCoordenadas(double lat, double lon)
    {
        // NaN pasa todas las comparaciones (NaN < -90 es falso): se rechaza antes, igual que ±∞
        if (double.IsNaN(lat) || double.IsInfinity(lat) || double.IsNaN(lon) || double.IsInfinity(lon))
            throw new ErrorCampus("Las coordenadas no son válidas.", TipoErrorCampus.DatoInvalido);
        if (lat < -90 || lat > 90) throw new ErrorCampus("La latitud debe estar entre -90 y 90.", TipoErrorCampus.DatoInvalido);
        if (lon < -180 || lon > 180) throw new ErrorCampus("La longitud debe estar entre -180 y 180.", TipoErrorCampus.DatoInvalido);
        if (DistanciaAlCentroM(lat, lon) > RadioCampusM)
            throw new ErrorCampus("La ubicación queda fuera del campus. Revisa el punto en el mapa.", TipoErrorCampus.DatoInvalido);
    }

    // Centro aproximado del campus (promedio de los lugares actuales, UMNG Cajicá) y radio máximo aceptado:
    // el campus mide unos cientos de metros, así que 3 km deja margen de sobra y aun así atrapa un punto
    // tocado por error lejos (otro municipio) o coordenadas con lat/lon invertidas.
    internal const double CentroCampusLat = 4.9427, CentroCampusLon = -74.0126;
    internal const double RadioCampusM = 3000.0;

    /// <summary>Distancia en metros al centro del campus (aproximación equirectangular: exacta de sobra a esta escala).</summary>
    internal static double DistanciaAlCentroM(double lat, double lon)
    {
        const double RadioTierraM = 6371000.0, ARad = Math.PI / 180.0;
        double x = (lon - CentroCampusLon) * ARad * Math.Cos((lat + CentroCampusLat) * 0.5 * ARad);
        double y = (lat - CentroCampusLat) * ARad;
        return Math.Sqrt(x * x + y * y) * RadioTierraM;
    }
}
