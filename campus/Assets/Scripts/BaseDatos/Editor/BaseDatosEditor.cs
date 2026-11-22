using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Herramientas del Editor para la base de datos local (menú Campus > Base de datos).
///
/// Administradores: cada cuenta es un "espacio" con un código de registro único.
/// Tú (dueño del proyecto) ves los códigos aquí y se los entregas a la persona; ella se registra
/// en la app con usuario + contraseña + código. Una vez registrada, nadie puede cambiar su usuario
/// ni su contraseña (la base lo impide). Solo se puede eliminar el espacio para quitarle el acceso.
/// </summary>
public class BaseDatosEditor : EditorWindow
{
    private List<RepositorioCampus.EspacioAdmin> espacios = new List<RepositorioCampus.EspacioAdmin>();
    private Dictionary<int, string> codigos = new Dictionary<int, string>();
    private bool mostrarCodigos;
    private string mensaje = "";
    private MessageType tipoMensaje = MessageType.None;
    private Vector2 scroll;

    // ------------------------------------------------------------------
    // Menús
    // ------------------------------------------------------------------
    [MenuItem("Campus/Base de datos/Administradores...", false, 1)]
    private static void AbrirVentana()
    {
        BaseDatosEditor w = GetWindow<BaseDatosEditor>(true, "Administradores", true);
        w.minSize = new Vector2(440, 420);
        w.Refrescar();
    }

    [MenuItem("Campus/Base de datos/Mostrar archivo campus.db", false, 20)]
    private static void Mostrar()
    {
        BaseDatosLocal.AbrirSincrono();   // la crea si todavía no existe
        EditorUtility.RevealInFinder(BaseDatosLocal.RutaMaestra);
        Debug.Log("Base de datos maestra: " + BaseDatosLocal.RutaMaestra);
    }

    [MenuItem("Campus/Base de datos/Reconstruir desde semilla.sql", false, 40)]
    private static void Reconstruir()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Campus", "Detén el modo Play antes de reconstruir la base.", "OK");
            return;
        }
        if (!File.Exists(BaseDatosLocal.RutaSemilla))
        {
            EditorUtility.DisplayDialog("Campus", "No existe el archivo:\n" + BaseDatosLocal.RutaSemilla, "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Campus",
                "Se BORRARÁ la base actual (lugares, servicios, red, búsquedas y cuentas de administrador) " +
                "y se volverá a crear con Assets/BaseDatos/semilla.sql.\n\n" +
                "Los espacios de administrador se pierden; se crean 5 nuevos con códigos nuevos.\n\n" +
                "Esta acción no se puede deshacer.",
                "Borrar y reconstruir", "Cancelar"))
            return;

        try
        {
            // Los códigos viejos ya no sirven: se archiva el archivo para que se generen 5 nuevos
            if (File.Exists(RepositorioCampus.RutaCodigos))
                File.Move(RepositorioCampus.RutaCodigos,
                          RepositorioCampus.RutaCodigos.Replace(".txt", $"_anterior_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt"));

            BaseDatosLocal.ReconstruirDesdeSemilla();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Campus",
                "Base reconstruida.\n\nLos códigos de registro nuevos están en Campus > Base de datos > Administradores.", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Reconstruir base: " + e);
            EditorUtility.DisplayDialog("Campus", "Error al reconstruir:\n" + e.Message, "OK");
        }
    }

    // ------------------------------------------------------------------
    // Ventana de administradores
    // ------------------------------------------------------------------
    private void Refrescar()
    {
        try
        {
            espacios = RepositorioCampus.ListarEspaciosAdmin();
            codigos = RepositorioCampus.LeerCodigos();
        }
        catch (System.Exception e) { Avisar("No se pudo leer la base: " + e.Message, MessageType.Error); }
    }

    private void Avisar(string texto, MessageType tipo) { mensaje = texto; tipoMensaje = tipo; }

    private void OnFocus() => Refrescar();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Entrega a cada persona el código de un espacio LIBRE. En la app, en Registro, escribe usuario, " +
            "contraseña y ese código. Después nadie puede cambiar ese usuario ni su contraseña.",
            MessageType.Info);

        int libres = espacios.FindAll(e => e.Libre).Count;
        EditorGUILayout.LabelField($"Espacios: {espacios.Count}   ·   Libres: {libres}   ·   Registrados: {espacios.Count - libres}",
                                   EditorStyles.boldLabel);

        mostrarCodigos = EditorGUILayout.ToggleLeft("Mostrar códigos de los espacios libres", mostrarCodigos);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (RepositorioCampus.EspacioAdmin e in espacios.ToArray())
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"#{e.id}", GUILayout.Width(36));

            if (e.Libre)
            {
                string codigo = codigos.TryGetValue(e.id, out string c) ? c : "(código no disponible)";
                EditorGUILayout.LabelField("LIBRE", GUILayout.Width(70));
                if (mostrarCodigos) EditorGUILayout.SelectableLabel(codigo, GUILayout.Height(18));
                else EditorGUILayout.LabelField("•••••-•••••");

                GUI.enabled = mostrarCodigos && codigos.ContainsKey(e.id);
                if (GUILayout.Button("Copiar", GUILayout.Width(60)))
                {
                    EditorGUIUtility.systemCopyBuffer = codigo;
                    Avisar($"Código del espacio #{e.id} copiado.", MessageType.Info);
                }
                GUI.enabled = true;
            }
            else
            {
                EditorGUILayout.LabelField("REGISTRADO", GUILayout.Width(90));
                EditorGUILayout.LabelField($"{e.nickname}   ({e.registradoEn})");
            }

            string accion = e.Libre ? "Eliminar" : "Quitar acceso";
            if (GUILayout.Button(accion, GUILayout.Width(95)) &&
                EditorUtility.DisplayDialog("Campus",
                    e.Libre
                        ? $"¿Eliminar el espacio libre #{e.id}? Su código dejará de funcionar."
                        : $"¿Quitar el acceso a '{e.nickname}'? Se elimina su cuenta y no podrá volver a entrar.",
                    accion, "Cancelar"))
            {
                try { RepositorioCampus.EliminarEspacioAdmin(e.id); Avisar($"Espacio #{e.id} eliminado.", MessageType.Info); }
                catch (System.Exception ex) { Avisar(ex.Message, MessageType.Error); }
                Refrescar();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Agregar 5 espacios", GUILayout.Height(26)))
        {
            try
            {
                var nuevos = RepositorioCampus.CrearEspaciosAdmin(5);
                Avisar($"Se crearon {nuevos.Count} espacios nuevos.", MessageType.Info);
            }
            catch (System.Exception ex) { Avisar(ex.Message, MessageType.Error); }
            Refrescar();
        }
        if (GUILayout.Button("Abrir archivo de códigos", GUILayout.Height(26)))
        {
            if (File.Exists(RepositorioCampus.RutaCodigos)) EditorUtility.RevealInFinder(RepositorioCampus.RutaCodigos);
            else Avisar("Todavía no hay archivo de códigos.", MessageType.Warning);
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(mensaje)) EditorGUILayout.HelpBox(mensaje, tipoMensaje);

        EditorGUILayout.LabelField(
            "Los códigos se guardan en CodigosRegistroAdmin.txt (fuera de Assets: no viaja en el APK ni se sube a git). " +
            "En la base solo queda su huella (hash).", EditorStyles.wordWrappedMiniLabel);
    }
}

/// <summary>Antes de compilar el APK se asegura de que la base maestra exista y esté cerrada.</summary>
public class BaseDatosPreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        BaseDatosLocal.AbrirSincrono();   // si no existe, la crea desde semilla.sql
        int libres = RepositorioCampus.ListarEspaciosAdmin().FindAll(e => e.Libre).Count;
        int registrados = RepositorioCampus.ContarAdminsRegistrados();
        BaseDatosLocal.Cerrar();          // libera el archivo para que se empaquete completo

        if (libres == 0 && registrados == 0)
            Debug.LogWarning("La base de datos no tiene espacios de administrador: nadie podrá registrarse ni entrar al modo administrador. " +
                             "Créalos en Campus > Base de datos > Administradores.");
        AssetDatabase.Refresh();
    }
}
