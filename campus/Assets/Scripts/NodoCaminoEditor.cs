#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Funciones compartidas para crear y conectar nodos desde el editor.</summary>
public static class HerramientasRed
{
    /// <summary>Si haces clic a menos de esta distancia de un nodo, se une a él en vez de crear otro.</summary>
    public static float DistanciaImantado = 1.5f;

    public static NodoCamino CrearNodoEn(Transform padre, Vector3 posicion)
    {
        GameObject go = new GameObject("Nodo");
        Undo.RegisterCreatedObjectUndo(go, "Crear nodo");
        go.transform.SetParent(padre, true);
        go.transform.position = posicion;
        go.name = "Nodo_" + (padre != null ? padre.childCount : 1);

        NodoCamino nodo = go.AddComponent<NodoCamino>();
        nodo.entradaDeLugarId = 0;   // todo nodo nuevo empieza como punto de paso
        EditorSceneManager.MarkSceneDirty(go.scene);
        return nodo;
    }

    public static void Conectar(NodoCamino a, NodoCamino b)
    {
        if (a == null || b == null || a == b) return;

        Undo.RecordObjects(new Object[] { a, b }, "Conectar nodos");
        if (!a.conexiones.Contains(b)) a.conexiones.Add(b);
        if (!b.conexiones.Contains(a)) b.conexiones.Add(a);

        EditorUtility.SetDirty(a);
        EditorUtility.SetDirty(b);
        EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
    }

    public static void AlternarConexionSeleccionados()
    {
        NodoCamino a = null, b = null;
        int cuenta = 0;
        foreach (GameObject g in Selection.gameObjects)
        {
            NodoCamino n = g.GetComponent<NodoCamino>();
            if (n == null) continue;
            cuenta++;
            if (a == null) a = n; else if (b == null) b = n;
        }

        if (cuenta != 2)
        {
            EditorUtility.DisplayDialog("Campus",
                "Selecciona exactamente 2 nodos (Ctrl + clic en la Hierarchy).", "OK");
            return;
        }

        if (a.conexiones.Contains(b) || b.conexiones.Contains(a))
        {
            Undo.RecordObjects(new Object[] { a, b }, "Desconectar nodos");
            a.conexiones.Remove(b);
            b.conexiones.Remove(a);
            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(b);
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
        }
        else
        {
            Conectar(a, b);
        }
    }
}

/// <summary>
/// Modo "dibujar con clics": cada clic en la vista Scene agrega un nodo conectado al anterior.
/// Sirve para trazar curvas rápido. Clic sobre un nodo existente = unir con él. Esc = terminar.
/// </summary>
public static class ModoDibujoRed
{
    public static bool Activo { get; private set; }

    private static RedCaminos red;
    private static NodoCamino ultimo;

    public static void Iniciar(RedCaminos redObjetivo, NodoCamino desde)
    {
        red = redObjetivo;
        ultimo = desde;

        if (!Activo)
        {
            SceneView.duringSceneGui += AlDibujarEscena;
            EditorApplication.playModeStateChanged += AlCambiarModo;
            Activo = true;
        }
        SceneView.RepaintAll();
    }

    public static void Detener()
    {
        if (!Activo) return;

        SceneView.duringSceneGui -= AlDibujarEscena;
        EditorApplication.playModeStateChanged -= AlCambiarModo;
        Activo = false;
        ultimo = null;
        red = null;
        SceneView.RepaintAll();
    }

    private static void AlCambiarModo(PlayModeStateChange estado) => Detener();

    private static void AlDibujarEscena(SceneView vista)
    {
        if (!Activo || red == null) { Detener(); return; }

        Event e = Event.current;

        Handles.BeginGUI();
        GUI.Box(new Rect(10, 10, 480, 46), GUIContent.none);
        GUI.Label(new Rect(18, 14, 470, 40),
            "DIBUJANDO CAMINOS  (usa la vista superior)\n" +
            "Clic = nodo nuevo conectado  ·  Clic sobre un nodo = unir con él  ·  Esc = terminar");
        Handles.EndGUI();

        // Evita que el clic seleccione otros objetos de la escena
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Detener();
            e.Use();
            return;
        }

        // El último nodo se marca con un círculo verde
        if (ultimo != null)
        {
            Handles.color = Color.green;
            Handles.DrawWireDisc(ultimo.transform.position, Vector3.up, 2.2f);
        }

        // Alt + arrastrar sigue sirviendo para mover la vista
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Ray rayo = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane suelo = new Plane(Vector3.up, new Vector3(0f, red.transform.position.y, 0f));

            if (suelo.Raycast(rayo, out float t))
                ColocarNodo(rayo.GetPoint(t));

            e.Use();
        }
    }

    private static void ColocarNodo(Vector3 punto)
    {
        NodoCamino cercano = null;
        float mejor = HerramientasRed.DistanciaImantado;

        foreach (NodoCamino n in red.GetComponentsInChildren<NodoCamino>())
        {
            Vector3 p = n.transform.position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(punto.x, punto.z));
            if (d < mejor) { mejor = d; cercano = n; }
        }

        if (cercano != null)
        {
            HerramientasRed.Conectar(ultimo, cercano);
            ultimo = cercano;
        }
        else
        {
            NodoCamino nuevo = HerramientasRed.CrearNodoEn(red.transform, punto);
            HerramientasRed.Conectar(ultimo, nuevo);
            ultimo = nuevo;
        }

        Selection.activeGameObject = ultimo.gameObject;
        SceneView.RepaintAll();
    }
}

[CustomEditor(typeof(NodoCamino))]
public class NodoCaminoEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        NodoCamino nodo = (NodoCamino)target;

        if (GUILayout.Button("Crear nodo conectado  (Alt+N)"))
            CrearConectado(nodo);

        if (GUILayout.Button("Seguir dibujando con clics desde este nodo  (Alt+D)"))
            DibujarDesde(nodo);

        if (GUILayout.Button("Conectar / desconectar los 2 nodos seleccionados  (Alt+C)"))
            HerramientasRed.AlternarConexionSeleccionados();
    }

    [MenuItem("Campus/Crear nodo conectado &n")]
    private static void MenuCrear()
    {
        NodoCamino n = NodoSeleccionado();
        if (n != null) CrearConectado(n);
    }

    [MenuItem("Campus/Dibujar caminos con clics &d")]
    private static void MenuDibujar()
    {
        NodoCamino n = NodoSeleccionado();
        if (n != null) DibujarDesde(n);
        else
        {
            RedCaminos red = Object.FindFirstObjectByType<RedCaminos>();
            if (red != null) ModoDibujoRed.Iniciar(red, null);
        }
    }

    [MenuItem("Campus/Conectar o desconectar nodos seleccionados &c")]
    private static void MenuConectar() => HerramientasRed.AlternarConexionSeleccionados();

    private static NodoCamino NodoSeleccionado() =>
        Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<NodoCamino>() : null;

    private static void CrearConectado(NodoCamino origen)
    {
        NodoCamino nuevo = HerramientasRed.CrearNodoEn(
            origen.transform.parent, origen.transform.position + new Vector3(5f, 0f, 0f));
        HerramientasRed.Conectar(origen, nuevo);
        Selection.activeGameObject = nuevo.gameObject;   // queda seleccionado: muévelo con W
    }

    private static void DibujarDesde(NodoCamino nodo)
    {
        RedCaminos red = nodo.GetComponentInParent<RedCaminos>();
        if (red == null)
        {
            EditorUtility.DisplayDialog("Campus", "Este nodo debe estar dentro del objeto RedCaminos.", "OK");
            return;
        }
        ModoDibujoRed.Iniciar(red, nodo);
    }
}

[CustomEditor(typeof(RedCaminos))]
public class RedCaminosEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dibujo rápido de caminos", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "1) Pon la vista Scene en vista superior (clic en el cono verde Y del gizmo).\n" +
            "2) Presiona el botón y haz clic sobre el mapa: cada clic crea un nodo conectado al anterior. " +
            "Para una curva, haz varios clics seguidos.\n" +
            "3) Clic sobre un nodo existente = unir con él. Esc = terminar.",
            MessageType.Info);

        HerramientasRed.DistanciaImantado = EditorGUILayout.Slider(
            "Distancia de imantado", HerramientasRed.DistanciaImantado, 0.3f, 5f);

        if (!ModoDibujoRed.Activo)
        {
            if (GUILayout.Button("Empezar a dibujar con clics"))
                ModoDibujoRed.Iniciar((RedCaminos)target, null);
        }
        else if (GUILayout.Button("Terminar de dibujar  (Esc)"))
        {
            ModoDibujoRed.Detener();
        }
    }
}
#endif