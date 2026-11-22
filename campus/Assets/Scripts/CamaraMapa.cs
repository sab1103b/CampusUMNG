using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
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

    private Camera cam;
    private Vector3 foco;                 // punto del suelo que mira la cámara
    private float alturaObjetivo;
    private bool siguiendo;

    // Arrastre con un dedo o con el mouse
    private bool arrastrando;
    private Vector2 posAnterior;

    // Pellizco con dos dedos
    private bool pellizcando;
    private float distanciaAnterior;
    private Vector2 centroAnterior;

    private readonly List<RaycastResult> impactosUI = new List<RaycastResult>();
    private PointerEventData datosPuntero;
    private EventSystem sistemaPuntero;

    // Ángulos actuales (se acercan suavemente a los del modo elegido)
    private bool es3D;
    private float inclinacion = 90f;
    private float giro;

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
        alturaObjetivo = altura;
        siguiendo = seguirAlIniciar;
        if (mapa != null) mapa.OnInitialized += () => mapaListo = true;
    }

    private bool mapaListo;

    private void OnEnable() => EnhancedTouchSupport.Enable();
    private void OnDisable() => EnhancedTouchSupport.Disable();

    private void LateUpdate()
    {
        if (Toque.activeTouches.Count > 0) ManejarToques();
        else ManejarMouse();

        if (siguiendo && objetivo != null)
        {
            float t = 1f - Mathf.Exp(-suavizado * Time.unscaledDeltaTime);
            foco.x = Mathf.Lerp(foco.x, objetivo.position.x, t);
            foco.z = Mathf.Lerp(foco.z, objetivo.position.z, t);
        }

        // Zoom suave hacia la altura pedida
        altura = Mathf.Lerp(altura, alturaObjetivo, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

        LimitarFoco();

        // Transición suave entre la vista de arriba (2D) y la isométrica (3D)
        float k = 1f - Mathf.Exp(-velocidadTransicion * Time.unscaledDeltaTime);
        inclinacion = Mathf.Lerp(inclinacion, es3D ? inclinacion3D : 90f, k);
        giro = Mathf.LerpAngle(giro, es3D ? giro3D : 0f, k);

        // La cámara orbita alrededor del foco a 'altura' unidades de distancia.
        // En 2D (inclinación 90) queda justo encima, igual que antes.
        Quaternion rot = Quaternion.Euler(inclinacion, giro, 0f);
        transform.rotation = rot;
        transform.position = foco - rot * Vector3.forward * altura;
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
            mapa = mapaCampus;
            mapa.OnInitialized += () => mapaListo = true;
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
    public void CentrarEnUsuario() => siguiendo = true;

    /// <summary>Deja la cámara donde está (por ejemplo, al editar el mapa).</summary>
    public void DejarDeSeguir() => siguiendo = false;

    /// <summary>Lleva la cámara a una posición del mundo (por ejemplo, un lugar elegido).</summary>
    public void IrA(Vector3 posicionMundo)
    {
        siguiendo = false;
        foco = new Vector3(posicionMundo.x, 0f, posicionMundo.z);
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
                alturaObjetivo = altura = Mathf.Clamp(altura * (distanciaAnterior / distancia), alturaMin, alturaMax);

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
    private void LimitarFoco()
    {
        if (mapa == null || !mapaListo || radioMaximoM <= 0f) return;

        Vector3 centro = mapa.transform.position;
        float radio = radioMaximoM * UnidadesPorMetro();
        Vector2 d = new Vector2(foco.x - centro.x, foco.z - centro.z);
        if (d.magnitude > radio)
        {
            d = d.normalized * radio;
            foco.x = centro.x + d.x;
            foco.z = centro.z + d.y;
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

    // ¿Esa posición de pantalla está sobre algún elemento de la interfaz que recibe toques?
    private bool SobreUI(Vector2 posicionPantalla)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        if (datosPuntero == null || sistemaPuntero != es)
        {
            datosPuntero = new PointerEventData(es);
            sistemaPuntero = es;
        }
        datosPuntero.position = posicionPantalla;
        impactosUI.Clear();
        es.RaycastAll(datosPuntero, impactosUI);
        return impactosUI.Count > 0;
    }
}
