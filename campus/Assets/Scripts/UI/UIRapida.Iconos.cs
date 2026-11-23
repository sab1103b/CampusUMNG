using UnityEngine;
using UnityEngine.UI;

/// <summary>UIRapida (parte 2): sprites dibujados por código: círculo, redondeado, sombra suave e íconos.</summary>
public static partial class UIRapida
{
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

    private static readonly System.Collections.Generic.Dictionary<int, Sprite> contornos =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// Solo el CONTORNO del rectángulo redondeado de <see cref="Redondeado"/> (trazo de "trazoPx" píxeles hacia adentro, 9-slice).
    /// Con el mismo pixelsPerUnitMultiplier que <see cref="Redondear"/> (22 / radio) el trazo mide trazoPx · radio / 22
    /// unidades: con 2 px y RadioControl ≈ GrosorBordeCampo. El contorno de foco usa <see cref="TrazoContornoPx"/>.
    /// </summary>
    public static Sprite ContornoRedondeado(float trazoPx = 2f)
    {
        trazoPx = Mathf.Clamp(trazoPx, 0.5f, 12f);
        int clave = Mathf.RoundToInt(trazoPx * 8f);
        if (contornos.TryGetValue(clave, out Sprite s) && s != null) return s;
        const int n = 64;
        const float r = 22f / n;
        float medio = trazoPx * 0.5f / n;   // mitad del trazo
        Texture2D tex = Textura(n, (x, y) =>
        {
            float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - (0.5f - r), 0f);
            float dy = Mathf.Max(Mathf.Abs(y - 0.5f) - (0.5f - r), 0f);
            float d = Mathf.Sqrt(dx * dx + dy * dy) - r;           // < 0 dentro de la forma
            return Relleno(Mathf.Abs(d + medio) - medio, n);
        });
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                          SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        contornos[clave] = s;
        return s;
    }

    /// <summary>Píxeles de trazo de <see cref="ContornoRedondeado"/> para que mida "grosor" unidades con ese radio de esquina.</summary>
    public static float TrazoContornoPx(float grosor, float radio = RadioControl) => grosor * 22f / Mathf.Max(1f, radio);

    // ----- Píldora (9-slice): el borde del sprite ES el radio, así con pixelsPerUnitMultiplier = BordePildora / (alto/2)
    // los extremos son semicírculos exactos con cualquier alto (Redondeado tiene 2 px rectos y no llega a píldora) -----
    private const int BordePildora = 32;
    private static Sprite pildora;

    /// <summary>Rectángulo blanco con extremos semicirculares. Úsalo con <see cref="RedondearPildora"/> (ajusta la escala al alto).</summary>
    public static Sprite Pildora()
    {
        if (pildora != null) return pildora;
        const int n = 2 * BordePildora + 4;   // 4 px de centro estirable
        const float r = (float)BordePildora / n;
        Texture2D tex = Textura(n, (x, y) =>
        {
            float dx = Mathf.Max(Mathf.Abs(x - 0.5f) - (0.5f - r), 0f);
            float dy = Mathf.Max(Mathf.Abs(y - 0.5f) - (0.5f - r), 0f);
            return Relleno(Mathf.Sqrt(dx * dx + dy * dy) - r, n);
        });
        pildora = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                SpriteMeshType.FullRect, new Vector4(BordePildora, BordePildora, BordePildora, BordePildora));
        return pildora;
    }

    /// <summary>Aplica <see cref="Pildora"/> a una imagen cuyo lado corto mide "lado" unidades (radio = lado / 2).</summary>
    internal static void AplicarPildora(Image img, float lado)
    {
        if (img == null || lado <= 0f) return;
        if (img.sprite != Pildora()) img.sprite = Pildora();
        if (img.type != Image.Type.Sliced) img.type = Image.Type.Sliced;
        float ppu = BordePildora / (lado * 0.5f);
        if (!Mathf.Approximately(img.pixelsPerUnitMultiplier, ppu)) img.pixelsPerUnitMultiplier = ppu;
    }

    // ----- Sombra suave (9-slice): opaca dentro de la forma de la tarjeta y difuminada SombraExtra hacia afuera -----
    private const float EscalaSombra = 0.5f;   // píxeles del sprite por unidad de canvas (se dibuja a media resolución)
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> sombras =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// Sprite de sombra para tarjetas con ese radio de esquina. La imagen que lo usa debe medir la tarjeta + SombraExtra
    /// por lado y llevar pixelsPerUnitMultiplier = EscalaSombra (lo hace <see cref="EstiloSombra"/>).
    /// </summary>
    public static Sprite SombraSuave(float radio = RadioTarjeta)
    {
        int clave = Mathf.Max(0, Mathf.RoundToInt(radio));
        if (sombras.TryGetValue(clave, out Sprite s) && s != null) return s;
        float difuso = SombraExtra * EscalaSombra;               // ancho del difuminado en píxeles del sprite
        float r = clave * EscalaSombra;                           // radio de la esquina en píxeles del sprite
        int borde = Mathf.CeilToInt(difuso + r) + 1;
        int n = 2 * borde + 2;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[n * n];
        float medio = n * 0.5f, interior = medio - difuso;   // semilado de la forma de la tarjeta
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - medio) - (interior - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - medio) - (interior - r), 0f);
                float f = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - r) / Mathf.Max(difuso, 1f));   // 0 en el borde de la tarjeta, 1 afuera
                float a = 1f - f * f * (3f - 2f * f);                                                    // caída suave
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                          SpriteMeshType.FullRect, new Vector4(borde, borde, borde, borde));
        sombras[clave] = s;
        return s;
    }

    // ----- Esquinas invertidas (9-slice): opacas FUERA del rectángulo redondeado y transparentes dentro -----
    // Recorte suave de fotos sin Mask de stencil (que deja la esquina dentada): se dibujan encima de la foto
    // con el color del fondo. EscalaEsquinas píxeles por unidad de canvas (antialias de ½ unidad).
    private const float EscalaEsquinas = 2f;
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> esquinas =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    /// <summary>
    /// Sprite de esquinas invertidas para un radio dado (unidades de canvas). Se usa Sliced con
    /// pixelsPerUnitMultiplier = 2 (lo hace <see cref="MarcoEsquinas"/>). "conSombra": la parte de la esquina que tapa
    /// la sombra estándar (<see cref="SombraDe(Graphic, float)"/>) lleva esa sombra horneada en el RGB, así al teñirla
    /// con el color del fondo la sombra no se corta en las esquinas (exacto si el fondo es liso).
    /// </summary>
    public static Sprite EsquinasInvertidas(float radio = RadioTarjeta, bool conSombra = true)
    {
        int r = Mathf.Max(1, Mathf.RoundToInt(radio));
        int clave = conSombra ? r : -r;
        if (esquinas.TryGetValue(clave, out Sprite s) && s != null) return s;
        int borde = Mathf.CeilToInt(r * EscalaEsquinas) + 2;
        int n = 2 * borde + 2;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[n * n];
        float alfaSombra = Sombra.a;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Posición en unidades de canvas, medida desde la esquina más cercana (hacia adentro positiva)
                float ux = (x < n / 2 ? x + 0.5f : n - x - 0.5f) / EscalaEsquinas;
                float uy = (y < n / 2 ? y + 0.5f : n - y - 0.5f) / EscalaEsquinas;
                bool arriba = y >= n / 2;
                // Distancia (unidades) al borde redondeado de la tarjeta: negativa dentro
                float dx = Mathf.Max(r - ux, 0f), dy = Mathf.Max(r - uy, 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                float cobertura = Mathf.Clamp01(0.5f + d * EscalaEsquinas);   // 1 fuera de la tarjeta (antialias de 1 px)
                float gris = 1f;
                if (conSombra && cobertura > 0f)
                {
                    // Sombra de SombraDe: misma forma bajada SombraY y difuminada SombraExtra hacia afuera
                    // (arriba el borde de la sombra queda SombraY más adentro; abajo, SombraY más afuera)
                    float uys = arriba ? uy - SombraY : uy + SombraY;
                    float sx = Mathf.Max(r - ux, 0f), sy = Mathf.Max(r - uys, 0f);
                    float ds = Mathf.Sqrt(sx * sx + sy * sy) - r;
                    float f = Mathf.Clamp01(ds / SombraExtra);
                    float a = ds <= 0f ? 1f : 1f - f * f * (3f - 2f * f);
                    gris = 1f - alfaSombra * a;
                }
                byte g = (byte)(gris * 255f);
                px[y * n + x] = new Color32(g, g, g, (byte)(cobertura * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                          SpriteMeshType.FullRect, new Vector4(borde, borde, borde, borde));
        esquinas[clave] = s;
        return s;
    }

    /// <summary>Pone el fondo redondeado a una imagen (radio de esquina en unidades de la interfaz).</summary>
    public static void Redondear(Image img, float radio = RadioControl)
    {
        img.sprite = Redondeado();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 22f / Mathf.Max(1f, radio);
    }

    // Grosor de trazo de los íconos (medio ancho, en fracción del ícono): uno para líneas y otro para detalles
    private const float Trazo = 0.05f, TrazoDetalle = 0.035f;
    // Compensación óptica: la equis (diagonales) y el menos (una sola raya) se ven más finos que el resto con el mismo trazo
    private const float TrazoOptico = 0.055f;
    // Grosor del borde del pin de destino ("pin_borde" = silueta del "pin" dilatada esta fracción del ícono)
    private const float BordePin = 0.04f;

    /// <summary>
    /// Íconos blancos (se tiñen con Image.color): mover, nodo, conectar, puerta, alerta, eliminar, abajo, mas, menos, lapiz,
    /// basura, mapa, reloj, lista, capas, pin, ruta, persona, llave, inicio, salir, cubo, rectangulo, cilindro,
    /// ubicacion, ojo, ojo_tachado, check, pin_contorno y pin_borde (silueta del pin dilatada, sin hueco: su borde, dibujado detrás).
    /// Todos son de contorno (Trazo / TrazoDetalle) salvo "pin", que va relleno a propósito: es un marcador
    /// sobre el mapa y debe leerse como una mancha sólida a cualquier tamaño. En botones, avatares y filas usa
    /// "pin_contorno" (mismo lenguaje que el resto de íconos de la interfaz). Los PUNTOS pequeños (centro de "nodo"
    /// y de "ubicacion", pomo de "puerta", viñetas de "lista", origen de "ruta") sí van rellenos: un punto en contorno
    /// a 44 px se vuelve un anillo ilegible. Las superficies (puerta de "inicio", cuerpos) van en contorno.
    /// </summary>
    public static Sprite Icono(string nombre)
    {
        if (iconos.TryGetValue(nombre, out Sprite s) && s != null) return s;   // la textura pudo destruirse (cambio de escena)
        const int n = 96;
        System.Func<float, float, float> forma;
        switch (nombre)
        {
            case "mover":       // cruz con puntas de flecha en V (trazo, igual que el resto de íconos de contorno)
                forma = (x, y) => Min(Seg(x, y, 0.5f, 0.14f, 0.5f, 0.86f, Trazo), Seg(x, y, 0.14f, 0.5f, 0.86f, 0.5f, Trazo),
                                      Punta(x, y, 0.5f, 0.86f, 0f, 1f), Punta(x, y, 0.5f, 0.14f, 0f, -1f),
                                      Punta(x, y, 0.86f, 0.5f, 1f, 0f), Punta(x, y, 0.14f, 0.5f, -1f, 0f));
                break;
            case "nodo":        // punto con anillo
                forma = (x, y) => Min(Circ(x, y, 0.5f, 0.5f, 0.17f), Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.33f)) - Trazo);
                break;
            case "conectar":    // dos nodos en contorno (como "nodo") unidos por una línea que llega a su borde
                forma = FormaConectar();
                break;
            case "puerta":      // marco de puerta con punto
                forma = (x, y) => Min(Mathf.Abs(Caja(x, y, 0.5f, 0.47f, 0.24f, 0.34f)) - Trazo,
                                      Circ(x, y, 0.62f, 0.47f, 0.045f), Caja(x, y, 0.5f, 0.11f, 0.36f, TrazoDetalle));
                break;
            case "alerta":      // círculo con "!" (avisos de error)
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.36f)) - Trazo,
                                      Seg(x, y, 0.5f, 0.45f, 0.5f, 0.67f, Trazo), Circ(x, y, 0.5f, 0.31f, 0.055f));
                break;
            case "eliminar":    // equis
                forma = (x, y) => Min(Seg(x, y, 0.22f, 0.22f, 0.78f, 0.78f, TrazoOptico), Seg(x, y, 0.22f, 0.78f, 0.78f, 0.22f, TrazoOptico));
                break;
            case "check":       // marca de verificación (casillas, confirmaciones); diagonales: trazo con compensación óptica
                forma = (x, y) => Min(Seg(x, y, 0.2f, 0.52f, 0.41f, 0.3f, TrazoOptico), Seg(x, y, 0.41f, 0.3f, 0.81f, 0.72f, TrazoOptico));
                break;
            case "abajo":       // flecha de lista desplegable
                forma = (x, y) => Min(Seg(x, y, 0.2f, 0.64f, 0.5f, 0.34f, Trazo), Seg(x, y, 0.5f, 0.34f, 0.8f, 0.64f, Trazo));
                break;
            case "mas":         // signo más
                forma = (x, y) => Min(Seg(x, y, 0.5f, 0.2f, 0.5f, 0.8f, Trazo), Seg(x, y, 0.2f, 0.5f, 0.8f, 0.5f, Trazo));
                break;
            case "menos":       // signo menos (mismo largo que "mas"; trazo con compensación óptica)
                forma = (x, y) => Seg(x, y, 0.2f, 0.5f, 0.8f, 0.5f, TrazoOptico);
                break;
            case "lapiz":       // lápiz en diagonal en contorno: cuerpo y línea de la punta (sin virola: menos trazos)
                forma = (x, y) =>
                {
                    float a = (x + y) * 0.7071f;                 // avance a lo largo del lápiz
                    float lat = Mathf.Abs(x - y) * 0.7071f;      // distancia al eje
                    float cuerpo = Mathf.Max(lat - 0.12f * Mathf.Clamp01((a - 0.2f) / 0.3f), Mathf.Max(0.2f - a, a - 1.15f));
                    return Min(Mathf.Abs(cuerpo) - Trazo,
                               Seg(x, y, 0.269f, 0.438f, 0.438f, 0.269f, TrazoDetalle));   // fin de la punta (a = 0,5)
                };
                break;
            case "basura":      // papelera: tapa, asa y cuerpo con líneas
                forma = (x, y) => Min(Seg(x, y, 0.2f, 0.76f, 0.8f, 0.76f, Trazo),
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.85f, 0.1f, 0.05f)) - TrazoDetalle,
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.42f, 0.24f, 0.28f)) - Trazo,
                                      Seg(x, y, 0.42f, 0.28f, 0.42f, 0.58f, TrazoDetalle), Seg(x, y, 0.58f, 0.28f, 0.58f, 0.58f, TrazoDetalle));
                break;
            case "mapa":        // mapa plegado en tres paneles
                forma = (x, y) => Min(Min(Seg(x, y, 0.14f, 0.24f, 0.14f, 0.8f, Trazo), Seg(x, y, 0.86f, 0.2f, 0.86f, 0.76f, Trazo),
                                          Seg(x, y, 0.38f, 0.2f, 0.38f, 0.76f, TrazoDetalle), Seg(x, y, 0.62f, 0.24f, 0.62f, 0.8f, TrazoDetalle)),
                                      Min(Seg(x, y, 0.14f, 0.8f, 0.38f, 0.76f, Trazo), Seg(x, y, 0.38f, 0.76f, 0.62f, 0.8f, Trazo),
                                          Seg(x, y, 0.62f, 0.8f, 0.86f, 0.76f, Trazo)),
                                      Min(Seg(x, y, 0.14f, 0.24f, 0.38f, 0.2f, Trazo), Seg(x, y, 0.38f, 0.2f, 0.62f, 0.24f, Trazo),
                                          Seg(x, y, 0.62f, 0.24f, 0.86f, 0.2f, Trazo)));
                break;
            case "reloj":       // esfera y manecillas
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.36f)) - Trazo,
                                      Seg(x, y, 0.5f, 0.5f, 0.5f, 0.75f, Trazo), Seg(x, y, 0.5f, 0.5f, 0.67f, 0.41f, Trazo));
                break;
            case "lista":       // tres renglones con viñeta
                forma = (x, y) => Min(Circ(x, y, 0.2f, 0.72f, 0.065f), Seg(x, y, 0.38f, 0.72f, 0.84f, 0.72f, Trazo),
                                      Circ(x, y, 0.2f, 0.5f, 0.065f), Seg(x, y, 0.38f, 0.5f, 0.84f, 0.5f, Trazo),
                                      Circ(x, y, 0.2f, 0.28f, 0.065f), Seg(x, y, 0.38f, 0.28f, 0.84f, 0.28f, Trazo));
                break;
            case "capas":       // rombo arriba y dos capas (chevrones) debajo: hojas apiladas vistas en perspectiva
                forma = (x, y) => Min(Min(Seg(x, y, 0.14f, 0.62f, 0.5f, 0.8f, Trazo), Seg(x, y, 0.5f, 0.8f, 0.86f, 0.62f, Trazo),
                                          Seg(x, y, 0.86f, 0.62f, 0.5f, 0.44f, Trazo), Seg(x, y, 0.5f, 0.44f, 0.14f, 0.62f, Trazo)),
                                      Min(Seg(x, y, 0.14f, 0.46f, 0.5f, 0.28f, Trazo), Seg(x, y, 0.5f, 0.28f, 0.86f, 0.46f, Trazo),
                                          Seg(x, y, 0.14f, 0.3f, 0.5f, 0.12f, Trazo), Seg(x, y, 0.5f, 0.12f, 0.86f, 0.3f, Trazo)));
                break;
            case "pin":         // marcador de ubicación con hueco
                forma = (x, y) => Mathf.Max(
                    Min(Circ(x, y, 0.5f, 0.6f, 0.27f),
                        // punta: triángulo cortado en y = 0,5 (por debajo del ancho del círculo: sin "orejas")
                        Mathf.Max(Mathf.Abs(x - 0.5f) - (y - 0.08f) * 0.5f, Mathf.Max(y - 0.5f, 0.08f - y))),
                    -Circ(x, y, 0.5f, 0.6f, 0.1f));
                break;
            case "pin_borde":   // silueta del "pin" dilatada BordePin y SIN hueco: se dibuja detrás del pin con la MISMA
                                // posición, tamaño y pivote (punta en y = 0,08), así el borde rodea la punta de forma pareja
                forma = (x, y) => Min(Circ(x, y, 0.5f, 0.6f, 0.27f + BordePin),
                                      // lado del cono como distancia real (÷ √1,25) para que el borde tenga el mismo grosor
                                      Mathf.Max((Mathf.Abs(x - 0.5f) - (y - 0.08f) * 0.5f) * 0.8944f - BordePin,
                                                Mathf.Max(y - 0.5f, 0.012f - y)));
                break;
            case "pin_contorno": // el "pin" en contorno (botones, avatares, filas): silueta con Trazo y el hueco como anillo.
                                 // El relleno ("pin") queda solo para marcadores sobre el mapa.
                forma = (x, y) =>
                {
                    float silueta = Min(Circ(x, y, 0.5f, 0.6f, 0.27f),
                                        // lado del cono como distancia real (÷ √1,25): el trazo tiene el mismo grosor
                                        Mathf.Max((Mathf.Abs(x - 0.5f) - (y - 0.08f) * 0.5f) * 0.8944f,
                                                  Mathf.Max(y - 0.5f, 0.08f - y)));
                    return Min(Mathf.Abs(silueta) - Trazo, Mathf.Abs(Circ(x, y, 0.5f, 0.6f, 0.085f)) - TrazoDetalle);
                };
                break;
            case "ruta":        // origen (punto) y destino (anillo) unidos por un camino curvo continuo
                forma = FormaRuta();
                break;
            case "persona":     // cabeza y hombros en contorno; la base del torso se corta en 0,16 (sobre el centro de la
                                // elipse) para que las esquinas de abajo no se adelgacen en punta
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.5f, 0.7f, 0.14f)) - Trazo,
                                      Mathf.Abs(Mathf.Max(Elipse(x, y, 0.5f, 0.14f, 0.32f, 0.3f), 0.16f - y)) - Trazo);
                break;
            case "ubicacion":   // mira: anillo, cuatro marcas y punto central (Tú estás aquí)
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.29f)) - Trazo,
                                      Circ(x, y, 0.5f, 0.5f, 0.09f),
                                      Min(Seg(x, y, 0.5f, 0.06f, 0.5f, 0.2f, Trazo), Seg(x, y, 0.5f, 0.8f, 0.5f, 0.94f, Trazo),
                                          Seg(x, y, 0.06f, 0.5f, 0.2f, 0.5f, Trazo), Seg(x, y, 0.8f, 0.5f, 0.94f, 0.5f, Trazo)));
                break;
            case "ojo":         // mostrar contraseña: párpado almendrado y pupila
                forma = Ojo;
                break;
            case "ojo_tachado": // ocultar contraseña: párpado SIN pupila (la diagonal la partía en astillas) y diagonal
                forma = (x, y) =>   // con hueco de 1,5·Trazo alrededor para que no se pegue al párpado
                {
                    float raya = Seg(x, y, 0.16f, 0.16f, 0.84f, 0.84f, Trazo);
                    return Min(Mathf.Max(Parpado(x, y), -(raya - 1.5f * Trazo)), raya);
                };
                break;
            case "llave":       // aro de radio 0,18 (peso óptico como "persona"/"reloj"), vástago a 45° que nace en el
                                // aro y dos dientes perpendiculares (0,12) separados 0,22; conjunto centrado en el ícono
                forma = (x, y) => Min(Mathf.Abs(Circ(x, y, 0.3f, 0.66f, 0.18f)) - Trazo,
                                      Seg(x, y, 0.427f, 0.533f, 0.795f, 0.165f, Trazo),
                                      Seg(x, y, 0.639f, 0.321f, 0.724f, 0.406f, Trazo),
                                      Seg(x, y, 0.795f, 0.165f, 0.88f, 0.25f, Trazo));
                break;
            case "inicio":      // casa: techo, muros y puerta (todo en contorno)
                forma = (x, y) => Min(Seg(x, y, 0.12f, 0.5f, 0.5f, 0.85f, Trazo), Seg(x, y, 0.5f, 0.85f, 0.88f, 0.5f, Trazo),
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.32f, 0.25f, 0.2f)) - Trazo,
                                      Mathf.Abs(Caja(x, y, 0.5f, 0.22f, 0.085f, 0.1f)) - TrazoDetalle);
                break;
            case "salir":       // marco abierto y flecha hacia afuera
                forma = (x, y) => Min(Seg(x, y, 0.52f, 0.15f, 0.15f, 0.15f, Trazo), Seg(x, y, 0.15f, 0.15f, 0.15f, 0.85f, Trazo),
                                      Seg(x, y, 0.15f, 0.85f, 0.52f, 0.85f, Trazo),
                                      Seg(x, y, 0.38f, 0.5f, 0.86f, 0.5f, Trazo), Punta(x, y, 0.86f, 0.5f, 1f, 0f));
                break;
            // Formas 3D en contorno (mismo trazo que el resto de íconos)
            case "cubo":        // cubo isométrico: hexágono y las tres aristas que llegan al vértice frontal
                forma = (x, y) => Min(
                    Min(Seg(x, y, 0.5f, 0.86f, 0.812f, 0.68f, Trazo), Seg(x, y, 0.812f, 0.68f, 0.812f, 0.32f, Trazo),
                        Seg(x, y, 0.812f, 0.32f, 0.5f, 0.14f, Trazo), Seg(x, y, 0.5f, 0.14f, 0.188f, 0.32f, Trazo),
                        Seg(x, y, 0.188f, 0.32f, 0.188f, 0.68f, Trazo), Seg(x, y, 0.188f, 0.68f, 0.5f, 0.86f, Trazo)),
                    Seg(x, y, 0.5f, 0.5f, 0.5f, 0.14f, TrazoDetalle),
                    Seg(x, y, 0.5f, 0.5f, 0.188f, 0.68f, TrazoDetalle),
                    Seg(x, y, 0.5f, 0.5f, 0.812f, 0.68f, TrazoDetalle));
                break;
            case "rectangulo":
                forma = (x, y) => Mathf.Abs(Caja(x, y, 0.5f, 0.5f, 0.36f, 0.26f)) - Trazo;   // proporción de edificio alargado, más alto que antes (se leía como raya)
                break;
            case "cilindro":    // dos lados rectos, tapa superior completa y media elipse abajo (borde visible)
                forma = (x, y) => Min(Seg(x, y, 0.24f, 0.27f, 0.24f, 0.73f, Trazo), Seg(x, y, 0.76f, 0.27f, 0.76f, 0.73f, Trazo),
                                      ArcoElipse(x, y, 0.5f, 0.73f, 0.26f, 0.1f, 0f, 2f * Mathf.PI, 32),
                                      ArcoElipse(x, y, 0.5f, 0.27f, 0.26f, 0.1f, Mathf.PI, 2f * Mathf.PI, 16));
                break;
            default:            // nombre desconocido: aviso y forma neutra (anillo) para que no pase inadvertido
                Debug.LogWarning("UIRapida.Icono: no existe el ícono '" + nombre + "'; se usa un anillo.");
                forma = (x, y) => Mathf.Abs(Circ(x, y, 0.5f, 0.5f, 0.3f)) - Trazo;
                break;
        }
        s = Dibujar(n, (x, y) => Relleno(forma(x, y), n), true);   // con mipmaps: se ven de 44 a 130 unidades
        iconos[nombre] = s;
        return s;
    }

    // Ícono "ojo": almendra = intersección de dos círculos (de 0,1 a 0,9 en x; de 0,28 a 0,72 en y) y pupila
    private static float Ojo(float x, float y) => Min(Parpado(x, y), Circ(x, y, 0.5f, 0.5f, 0.11f));

    // Contorno del párpado (almendra) sin pupila
    private static float Parpado(float x, float y)
    {
        const float r = 0.4736f;
        float almendra = Mathf.Max(Circ(x, y, 0.5f, 0.2464f, r), Circ(x, y, 0.5f, 0.7536f, r));
        return Mathf.Abs(almendra) - Trazo;
    }

    // Ícono "conectar": anillos de radio 0,13 en (0,24;0,28) y (0,76;0,72); la línea arranca en el borde exterior
    // de cada anillo para que los nodos se lean huecos (mismo lenguaje que el ícono "nodo").
    private static System.Func<float, float, float> FormaConectar()
    {
        const float r = 0.13f;
        Vector2 a = new Vector2(0.24f, 0.28f), b = new Vector2(0.76f, 0.72f);
        Vector2 u = (b - a).normalized * (r + Trazo);
        Vector2 a2 = a + u, b2 = b - u;
        return (x, y) => Min(Mathf.Abs(Circ(x, y, a.x, a.y, r)) - Trazo, Mathf.Abs(Circ(x, y, b.x, b.y, r)) - Trazo,
                             Seg(x, y, a2.x, a2.y, b2.x, b2.y, Trazo));
    }

    // Ícono "ruta": un punto de origen y un anillo de destino unidos por una curva suave en S (Bézier cúbica)
    // dibujada como línea continua (polilínea de "partes" tramos repartidos por LONGITUD de la curva).
    // Se eligió renderizando variantes a 44 y 64 px: el punteado (5 o 7 tramos) se leía como puntos sueltos;
    // la línea continua con los extremos marcados se lee como "camino de A a B" a cualquier tamaño.
    private static System.Func<float, float, float> FormaRuta()
    {
        const int partes = 40, muestras = 200;
        const float inicio = 0.12f, fin = 0.17f;   // la línea entra un poco bajo el punto y llega al borde del anillo
        Vector2[] p = new Vector2[muestras + 1];
        float[] largo = new float[muestras + 1];
        for (int i = 0; i <= muestras; i++)
        {
            p[i] = BezierRuta(i / (float)muestras);
            if (i > 0) largo[i] = largo[i - 1] + Vector2.Distance(p[i - 1], p[i]);
        }
        System.Func<float, Vector2> en = s =>
        {
            for (int i = 1; i <= muestras; i++)
                if (largo[i] >= s) return Vector2.Lerp(p[i - 1], p[i], (s - largo[i - 1]) / Mathf.Max(largo[i] - largo[i - 1], 1e-6f));
            return p[muestras];
        };
        Vector2[] q = new Vector2[partes + 1];
        float s0 = inicio, s1 = largo[muestras] - fin;
        for (int i = 0; i <= partes; i++) q[i] = en(Mathf.Lerp(s0, s1, i / (float)partes));
        return (x, y) =>
        {
            float m = Min(Circ(x, y, 0.2f, 0.2f, 0.1f), Mathf.Abs(Circ(x, y, 0.8f, 0.8f, 0.1f)) - Trazo);
            for (int i = 0; i < partes; i++) m = Min(m, Seg(x, y, q[i].x, q[i].y, q[i + 1].x, q[i + 1].y, Trazo));
            return m;
        };
    }

    // Puntos de control: (0,2;0,2) -> (0,62;0,2) -> (0,38;0,8) -> (0,8;0,8): S suave
    private static Vector2 BezierRuta(float t)
    {
        Vector2 a = new Vector2(0.2f, 0.2f), b = new Vector2(0.62f, 0.2f), c = new Vector2(0.38f, 0.8f), d = new Vector2(0.8f, 0.8f);
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    // Contorno de un arco de elipse (de a0 a a1, en radianes) como polilínea de "partes" segmentos de Trazo:
    // la distancia queda bien normalizada (la fórmula de Elipse() deforma el grosor en elipses achatadas)
    private static float ArcoElipse(float x, float y, float cx, float cy, float rx, float ry, float a0, float a1, int partes)
    {
        float m = float.MaxValue;
        float px = cx + rx * Mathf.Cos(a0), py = cy + ry * Mathf.Sin(a0);
        for (int i = 1; i <= partes; i++)
        {
            float ang = a0 + (a1 - a0) * i / partes;
            float qx = cx + rx * Mathf.Cos(ang), qy = cy + ry * Mathf.Sin(ang);
            m = Min(m, Seg(x, y, px, py, qx, qy, Trazo));
            px = qx; py = qy;
        }
        return m;
    }

    // ----- Dibujo con distancias (coordenadas de 0 a 1; negativo = dentro de la forma) -----
    private static Sprite Dibujar(int n, System.Func<float, float, float> alfa, bool mipmaps = false)
    {
        Texture2D tex = Textura(n, alfa, mipmaps);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    // "mipmaps" (solo íconos): el ícono de 96 px se dibuja a 44-64 unidades (≈ 25-60 px en pantallas chicas);
    // sin mipmaps la reducción con filtro bilineal salta píxeles y el trazo fino parpadea o se ve dentado.
    // Las formas 9-slice (redondeado, sombras) se dibujan a escala ≥ 1 y no los necesitan.
    private static Texture2D Textura(int n, System.Func<float, float, float> alfa, bool mipmaps = false)
    {
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, mipmaps)
        {
            filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alfa((x + 0.5f) / n, (y + 0.5f) / n)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(mipmaps, true);   // mipmaps solo en íconos; sin copia en CPU (no se vuelve a leer)
        return tex;
    }

    // Distancia → opacidad con borde suavizado de un píxel
    private static float Relleno(float distancia, int n) => Mathf.Clamp01(0.5f - distancia * n);

    // Sobrecargas sin "params": no crean un arreglo por cada píxel al dibujar
    private static float Min(float a, float b) => a < b ? a : b;
    private static float Min(float a, float b, float c) => Min(Min(a, b), c);
    private static float Min(float a, float b, float c, float d) => Min(Min(a, b), Min(c, d));
    private static float Min(float a, float b, float c, float d, float e) => Min(Min(a, b, c, d), e);
    private static float Min(float a, float b, float c, float d, float e, float f) => Min(Min(a, b, c), Min(d, e, f));

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

    // Punta de flecha en V (dos trazos a 45°) con el vértice en (px,py) apuntando hacia (dx,dy) (vector unitario)
    private static float Punta(float x, float y, float px, float py, float dx, float dy)
    {
        const float brazo = 0.11f;                                     // avance y separación lateral de cada brazo
        float nx = -dy, ny = dx;                                       // perpendicular
        float bx = px - dx * brazo, by = py - dy * brazo;
        return Min(Seg(x, y, px, py, bx + nx * brazo, by + ny * brazo, Trazo),
                   Seg(x, y, px, py, bx - nx * brazo, by - ny * brazo, Trazo));
    }
}
