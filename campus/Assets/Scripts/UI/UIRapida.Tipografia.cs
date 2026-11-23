using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// UIRapida (tipografía): UNA familia, Roboto, con pesos reales (Regular, Medium, Bold).
///
/// Jerarquía:
///  · Títulos de barra y de modal (TamTitulo / TamSubtitulo / TituloModal): Bold.
///  · Etiquetas de campo, botones secundarios y encabezados menores: Medium (<see cref="FuenteMedia"/> / <see cref="PesoMedio"/>).
///  · Acción principal (botón verde): Bold.
///  · Cuerpo, campos y textos de apoyo: Regular.
///
/// Cómo funciona: al primer uso (en ejecución) se crean tres TMP_FontAsset DINÁMICOS desde
/// Resources/Fuentes/Roboto-*.ttf. En la tabla de pesos de los tres, [4] = Regular, [5] = Medium y [7] = Bold:
/// así FontStyles.Bold usa el Bold real (TMP consulta fontWeightTable[7]) y no la negrita sintética.
/// Los glifos que falten caen a LiberationSans SDF (fallbackFontAssetTable).
/// En el Editor fuera de Play (IntegradorInterfaz, ControlesMapaEditor) se usa LiberationSans SDF, que es un asset
/// guardable en la escena; en ejecución AplicarFuentes lo reemplaza por Roboto.
/// Si Roboto no carga, todo cae a LiberationSans y se avisa una sola vez.
/// </summary>
public static partial class UIRapida
{
    private const string RutaRespaldo = "Fonts & Materials/LiberationSans SDF";
    private const string RutaRoboto = "Fuentes/Roboto-";

    private static TMP_FontAsset fuenteRegular, fuenteMedia, fuenteNegrita, fuenteRespaldo;
    private static bool fuentesPreparadas, fuentesEnJuego, avisoFuente;
    private static Font legacyRegular, legacyNegrita;

    /// <summary>Roboto Regular (con tabla de pesos: FontStyles.Bold usa Roboto Bold). Null solo si no hay ninguna fuente.</summary>
    public static TMP_FontAsset Fuente { get { PrepararFuentes(); return fuenteRegular; } }

    /// <summary>Roboto Medium (etiquetas, botones secundarios). Si no carga, la misma <see cref="Fuente"/>.</summary>
    public static TMP_FontAsset FuenteMedia { get { PrepararFuentes(); return fuenteMedia != null ? fuenteMedia : fuenteRegular; } }

    /// <summary>Roboto Bold como asset propio (normalmente basta con FontStyles.Bold sobre <see cref="Fuente"/>).</summary>
    [System.Obsolete("Usa Fuente con FontStyles.Bold (o UIRapida.Texto con negrita = true).")]
    public static TMP_FontAsset FuenteNegrita { get { PrepararFuentes(); return fuenteNegrita != null ? fuenteNegrita : fuenteRegular; } }

    /// <summary>Igual que <see cref="Fuente"/> (se conserva por compatibilidad; el peso lo da FontStyles.Bold).</summary>
    public static TMP_FontAsset FuenteTitulos => Fuente;
    /// <summary>Igual que <see cref="Fuente"/> (se conserva por compatibilidad).</summary>
    public static TMP_FontAsset FuenteTexto => Fuente;

    /// <summary>Roboto Regular para Text / InputField antiguos (uGUI). Respaldo: la fuente integrada de Unity.</summary>
    public static Font FuenteLegacy
    {
        get
        {
            if (legacyRegular == null) legacyRegular = CargarLegacy("Regular");
            return legacyRegular;
        }
    }

    /// <summary>Roboto Bold para Text antiguos de botones (uGUI). Respaldo: <see cref="FuenteLegacy"/>.</summary>
    public static Font FuenteLegacyNegrita
    {
        get
        {
            if (legacyNegrita == null) legacyNegrita = Resources.Load<Font>(RutaRoboto + "Bold");
            return legacyNegrita != null ? legacyNegrita : FuenteLegacy;
        }
    }

    private static Font CargarLegacy(string peso)
    {
        Font f = Resources.Load<Font>(RutaRoboto + peso);
        if (f != null) return f;
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return null; }
    }

    /// <summary>
    /// Pone Roboto a un Text antiguo (uGUI). negrita: usa el archivo Bold real (y quita la negrita sintética).
    /// </summary>
    public static void FuenteLegacyEn(UnityEngine.UI.Text t, bool negrita = false)
    {
        if (t == null) return;
        Font f = negrita ? FuenteLegacyNegrita : FuenteLegacy;
        if (f == null) return;
        t.font = f;
        if (negrita && f == legacyNegrita)   // el peso ya viene del archivo: quitar la negrita sintética
            t.fontStyle = t.fontStyle == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal;
    }

    /// <summary>
    /// Pasa todos los Text antiguos (uGUI) bajo la raíz a Roboto: los que estaban en negrita usan Roboto Bold real.
    /// Incluye texto y placeholder de los InputField antiguos.
    /// </summary>
    public static void AplicarFuentesLegacy(Transform raiz)
    {
        if (raiz == null) return;
        foreach (UnityEngine.UI.Text t in raiz.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            FuenteLegacyEn(t, t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic);
        }
    }

    private static void PrepararFuentes()
    {
        bool enJuego = Application.isPlaying;
        if (fuentesPreparadas && fuentesEnJuego == enJuego && fuenteRegular != null) return;
        fuentesPreparadas = true;
        fuentesEnJuego = enJuego;

        fuenteRespaldo = Resources.Load<TMP_FontAsset>(RutaRespaldo);
        if (fuenteRespaldo == null) fuenteRespaldo = TMP_Settings.defaultFontAsset;
        fuenteRegular = fuenteMedia = fuenteNegrita = fuenteRespaldo;

        // Fuera de Play no se crean assets dinámicos: no se pueden guardar en la escena
        if (!enJuego) return;

        string fallo = null;
        try
        {
            TMP_FontAsset r = CrearDinamica("Regular");
            if (r == null) fallo = "no se pudo crear Roboto-Regular";
            else
            {
                TMP_FontAsset m = CrearDinamica("Medium");
                TMP_FontAsset b = CrearDinamica("Bold");
                foreach (TMP_FontAsset a in new[] { r, m, b })
                {
                    if (a == null) continue;
                    TMP_FontWeightPair[] tabla = a.fontWeightTable;
                    if (tabla != null && tabla.Length > 7)
                    {
                        tabla[4].regularTypeface = r;
                        tabla[5].regularTypeface = m != null ? m : r;
                        tabla[7].regularTypeface = b;   // null si no cargó: TMP vuelve a la negrita sintética
                    }
                    if (fuenteRespaldo != null)
                        a.fallbackFontAssetTable = new List<TMP_FontAsset> { fuenteRespaldo };
                }
                fuenteRegular = r;
                fuenteMedia = m != null ? m : r;
                fuenteNegrita = b != null ? b : r;
                if (m == null || b == null) fallo = "faltan pesos de Roboto (se usa Regular con negrita sintética)";
            }
        }
        catch (System.Exception e)
        {
            fuenteRegular = fuenteMedia = fuenteNegrita = fuenteRespaldo;
            fallo = e.Message;
        }

        if (fallo != null && !avisoFuente)
        {
            avisoFuente = true;
            Debug.LogWarning("UIRapida: tipografía Roboto incompleta (" + fallo + "); se usa " +
                             (fuenteRegular != null ? fuenteRegular.name : "la fuente que ya tenga cada texto") + ".");
        }
    }

    private static TMP_FontAsset CrearDinamica(string peso)
    {
        Font f = Resources.Load<Font>(RutaRoboto + peso);
        if (f == null) return null;
        TMP_FontAsset a = TMP_FontAsset.CreateFontAsset(f, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                        AtlasPopulationMode.Dynamic, true);
        if (a == null) return null;
        if (a.material == null || a.material.shader == null || !a.material.shader.isSupported)
        {
            // Sin el shader móvil de TMP en la compilación: se usa el del respaldo (mismo tipo SDF)
            if (a.material != null && fuenteRespaldo != null && fuenteRespaldo.material != null)
                a.material.shader = fuenteRespaldo.material.shader;
            else return null;
        }
        a.name = "Campus Roboto " + peso;   // nombre propio: no confundir con el antiguo "Roboto-Bold SDF"
        return a;
    }

    // Fuentes que la app reemplaza por la suya: la de TMP por defecto, LiberationSans y la antigua Roboto Bold SDF
    private static bool EsFuenteReemplazable(TMP_FontAsset f)
    {
        if (f == null) return true;
        PrepararFuentes();
        if (f == fuenteRegular) return true;
        if (f == fuenteMedia || f == fuenteNegrita) return false;   // pesos elegidos a propósito
        return f == TMP_Settings.defaultFontAsset || f == fuenteRespaldo || f.name.StartsWith("LiberationSans") || EsRobotoBold(f);
    }

    private static bool EsRobotoBold(TMP_FontAsset f) => f != null && f.name.StartsWith("Roboto-Bold");

    /// <summary>
    /// Fuente para un texto nuevo: la de la app, salvo que se pase a propósito una fuente distinta (se respeta,
    /// p. ej. <see cref="FuenteMedia"/>). "negrita" se conserva por compatibilidad: el peso lo pone FontStyles.Bold.
    /// </summary>
    public static TMP_FontAsset FuenteDe(TMP_FontAsset fuenteElegida, bool negrita)
    {
        if (!EsFuenteReemplazable(fuenteElegida)) return fuenteElegida;
        TMP_FontAsset propia = Fuente;
        return propia != null ? propia : fuenteElegida;
    }

    /// <summary>Cambia el peso de un texto: pone o quita FontStyles.Bold (Bold real de Roboto) y asegura la fuente de la app.</summary>
    public static void Peso(TMP_Text t, bool negrita)
    {
        if (t == null) return;
        TMP_FontAsset f = Fuente;
        if (f != null && EsFuenteReemplazable(t.font) && t.font != f) t.font = f;
        t.fontStyle = negrita ? (t.fontStyle | FontStyles.Bold) : (t.fontStyle & ~FontStyles.Bold);
    }

    /// <summary>Peso medio (Roboto Medium, sin negrita): etiquetas de campo, botones secundarios.</summary>
    public static void PesoMedio(TMP_Text t)
    {
        if (t == null) return;
        TMP_FontAsset m = FuenteMedia;
        if (m != null) t.font = m;
        t.fontStyle &= ~FontStyles.Bold;
    }

    /// <summary>
    /// Pasa todos los TMP_Text bajo esa raíz a Roboto conservando su fontStyle (la negrita usa el Bold real).
    /// Respeta los textos ya puestos en Medium. Los que estaban en el antiguo Roboto Bold SDF sin Bold reciben Bold.
    /// Llamar después de estilizar.
    /// </summary>
    public static void AplicarFuentes(Transform raiz)
    {
        if (raiz == null) return;
        TMP_FontAsset f = Fuente;
        if (f == null) return;   // PrepararFuentes ya avisó una vez en la consola
        foreach (TMP_Text t in raiz.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.font == f || !EsFuenteReemplazable(t.font)) continue;
            if (EsRobotoBold(t.font)) t.fontStyle |= FontStyles.Bold;
            t.font = f;
        }
    }
}
