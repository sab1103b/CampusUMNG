using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Cámara cenital para ver el mapa de Mapbox por debajo del Canvas.
/// Sigue al marcador, y permite arrastrar (mover) y pellizcar (zoom).
/// En el editor: clic y arrastrar para mover, rueda del ratón para zoom.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CamaraMapa : MonoBehaviour
{
    [Header("Seguimiento")]
    [SerializeField] private Transform objetivo;          // el marcador "Tú estás aquí"
    [SerializeField] private bool seguirObjetivo = true;
    [SerializeField] private float suavizado = 5f;

    [Header("Zoom (altura de la cámara sobre el mapa)")]
    [SerializeField] private float altura = 120f;
    [SerializeField] private float alturaMin = 30f;
    [SerializeField] private float alturaMax = 400f;

    private Camera cam;
    private Vector3 foco;
    private Vector2 mouseAnterior;
    private bool arrastrando;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // mirando hacia abajo
        foco = new Vector3(transform.position.x, 0f, transform.position.z);
    }

    private void LateUpdate()
    {
        ManejarEntrada();

        if (seguirObjetivo && objetivo != null)
        {
            float t = Time.deltaTime * suavizado;
            foco.x = Mathf.Lerp(foco.x, objetivo.position.x, t);
            foco.z = Mathf.Lerp(foco.z, objetivo.position.z, t);
        }

        transform.position = new Vector3(foco.x, altura, foco.z);
    }

    /// <summary>Conecta esto al botón "Mi ubicación" para volver a centrar el mapa.</summary>
    public void CentrarEnUsuario() => seguirObjetivo = true;

    private void ManejarEntrada()
    {
        // Zoom con la rueda (editor)
        float rueda = Input.mouseScrollDelta.y;
        if (Mathf.Abs(rueda) > 0.01f)
            altura = Mathf.Clamp(altura - rueda * altura * 0.1f, alturaMin, alturaMax);

        if (Input.touchCount >= 2)
        {
            // Pellizco: zoom
            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            float antes = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
            float ahora = (a.position - b.position).magnitude;
            altura = Mathf.Clamp(altura + (antes - ahora) * altura * 0.003f, alturaMin, alturaMax);
        }
        else if (Input.touchCount == 1)
        {
            // Un dedo: mover el mapa (si no empezó sobre un botón o panel)
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved && !SobreUI(t.fingerId))
                Mover(t.deltaPosition);
        }
        else
        {
            // Ratón (editor)
            if (Input.GetMouseButtonDown(0))
            {
                arrastrando = !SobreUI(-1);
                mouseAnterior = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0) && arrastrando)
            {
                Vector2 p = Input.mousePosition;
                Mover(p - mouseAnterior);
                mouseAnterior = p;
            }
        }
    }

    private void Mover(Vector2 deltaPixeles)
    {
        seguirObjetivo = false;   // al mover a mano se deja de seguir al marcador
        float unidadesPorPixel = 2f * altura * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
        foco.x -= deltaPixeles.x * unidadesPorPixel;
        foco.z -= deltaPixeles.y * unidadesPorPixel;
    }

    private static bool SobreUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return fingerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(fingerId)
            : EventSystem.current.IsPointerOverGameObject();
    }
}
