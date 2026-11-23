using System.Collections.Generic;
using Mapbox.Utils;
using UnityEngine;

/// <summary>Cómo llegar: estilo y ancho de la línea, proyección de la ruta al mundo y suavizado de esquinas.</summary>
public partial class ComoLlegarController
{
    // Color de ruta por código (no depende del material de la escena) + contorno blanco más ancho debajo
    private void PrepararEstiloLinea()
    {
        Material baseMat = linea.sharedMaterial;
        if (baseMat != null)
        {
            if (materialLinea != null) Destroy(materialLinea);
            materialLinea = new Material(baseMat) { name = "Ruta_Linea" };
            GeoMapa.PonerColor(materialLinea, UIRapida.RutaColor);
            linea.sharedMaterial = materialLinea;
        }
        linea.startColor = linea.endColor = UIRapida.RutaColor;   // por si el shader usa color de vértice

        if (contorno == null)
        {
            GameObject go = new GameObject("RutaContorno");
            go.transform.SetParent(linea.transform, false);
            contorno = go.AddComponent<LineRenderer>();
        }
        contorno.useWorldSpace = true;
        contorno.numCornerVertices = linea.numCornerVertices;
        contorno.numCapVertices = linea.numCapVertices;
        contorno.alignment = linea.alignment;
        contorno.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        contorno.receiveShadows = false;
        if (baseMat != null)
        {
            if (materialContorno != null) Destroy(materialContorno);
            materialContorno = new Material(baseMat) { name = "Ruta_Contorno" };
            GeoMapa.PonerColor(materialContorno, UIRapida.SobreColor);
            contorno.sharedMaterial = materialContorno;
            if (linea.sharedMaterial != null) linea.sharedMaterial.renderQueue = materialContorno.renderQueue + 1;
        }
        contorno.startColor = contorno.endColor = UIRapida.SobreColor;
        contorno.positionCount = 0;
        contorno.enabled = false;
        zoomAnchoAplicado = float.NaN;
        anchoAplicado = -1f;
    }

    // Ancho = max(anchoRutaM metros reales, anchoMinimoPx píxeles en pantalla).
    // La escala del mapa solo se vuelve a medir cuando cambia el zoom de Mapbox; el mínimo en
    // píxeles depende de la pose de la cámara: solo se recalcula si cambió el zoom, la pose
    // (CamaraMapa.VersionPose) o el alto de la pantalla. Quieto no hace nada.
    private int versionPoseAncho = int.MinValue;
    private int altoPantallaAncho = -1;
    private bool porPixelValido;

    private void ActualizarAncho()
    {
        float zoom = mapa.Zoom;
        int version = CamaraMapa.VersionPose;
        bool sinCambios = zoom == zoomAnchoAplicado && version == versionPoseAncho &&
                          Screen.height == altoPantallaAncho && porPixelValido && anchoAplicado > 0f &&
                          (!pinCreado || escalaPinAplicada > 0f);
        if (sinCambios) return;

        if (zoom != zoomAnchoAplicado)
        {
            zoomAnchoAplicado = zoom;
            unidadesPorMetroRuta = GeoMapa.UnidadesPorMetro(mapa);
        }

        float porPixel = UnidadesPorPixel();
        // Sin cámara activa (porPixel = 0) se vuelve a intentar en el siguiente frame
        porPixelValido = porPixel > 0f;
        versionPoseAncho = version;
        altoPantallaAncho = Screen.height;
        ActualizarEscalaPin(porPixel);

        float ancho = Mathf.Max(0.01f, anchoRutaM * unidadesPorMetroRuta, anchoMinimoPx * porPixel);
        if (anchoAplicado > 0f && Mathf.Abs(ancho - anchoAplicado) <= anchoAplicado * 0.01f) return;   // cambio imperceptible
        anchoAplicado = ancho;
        linea.startWidth = linea.endWidth = ancho;
        if (contorno != null) contorno.startWidth = contorno.endWidth = ancho * factorContorno;   // proporcional
    }

    // Marcador creado por código: tamPinM metros reales, sin bajar de tamPinMinimoPx píxeles en pantalla
    private void ActualizarEscalaPin(float porPixel)
    {
        if (!pinCreado || pinDestino == null) return;
        float escala = Mathf.Max(0.01f, tamPinM * unidadesPorMetroRuta, tamPinMinimoPx * porPixel);
        if (escalaPinAplicada > 0f && Mathf.Abs(escala - escalaPinAplicada) <= escalaPinAplicada * 0.01f) return;
        escalaPinAplicada = escala;
        pinDestino.localScale = new Vector3(escala, escala, escala);
    }

    // Pin de destino creado por código: el MISMO dibujo que el ícono "pin" de la interfaz (UIRapida.Icono),
    // en un cartel (billboard) que siempre mira a la cámara
    private const float AnchoPinUnidades = 1.3f;   // ancho del ícono en "unidades de marcador"
    private const float BordePinFactor = 1.18f;    // solo si falta el sprite "pin_borde": borde = ícono escalado
    private const float PuntaPinY = 0.08f;         // la punta del ícono "pin" está a 0,08 del borde inferior
    private Material materialPin;                  // copia propia (Sprites/Default): se destruye en OnDestroy

    /// <summary>
    /// Marcador de destino por código (la escena no trae pinDestino): el ícono "pin" de la interfaz
    /// (UIRapida.Icono("pin")) en UIRapida.DestinoColor sobre su borde blanco UIRapida.Icono("pin_borde")
    /// (silueta dilatada del mismo pin, con la MISMA posición y escala: las puntas coinciden),
    /// como un cartel que siempre mira a la cámara (OrientarPin): se ve igual que el pin de la UI en 2D y en 3D.
    /// La punta del ícono queda exactamente sobre el punto del destino. Sin colisionadores (no tapa toques
    /// del mapa). Medidas en "unidades de marcador": la escala real (metros con mínimo en píxeles) la pone
    /// ActualizarEscalaPin. Lo ocultan Pausar, OcultarDestino y LimpiarRuta como a un pin de la escena.
    /// </summary>
    private void CrearPinDestino()
    {
        GameObject raiz = new GameObject("PinDestino");
        pinDestino = raiz.transform;
        pinCreado = true;
        escalaPinAplicada = -1f;

        Sprite icono = UIRapida.Icono("pin");
        if (icono == null) return;
        // Sprites/Default está en "Always Included Shaders" (viaja en el APK) y URP lo dibuja como unlit transparente
        Shader s = Shader.Find("Sprites/Default");
        if (s != null) materialPin = new Material(s) { name = "PinDestino" };

        // Escala del sprite para que el ícono mida AnchoPinUnidades, y altura para que la punta toque el suelo
        float tamSprite = Mathf.Max(0.0001f, icono.bounds.size.x);
        float escala = AnchoPinUnidades / tamSprite;
        float centroY = (0.5f - PuntaPinY) * AnchoPinUnidades;
        Sprite borde = UIRapida.Icono("pin_borde");
        if (borde != null) ParteDelPin(raiz.transform, borde, UIRapida.SobreColor, escala, centroY, 0);          // borde (detrás)
        else ParteDelPin(raiz.transform, icono, UIRapida.SobreColor, escala * BordePinFactor, centroY, 0);      // respaldo
        ParteDelPin(raiz.transform, icono, UIRapida.DestinoColor, escala, centroY, 1);                           // ícono
    }

    private void ParteDelPin(Transform padre, Sprite sprite, Color color, float escala, float centroY, int orden)
    {
        GameObject go = new GameObject(orden == 0 ? "PinBorde" : "PinIcono");
        go.transform.SetParent(padre, false);
        go.transform.localPosition = new Vector3(0f, centroY, 0f);
        go.transform.localScale = new Vector3(escala, escala, 1f);
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        if (materialPin != null) r.sharedMaterial = materialPin;
        r.sortingOrder = orden;   // el ícono siempre por delante de su borde
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // El cartel del pin mira a la cámara (misma rotación que ella): siempre se ve de frente y derecho en pantalla.
    // Solo escribe el transform si la cámara giró.
    private void LateUpdate()
    {
        if (!pinCreado || pinDestino == null || !pinDestino.gameObject.activeSelf) return;
        CamaraMapa cm = Camara();
        if (cm == null) return;
        Quaternion giro = cm.transform.rotation;
        if (Quaternion.Angle(pinDestino.rotation, giro) > 0.01f) pinDestino.rotation = giro;
    }

    // Cuántas unidades del mundo ocupa un píxel en el punto del suelo que mira la cámara
    private float UnidadesPorPixel()
    {
        if (camaraRuta == null || !camaraRuta.isActiveAndEnabled)
        {
            // Sin cámara activa se vuelve a buscar como máximo una vez por segundo (no en cada frame)
            if (Time.unscaledTime < proximaBusquedaCamara) return 0f;
            proximaBusquedaCamara = Time.unscaledTime + 1f;
            CamaraMapa cm = FindFirstObjectByType<CamaraMapa>();
            camaraRuta = cm != null ? cm.GetComponent<Camera>() : Camera.main;
            if (camaraRuta == null || !camaraRuta.isActiveAndEnabled) return 0f;
        }
        if (Screen.height <= 0) return 0f;
        if (camaraRuta.orthographic) return 2f * camaraRuta.orthographicSize / Screen.height;

        Vector3 posCam = camaraRuta.transform.position;
        float distancia = GeoMapa.PuntoEnSuelo(camaraRuta, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out Vector3 suelo)
            ? Vector3.Distance(posCam, suelo)
            : Mathf.Abs(posCam.y);
        return 2f * distancia * Mathf.Tan(camaraRuta.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
    }

    // Pasa la ruta (lat/lon) al mundo de Unity y la dibuja
    private void Proyectar(Vector3 origenMundo)
    {
        mapaCambiado = false;
        if (trazoGeo == null || trazoGeo.Count == 0) return;
        referenciaMundo = DestinoEnMundo();

        ActualizarAncho();

        // El arreglo solo se vuelve a crear si cambia la cantidad de puntos (al mover o hacer zoom se reutiliza)
        int n = trazoGeo.Count;
        if (puntosMundo.Length != n) puntosMundo = new Vector3[n];
        Vector3[] puntos = puntosMundo;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = mapa.GeoToWorldPosition(trazoGeo[i], false);
            p.y = origenMundo.y;
            puntos[i] = p;
        }
        puntos[0] = origenMundo;   // el inicio sale exactamente del marcador
        origenDibujado = origenDespierto = origenMundo;
        CamaraMapa.Despertar();    // la ruta cambió de forma visible: este frame se dibuja a 60 fps

        PuntosEsperados = n;
        linea.positionCount = n;
        linea.SetPositions(puntos);

        if (contorno != null)
        {
            // Misma ruta (desfase mínimo fijo, no depende del zoom): el borde blanco queda debajo
            // de la línea azul por el renderQueue, sin separarse de ella en la vista 3D
            for (int i = 0; i < n; i++) puntos[i].y -= desfaseContorno;
            contorno.positionCount = n;
            contorno.SetPositions(puntos);
            contorno.enabled = linea.enabled && !pausado;
            for (int i = 0; i < n; i++) puntos[i].y += desfaseContorno;
        }

        // El pin queda donde termina la ruta (la entrada del lugar o el lugar)
        if (pinDestino != null)
        {
            Vector3 fin = puntos[puntos.Length - 1];
            // El pin creado por código se apoya en el suelo (la línea va a la altura del marcador)
            if (pinCreado) fin.y = mapa.GeoToWorldPosition(trazoGeo[n - 1], false).y;
            pinDestino.position = fin;
            pinDestino.gameObject.SetActive(!pausado);
        }

        // Al pedir la ruta (MostrarRuta) se encuadra una sola vez todo el recorrido; después la cámara es del usuario
        if (encuadrarPendiente && !pausado)
        {
            encuadrarPendiente = false;
            CamaraMapa cm = Camara();
            // Si el usuario ya movió el mapa o la ruta tardó (sin GPS), no se le quita la cámara
            if (cm != null && !EncuadreCaducado(cm))
            {
                Vector3 min = puntos[0], max = puntos[0];
                for (int i = 1; i < n; i++) { min = Vector3.Min(min, puntos[i]); max = Vector3.Max(max, puntos[i]); }
                cm.Encuadrar(min, max);
            }
        }
    }

    // Encuadre pedido hace más de SegundosEncuadre o después de un gesto manual sobre el mapa: ya no se aplica
    private const float SegundosEncuadre = 3f;
    private float encuadreDesde;

    private void PedirEncuadre()
    {
        encuadrarPendiente = true;
        encuadreDesde = Time.unscaledTime;
    }

    private bool EncuadreCaducado(CamaraMapa cm) =>
        Time.unscaledTime - encuadreDesde > SegundosEncuadre || cm.UltimaInteraccion > encuadreDesde;

    // Cámara del mapa (se busca una vez; si se destruye o no hay, se vuelve a buscar como máximo una vez por
    // segundo: LateUpdate la pide en cada frame con el pin visible)
    private CamaraMapa camaraMapa;
    private float proximaBusquedaCamaraMapa;
    private CamaraMapa Camara()
    {
        if (camaraMapa == null && Time.unscaledTime >= proximaBusquedaCamaraMapa)
        {
            proximaBusquedaCamaraMapa = Time.unscaledTime + 1f;
            camaraMapa = FindFirstObjectByType<CamaraMapa>();
        }
        return camaraMapa;
    }

    // Pin del destino sin ruta (MostrarDestino): se pone sobre el lugar, apoyado en el suelo
    private void ProyectarDestinoSinRuta()
    {
        mapaCambiado = false;
        if (pinDestino == null || destinoMostrado == null) return;
        ActualizarAncho();   // escala del pin según el zoom (la línea está apagada)
        pinDestino.position = mapa.GeoToWorldPosition(new Vector2d(destinoMostrado.latitud, destinoMostrado.longitud), false);
        pinDestino.gameObject.SetActive(!pausado);
        CamaraMapa.Despertar();
    }

    /// <summary>
    /// Redondea cada esquina de la ruta con una curva (Bézier cuadrática) que empieza y termina
    /// a 'radio' metros del nodo. Los tramos rectos no se tocan, y el radio nunca pasa del 45%
    /// de los tramos vecinos, así dos curvas seguidas no se cruzan y la línea no se sale del camino.
    /// El primer y el último punto (tu ubicación y el destino) quedan exactamente igual.
    /// </summary>
    private List<Vector2d> Suavizar(List<Vector2d> pts)
    {
        if (pts.Count < 3) return pts;

        List<Vector2d> salida = trazoSuavizado;   // se reutiliza entre recálculos
        salida.Clear();
        salida.Add(pts[0]);
        double cosMinimo = System.Math.Cos(anguloMinimoGrados * System.Math.PI / 180.0);

        for (int i = 1; i < pts.Count - 1; i++)
        {
            Vector2d anterior = pts[i - 1], esquina = pts[i], siguiente = pts[i + 1];
            double d1 = RedCaminos.Metros(anterior, esquina);
            double d2 = RedCaminos.Metros(esquina, siguiente);
            double r = System.Math.Min(radioCurvaM, 0.45 * System.Math.Min(d1, d2));

            // Giro casi recto o tramos muy cortos: se deja la esquina tal cual
            if (r < 0.5 || CosenoGiro(anterior, esquina, siguiente) > cosMinimo)
            {
                salida.Add(esquina);
                continue;
            }

            Vector2d a = Interpolar(esquina, anterior, r / d1);    // donde empieza la curva
            Vector2d b = Interpolar(esquina, siguiente, r / d2);   // donde termina

            for (int k = 0; k <= puntosPorCurva; k++)
            {
                double t = (double)k / puntosPorCurva;
                double u = 1.0 - t;
                // Bézier cuadrática con la esquina como punto de control
                salida.Add(new Vector2d(
                    u * u * a.x + 2 * u * t * esquina.x + t * t * b.x,
                    u * u * a.y + 2 * u * t * esquina.y + t * t * b.y));
            }
        }

        salida.Add(pts[pts.Count - 1]);
        return salida;
    }

    private static Vector2d Interpolar(Vector2d desde, Vector2d hacia, double t) =>
        new Vector2d(desde.x + (hacia.x - desde.x) * t, desde.y + (hacia.y - desde.y) * t);

    // Coseno del ángulo entre la dirección de llegada y la de salida (1 = sigue recto)
    private static double CosenoGiro(Vector2d a, Vector2d b, Vector2d c)
    {
        double k = System.Math.Cos(b.x * System.Math.PI / 180.0);   // corrige la longitud según la latitud
        double x1 = (b.y - a.y) * k, y1 = b.x - a.x;
        double x2 = (c.y - b.y) * k, y2 = c.x - b.x;
        double n = System.Math.Sqrt((x1 * x1 + y1 * y1) * (x2 * x2 + y2 * y2));
        return n < 1e-18 ? 1.0 : (x1 * x2 + y1 * y2) / n;
    }
}
