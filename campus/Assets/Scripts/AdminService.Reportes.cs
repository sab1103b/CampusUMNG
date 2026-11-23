using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Modo administrador: reporte PDF de búsquedas (ranking del mes). Genera el archivo, lo guarda en
/// Descargas/CampusConecta con MediaStore (Android 10+) o en la carpeta de la app, y lo abre con un Intent.
/// </summary>
public partial class AdminService
{
    // ----- Reporte PDF -----

    /// <summary>
    /// Genera el PDF con el ranking de los 10 lugares más buscados del mes actual.
    /// Si sale bien, el callback recibe un texto corto para mostrar (p. ej. "Reporte guardado en Descargas/CampusConecta.").
    /// </summary>
    public void GenerarReportePdf(Action<bool, string> alTerminar)
    {
        if (!VerificarSesion(alTerminar)) return;

        // El callback va FUERA del try: si fallara dentro del callback, no se vuelve a llamar con error
        bool ok;
        string texto;
        try
        {
            texto = GuardarReportePdf();
            ok = true;
        }
        catch (Exception e)
        {
            Debug.LogError("Reporte PDF: " + e);
            texto = "No se pudo generar el reporte. Intenta de nuevo.";
            ok = false;
        }
        alTerminar?.Invoke(ok, texto);
    }

#if UNITY_EDITOR
    /// <summary>Solo para herramientas del Editor (Campus > Pruebas en Play): genera el PDF sin sesión.</summary>
    public static string GuardarReportePdfDesdeEditor() => GuardarReportePdf();
#endif

    /// <summary>
    /// Crea el PDF con el ranking del mes actual y devuelve un texto amigable de dónde quedó.
    /// Editor: carpeta "Reportes" junto a Assets. Android 10+: Descargas/CampusConecta (MediaStore)
    /// y se intenta abrir con el visor de PDF del celular; si falla o es un Android anterior,
    /// carpeta Reportes de la app. No verifica sesión: por eso es privado (lo llama GenerarReportePdf).
    /// </summary>
    private static string GuardarReportePdf()
    {
        DateTime ahora = DateTime.Now;
        DateTime inicioMes = new DateTime(ahora.Year, ahora.Month, 1);
        var ranking = RepositorioCampus.RankingBusquedas(10, inicioMes, inicioMes.AddMonths(1), out int total);
        string periodo = "Periodo: " + ReportePdf.NombrePeriodo(inicioMes);
        byte[] pdf = ReportePdf.Generar(ranking, total, ahora, periodo);
        string nombre = "reporte_busquedas_" + ahora.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf";

        if (!Application.isEditor && Application.platform == RuntimePlatform.Android && GuardarEnDescargasAndroid(nombre, pdf))
        {
            Registro.Info("Reporte PDF guardado en Descargas/CampusConecta/" + nombre);
            return "Reporte guardado en Descargas/CampusConecta.";
        }

        string carpeta = Application.isEditor
            ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reportes")
            : Path.Combine(Application.persistentDataPath, "Reportes");
        Directory.CreateDirectory(carpeta);

        string ruta = Path.Combine(carpeta, nombre);
        File.WriteAllBytes(ruta, pdf);
        Registro.Info("Reporte PDF generado: " + ruta);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(ruta);
        return "Reporte guardado en Reportes/" + nombre;
#else
        return DescribirCarpetaApp(carpeta);
#endif
    }

    /// <summary>
    /// Android 7–9 (o si falló Descargas): el PDF queda en la carpeta de la app. Se muestra la ruta tal
    /// como se ve en un explorador de archivos ("Android/data/&lt;paquete&gt;/files/Reportes").
    /// </summary>
    private static string DescribirCarpetaApp(string carpeta)
    {
        string normal = carpeta.Replace('\\', '/');
        int i = normal.IndexOf("Android/data/", StringComparison.Ordinal);
        if (i >= 0)
            return "Reporte guardado en " + normal.Substring(i) + ". Ábrelo con el administrador de archivos.";
        // Almacenamiento interno (no visible para otras apps)
        return "Reporte guardado en la memoria interna de la app (carpeta Reportes).";
    }

    /// <summary>
    /// Android 10+ (API 29): guarda en Descargas/CampusConecta con MediaStore, sin pedir permisos.
    /// Devuelve false si no se pudo (Android anterior o error), para usar la carpeta de la app.
    /// </summary>
    private static bool GuardarEnDescargasAndroid(string nombre, byte[] pdf)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject resolver = null;
        AndroidJavaObject uri = null;
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                if (version.GetStatic<int>("SDK_INT") < 29) return false;

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var actividad = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                resolver = actividad.Call<AndroidJavaObject>("getContentResolver");

            using (var valores = new AndroidJavaObject("android.content.ContentValues"))
            using (var descargas = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (var coleccion = descargas.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            {
                valores.Call("put", "_display_name", nombre);                    // MediaColumns.DISPLAY_NAME
                valores.Call("put", "mime_type", "application/pdf");             // MediaColumns.MIME_TYPE
                valores.Call("put", "relative_path", "Download/CampusConecta");  // MediaColumns.RELATIVE_PATH
                uri = resolver.Call<AndroidJavaObject>("insert", coleccion, valores);
            }
            if (uri == null) return false;

            // Java usa bytes con signo
            sbyte[] datos = new sbyte[pdf.Length];
            Buffer.BlockCopy(pdf, 0, datos, 0, pdf.Length);

            using (var salida = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                if (salida == null) throw new IOException("openOutputStream devolvió null");
                // El flujo se cierra siempre (aunque falle la escritura): no queda abierto ni bloquea el archivo
                try
                {
                    salida.Call("write", datos);
                    salida.Call("flush");
                }
                finally
                {
                    try { salida.Call("close"); }
                    catch (Exception e) { Debug.LogWarning("No se pudo cerrar el PDF: " + e.Message); }
                }
            }
            AbrirPdfAndroid(uri);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("No se pudo guardar en Descargas, se usa la carpeta de la app: " + e.Message);
            // Si alcanzó a crear la entrada vacía en Descargas, se borra
            try { if (resolver != null && uri != null) resolver.Call<int>("delete", uri, "", new string[0]); }
            catch (Exception) { }
            return false;
        }
        finally
        {
            if (uri != null) uri.Dispose();
            if (resolver != null) resolver.Dispose();
        }
#else
        return false;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Abre el PDF recién guardado con el visor del celular. Si no hay visor, no pasa nada.</summary>
    private static void AbrirPdfAndroid(AndroidJavaObject uri)
    {
        try
        {
            using (var intentClase = new AndroidJavaClass("android.content.Intent"))
            using (var intent = new AndroidJavaObject("android.content.Intent", intentClase.GetStatic<string>("ACTION_VIEW")))
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var actividad = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                intent.Call<AndroidJavaObject>("setDataAndType", uri, "application/pdf").Dispose();
                int banderas = intentClase.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION") |
                               intentClase.GetStatic<int>("FLAG_ACTIVITY_NEW_TASK");
                intent.Call<AndroidJavaObject>("addFlags", banderas).Dispose();
                actividad.Call("startActivity", intent);
            }
        }
        catch (Exception e)
        {
            // ActivityNotFoundException: no hay visor de PDF instalado; el archivo ya quedó en Descargas
            Debug.LogWarning("No se pudo abrir el PDF: " + e.Message);
        }
    }
#endif
}
