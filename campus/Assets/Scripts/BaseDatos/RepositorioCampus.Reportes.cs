using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Búsquedas registradas, estadísticas mensuales y ranking del reporte PDF.</summary>
public static partial class RepositorioCampus
{
    /// <summary>Cuántas búsquedas hay registradas en la base abierta.</summary>
    public static int ContarBusquedas() => Db.ExecuteScalar<int>("SELECT COUNT(*) FROM busquedas_log");

#if UNITY_EDITOR
    /// <summary>
    /// Borra TODAS las búsquedas de la base abierta (en el Editor, la maestra). Para quitar las
    /// búsquedas hechas probando en Play antes de compilar el APK. Devuelve cuántas borró.
    /// </summary>
    public static int BorrarBusquedas()
    {
        int n = 0;
        Escribir(() => { n = Db.Execute("DELETE FROM busquedas_log"); });
        return n;
    }
#endif

    /// <summary>
    /// Fecha en el formato que guarda SQLite (datetime('now','localtime')): "yyyy-MM-dd HH:mm:ss".
    /// Siempre con cultura invariante: con otra cultura el separador o los dígitos podrían cambiar
    /// y la comparación de textos dejaría de ordenar bien.
    /// </summary>
    public static string FechaSql(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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

        // El JOIN solo trae búsquedas desde el inicio del mes anterior (usa idx_busquedas_lugar_fecha):
        // el historial viejo no se recorre aunque crezca. LEFT JOIN: los lugares sin búsquedas salen en 0.
        List<FilaEstadistica> filas = Db.Query<FilaEstadistica>(
            @"SELECT l.nombre, l.categoria,
                     SUM(CASE WHEN b.fecha_hora >= ? THEN 1 ELSE 0 END) AS mes_actual,
                     SUM(CASE WHEN b.fecha_hora < ? THEN 1 ELSE 0 END) AS mes_anterior
              FROM lugares l
              LEFT JOIN busquedas_log b ON b.lugar_id = l.id AND b.fecha_hora >= ?
              GROUP BY l.id, l.nombre, l.categoria
              ORDER BY mes_actual DESC, l.nombre COLLATE NOCASE",
            FechaSql(inicioMes), FechaSql(inicioMes), FechaSql(inicioAnterior));

        return filas.ConvertAll(x => new EstadisticaLugar
        {
            nombre = x.nombre, categoria = x.categoria, mesActual = x.mes_actual, mesAnterior = x.mes_anterior,
        });
    }

    // ==================================================================
    // Reporte
    // ==================================================================
    /// <summary>Ranking histórico (todas las búsquedas). Solo lugares con al menos una búsqueda.</summary>
    public static List<LugarRanking> RankingBusquedas(int limite, out int totalBusquedas) =>
        RankingBusquedas(limite, DateTime.MinValue, DateTime.MaxValue, out totalBusquedas);

    /// <summary>Ranking de búsquedas con fecha en [desde, hasta). Solo lugares con al menos una búsqueda.</summary>
    public static List<LugarRanking> RankingBusquedas(int limite, DateTime desde, DateTime hasta, out int totalBusquedas)
    {
        string d = desde == DateTime.MinValue ? "" : FechaSql(desde);
        string h = hasta == DateTime.MaxValue ? "9999" : FechaSql(hasta);

        List<FilaRanking> filas = Db.Query<FilaRanking>(
            @"SELECT l.nombre, l.categoria, COUNT(b.id) AS total
              FROM lugares l
              JOIN busquedas_log b ON b.lugar_id = l.id
              WHERE b.fecha_hora >= ? AND b.fecha_hora < ?
              GROUP BY l.id, l.nombre, l.categoria
              ORDER BY total DESC, l.nombre COLLATE NOCASE
              LIMIT ?", d, h, limite);

        totalBusquedas = Db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM busquedas_log WHERE fecha_hora >= ? AND fecha_hora < ?", d, h);
        return filas.ConvertAll(f => new LugarRanking { nombre = f.nombre, categoria = f.categoria, total = f.total });
    }
}
