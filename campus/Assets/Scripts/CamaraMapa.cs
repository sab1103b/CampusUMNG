using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Rendering;
using Toque = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Cámara cenital del mapa: se mueve y hace zoom con el dedo, con el mouse y con botones.
/// El mapa de Mapbox no se mueve: se mueve la cámara, así la ruta, la red y el marcador
/// siempre quedan en su lugar.
///
/// Celular: un dedo arrastra el mapa; dos dedos hacen zoom (pellizco) y también lo mueven.
/// PC / Editor: clic izquierdo y arrastrar para mover; rueda del mouse para zoom.
/// Botones (OnClick): ZoomMas(), ZoomMenos(), CentrarEnUsuario().
///
/// Los toques que empiezan sobre la interfaz (botones, buscador, paneles) no mueven el mapa.
/// Ponlo en la cámara del mapa (Camera_Mapa).
///
/// Modo de ahorro de batería: tras ~3 s sin toques, sin teclado en pantalla, sin animación de la
/// cámara y sin cambios visibles de la ruta, la app dibuja 1 de cada 2 frames (OnDemandRendering.renderFrameInterval = 2,
/// unos 30 fps); la lógica (Update) sigue a la frecuencia normal. Cualquier toque, clic o movimiento de
/// cámara vuelve a 60 fps al instante. La interfaz no tiene que hacer nada: tocarla ya cuenta.
/// API para quien anime algo SIN que el usuario toque (p. ej. un aviso que aparece solo o una
/// animación disparada por datos): llamar <see cref="Despertar"/> (estático, barato, se puede llamar
/// cada frame mientras dure la animación). ComoLlegarController lo llama solo cuando la ruta cambia de
/// forma visible (recálculo, reproyección o el inicio de la línea siguiendo al marcador).
/// Animaciones decorativas (p. ej. el pulso del halo de ubicación) NO deben despertar: consultan
/// <see cref="Ahorrando"/> y solo se animan cuando ya se dibuja a 60 fps por otro motivo.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CamaraMapa : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El marcador 'Tú estás aquí'")]
    [SerializeField] private Transform objetivo;
    [Tooltip("Para limitar hasta dónde se puede mover la cámara")]
    [SerializeField] private AbstractMap mapa;

    [Header("Seguimiento")]
    [SerializeField] private bool seguirAlIniciar = true;
    [SerializeField] private float suavizado = 6f;

    [Header("Zoom (altura de la cámara sobre el mapa)")]
    [SerializeField] private float altura = 120f;
    [SerializeField] private float alturaMin = 30f;
    [SerializeField] private float alturaMax = 400f;
    [Tooltip("Cuánto acerca/aleja cada toque de los botones + y − (0.3 = 30%)")]
    [SerializeField, Range(0.1f, 0.6f)] private float pasoBotones = 0.3f;
    [Tooltip("Sensibilidad de la rueda del mouse")]
    [SerializeField] private float sensibilidadRueda = 0.15f;

    [Header("Perspectiva 2D / 3D isométrica")]
    [SerializeField] private bool iniciarEn3D = false;
    [Tooltip("Inclinación de la cámara en 3D (90 = vista desde arriba, menos = más inclinada)")]
    [SerializeField, Range(30f, 80f)] private float inclinacion3D = 50f;
    [Tooltip("Giro de la cámara en 3D: 45 da la vista isométrica clásica en diagonal; 0 deja el norte arriba")]
    [SerializeField, Range(-180f, 180f)] private float giro3D = 45f;
    [Tooltip("Rapidez del cambio entre 2D y 3D")]
    [SerializeField] private float velocidadTransicion = 5f;

    [Header("Límite de movimiento")]
    [Tooltip("Distancia máxima (metros) desde el centro del mapa a la que puede ir la cámara")]
    [SerializeField] private float radioMaximoM = 900f;

    [Header("Ahorro de batería")]
    [Tooltip("Segundos sin actividad para dibujar a la mitad de frames (0 = nunca ahorrar)")]
    [SerializeField] private float segundosParaAhorro = 3f;
    [Tooltip("En ahorro se dibuja 1 de cada N frames (2 = 30 fps con targetFrameRate 60)")]
    [SerializeField, Range(2, 4)] private int intervaloAhorro = 2;

    // Último instante con actividad (compartido: Despertar() es estático, no necesita la instancia)
    private static float ultimaActividad;

    /// <summary>
    /// Avisa que hay algo animándose en pantalla: sale del modo de ahorro (vuelve a dibujar todos los
    /// frames) y lo mantiene así ~3 s más. Llamarlo cada frame mientras dure una animación sin toques.
    /// </summary>
    public static void Despertar() => ultimaActividad = Time.unscaledTime;

    /// <summary>True mientras está el modo de ahorro (se dibuja 1 de cada N frames). No despierta nada.</summary>
    public static bool Ahorrando { get; private set; }

    // Lo último que se escribió en el transform (solo se vuelve a escribir si cambió)
    private Vector3 posEscrita;
    private Quaternion rotEscrita;
    private bool transformEscrito;

    private Camera cam;
    private Vector3 foco;                 // punto del suelo que mira la cámara
    private float alturaObjetivo;
    private float alturaInicial = 120f;
    private bool siguiendo;

    // Arrastre con un dedo o con el mouse
    private bool arrastrando;
    private Vector2 posAnterior;

    // Pellizco con dos dedos
    private bool pellizcando;
    private float distanciaAnterior;
    private Vector2 centroAnterior;


    // Ángulos actuales (se acercan suavemente a los del modo elegido)
    private bool es3D;
    private float inclinacion = 90f;
    private float giro;

    // Viaje suave del foco (IrA / Encuadrar): se interpola en LateUpdate como la altura; un arrastre lo cancela
    private bool viajando;
    private Vector3 focoObjetivo;
    private const float VelocidadViaje = 8f;

    /// <summary>
    /// Time.unscaledTime del último gesto manual sobre el mapa (arrastre, pellizco o rueda); -1 si nunca hubo.
    /// ComoLlegarController lo usa para no encuadrar la ruta si el usuario ya movió el mapa.
    /// </summary>
    public float UltimaInteraccion { get; private set; } = -1f;

    /// <summary>True mientras la cámara sigue al marcador (se apaga al mover el mapa a mano).</summary>
    public bool Siguiendo => siguiendo;

    /// <summary>True si la cámara está (o va hacia) la vista 3D isométrica.</summary>
    public bool Es3D => es3D;

    /// <summary>Se dispara al cambiar entre 2D y 3D (true = 3D). Útil para cambiar el texto del botón.</summary>
    public event System.Action<bool> PerspectivaCambiada;

    // ==================================================================
    // Ciclo de vida
    // ==================================================================
    private void Awake()
    {
        cam = GetComponent<Camera>();
        es3D = iniciarEn3D;
        inclinacion = es3D ? inclinacion3D : 90f;
        giro = es3D ? giro3D : 0f;
        transform.rotation = Quaternion.Euler(inclinacion, giro, 0f);
        foco = new Vector3(transform.position.x, 0f, transform.position.z);
        altura = Mathf.Clamp(altura, alturaMin, alturaMax);
        alturaObjetivo = alturaInicial = altura;
        siguiendo = seguirAlIniciar;
        if (mapa != null) mapa.OnInitialized += AlIniciarMapa;
    }

    private bool mapaListo;

    private void AlIniciarMapa() => mapaListo = true;

    private void OnDestroy()
    {
        if (mapa != null) mapa.OnInitialized -= AlIniciarMapa;
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Despertar();
        transformEscrito = false;   // al volver se escribe la posición una vez
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        OnDemandRendering.renderFrameInterval = 1;   // sin esta cámara no queda nadie que salga del ahorro
        Ahorrando = false;
    }

    private void LateUpdate()
    {
        if (Toque.activeTouches.Count > 0) { ManejarToques(); Despertar(); }
        else ManejarMouse();
        if (HayEntradaSinToque()) Despertar();

        if (viajando)
        {
            float tv = 1f - Mathf.Exp(-VelocidadViaje * Time.unscaledDeltaTime);
            foco = Vector3.Lerp(foco, focoObjetivo, tv);
            // Llegó (a menos de ~1 % de la altura): se fija exacto y termina el viaje
            float cerca = altura * 0.01f;
            if ((focoObjetivo - foco).sqrMagnitude <= cerca * cerca) { foco = focoObjetivo; viajando = false; }
            Despertar();   // mientras interpola se dibuja a 60 fps
        }
        else if (siguiendo && objetivo != null)
        {
            float t = 1f - Mathf.Exp(-suavizado * Time.unscaledDeltaTime);
            foco.x = Mathf.Lerp(foco.x, objetivo.position.x, t);
            foco.z = Mathf.Lerp(foco.z, objetivo.position.z, t);
        }

        // Zoom suave hacia la altura pedida
        altura = Mathf.Lerp(altura, alturaObjetivo, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

        LimitarFoco(ref foco);

        // Transición suave entre la vista de arriba (2D) y la isométrica (3D)
        float k = 1f - Mathf.Exp(-velocidadTransicion * Time.unscaledDeltaTime);
        inclinacion = Mathf.Lerp(inclinacion, es3D ? inclinacion3D : 90f, k);
        giro = Mathf.LerpAngle(giro, es3D ? giro3D : 0f, k);

        // La cámara orbita alrededor del foco a 'altura' unidades de distancia.
        // En 2D (inclinación 90) queda justo encima, igual que antes.
        Quaternion rot = Quaternion.Euler(inclinacion, giro, 0f);
        Vector3 pos = foco - rot * Vector3.forward * altura;

        // Solo se escribe el transform si cambió de forma visible (los Lerp se acercan sin llegar nunca):
        // así una cámara quieta no marca el transform como modificado en cada frame.
        if (!transformEscrito || (pos - posEscrita).sqrMagnitude > 1e-6f || Quaternion.Angle(rot, rotEscrita) > 0.01f)
        {
            transform.SetPositionAndRotation(pos, rot);
            posEscrita = pos;
            rotEscrita = rot;
            transformEscrito = true;
            Despertar();   // la cámara se está animando
        }

        ActualizarAhorro();
    }

    // 60 fps con actividad; tras 'segundosParaAhorro' sin actividad se dibuja 1 de cada 'intervaloAhorro' frames
    private void ActualizarAhorro()
    {
        bool ahorrar = segundosParaAhorro > 0f && Time.unscaledTime - ultimaActividad > segundosParaAhorro;
        // Escribiendo en el buscador (teclado en pantalla abierto) no se ahorra: el cursor y el texto deben ir fluidos
        if (ahorrar && TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible) { Despertar(); ahorrar = false; }
        Ahorrando = ahorrar;
        int intervalo = ahorrar ? intervaloAhorro : 1;
        if (OnDemandRendering.renderFrameInterval != intervalo) OnDemandRendering.renderFrameInterval = intervalo;
    }

    // Mouse (clic, rueda o movimiento: hover de botones en PC) o teclado físico
    private static bool HayEntradaSinToque()
    {
        Mouse m = Mouse.current;
        if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed ||
                          m.scroll.ReadValue().sqrMagnitude > 0f || m.delta.ReadValue().sqrMagnitude > 0f))
            return true;
        Keyboard k = Keyboard.current;
        return k != null && k.anyKey.isPressed;
    }

    // ==================================================================
    // Perspectiva (conéctalos al OnClick)
    // ==================================================================

    /// <summary>Cambia entre la vista de arriba (2D) y la isométrica (3D).</summary>
    public void AlternarPerspectiva() => PonerPerspectiva(!es3D);

    public void Ver2D() => PonerPerspectiva(false);
    public void Ver3D() => PonerPerspectiva(true);

    private void PonerPerspectiva(bool tresD)
    {
        if (es3D == tresD) return;
        es3D = tresD;
        PerspectivaCambiada?.Invoke(es3D);
    }

    /// <summary>Configuración por código (cuando el componente se agrega al iniciar la app).</summary>
    public void Configurar(Transform marcador, AbstractMap mapaCampus, bool mapaYaIniciado)
    {
        objetivo = marcador;
        if (mapaCampus != null && mapaCampus != mapa)
        {
            if (mapa != null) mapa.OnInitialized -= AlIniciarMapa;
            mapa = mapaCampus;
            mapa.OnInitialized += AlIniciarMapa;
        }
        mapaListo |= mapaYaIniciado;
        unidadesPorMetroCache = -1f;
    }

    // ==================================================================
    // Botones (conéctalos al OnClick)
    // ==================================================================
    public void ZoomMas() => alturaObjetivo = Mathf.Clamp(alturaObjetivo * (1f - pasoBotones), alturaMin, alturaMax);
    public void ZoomMenos() => alturaObjetivo = Mathf.Clamp(alturaObjetivo / (1f - pasoBotones), alturaMin, alturaMax);

    /// <summary>Vuelve a centrar el mapa en "Tú estás aquí" y lo sigue.</summary>
    public void CentrarEnUsuario() { siguiendo = true; viajando = false; }

    /// <summary>Deja la cámara donde está (por ejemplo, al editar el mapa).</summary>
    public void DejarDeSeguir() { siguiendo = false; viajando = false; }

    /// <summary>
    /// Lleva la cámara a una posición del mundo (por ejemplo, un lugar elegido) con un desplazamiento
    /// suave (no salta). Deja de seguir al usuario; arrastrar el mapa cancela el viaje.
    /// </summary>
    public void IrA(Vector3 posicionMundo)
    {
        siguiendo = false;
        focoObjetivo = new Vector3(posicionMundo.x, 0f, posicionMundo.z);
        LimitarFoco(ref focoObjetivo);   // un destino fuera del límite no deja el viaje sin terminar
        viajando = true;
        Despertar();
    }

    /// <summary>Lleva la cámara a una coordenada (lat, lon) y deja de seguir al usuario. Sin mapa no hace nada.</summary>
    public void IrA(Vector2d latLon)
    {
        if (mapa == null) return;
        IrA(mapa.GeoToWorldPosition(latLon, false));
    }

    /// <summary>
    /// Encuadra dos puntos del mundo (p. ej. tu ubicación y el destino de la ruta): centra la cámara entre
    /// ellos y ajusta la altura para que ambos se vean con un margen (1,5 = el tramo ocupa ~2/3 de la
    /// pantalla, deja sitio a la ficha y a la barra). Deja de seguir al usuario. La altura respeta los
    /// límites de zoom; si los puntos están muy juntos, no acerca más que la mitad de la altura inicial.
    /// </summary>
    public void Encuadrar(Vector3 a, Vector3 b, float margen = 1.5f)
    {
        IrA((a + b) * 0.5f);
        if (cam == null || Screen.height <= 0) return;

        // Tamaño del tramo visto desde la cámara (en 3D está girada e inclinada)
        Quaternion soloGiro = Quaternion.Euler(0f, es3D ? giro3D : 0f, 0f);
        Vector3 d = b - a;
        float ancho = Mathf.Abs(Vector3.Dot(d, soloGiro * Vector3.right));
        float largo = Mathf.Abs(Vector3.Dot(d, soloGiro * Vector3.forward)) *
                      Mathf.Sin((es3D ? inclinacion3D : 90f) * Mathf.Deg2Rad);

        float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspecto = Mathf.Max(0.1f, (float)Screen.width / Screen.height);
        float necesaria = Mathf.Max(largo, ancho / aspecto) * margen / (2f * Mathf.Max(0.01f, tan));
        // Tramo muy corto: no se acerca más que la mitad de la altura inicial (se ve el contexto alrededor)
        alturaObjetivo = Mathf.Clamp(Mathf.Max(necesaria, alturaInicial * 0.5f), alturaMin, alturaMax);
    }

    // ==================================================================
    // Entrada táctil
    // ==================================================================
    private void ManejarToques()
    {
        var toques = Toque.activeTouches;

        if (toques.Count >= 2)
        {
            Toque a = toques[0], b = toques[1];
            Vector2 centro = (a.screenPosition + b.screenPosition) * 0.5f;
            float distancia = Vector2.Distance(a.screenPosition, b.screenPosition);

            if (!pellizcando)
            {
                // El pellizco solo empieza si ningún dedo está sobre la interfaz
                pellizcando = !SobreUI(a.screenPosition) && !SobreUI(b.screenPosition);
                distanciaAnterior = distancia;
                centroAnterior = centro;
                arrastrando = false;
                return;
            }

            if (distanciaAnterior > 1f && distancia > 1f)
            {
                alturaObjetivo = altura = Mathf.Clamp(altura * (distanciaAnterior / distancia), alturaMin, alturaMax);
                if (Mathf.Abs(distancia - distanciaAnterior) > 0.5f) UltimaInteraccion = Time.unscaledTime;
            }

            Mover(centro - centroAnterior);   // dos dedos también arrastran
            distanciaAnterior = distancia;
            centroAnterior = centro;
            return;
        }

        Toque t = toques[0];

        // Venía de un pellizco y se levantó un dedo: el que queda sigue arrastrando sin saltos
        if (pellizcando)
        {
            pellizcando = false;
            arrastrando = true;
            posAnterior = t.screenPosition;
            return;
        }

        switch (t.phase)
        {
            case UnityEngine.InputSystem.TouchPhase.Began:
                arrastrando = !SobreUI(t.screenPosition);   // si empieza sobre un botón o panel, no mueve el mapa
                posAnterior = t.screenPosition;
                break;

            case UnityEngine.InputSystem.TouchPhase.Moved:
            case UnityEngine.InputSystem.TouchPhase.Stationary:
                if (!arrastrando) break;
                Mover(t.screenPosition - posAnterior);
                posAnterior = t.screenPosition;
                break;

            default:   // Ended / Canceled
                arrastrando = false;
                break;
        }
    }

    // ==================================================================
    // Mouse (PC / Editor / Simulator sin toques)
    // ==================================================================
    private void ManejarMouse()
    {
        pellizcando = false;
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 pos = mouse.position.ReadValue();

        // Rueda: zoom. El Input System nuevo entrega ±1 por cada "clic" de la rueda;
        // configuraciones antiguas entregan ±120. Se aceptan los dos.
        float rueda = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(rueda) > 0.001f && !SobreUI(pos))
        {
            float pasos = Mathf.Abs(rueda) >= 10f ? rueda / 120f : rueda;
            pasos = Mathf.Clamp(pasos, -5f, 5f);   // un giro muy rápido no salta de golpe al límite
            alturaObjetivo = Mathf.Clamp(alturaObjetivo * Mathf.Pow(1f - sensibilidadRueda, pasos), alturaMin, alturaMax);
            UltimaInteraccion = Time.unscaledTime;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            arrastrando = !SobreUI(pos);
            posAnterior = pos;
        }
        else if (mouse.leftButton.isPressed && arrastrando)
        {
            Mover(pos - posAnterior);
            posAnterior = pos;
        }
        else if (!mouse.leftButton.isPressed)
        {
            arrastrando = false;
        }
    }

    // ==================================================================
    // Utilidades
    // ==================================================================

    // Arrastrar el dedo hacia la derecha mueve el mapa hacia la derecha (la cámara va a la izquierda)
    private void Mover(Vector2 deltaPixeles)
    {
        if (deltaPixeles.sqrMagnitude < 0.01f) return;
        siguiendo = false;   // al mover a mano se deja de seguir al marcador
        viajando = false;    // y se cancela un viaje suave en curso (IrA / Encuadrar)
        UltimaInteraccion = Time.unscaledTime;

        float unidadesPorPixel = 2f * altura * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

        // Se arrastra según hacia dónde mira la cámara (en 3D está girada).
        // Inclinada, cada píxel vertical cubre más suelo: se compensa con el seno de la inclinación.
        Quaternion soloGiro = Quaternion.Euler(0f, giro, 0f);
        Vector3 derecha = soloGiro * Vector3.right;
        Vector3 adelante = soloGiro * Vector3.forward;
        float factorVertical = 1f / Mathf.Max(0.35f, Mathf.Sin(inclinacion * Mathf.Deg2Rad));

        foco -= derecha * (deltaPixeles.x * unidadesPorPixel)
              + adelante * (deltaPixeles.y * unidadesPorPixel * factorVertical);
    }

    // No deja que la cámara se vaya fuera del mapa cargado
    private void LimitarFoco(ref Vector3 punto)
    {
        if (mapa == null || !mapaListo || radioMaximoM <= 0f) return;

        Vector3 centro = mapa.transform.position;
        float radio = radioMaximoM * UnidadesPorMetro();
        Vector2 d = new Vector2(punto.x - centro.x, punto.z - centro.z);
        if (d.magnitude > radio)
        {
            d = d.normalized * radio;
            punto.x = centro.x + d.x;
            punto.z = centro.z + d.y;
        }
    }

    private float unidadesPorMetroCache = -1f;

    private float UnidadesPorMetro()
    {
        if (unidadesPorMetroCache > 0f) return unidadesPorMetroCache;

        Vector2d c = mapa.CenterLatitudeLongitude;
        Vector3 a = mapa.GeoToWorldPosition(c, false);
        Vector3 b = mapa.GeoToWorldPosition(new Vector2d(c.x + 0.001, c.y), false);
        float unidades = Vector3.Distance(a, b);
        if (unidades > 0.0001f) unidadesPorMetroCache = unidades / 111.32f;   // 0.001° de latitud ≈ 111.32 m
        return unidadesPorMetroCache > 0f ? unidadesPorMetroCache : 1f;
    }

    private static bool SobreUI(Vector2 posicionPantalla) => EntradaMapa.SobreUI(posicionPantalla);
}
