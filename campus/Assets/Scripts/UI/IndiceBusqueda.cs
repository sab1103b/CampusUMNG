using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Índice de búsqueda de lugares y espacios interiores (extraído de InterfazCampus).
/// Guarda los textos normalizados (minúsculas, sin tildes) una sola vez por carga de datos y resuelve las
/// coincidencias por texto y por categoría. No toca la interfaz: InterfazCampus solo pinta los resultados.
/// </summary>
public class IndiceBusqueda
{
    /// <summary>Un resultado: un lugar (servicio = null) o un espacio interior con su lugar dueño.</summary>
    public struct Resultado
    {
        public LugaresService.Lugar lugar;
        public LugaresService.Servicio servicio;
    }

    private struct EntradaLugar
    {
        public LugaresService.Lugar lugar;
        public string nombre, categoria, raizCategoria, detalle;
    }

    private struct EntradaServicio
    {
        public LugaresService.Servicio servicio;
        public LugaresService.Lugar dueño;
        public string nombre, categoria, raizCategoria, detalle, categoriaDueño, raizCategoriaDueño;
    }

    private readonly List<EntradaLugar> lugares = new List<EntradaLugar>();
    private readonly List<EntradaServicio> servicios = new List<EntradaServicio>();
    private bool listo;
    private object listaLugaresIndexada, listaServiciosIndexada;

    /// <summary>Marca el índice para recalcularse con los datos nuevos (en la próxima búsqueda).</summary>
    public void Invalidar() { listo = false; }

    /// <summary>Recalcula el índice solo si cambiaron las listas de datos (o se invalidó).</summary>
    public void Preparar(LugaresService datos)
    {
        if (datos == null) return;
        if (listo && ReferenceEquals(listaLugaresIndexada, datos.Lugares)
                  && ReferenceEquals(listaServiciosIndexada, datos.Servicios)) return;

        lugares.Clear();
        foreach (LugaresService.Lugar l in datos.Lugares)
        {
            string cat = Normalizar(l.categoria);
            lugares.Add(new EntradaLugar
            {
                lugar = l,
                nombre = Normalizar(l.nombre),
                categoria = cat,
                raizCategoria = cat.TrimEnd('s'),
                detalle = Normalizar(l.descripcion) + "\n" + Normalizar(l.indicacion),
            });
        }

        servicios.Clear();
        foreach (LugaresService.Servicio s in datos.Servicios)
        {
            LugaresService.Lugar dueño = datos.BuscarPorId(s.lugar_id);
            if (dueño == null) continue;
            string cat = Normalizar(s.categoria), catDueño = Normalizar(dueño.categoria);
            servicios.Add(new EntradaServicio
            {
                servicio = s,
                dueño = dueño,
                nombre = Normalizar(s.nombre),
                categoria = cat,
                raizCategoria = cat.TrimEnd('s'),
                // Incluye el nombre de su lugar: "camacho" también encuentra los espacios del Camacho Leiva
                // (el lugar sale primero: los lugares se listan antes que los servicios)
                detalle = Normalizar(s.descripcion) + "\n" + Normalizar(s.indicacion) + "\n" + Normalizar(dueño.nombre),
                categoriaDueño = catDueño,
                raizCategoriaDueño = catDueño.TrimEnd('s'),
            });
        }

        listaLugaresIndexada = datos.Lugares;
        listaServiciosIndexada = datos.Servicios;
        listo = true;
    }

    // Candidato con su puntaje de relevancia y su orden de llegada (desempate: orden estable)
    private struct Candidato
    {
        public Resultado resultado;
        public int puntaje, orden;
    }

    // Listas temporales reutilizables (no se crean en cada búsqueda)
    private readonly List<Candidato> candidatos = new List<Candidato>();
    private readonly List<string> palabras = new List<string>();
    private static readonly Comparison<Candidato> PorRelevancia = (a, b) =>
        a.puntaje != b.puntaje ? a.puntaje.CompareTo(b.puntaje) : a.orden.CompareTo(b.orden);

    /// <summary>
    /// Llena <paramref name="salida"/> con hasta <paramref name="max"/> resultados ordenados por relevancia y devuelve
    /// el total de coincidencias. <paramref name="q"/> y <paramref name="filtro"/> ya normalizados; filtro null = todas.
    /// Con varias palabras ("biblioteca camacho") cada una debe aparecer en el nombre, la categoría o el detalle.
    /// Relevancia: el nombre empieza con q (0), el nombre contiene q (1), la categoría (2), el detalle (3); a igual
    /// puntaje, el orden de siempre (lugares antes que espacios interiores). Se ordena ANTES de cortar en max.
    /// Con texto vacío y un filtro, los espacios interiores entran solo por su propia categoría.
    /// </summary>
    public int Buscar(string q, string filtro, int max, List<Resultado> salida)
    {
        salida.Clear();
        candidatos.Clear();
        PartirPalabras(q);
        string raizFiltro = filtro != null ? filtro.TrimEnd('s') : null;

        foreach (EntradaLugar e in lugares)
        {
            if (!CoincideCategoria(e.categoria, e.raizCategoria, filtro, raizFiltro)) continue;
            if (q.Length > 0 && !Coincide(e.nombre, e.categoria, e.detalle)) continue;
            candidatos.Add(new Candidato { resultado = new Resultado { lugar = e.lugar }, puntaje = Puntaje(e.nombre, e.categoria, q), orden = candidatos.Count });
        }

        // Servicios (aulas, biblioteca, laboratorios, oficinas...): al escribir, o al elegir una categoría
        // que coincida con la suya (por ejemplo "Biblioteca" muestra la biblioteca del Camacho Leiva)
        bool porCategoria = q.Length == 0 && filtro != null;
        if (q.Length > 0 || porCategoria)
        {
            foreach (EntradaServicio e in servicios)
            {
                bool coincideServicio = CoincideCategoria(e.categoria, e.raizCategoria, filtro, raizFiltro);
                if (porCategoria) { if (!coincideServicio) continue; }
                else if (!coincideServicio && !CoincideCategoria(e.categoriaDueño, e.raizCategoriaDueño, filtro, raizFiltro)) continue;
                if (q.Length > 0 && !Coincide(e.nombre, e.categoria, e.detalle)) continue;
                candidatos.Add(new Candidato
                {
                    resultado = new Resultado { lugar = e.dueño, servicio = e.servicio },
                    puntaje = Puntaje(e.nombre, e.categoria, q),
                    orden = candidatos.Count,
                });
            }
        }

        if (q.Length > 0) candidatos.Sort(PorRelevancia);   // sin texto todos valen 0: queda el orden de siempre
        int n = Math.Min(max, candidatos.Count);
        for (int i = 0; i < n; i++) salida.Add(candidatos[i].resultado);
        int total = candidatos.Count;
        candidatos.Clear();   // no retiene referencias hasta la próxima búsqueda
        return total;
    }

    // q ya normalizado (Trim incluido) se parte por espacios en la lista reutilizable "palabras"
    private void PartirPalabras(string q)
    {
        palabras.Clear();
        int inicio = -1;
        for (int i = 0; i <= q.Length; i++)
        {
            bool corte = i == q.Length || char.IsWhiteSpace(q[i]);
            if (!corte) { if (inicio < 0) inicio = i; continue; }
            if (inicio >= 0) { palabras.Add(q.Substring(inicio, i - inicio)); inicio = -1; }
        }
        // Con varias palabras se ignoran los conectores ("baño en camacho" = "baño camacho"); si solo hay
        // conectores se conservan, para que una búsqueda corta siga funcionando como antes
        int utiles = 0;
        foreach (string p in palabras) if (!EsConector(p)) utiles++;
        if (utiles > 0 && utiles < palabras.Count) palabras.RemoveAll(EsConectorPred);
    }

    private static readonly HashSet<string> Conectores = new HashSet<string>
        { "de", "del", "la", "las", "el", "los", "en", "y", "a", "al", "un", "una", "por", "para", "con" };
    private static bool EsConector(string p) => Conectores.Contains(p);
    private static readonly Predicate<string> EsConectorPred = EsConector;

    // Cada palabra debe estar en el nombre o la categoría (desde 1 letra) o en el detalle (descripción, indicaciones
    // y, para un espacio interior, el nombre de su lugar; desde 3 letras)
    private bool Coincide(string nombre, string categoria, string detalle)
    {
        foreach (string p in palabras)
            if (nombre.IndexOf(p, StringComparison.Ordinal) < 0 && categoria.IndexOf(p, StringComparison.Ordinal) < 0 &&
                (p.Length < 3 || detalle.IndexOf(p, StringComparison.Ordinal) < 0)) return false;
        return true;
    }

    // Relevancia: 0 = el nombre empieza con el texto, 1 = el nombre lo contiene, 2 = la categoría, 3 = el resto.
    // Con varias palabras se usa la palabra que mejor encaja en el nombre (todas ya coinciden en algún campo).
    private int Puntaje(string nombre, string categoria, string q)
    {
        if (q.Length == 0) return 0;
        int mejor = PuntajeDe(nombre, categoria, q);
        if (palabras.Count > 1)
            foreach (string p in palabras) mejor = Math.Min(mejor, PuntajeDe(nombre, categoria, p));
        return mejor;
    }

    private static int PuntajeDe(string nombre, string categoria, string q)
    {
        if (nombre.StartsWith(q, StringComparison.Ordinal)) return 0;
        if (nombre.IndexOf(q, StringComparison.Ordinal) >= 0) return 1;
        if (categoria.IndexOf(q, StringComparison.Ordinal) >= 0) return 2;
        return 3;
    }

    // a / b ya normalizados; "Aulas" coincide con "Aula", "Cafetería" con "Cafeteria"...
    public static bool CoincideCategoria(string a, string raizA, string b, string raizB)
    {
        if (b == null) return true;          // sin filtro o "Todos"
        if (a.Length == 0) return false;     // sin categoría: no entra en ningún filtro
        if (a == b || raizA == raizB) return true;   // singular/plural ("aula" = "aulas")
        // Prefijo solo con raíces de 3 letras o más: una raíz corta ("a", "ba") no debe abarcar otras categorías
        return (raizB.Length >= 3 && a.StartsWith(raizB, StringComparison.Ordinal)) ||
               (raizA.Length >= 3 && b.StartsWith(raizA, StringComparison.Ordinal));
    }

    /// <summary>Minúsculas y sin tildes: "Cafetería" = "cafeteria".</summary>
    public static string Normalizar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim().ToLowerInvariant())
        {
            switch (ch)
            {
                case 'á': case 'à': case 'ä': sb.Append('a'); break;
                case 'é': case 'è': case 'ë': sb.Append('e'); break;
                case 'í': case 'ì': case 'ï': sb.Append('i'); break;
                case 'ó': case 'ò': case 'ö': sb.Append('o'); break;
                case 'ú': case 'ù': case 'ü': sb.Append('u'); break;
                case 'ñ': sb.Append('n'); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
