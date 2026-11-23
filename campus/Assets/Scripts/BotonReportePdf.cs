using System.Collections;
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

    private bool generando;   // bloquea el doble toque mientras se genera

    // Si el objeto se desactiva a mitad de la corrutina, esta se detiene: se libera el botón
    private void OnDisable()
    {
        if (!generando) return;
        generando = false;
        UIRapida.EstadoDeshabilitado(boton, false);
    }

    public void Generar()
    {
        if (generando || admin == null || !isActiveAndEnabled) return;
        StartCoroutine(GenerarConProgreso());
    }

    // La generación es síncrona: primero se muestra el estado y se espera un cuadro para que se dibuje
    private IEnumerator GenerarConProgreso()
    {
        generando = true;
        UIRapida.EstadoDeshabilitado(boton, true);   // estado deshabilitado de la app (≥4,5:1)
        if (textoEstado != null) textoEstado.text = "Generando reporte…";
        yield return null;

        try
        {
            admin.GenerarReportePdf((ok, resultado) =>
            {
                if (textoEstado != null) textoEstado.text = resultado;   // AdminService ya devuelve un texto corto y amigable
            });
        }
        finally
        {
            UIRapida.EstadoDeshabilitado(boton, false);
            generando = false;
        }
    }
}
