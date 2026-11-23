using UnityEngine;

/// <summary>
/// Mantiene el contenido de un Canvas dentro de la zona segura de la pantalla (Screen.safeArea):
/// fuera de la cámara frontal (notch) y de las barras del sistema en Android con pantalla de borde a borde.
///
/// Al ponerlo en la raíz de un Canvas crea un hijo "ZonaSegura", mueve ahí todo el contenido y le ajusta
/// los márgenes. Los fondos que deben llegar hasta el borde (por ejemplo, un color de fondo) pueden marcarse
/// con <see cref="ZonaSegura.FueraDeZona"/> para que se queden afuera.
/// Se recalcula si cambia el tamaño o la orientación de la pantalla.
/// </summary>
[DisallowMultipleComponent]
public class ZonaSegura : MonoBehaviour
{
    /// <summary>Marca un hijo directo para que NO se mueva a la zona segura (fondos a pantalla completa).</summary>
    public class FueraDeZona : MonoBehaviour { }

    private RectTransform zona;
    private Rect ultimaZona;
    private Vector2Int ultimaPantalla;
    private RectTransform bordeSuperior, bordeInferior;

    /// <summary>Contenedor ajustado a la zona segura (créalo con <see cref="En"/>).</summary>
    public RectTransform Zona => zona;

    /// <summary>Aplica la zona segura a un Canvas y devuelve el contenedor donde va el contenido.</summary>
    public static RectTransform En(GameObject canvas) => En(canvas, null);

    /// <summary>
    /// Igual que <see cref="En(GameObject)"/>, y además pinta las franjas fuera de la zona segura
    /// (arriba: cámara frontal; abajo: barra de gestos) con ese color, como una barra de estado.
    /// </summary>
    public static RectTransform En(GameObject canvas, Color? colorBordes) => En(canvas, colorBordes, colorBordes);

    /// <summary>
    /// Pinta la franja de arriba (cámara frontal) y la de abajo (barra de gestos) con colores distintos.
    /// null = esa franja no se pinta.
    /// </summary>
    public static RectTransform En(GameObject canvas, Color? colorArriba, Color? colorAbajo)
    {
        ZonaSegura z = canvas.GetComponent<ZonaSegura>();
        if (z == null) z = canvas.AddComponent<ZonaSegura>();
        z.Preparar();
        if (colorArriba.HasValue || colorAbajo.HasValue) z.PintarBordes(colorArriba, colorAbajo);
        return z.zona;
    }

    private void PintarBordes(Color? arriba, Color? abajo)
    {
        if (arriba.HasValue)
        {
            if (bordeSuperior == null) bordeSuperior = CrearBorde("BordeSuperior");
            bordeSuperior.GetComponent<UnityEngine.UI.Image>().color = arriba.Value;
        }
        else if (bordeSuperior != null) { Destroy(bordeSuperior.gameObject); bordeSuperior = null; }

        if (abajo.HasValue)
        {
            if (bordeInferior == null) bordeInferior = CrearBorde("BordeInferior");
            bordeInferior.GetComponent<UnityEngine.UI.Image>().color = abajo.Value;
        }
        else if (bordeInferior != null) { Destroy(bordeInferior.gameObject); bordeInferior = null; }
        Ajustar(true);
    }

    private RectTransform CrearBorde(string nombre)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(FueraDeZona));
        go.layer = gameObject.layer;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.SetAsLastSibling();                                         // encima del contenido (que nunca llega ahí)
        go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        return rt;
    }

    private void Awake() => Preparar();

    private void Preparar()
    {
        if (zona != null) return;

        Transform existente = transform.Find("ZonaSegura");
        if (existente != null) zona = (RectTransform)existente;
        else
        {
            GameObject go = new GameObject("ZonaSegura", typeof(RectTransform));
            go.layer = gameObject.layer;
            zona = (RectTransform)go.transform;
            zona.SetParent(transform, false);
            zona.anchorMin = Vector2.zero;
            zona.anchorMax = Vector2.one;
            zona.offsetMin = zona.offsetMax = Vector2.zero;
        }

        // Todo el contenido pasa a la zona segura (conservando su orden y anclas relativas)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform hijo = transform.GetChild(i);
            if (hijo == zona || hijo.GetComponent<FueraDeZona>() != null) continue;
            hijo.SetParent(zona, false);
            hijo.SetSiblingIndex(0);
        }
        Ajustar(true);
    }

    // La zona segura casi nunca cambia: se revisa como máximo 4 veces por segundo
    private const float IntervaloRevision = 0.25f;
    private float proximaRevision;

    private void Update()
    {
        if (Time.unscaledTime < proximaRevision) return;
        proximaRevision = Time.unscaledTime + IntervaloRevision;
        Ajustar(false);
    }

    private void Ajustar(bool forzar)
    {
        Rect seguro = Screen.safeArea;
        Vector2Int pantalla = new Vector2Int(Screen.width, Screen.height);
        if (!forzar && seguro == ultimaZona && pantalla == ultimaPantalla) return;
        ultimaZona = seguro;
        ultimaPantalla = pantalla;
        if (zona == null || pantalla.x <= 0 || pantalla.y <= 0) return;

        // Anclas en proporción de la pantalla: no dependen de la escala del Canvas
        zona.anchorMin = new Vector2(seguro.xMin / pantalla.x, seguro.yMin / pantalla.y);
        zona.anchorMax = new Vector2(seguro.xMax / pantalla.x, seguro.yMax / pantalla.y);
        zona.offsetMin = zona.offsetMax = Vector2.zero;

        if (bordeSuperior != null)
        {
            bordeSuperior.anchorMin = new Vector2(0f, zona.anchorMax.y);
            bordeSuperior.anchorMax = Vector2.one;
            bordeSuperior.offsetMin = bordeSuperior.offsetMax = Vector2.zero;
            bordeSuperior.gameObject.SetActive(zona.anchorMax.y < 0.999f);
        }
        if (bordeInferior != null)
        {
            bordeInferior.anchorMin = Vector2.zero;
            bordeInferior.anchorMax = new Vector2(1f, zona.anchorMin.y);
            bordeInferior.offsetMin = bordeInferior.offsetMax = Vector2.zero;
            bordeInferior.gameObject.SetActive(zona.anchorMin.y > 0.001f);
        }
    }
}
