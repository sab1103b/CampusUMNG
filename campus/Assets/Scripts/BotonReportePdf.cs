using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Botón "Descargar reporte PDF" del panel de administrador.
/// Ponlo en el botón y conecta su OnClick a BotonReportePdf.Generar().
/// </summary>
public class BotonReportePdf : MonoBehaviour
{
    [SerializeField] private AdminService admin;
    [SerializeField] private Button boton;
    [Tooltip("Texto opcional para mostrar el resultado")]
    [SerializeField] private TMP_Text textoEstado;

    public void Generar()
    {
        if (boton != null) boton.interactable = false;

        admin.GenerarReportePdf((ok, resultado) =>
        {
            if (boton != null) boton.interactable = true;
            if (textoEstado == null) return;

            textoEstado.text = ok
                ? "Reporte guardado:\n" + System.IO.Path.GetFileName(resultado)
                : resultado;
        });
    }
}
