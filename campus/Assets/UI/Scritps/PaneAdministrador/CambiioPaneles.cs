using UnityEngine;

public class PanelSwitcher : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] private GameObject panelLugares;
    [SerializeField] private GameObject panelConfiguracion;

    [Header("Barras")]
    [SerializeField] private GameObject barraLugares;
    [SerializeField] private GameObject barraConfiguracion;

    private void Start()
    {
        MostrarLugares();
    }

    public void MostrarLugares()
    {
        panelLugares.SetActive(true);
        barraLugares.SetActive(true);

        panelConfiguracion.SetActive(false);
        barraConfiguracion.SetActive(false);
    }

    public void MostrarConfiguracion()
    {
        panelConfiguracion.SetActive(true);
        barraConfiguracion.SetActive(true);

        panelLugares.SetActive(false);
        barraLugares.SetActive(false);
    }
}
