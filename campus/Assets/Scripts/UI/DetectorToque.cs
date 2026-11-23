using UnityEngine;
using UnityEngine.InputSystem;
using Toque = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Detecta un "toque corto sin arrastre" sobre el mapa (dedo o mouse): empieza y termina fuera de la interfaz,
/// se mueve menos que EntradaMapa.ToleranciaToque y dura menos de TiempoMaximo. Dos dedos (pellizco) lo anulan.
/// Lo usan el editor del mapa y el formulario de lugar. Quien lo use debe tener EnhancedTouchSupport activo.
/// No asigna memoria por frame.
/// </summary>
public sealed class DetectorToque
{
    /// <summary>Duración máxima (s) para contar como toque y no como pulsación larga.</summary>
    public float TiempoMaximo = 0.6f;

    private Vector2 inicio;
    private float tiempoInicio;
    private bool enCurso, empezoSobreUI;

    /// <summary>Olvida el toque en curso (al abrir un modal, al arrastrar el pin, al desactivar la pantalla...).</summary>
    public void Reiniciar() => enCurso = false;

    /// <summary>Llamar una vez por frame. Devuelve true el frame en que termina un toque corto; "posicion" es dónde.</summary>
    public bool Actualizar(out Vector2 posicion)
    {
        posicion = default;
        var toques = Toque.activeTouches;
        if (toques.Count > 0)
        {
            if (toques.Count > 1) { enCurso = false; return false; }   // pellizco: no es un toque
            Toque t = toques[0];
            switch (t.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began: Empezar(t.screenPosition); break;
                case UnityEngine.InputSystem.TouchPhase.Ended: return Terminar(t.screenPosition, out posicion);
                case UnityEngine.InputSystem.TouchPhase.Canceled: enCurso = false; break;
            }
            return false;
        }

        Mouse m = Mouse.current;
        if (m == null) return false;
        if (m.leftButton.wasPressedThisFrame) Empezar(m.position.ReadValue());
        else if (m.leftButton.wasReleasedThisFrame) return Terminar(m.position.ReadValue(), out posicion);
        return false;
    }

    private void Empezar(Vector2 p)
    {
        enCurso = true;
        inicio = p;
        tiempoInicio = Time.unscaledTime;
        empezoSobreUI = EntradaMapa.SobreUI(p);
    }

    private bool Terminar(Vector2 p, out Vector2 posicion)
    {
        posicion = p;
        if (!enCurso) return false;
        enCurso = false;
        float tol = EntradaMapa.ToleranciaToque;
        return !empezoSobreUI
               && (p - inicio).sqrMagnitude < tol * tol
               && Time.unscaledTime - tiempoInicio < TiempoMaximo
               && !EntradaMapa.SobreUI(p);
    }
}
