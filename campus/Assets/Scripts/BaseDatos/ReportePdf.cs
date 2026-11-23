using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Genera el PDF "Reporte de lugares más buscados" sin librerías externas
/// Encabezado con banda verde institucional (#1c7126), periodo, total y tabla del ranking.
/// Usa las fuentes estándar Helvetica con codificación WinAnsi (soporta tildes y ñ).
/// </summary>
public static class ReportePdf
{
    private const float AnchoPagina = 595f;   // A4 en puntos
    private const float AltoPagina = 842f;

    private static readonly float[] AnchosColumnas = { 30f, 220f, 130f, 70f };
    private static readonly string[] Encabezados = { "#", "Lugar", "Categoría", "Búsquedas" };
    private const float AltoFila = 22f;

    // Verde institucional #1c7126 en RGB 0-1
    private const string VerdePdf = "0.11 0.443 0.149";

    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    };

    /// <summary>"noviembre de 2026"</summary>
    public static string NombrePeriodo(DateTime mes) => $"{Meses[mes.Month - 1]} de {mes.Year}";

    /// <summary>
    /// "1 de octubre – 2 de noviembre de 2026" (días incluidos). Si los años difieren, cada fecha lleva
    /// el suyo: "1 de diciembre de 2026 – 2 de enero de 2027".
    /// </summary>
    public static string NombreRango(DateTime desde, DateTime hasta)
    {
        string fin = $"{hasta.Day} de {Meses[hasta.Month - 1]} de {hasta.Year}";
        string inicio = $"{desde.Day} de {Meses[desde.Month - 1]}" + (desde.Year != hasta.Year ? $" de {desde.Year}" : "");
        return inicio + " – " + fin;
    }

    /// <summary>Crea el PDF en memoria con el ranking dado (periodo = mes de 'fecha').</summary>
    public static byte[] Generar(IList<RepositorioCampus.LugarRanking> ranking, int totalBusquedas, DateTime fecha) =>
        Generar(ranking, totalBusquedas, fecha, "Periodo: " + NombrePeriodo(fecha));

    /// <summary>Crea el PDF en memoria con el ranking dado y el texto del periodo como subtítulo.</summary>
    public static byte[] Generar(IList<RepositorioCampus.LugarRanking> ranking, int totalBusquedas, DateTime fecha, string periodo)
    {
        string contenido = DibujarPagina(ranking, totalBusquedas, fecha, periodo ?? "");
        return Ensamblar(contenido);
    }

    // ------------------------------------------------------------------
    // Contenido de la página
    // ------------------------------------------------------------------
    private static string DibujarPagina(IList<RepositorioCampus.LugarRanking> ranking, int total, DateTime fecha, string periodo)
    {
        StringBuilder s = new StringBuilder();
        float anchoTabla = 0f;
        foreach (float a in AnchosColumnas) anchoTabla += a;
        float x0 = (AnchoPagina - anchoTabla) / 2f;

        // Banda verde con el título en blanco
        s.Append($"{VerdePdf} rg 0 {N(AltoPagina - 100f)} {N(AnchoPagina)} 100 re f\n");
        TextoCentrado(s, "Reporte de lugares más buscados", "F2", 20f, AnchoPagina / 2f, AltoPagina - 50f, "1 1 1");
        TextoCentrado(s, "Campus Conecta · UMNG Campus Cajicá", "F1", 11f, AnchoPagina / 2f, AltoPagina - 75f, "1 1 1");

        // Periodo y datos generales
        TextoCentrado(s, periodo, "F2", 13f, AnchoPagina / 2f, 712f, VerdePdf);
        // Cultura invariante: con otra cultura '/' y ':' se cambian por los separadores del celular
        string generado = fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        Texto(s, $"Generado: {generado}   |   Total de búsquedas: {total.ToString(CultureInfo.InvariantCulture)}","F1", 10f, x0, 700f - 12f, "0.2 0.2 0.2");

        if (ranking.Count == 0)
        {
            Texto(s, "Aún no hay búsquedas registradas en este periodo.", "F1", 11f, x0, 650f, "0.3 0.3 0.3");
            return s.ToString();
        }

        // Filas: encabezado + datos
        List<string[]> filas = new List<string[]> { Encabezados };
        for (int i = 0; i < ranking.Count; i++)
        {
            filas.Add(new[]
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Recortar(ranking[i].nombre, 40),
                Recortar(ranking[i].categoria, 24),
                ranking[i].total.ToString(CultureInfo.InvariantCulture),
            });
        }

        float yTop = 670f;

        for (int f = 0; f < filas.Count; f++)
        {
            float y = yTop - (f + 1) * AltoFila;   // borde inferior de la fila

            // Fondo: verde en el encabezado, filas alternas gris claro
            string fondo = f == 0 ? VerdePdf : (f % 2 == 0 ? "0.941 0.953 0.969" : null);
            if (fondo != null) s.Append($"{fondo} rg {N(x0)} {N(y)} {N(anchoTabla)} {N(AltoFila)} re f\n");

            string fuente = f == 0 ? "F2" : "F1";
            string color = f == 0 ? "1 1 1" : "0 0 0";
            float x = x0;

            for (int c = 0; c < AnchosColumnas.Length; c++)
            {
                float yTexto = y + 7f;
                bool centrado = c == 0 || c == 3;
                if (centrado) TextoCentrado(s, filas[f][c], fuente, 10f, x + AnchosColumnas[c] / 2f, yTexto, color);
                else Texto(s, filas[f][c], fuente, 10f, x + 6f, yTexto, color);
                x += AnchosColumnas[c];
            }
        }

        // Cuadrícula
        float yBase = yTop - filas.Count * AltoFila;
        s.Append("0.773 0.8 0.839 RG 0.5 w\n");
        for (int f = 0; f <= filas.Count; f++)
        {
            float y = yTop - f * AltoFila;
            s.Append($"{N(x0)} {N(y)} m {N(x0 + anchoTabla)} {N(y)} l S\n");
        }
        float xl = x0;
        for (int c = 0; c <= AnchosColumnas.Length; c++)
        {
            s.Append($"{N(xl)} {N(yTop)} m {N(xl)} {N(yBase)} l S\n");
            if (c < AnchosColumnas.Length) xl += AnchosColumnas[c];
        }

        return s.ToString();
    }

    private static void Texto(StringBuilder s, string texto, string fuente, float tam, float x, float y, string color)
    {
        s.Append($"BT {color} rg /{fuente} {N(tam)} Tf {N(x)} {N(y)} Td ({Escapar(texto)}) Tj ET\n");
    }

    private static void TextoCentrado(StringBuilder s, string texto, string fuente, float tam, float xCentro, float y, string color)
    {
        float ancho = AnchoAprox(texto, tam, fuente == "F2");
        Texto(s, texto, fuente, tam, xCentro - ancho / 2f, y, color);
    }

    // Ancho aproximado en Helvetica (los dígitos miden exactamente 0.556 em)
    private static float AnchoAprox(string t, float tam, bool negrita)
    {
        float em = 0f;
        foreach (char ch in t)
        {
            if (char.IsDigit(ch)) em += 0.556f;
            else if (ch == ' ') em += 0.278f;
            else if ("iljtf.,:;'|!I".IndexOf(ch) >= 0) em += 0.28f;
            else if ("mwMW".IndexOf(ch) >= 0) em += 0.85f;
            else if (char.IsUpper(ch)) em += 0.68f;
            else em += 0.54f;
        }
        return em * tam * (negrita ? 1.05f : 1f);
    }

    private static string Recortar(string t, int max)
    {
        t = t ?? "";
        return t.Length <= max ? t : t.Substring(0, max - 1) + "…";
    }

    private static string Escapar(string t)
    {
        StringBuilder r = new StringBuilder();
        foreach (char ch in t)
        {
            if (ch == '(' || ch == ')' || ch == '\\') r.Append('\\').Append(ch);
            else if (ch == '…') r.Append("\\205");                // puntos suspensivos en WinAnsi
            else if (ch == '–') r.Append("\\226");                // raya corta (en dash) en WinAnsi
            else if (ch > 255) r.Append('?');                      // fuera de WinAnsi
            else r.Append(ch);
        }
        return r.ToString();
    }

    // Un byte por carácter (los textos ya se filtraron a WinAnsi en Escapar). No depende de
    // Encoding.GetEncoding, que en algunos builds de Android no trae ISO-8859-1.
    private static byte[] Latin1(string t)
    {
        byte[] b = new byte[t.Length];
        for (int i = 0; i < t.Length; i++) b[i] = t[i] <= 255 ? (byte)t[i] : (byte)'?';
        return b;
    }

    private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Estructura del archivo PDF (objetos + tabla xref)
    // ------------------------------------------------------------------
    private static byte[] Ensamblar(string contenidoPagina)
    {
        byte[] stream = Latin1(contenidoPagina);

        string[] objetos =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(AnchoPagina)} {N(AltoPagina)}] " +
                "/Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
            null, // contenido (stream), se escribe aparte
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Title (Reporte de busquedas - Campus Conecta) /Producer (Campus Conecta) >>",
        };

        using (MemoryStream ms = new MemoryStream())
        {
            void Escribir(string t) { byte[] b = Latin1(t); ms.Write(b, 0, b.Length); }

            Escribir("%PDF-1.4\n%âãÏÓ\n");
            long[] offsets = new long[objetos.Length];

            for (int i = 0; i < objetos.Length; i++)
            {
                offsets[i] = ms.Position;
                Escribir($"{i + 1} 0 obj\n");
                if (i == 3)
                {
                    Escribir($"<< /Length {stream.Length} >>\nstream\n");
                    ms.Write(stream, 0, stream.Length);
                    Escribir("\nendstream\n");
                }
                else
                {
                    Escribir(objetos[i] + "\n");
                }
                Escribir("endobj\n");
            }

            long inicioXref = ms.Position;
            Escribir($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
            foreach (long o in offsets) Escribir(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Escribir($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R /Info 7 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");

            return ms.ToArray();
        }
    }
}
