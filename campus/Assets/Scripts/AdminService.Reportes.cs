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

    // Hasta este día del mes (inclusive) el reporte incluye también el mes anterior
    private const int DiasMinimosMes = 3;

    /// <summary>Periodo que cubren el reporte PDF y la pantalla de estadísticas (misma regla en ambos).</summary>
    public struct PeriodoReporte
    {
        public DateTime desde;              // inclusive
        public DateTime hasta;              // exclusivo (inicio del mes siguiente)
        public bool incluyeMesAnterior;
        public string texto;                // "Periodo: noviembre de 2026" o "Periodo: 1 de octubre – 2 de noviembre de 2026"
    }

    /// <summary>
    /// Mes actual; en los primeros DiasMinimosMes días, o si el mes aún no tiene búsquedas, desde el 1 del
    /// mes anterior hasta hoy (el ranking del mes solo sería casi vacío). Lee la base: hilo principal.
    /// </summary>
    public static PeriodoReporte CalcularPeriodo(DateTime ahora)
    {
        DateTime inicioMes = new DateTime(ahora.Year, ahora.Month, 1);
        DateTime finMes = inicioMes.AddMonths(1);
        bool conAnterior = ahora.Day <= DiasMinimosMes || RepositorioCampus.ContarBusquedas(inicioMes, finMes) == 0;
        DateTime desde = conAnterior ? inicioMes.AddMonths(-1) : inicioMes;
        return new PeriodoReporte
        {
            desde = desde,
            hasta = finMes,
            incluyeMesAnterior = conAnterior,
            texto = "Periodo: " + (conAnterior ? ReportePdf.NombreRango(desde, ahora.Date) : ReportePdf.NombrePeriodo(inicioMes)),
        };
    }

    /// <summary>Periodo del reporte para mostrarlo en la interfaz (si la base no está lista: solo el mes).</summary>
    public PeriodoReporte PeriodoDelReporte(DateTime ahora)
    {
        if (BaseDatosLocal.Lista)
        {
            try { return CalcularPeriodo(ahora); }
            catch (Exception e) { Debug.LogWarning("Periodo del reporte: " + e.Message); }
        }
        DateTime inicioMes = new DateTime(ahora.Year, ahora.Month, 1);
        return new PeriodoReporte
        {
            desde = inicioMes, hasta = inicioMes.AddMonths(1), incluyeMesAnterior = false,
            texto = "Periodo: " + ReportePdf.NombrePeriodo(inicioMes),
        };
    }

    /// <summary>
    /// Genera el PDF con el ranking de los 10 lugares más buscados del periodo (ver CalcularPeriodo: el mismo
    /// que muestra la pantalla de estadísticas).
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
        PeriodoReporte p = CalcularPeriodo(ahora);
        var ranking = RepositorioCampus.RankingBusquedas(10, p.desde, p.hasta, out int total);
        byte[] pdf = ReportePdf.Generar(ranking, total, ahora, p.texto);
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

            using (var salida = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                if (salida == null) throw new IOException("openOutputStream devolvió null");
                // El flujo se cierra siempre (aunque falle la escritura): no queda abierto ni bloquea el archivo
                try
                {
                    // Por bloques de 64 KB (write(byte[], off, len)): ni una copia sbyte[] del PDF entero en C#
                    // ni un arreglo Java del mismo tamaño en el puente JNI. Java usa bytes con signo.
                    sbyte[] bloque = new sbyte[Math.Min(BloquePdf, Math.Max(1, pdf.Length))];
                    for (int desde = 0; desde < pdf.Length; desde += bloque.Length)
                    {
                        int n = Math.Min(bloque.Length, pdf.Length - desde);
                        Buffer.BlockCopy(pdf, desde, bloque, 0, n);
                        salida.Call("write", bloque, 0, n);
                    }
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
            // where = null y args = null: borra exactamente esa entrada (un "" sería una cláusula WHERE vacía)
            try { if (resolver != null && uri != null) BorrarEntrada(resolver, uri); }
            catch (Exception ex) { Debug.LogWarning("No se pudo borrar la entrada vacía de Descargas: " + ex.Message); }
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
    private const int BloquePdf = 64 * 1024;

    /// <summary>
    /// ContentResolver.delete(uri, null, null) con la firma JNI explícita: con null en los argumentos,
    /// AndroidJavaObject.Call no puede deducir los tipos (String y String[]) y la firma quedaría ambigua.
    /// </summary>
    private static void BorrarEntrada(AndroidJavaObject resolver, AndroidJavaObject uri)
    {
        IntPtr clase = AndroidJNI.GetObjectClass(resolver.GetRawObject());
        try
        {
            IntPtr metodo = AndroidJNI.GetMethodID(clase, "delete", "(Landroid/net/Uri;Ljava/lang/String;[Ljava/lang/String;)I");
            if (metodo == IntPtr.Zero)
            {
                // GetMethodID deja pendiente un NoSuchMethodError: se limpia y NO se llama CallIntMethod
                AndroidJNI.ExceptionClear();
                throw new IOException("ContentResolver.delete no está disponible");
            }
            jvalue[] args = new jvalue[3];
            args[0].l = uri.GetRawObject();
            args[1].l = IntPtr.Zero;
            args[2].l = IntPtr.Zero;
            AndroidJNI.CallIntMethod(resolver.GetRawObject(), metodo, args);
            IntPtr excepcion = AndroidJNI.ExceptionOccurred();
            if (excepcion != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(excepcion);
                throw new IOException("ContentResolver.delete lanzó una excepción");
            }
        }
        finally { AndroidJNI.DeleteLocalRef(clase); }
    }

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
