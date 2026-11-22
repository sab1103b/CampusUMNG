using Mapbox.Unity.Map;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Campus > Agregar controles del mapa (mover y zoom)
///  - Agrega CamaraMapa a la cámara del mapa (arrastrar, pellizco, rueda del mouse).
///  - Crea en la pantalla de usuario los botones +, − y "Yo" (volver a mi ubicación),
///    con sus OnClick ya conectados (se ven y se pueden cambiar en el Inspector).
/// Se puede ejecutar más de una vez: no duplica nada.
/// </summary>
public static class ControlesMapaEditor
{
    private static readonly Color Verde = UIRapida.Verde;   // #1c7126

    [MenuItem("Campus/Agregar controles del mapa (mover y zoom)", false, 101)]
    private static void Agregar()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Campus", "Detén el modo Play antes de agregar los controles.", "OK");
            return;
        }

        Scene escena = SceneManager.GetActiveScene();
        AbstractMap mapa = Object.FindFirstObjectByType<AbstractMap>();
        UbicacionController ubicacion = Object.FindFirstObjectByType<UbicacionController>();
        Camera camMapa = BuscarCamaraMapa();
        Transform usuario = BuscarPanelUsuario(escena);

        if (camMapa == null || usuario == null)
        {
            EditorUtility.DisplayDialog("Campus",
                "Abre la escena Santiago con la interfaz ya integrada (Campus > Integrar InterfazGrafica en Santiago).\n\n" +
                (camMapa == null ? "• No encontré la cámara del mapa (Camera_Mapa).\n" : "") +
                (usuario == null ? "• No encontré Panel_Usuario dentro de UI_CampusConecta.\n" : ""), "OK");
            return;
        }

        // ---------- Cámara ----------
        CamaraMapa control = camMapa.GetComponent<CamaraMapa>();
        if (control == null) control = camMapa.gameObject.AddComponent<CamaraMapa>();

        SerializedObject so = new SerializedObject(control);
        so.FindProperty("mapa").objectReferenceValue = mapa;
        if (ubicacion != null)
        {
            Object marcador = new SerializedObject(ubicacion).FindProperty("marcador").objectReferenceValue;
            so.FindProperty("objetivo").objectReferenceValue = marcador;
        }
        so.FindProperty("altura").floatValue = Mathf.Clamp(camMapa.transform.position.y, 30f, 400f);
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---------- Botones ----------
        Transform viejo = usuario.Find("ControlesMapa");
        if (viejo != null) Object.DestroyImmediate(viejo.gameObject);

        GameObject grupo = new GameObject("ControlesMapa", typeof(RectTransform), typeof(VerticalLayoutGroup));
        grupo.layer = 5;
        grupo.transform.SetParent(usuario, false);
        RectTransform rt = (RectTransform)grupo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-28f, 40f);
        rt.sizeDelta = new Vector2(130f, 580f);   // 4 botones de 130 + 3 espacios de 20
        VerticalLayoutGroup vlg = grupo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = true;

        // Debajo de la lista, la ficha y la barra de ruta: cuando se abren, quedan encima de los botones
        Transform resultados = usuario.Find("Resultados");
        if (resultados != null) grupo.transform.SetSiblingIndex(resultados.GetSiblingIndex());

        Sprite redondo = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        TMP_FontAsset fuente = BuscarFuente("Roboto-Bold SDF") ?? TMP_Settings.defaultFontAsset;

        Boton(grupo.transform, "BotonZoomMas", "+", 72, redondo, fuente, control.ZoomMas);
        Boton(grupo.transform, "BotonZoomMenos", "-", 96, redondo, fuente, control.ZoomMenos);   // guion ASCII: está en todas las fuentes
        Boton(grupo.transform, "BotonMiUbicacion", "Yo", 40, redondo, fuente, control.CentrarEnUsuario);
        Boton(grupo.transform, "BotonPerspectiva", "3D", 40, redondo, fuente, control.AlternarPerspectiva);   // InterfazCampus cambia el texto a "2D" en 3D

        EditorUtility.SetDirty(control);
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Selection.activeGameObject = grupo;

        EditorUtility.DisplayDialog("Campus",
            "Listo. La cámara del mapa ya se mueve con el dedo o el mouse, hace zoom con pellizco o rueda, " +
            "y en la pantalla de usuario están los botones +, − y Yo.\n\nEscena guardada.", "OK");
    }

    private static void Boton(Transform padre, string nombre, string texto, float tamano, Sprite sprite,
                              TMP_FontAsset fuente, UnityEngine.Events.UnityAction accion)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform), typeof(Image), typeof(Button));
        go.layer = 5;
        go.transform.SetParent(padre, false);

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = new Color(1f, 1f, 1f, 0.96f);
        img.preserveAspect = true;

        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        UnityEventTools.AddPersistentListener(b.onClick, accion);

        GameObject t = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
        t.layer = 5;
        t.transform.SetParent(go.transform, false);
        RectTransform trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        TextMeshProUGUI tmp = t.GetComponent<TextMeshProUGUI>();
        tmp.font = fuente;
        tmp.text = texto;
        tmp.fontSize = tamano;
        tmp.color = Verde;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    // La cámara base que no es solo de UI (en Santiago: Camera_Mapa)
    private static Camera BuscarCamaraMapa()
    {
        Camera mejor = null;
        foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.name == "Camera_Mapa") return c;
            if (c.cullingMask != (1 << 5) && mejor == null) mejor = c;
        }
        return mejor;
    }

    private static Transform BuscarPanelUsuario(Scene escena)
    {
        foreach (GameObject r in escena.GetRootGameObjects())
        {
            if (r.name != "UI_CampusConecta") continue;
            return r.transform.Find("Panel_Usuario");
        }
        return null;
    }

    private static TMP_FontAsset BuscarFuente(string nombre)
    {
        foreach (string guid in AssetDatabase.FindAssets(nombre + " t:TMP_FontAsset"))
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(ruta) == nombre)
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ruta);
        }
        return null;
    }
}
