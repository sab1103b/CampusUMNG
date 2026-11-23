using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Genera el APK de Android con las escenas activas de Build Settings.
/// Menú: Campus > Compilar APK. Línea de comandos (Unity cerrado):
///   Unity.exe -batchmode -quit -projectPath D:\campus\campus -buildTarget Android
///             -executeMethod CompilarAndroid.Compilar -logFile build.log
/// El APK queda en ../Builds/CampusConecta-{versión}.apk (fuera de Assets).
/// </summary>
public static class CompilarAndroid
{
    [MenuItem("Campus/Compilar APK")]
    public static void Compilar()
    {
        string[] escenas = EditorBuildSettings.scenes.Where(e => e.enabled).Select(e => e.path).ToArray();
        if (escenas.Length == 0) throw new InvalidOperationException("No hay escenas activas en Build Settings.");

        string carpeta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds"));
        Directory.CreateDirectory(carpeta);
        string ruta = Path.Combine(carpeta, $"CampusConecta-{PlayerSettings.bundleVersion}.apk");

        EditorUserBuildSettings.buildAppBundle = false;   // APK instalable directamente (no .aab de Play Store)
        BuildReport reporte = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = escenas,
            locationPathName = ruta,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        });

        BuildSummary r = reporte.summary;
        Debug.Log($"[CompilarAndroid] Resultado: {r.result} · {r.totalErrors} error(es) · " +
                  $"{r.totalSize / (1024f * 1024f):0.0} MB · {ruta}");
        if (Application.isBatchMode) EditorApplication.Exit(r.result == BuildResult.Succeeded ? 0 : 1);
    }
}
