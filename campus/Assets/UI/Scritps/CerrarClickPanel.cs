using UnityEngine;

public class CerrarPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelACerrar;

    public void Cerrar()
    {
        panelACerrar.SetActive(false);
    }
}