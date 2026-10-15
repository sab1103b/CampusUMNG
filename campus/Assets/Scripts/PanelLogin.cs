using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de login de administrador. Ponlo en el panel "Panel_AdminLogin".
/// </summary>
public class PanelLogin : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private AdminService admin;
    [SerializeField] private UIManager ui;

    [Header("Campos del panel")]
    [SerializeField] private TMP_InputField campoNickname;
    [SerializeField] private TMP_InputField campoLlave;      // Content Type = Password
    [SerializeField] private Button botonEntrar;
    [SerializeField] private TMP_Text textoError;

    [Header("Al entrar con éxito")]
    [SerializeField] private string pantallaDestino = "AdminPanel";

    // Cada vez que se abre el panel, empieza limpio
    private void OnEnable()
    {
        campoNickname.text = "";
        campoLlave.text = "";
        textoError.text = "";
        botonEntrar.interactable = true;
    }

    /// <summary>Conéctalo al OnClick del botón ENTRAR.</summary>
    public void Entrar()
    {
        string nickname = campoNickname.text.Trim();
        string llave = campoLlave.text;

        if (nickname.Length == 0 || llave.Length == 0)
        {
            textoError.text = "Escribe tu nickname y tu llave.";
            return;
        }

        textoError.text = "";
        botonEntrar.interactable = false;   // evita enviar dos veces seguidas

        admin.IniciarSesion(nickname, llave, (ok, mensaje) =>
        {
            botonEntrar.interactable = true;
            campoLlave.text = "";            // la llave nunca se queda escrita en pantalla

            if (ok) ui.Reemplazar(pantallaDestino);   // el login no queda en el historial
            else textoError.text = mensaje;
        });
    }
}
