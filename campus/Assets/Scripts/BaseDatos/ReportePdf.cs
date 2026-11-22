using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Genera el PDF "Reporte de lugares más buscados" sin librerías externas
/// (mismo diseño que tenía el de ReportLab: título, fecha, total y tabla con encabezado azul).
/// Usa las fuentes estándar Helvetica con codificación WinAnsi (soporta tildes y ñ).
/// </summary>
public static class ReportePdf
{
    private const float AnchoPagina = 595f;   // A4 en puntos
    private const float AltoPagina = 842f;

    private static readonly float[] AnchosColumnas = { 30f, 220f, 130f, 70f };
    private static readonly string[] Encabezados = { "#", "Lugar", "Categoría", "Búsquedas" };
    private const float AltoFila = 22f;

    /// <summary>Crea el PDF en memoria con el ranking dado.</summary>
    public static byte[] Generar(IList<RepositorioCampus.LugarRanking> ranking, int totalBusquedas, DateTime fecha)
    {
        string contenido = DibujarPagina(ranking, totalBusquedas, fecha);
        return Ensamblar(contenido);
    }

    // ------------------------------------------------------------------
    // Contenido de la página
    // ------------------------------------------------------------------
    private static string DibujarPagina(IList<RepositorioCampus.LugarRanking> ranking, int total, DateTime fecha)
    {
        StringBuilder s = new StringBuilder();
        float anchoTabla = 0f;
        foreach (float a in AnchosColumnas) anchoTabla += a;
        float x0 = (AnchoPagina - anchoTabla) / 2f;

        // Títulos (centrados como en ReportLab)
        TextoCentrado(s, "Reporte de lugares más buscados", "F2", 18f, AnchoPagina / 2f, 770f, "0 0 0");
        TextoCentrado(s, "Campus Cajicá", "F2", 12f, AnchoPagina / 2f, 745f, "0.2 0.2 0.2");
        Texto(s, $"Generado: {fecha:dd/MM/yyyy HH:mm}   |   Total de búsquedas: {total}", "F1", 10f, x0, 715f, "0 0 0");

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

        float yTop = 690f;

        for (int f = 0; f < filas.Count; f++)
        {
            float y = yTop - (f + 1) * AltoFila;   // borde inferior de la fila

            // Fondo: azul en el encabezado, filas alternas gris claro
            string fondo = f == 0 ? "0.122 0.227 0.373" : (f % 2 == 0 ? "0.941 0.953 0.969" : null);
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

        if (ranking.Count == 0)
            Texto(s, "Aún no hay lugares registrados.", "F1", 10f, x0, yBase - 20f, "0.4 0.4 0.4");

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
