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
            GeoMapa.PonerColor(materialContorno, Color.white);
            contorno.sharedMaterial = materialContorno;
            if (linea.sharedMaterial != null) linea.sharedMaterial.renderQueue = materialContorno.renderQueue + 1;
        }
        contorno.startColor = contorno.endColor = Color.white;
        contorno.positionCount = 0;
        contorno.enabled = false;
        zoomAnchoAplicado = float.NaN;
        anchoAplicado = -1f;
    }

    // Ancho = max(anchoRutaM metros reales, anchoMinimoPx píxeles en pantalla).
    // La escala del mapa solo se vuelve a medir cuando cambia el zoom de Mapbox; el mínimo en
    // píxeles depende de la cámara (que se acerca y aleja), así que se revisa cada frame con ruta.
    private void ActualizarAncho()
    {
        float zoom = mapa.Zoom;
        if (zoom != zoomAnchoAplicado)
        {
            zoomAnchoAplicado = zoom;
            unidadesPorMetroRuta = GeoMapa.UnidadesPorMetro(mapa);
        }

        float porPixel = UnidadesPorPixel();
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

    /// <summary>
    /// Marcador de destino por código (la escena no trae pinDestino): disco blanco en el suelo con un
    /// disco UIRapida.DestinoColor encima, un poste y una cabeza esférica DestinoColor con borde blanco
    /// (el destino es un lugar del campus: Verde; la línea de la ruta sigue en RutaColor). Visto desde
    /// arriba (2D) es un punto verde con borde blanco; en 3D, un alfiler. Sin colisionadores (no tapa toques del mapa).
    /// Medidas en "unidades de marcador": la escala real la pone ActualizarEscalaPin. Lo ocultan
    /// Pausar y LimpiarRuta como a un pin de la escena.
    /// </summary>
    private void CrearPinDestino()
    {
        GameObject raiz = new GameObject("PinDestino");
        pinDestino = raiz.transform;
        pinCreado = true;
        escalaPinAplicada = -1f;

        // Destino en DestinoColor con borde blanco (la línea y su contorno siguen en RutaColor)
        Material color = GeoMapa.Material(UIRapida.DestinoColor);
        Material blanco = GeoMapa.Material(Color.white);
        ParteDelPin(raiz.transform, PrimitiveType.Cylinder, blanco, new Vector3(0f, 0.01f, 0f), new Vector3(0.9f, 0.01f, 0.9f));
        ParteDelPin(raiz.transform, PrimitiveType.Cylinder, color, new Vector3(0f, 0.025f, 0f), new Vector3(0.65f, 0.01f, 0.65f));
        ParteDelPin(raiz.transform, PrimitiveType.Cylinder, color, new Vector3(0f, 0.5f, 0f), new Vector3(0.1f, 0.5f, 0.1f));
        ParteDelPin(raiz.transform, PrimitiveType.Sphere, blanco, new Vector3(0f, 1.15f, 0f), new Vector3(0.62f, 0.62f, 0.62f));
        ParteDelPin(raiz.transform, PrimitiveType.Sphere, color, new Vector3(0f, 1.17f, 0f), new Vector3(0.5f, 0.5f, 0.5f));
    }

    private static void ParteDelPin(Transform padre, PrimitiveType tipo, Material material, Vector3 pos, Vector3 escala)
    {
        GameObject go = GameObject.CreatePrimitive(tipo);
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(padre, false);
        go.transform.localPosition = pos;
        go.transform.localScale = escala;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
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

    // Cámara del mapa (se busca una vez; si se destruye, se vuelve a buscar)
    private CamaraMapa camaraMapa;
    private CamaraMapa Camara()
    {
        if (camaraMapa == null) camaraMapa = FindFirstObjectByType<CamaraMapa>();
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
