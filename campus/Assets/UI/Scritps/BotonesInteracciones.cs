using System.Collections;
using UnityEngine;

public class UIButtonAction : MonoBehaviour
{
    [Header("Cambio de Panel")]
    [SerializeField] private bool cambiarPanel = true;
    [SerializeField] private GameObject panelActual;
    [SerializeField] private GameObject panelDestino;

    [Header("Padre del Panel Destino")]
    [SerializeField] private bool activarPadreDestino;
    [SerializeField] private GameObject padrePanelDestino;

    [Header("Mensaje Emergente")]
    [SerializeField] private bool mostrarPopup;
    [SerializeField] private GameObject popup;

    [SerializeField] private bool cerrarPopupAutomaticamente = true;
    [SerializeField] private float tiempoPopup = 2f;

    [Header("Panel de Pausa")]
    [SerializeField] private bool usarPanelPausa = false;
    [SerializeField] private GameObject panelPausa;

    [Header("Cerrar Pausa al Pulsar Fuera")]
    [SerializeField] private GameObject zonaCerrarPausa;

    public void Ejecutar()
    {
        StartCoroutine(EjecutarProceso());
    }

    private IEnumerator EjecutarProceso()
    {
        // Abrir panel pausa
        if (usarPanelPausa && panelPausa != null)
        {
            panelPausa.SetActive(true);

            if (zonaCerrarPausa != null)
                zonaCerrarPausa.SetActive(true);

            yield break;
        }

        // Mostrar popup
        if (mostrarPopup && popup != null)
        {
            popup.SetActive(true);

            if (cerrarPopupAutomaticamente)
            {
                yield return new WaitForSeconds(tiempoPopup);
                popup.SetActive(false);
            }
        }

        // Cambio de panel
        if (cambiarPanel)
        {
            if (panelActual != null)
                panelActual.SetActive(false);

            if (panelPausa != null)
                panelPausa.SetActive(false);

            if (zonaCerrarPausa != null)
                zonaCerrarPausa.SetActive(false);

            if (activarPadreDestino && padrePanelDestino != null)
                padrePanelDestino.SetActive(true);

            if (panelDestino != null)
                panelDestino.SetActive(true);
        }
    }

    public void CerrarPopup()
    {
        if (popup != null)
            popup.SetActive(false);
    }

    public void ContinuarDesdePausa()
    {
        CerrarPanelPausa();
    }

    public void AbrirPanelDesdePausa(GameObject siguientePanel)
    {
        CerrarPanelPausa();

        if (siguientePanel != null)
            siguientePanel.SetActive(true);
    }

    public void ConfirmarDesdePausa()
    {
        CerrarPanelPausa();
        StartCoroutine(FlujoConfirmado());
    }

    private IEnumerator FlujoConfirmado()
    {
        if (mostrarPopup && popup != null)
        {
            popup.SetActive(true);

            if (cerrarPopupAutomaticamente)
            {
                yield return new WaitForSeconds(tiempoPopup);
                popup.SetActive(false);
            }
        }

        if (cambiarPanel)
        {
            if (panelActual != null)
                panelActual.SetActive(false);

            if (panelPausa != null)
                panelPausa.SetActive(false);

            if (zonaCerrarPausa != null)
                zonaCerrarPausa.SetActive(false);

            if (activarPadreDestino && padrePanelDestino != null)
                padrePanelDestino.SetActive(true);

            if (panelDestino != null)
                panelDestino.SetActive(true);
        }
    }

    public void CerrarPanelPausa()
    {
        if (zonaCerrarPausa != null)
            zonaCerrarPausa.SetActive(false);

        if (panelPausa != null)
            panelPausa.SetActive(false);
    }

    private void LateUpdate()
    {
        if (panelPausa == null || zonaCerrarPausa == null)
            return;

        bool pausaAbierta = panelPausa.activeSelf;
        bool zonaAbierta = zonaCerrarPausa.activeSelf;

        if (pausaAbierta != zonaAbierta)
        {
            zonaCerrarPausa.SetActive(pausaAbierta);
        }
    }
}