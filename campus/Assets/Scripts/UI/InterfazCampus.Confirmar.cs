using System;
using UnityEngine;

/// <summary>
/// InterfazCampus: modal de confirmación para acciones irreversibles (eliminar lugar o espacio interior).
/// Usa el modal único de la app (UIRapida.ModalConfirmar): velo oscuro + tarjeta con título, explicación y
/// "Cancelar" (Menta) / "Eliminar" (Rojo). Tocar el velo o el botón Atrás de Android = Cancelar (AtrasEnCapas).
/// El mismo modal en modo "solo informar" (un único botón Verde "Entendido") muestra los términos de uso.
/// </summary>
public partial class InterfazCampus
{
    private UIRapida.ModalConfirmacion modalConfirmar;

    private bool ConfirmacionAbierta => modalConfirmar != null && modalConfirmar.Abierto;

    private void PedirConfirmacion(string titulo, string texto, Action confirmar)
    {
        // Sin canvas no hay modal (no debería pasar): no se ejecuta una acción irreversible sin confirmar
        if (!ConstruirConfirmacion(btnSalirAdmin))
        {
            Debug.LogWarning("Confirmación sin modal: se cancela la acción «" + titulo + "».");
            return;
        }
        modalConfirmar.Configurar("Cancelar", "Eliminar", destructivo: true, soloInformar: false);
        Abrir(titulo, texto, () => confirmar?.Invoke());
    }

    /// <summary>Mismo modal con un solo botón "Entendido" (texto de solo lectura, p. ej. los términos de uso).</summary>
    private void MostrarInformacion(string titulo, string texto, Component ancla)
    {
        if (!ConstruirConfirmacion(ancla)) return;
        modalConfirmar.Configurar(null, "Entendido", destructivo: false, soloInformar: true);
        Abrir(titulo, texto, () => { });
    }

    private void Abrir(string titulo, string texto, UnityEngine.Events.UnityAction alConfirmar)
    {
        // alCancelar vacío: Mostrar conserva el callback anterior si recibe null
        modalConfirmar.Mostrar(titulo, texto, alConfirmar, () => { });
    }

    /// <summary>Cierra el modal sin ejecutar la acción (Cancelar, Atrás, cambio de pantalla).</summary>
    private void CerrarConfirmacion(bool animar = true)
    {
        if (modalConfirmar != null && modalConfirmar.Visible) modalConfirmar.Ocultar(animar);
    }

    // Crea el modal la primera vez y lo lleva al canvas de la pantalla que lo pide (administración o registro)
    private bool ConstruirConfirmacion(Component ancla)
    {
        if (ancla == null)
            ancla = btnSalirAdmin != null ? (Component)btnSalirAdmin
                  : contenedorLugares != null ? (Component)contenedorLugares : regTerminos;
        Canvas canvas = ancla != null ? ancla.GetComponentInParent<Canvas>(true) : null;
        if (canvas == null) return modalConfirmar != null;
        Transform raiz = canvas.rootCanvas.transform;

        if (modalConfirmar == null)
        {
            modalConfirmar = UIRapida.ModalConfirmar(raiz, "", "", "Cancelar", "Eliminar", true, null, null,
                                                     UIRapida.FuenteTexto, "ConfirmarEliminar");
            UIRapida.AplicarFuentes(modalConfirmar.transform);
        }
        else if (modalConfirmar.transform.parent != raiz)
            modalConfirmar.transform.SetParent(raiz, false);   // el velo es FueraDeZona: hijo directo de la raíz
        return true;
    }
}
