using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Atajos para construir interfaz por código con el estilo de Campus Conecta.</summary>
public static class UIRapida
{
    public static readonly Color Verde = Hex("#1c7126");        // verde oscuro de la marca
    public static readonly Color VerdeClaro = new Color(0.19f, 0.52f, 0.29f, 1f);
    public static readonly Color Oscuro = new Color(0.24f, 0.32f, 0.27f, 1f);
    public static readonly Color Menta = Hex("#adc1ad");        // verde claro (botones secundarios, etiquetas)

    public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
    public static readonly Color Gris = new Color(0.55f, 0.60f, 0.56f, 1f);
    public static readonly Color Rojo = new Color(0.72f, 0.24f, 0.18f, 1f);

    public static RectTransform Rect(string nombre, Transform padre)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(padre, false);
        return (RectTransform)go.transform;
    }

    public static Image Panel(string nombre, Transform padre, Color color)
    {
        RectTransform rt = Rect(nombre, padre);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static void Anclar(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivote, Vector2 posicion, Vector2 tamano)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;
    }

    public static void Estirar(RectTransform rt, float izq = 0, float abajo = 0, float der = 0, float arriba = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(izq, abajo);
        rt.offsetMax = new Vector2(-der, -arriba);
    }

    public static TMP_Text Texto(Transform padre, string nombre, string texto, float tam, Color color,
                                 TMP_FontAsset fuente, bool negrita = false,
                                 TextAlignmentOptions alineacion = TextAlignmentOptions.Left)
    {
        RectTransform rt = Rect(nombre, padre);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (fuente != null) t.font = fuente;
        t.text = texto;
        t.fontSize = tam;
        t.color = color;
        t.fontStyle = negrita ? FontStyles.Bold : FontStyles.Normal;
        t.alignment = alineacion;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    public static Button Boton(Transform padre, string nombre, string texto, float tam, Color fondo, Color colorTexto,
                               TMP_FontAsset fuente, UnityAction accion)
    {
        Image img = Panel(nombre, padre, fondo);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        if (accion != null) b.onClick.AddListener(accion);

        TMP_Text t = Texto(img.transform, "Texto", texto, tam, colorTexto, fuente, true, TextAlignmentOptions.Center);
        Estirar((RectTransform)t.transform, 8, 4, 8, 4);
        t.enableAutoSizing = true;
        t.fontSizeMin = 16;
        t.fontSizeMax = tam;
        return b;
    }

    public static TMP_Text TextoDe(Button b) => b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;

    // ==================================================================
    // Botón redondo blanco (+, -, Yo, 3D): el mismo en el mapa del usuario y en "Modificar mapa"
    // ==================================================================
    public static Button BotonRedondo(Transform padre, string nombre, string texto, float tamano,
                                      TMP_FontAsset fuente, UnityAction accion)
    {
        Image img = Panel(nombre, padre, new Color(1f, 1f, 1f, 0.96f));
        img.sprite = Circulo();
        img.preserveAspect = true;

        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        if (accion != null) b.onClick.AddListener(accion);

        TMP_Text t = Texto(img.transform, "Texto", texto, tamano, Verde, fuente, true, TextAlignmentOptions.Center);
        Estirar((RectTransform)t.transform);
        return b;
    }

    // ==================================================================
    // Sprites dibujados por código (no dependen de archivos ni de símbolos de la fuente)
    // ==================================================================
    private static Sprite circulo, redondeado;
    private static readonly System.Collections.Generic.Dictionary<string, Sprite> iconos =
        new System.Collections.Generic.Dictionary<string, Sprite>();

    /// <summary>Círculo blanco con borde suave.</summary>
    public static Sprite Circulo()
    {
        if (circulo == null)
            circulo = Dibujar(128, (x, y) => Relleno(Circ(x, y, 0.5f, 0.5f, 0.5f - 1f / 128f), 128));
        return circulo;
    }

    /// <summary>Rectángulo blanco con esquinas redondeadas (9-slice: se estira sin deformar las esquinas).</summary>
    public static Sprite Redondeado()
    {
        if (redondeado != null) return redondeado;
        const int n = 64;
        const float r = 22f / n;
        Texture2D tex = Textura(n, (x, y) =>
        {
            float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - (0.5f - r), 0f);
            float dy = Mathf.Max(Mathf.Abs(y - 0.5f) - (0.5f - r), 0f);
            return Relleno(Mathf.Sqrt(dx * dx + dy * dy) - r, n);
        });
        redondeado = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        return redondeado;
    }

    private static Sprite degradado;

    /// <summary>Degradado vertical: transparente arriba, negro abajo (para poner texto sobre fotos).</summary>
    public static Sprite Degradado()
    {
        if (degradado != null) return degradado;
        const int n = 64;
        Texture2D tex = new Texture2D(4, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[4 * n];
        for (int y = 0; y < n; y++)
        {
            float t = 1f - (y + 0.5f) / n;                      // 1 abajo, 0 arriba
            byte a = (byte)(Mathf.Pow(t, 1.6f) * 255f);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(0, 0, 0, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        degradado = Sprite.Create(tex, new Rect(0, 0, 4, n), new Vector2(0.5f, 0.5f), 100f);
        return degradado;
    }

    /// <summary>Pone el fondo redondeado a una imagen (radio de esquina en unidades de la interfaz).</summary>
    public static void Redondear(Image img, float radio = 26f)
    {
        img.sprite = Redondeado();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 22f / Mathf.Max(1f, radio);
    }

    /// <summary>Íconos blancos (se tiñen con Image.color): mover, nodo, conectar, puerta, eliminar, cubo, rectangulo, cilindro.</summary>
    public static Sprite Icono(string nombre)
    {
        if (iconos.TryGetValue(nombre, out Sprite s)) return s;
        const int n = 96;
        System.Func<float, float, float> forma;
        switch (nombre)
        {
            case "mover":       // cruz con puntas de flecha
                forma = (x, y) => Min(Seg(x, y, 0.5f, 0.15f, 0.5f, 0.85f, 0.045f), Seg(x, y, 0.15f, 0.5f, 0.85f, 0.5f, 0.045f),
                                      Flecha(x, y, 0.5f, 0.88f, 0f, 1f), Flecha(x, y, 0.5f, 0.12f, 0f, -1f),
                                      Flecha(x, y, 0.88f, 0.5f, 1f, 0f), Flecha(x, y, 0.12f, 0.5f, -1f, 0f));
                break;
            case "nodo":        // punto con anillo
                forma = (x, y) => Min(Circ(x, y, 0.5f, 0.5f, 0.17f), Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.33f)) - 0.04f);
                break;
            case "conectar":    // dos nodos unidos
                forma = (x, y) => Min(Circ(x, y, 0.24f, 0.28f, 0.13f), Circ(x, y, 0.76f, 0.72f, 0.13f),
                                      Seg(x, y, 0.24f, 0.28f, 0.76f, 0.72f, 0.04f));
                break;
            case "puerta":      // marco de puerta con punto
                forma = (x, y) => Min(Mathf.Abs(Caja(x, y, 0.5f, 0.47f, 0.24f, 0.34f)) - 0.04f,
                                      Circ(x, y, 0.62f, 0.47f, 0.045f), Caja(x, y, 0.5f, 0.11f, 0.36f, 0.035f));
                break;
            case "eliminar":    // equis
                forma = (x, y) => Min(Seg(x, y, 0.22f, 0.22f, 0.78f, 0.78f, 0.06f), Seg(x, y, 0.22f, 0.78f, 0.78f, 0.22f, 0.06f));
                break;
            case "mas":         // signo más grueso
                forma = (x, y) => Min(Seg(x, y, 0.5f, 0.2f, 0.5f, 0.8f, 0.07f), Seg(x, y, 0.2f, 0.5f, 0.8f, 0.5f, 0.07f));
                break;
            case "lapiz":       // lápiz en diagonal: punta afilada, cuerpo y borrador separados
                forma = (x, y) =>
                {
                    float a = (x + y) * 0.7071f;                 // avance a lo largo del lápiz
                    float lat = Mathf.Abs(x - y) * 0.7071f;      // distancia al eje
                    float punta = Mathf.Max(lat - 0.1f * Mathf.Clamp01((a - 0.2f) / 0.33f), Mathf.Max(0.2f - a, a - 0.53f));
                    return Min(punta, Seg(x, y, 0.42f, 0.42f, 0.68f, 0.68f, 0.1f), Seg(x, y, 0.74f, 0.74f, 0.83f, 0.83f, 0.1f));
                };
                break;
            case "basura":      // papelera: tapa, asa y cuerpo con líneas
                forma = (x, y) => Min(Seg(x, y, 0.2f, 0.76f, 0.8f, 0.76f, 0.045f),
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.85f, 0.1f, 0.05f)) - 0.03f,
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.42f, 0.24f, 0.28f)) - 0.045f,
                                      Seg(x, y, 0.42f, 0.28f, 0.42f, 0.58f, 0.03f), Seg(x, y, 0.58f, 0.28f, 0.58f, 0.58f, 0.03f));
                break;
            case "mapa":        // mapa plegado en tres paneles
                forma = (x, y) => Min(Seg(x, y, 0.14f, 0.24f, 0.14f, 0.8f, 0.045f), Seg(x, y, 0.86f, 0.2f, 0.86f, 0.76f, 0.045f),
                                      Seg(x, y, 0.38f, 0.2f, 0.38f, 0.76f, 0.045f), Seg(x, y, 0.62f, 0.24f, 0.62f, 0.8f, 0.045f),
                                      Seg(x, y, 0.14f, 0.8f, 0.38f, 0.76f, 0.045f), Seg(x, y, 0.38f, 0.76f, 0.62f, 0.8f, 0.045f),
                                      Seg(x, y, 0.62f, 0.8f, 0.86f, 0.76f, 0.045f), Seg(x, y, 0.14f, 0.24f, 0.38f, 0.2f, 0.045f),
                                      Seg(x, y, 0.38f, 0.2f, 0.62f, 0.24f, 0.045f), Seg(x, y, 0.62f, 0.24f, 0.86f, 0.2f, 0.045f));
                break;
            case "reloj":       // esfera y manecillas
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.36f)) - 0.055f,
                                      Seg(x, y, 0.5f, 0.5f, 0.5f, 0.75f, 0.05f), Seg(x, y, 0.5f, 0.5f, 0.67f, 0.41f, 0.05f));
                break;
            case "lista":       // tres renglones con viñeta
                forma = (x, y) => Min(Circ(x, y, 0.2f, 0.72f, 0.065f), Seg(x, y, 0.38f, 0.72f, 0.84f, 0.72f, 0.045f),
                                      Circ(x, y, 0.2f, 0.5f, 0.065f), Seg(x, y, 0.38f, 0.5f, 0.84f, 0.5f, 0.045f),
                                      Circ(x, y, 0.2f, 0.28f, 0.065f), Seg(x, y, 0.38f, 0.28f, 0.84f, 0.28f, 0.045f));
                break;
            case "capas":       // pisos apilados
                forma = (x, y) => Min(Caja(x, y, 0.5f, 0.74f, 0.32f, 0.06f) - 0.02f, Caja(x, y, 0.5f, 0.5f, 0.32f, 0.06f) - 0.02f,
                                      Caja(x, y, 0.5f, 0.26f, 0.32f, 0.06f) - 0.02f);
                break;
            case "pin":         // marcador de ubicación con hueco
                forma = (x, y) => Mathf.Max(
                    Min(Circ(x, y, 0.5f, 0.6f, 0.27f),
                        Mathf.Max(Mathf.Abs(x - 0.5f) - (y - 0.08f) * 0.55f, Mathf.Max(y - 0.6f, 0.08f - y))),
                    -Circ(x, y, 0.5f, 0.6f, 0.1f));
                break;
            case "persona":     // cabeza y hombros
                forma = (x, y) => Min(Circ(x, y, 0.5f, 0.68f, 0.16f),
                                      Mathf.Max(Elipse(x, y, 0.5f, 0.2f, 0.3f, 0.26f), y - 0.42f));
                break;
            case "llave":       // aro, vástago y dientes
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.3f, 0.62f, 0.15f)) - 0.05f,
                                      Seg(x, y, 0.41f, 0.51f, 0.83f, 0.18f, 0.05f),
                                      Seg(x, y, 0.64f, 0.35f, 0.73f, 0.45f, 0.045f),
                                      Seg(x, y, 0.75f, 0.26f, 0.84f, 0.36f, 0.045f));
                break;
            case "inicio":      // casa: techo, muros y puerta
                forma = (x, y) => Min(Seg(x, y, 0.12f, 0.5f, 0.5f, 0.85f, 0.055f), Seg(x, y, 0.5f, 0.85f, 0.88f, 0.5f, 0.055f),
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.32f, 0.25f, 0.2f)) - 0.05f,
                                      Caja(x, y, 0.5f, 0.22f, 0.07f, 0.11f));
                break;
            case "salir":       // marco abierto y flecha hacia afuera
                forma = (x, y) => Min(Seg(x, y, 0.52f, 0.15f, 0.15f, 0.15f, 0.05f), Seg(x, y, 0.15f, 0.15f, 0.15f, 0.85f, 0.05f),
                                      Seg(x, y, 0.15f, 0.85f, 0.52f, 0.85f, 0.05f),
                                      Seg(x, y, 0.38f, 0.5f, 0.78f, 0.5f, 0.05f), Flecha(x, y, 0.9f, 0.5f, 1f, 0f));
                break;
            case "cubo":
                forma = (x, y) => Caja(x, y, 0.5f, 0.5f, 0.28f, 0.28f);
                break;
            case "rectangulo":
                forma = (x, y) => Caja(x, y, 0.5f, 0.5f, 0.38f, 0.2f);
                break;
            default:            // cilindro: cuerpo con tapa elíptica
                forma = (x, y) => Min(Caja(x, y, 0.5f, 0.45f, 0.26f, 0.27f),
                                      Elipse(x, y, 0.5f, 0.72f, 0.26f, 0.1f), Elipse(x, y, 0.5f, 0.18f, 0.26f, 0.1f));
                break;
        }
        s = Dibujar(n, (x, y) => Relleno(forma(x, y), n));
        iconos[nombre] = s;
        return s;
    }

    // ----- Dibujo con distancias (coordenadas de 0 a 1; negativo = dentro de la forma) -----
    private static Sprite Dibujar(int n, System.Func<float, float, float> alfa)
    {
        Texture2D tex = Textura(n, alfa);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D Textura(int n, System.Func<float, float, float> alfa)
    {
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alfa((x + 0.5f) / n, (y + 0.5f) / n)) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // Distancia → opacidad con borde suavizado de un píxel
    private static float Relleno(float distancia, int n) => Mathf.Clamp01(0.5f - distancia * n);

    private static float Min(params float[] d)
    {
        float m = float.MaxValue;
        foreach (float v in d) m = Mathf.Min(m, v);
        return m;
    }

    private static float Circ(float x, float y, float cx, float cy, float r) =>
        Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

    private static float Elipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        float dx = (x - cx) / rx, dy = (y - cy) / ry;
        return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
    }

    private static float Caja(float x, float y, float cx, float cy, float hx, float hy)
    {
        float dx = Mathf.Abs(x - cx) - hx, dy = Mathf.Abs(y - cy) - hy;
        float fuera = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
        return fuera + Mathf.Min(Mathf.Max(dx, dy), 0f);
    }

    private static float Seg(float x, float y, float ax, float ay, float bx, float by, float grosor)
    {
        float px = x - ax, py = y - ay, vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01((px * vx + py * vy) / (vx * vx + vy * vy));
        float dx = px - vx * t, dy = py - vy * t;
        return Mathf.Sqrt(dx * dx + dy * dy) - grosor;
    }

    // Punta de flecha (triángulo) en (px,py) apuntando hacia (dx,dy)
    private static float Flecha(float x, float y, float px, float py, float dx, float dy)
    {
        const float largo = 0.13f, ancho = 0.11f;
        float bx = px - dx * largo, by = py - dy * largo;            // centro de la base
        float nx = -dy, ny = dx;                                       // perpendicular
        float lx = x - bx, ly = y - by;
        float a = lx * dx + ly * dy;                                   // avance hacia la punta
        float l = Mathf.Abs(lx * nx + ly * ny);                        // separación lateral
        // Triángulo = intersección de "delante de la base" y "dentro de los lados que se cierran en la punta"
        return Mathf.Max(-a, l - ancho * (1f - a / largo));
    }
}
