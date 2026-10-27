using TMPro;
using UnityEngine;

public class LugarInteresUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nombreText;
    [SerializeField] private TMP_Text categoriaText;

    private LugarInteres datos;

    public void Configurar(LugarInteres lugar)
    {
        datos = lugar;

        nombreText.text = datos.Nombre;
        categoriaText.text = datos.Categoria;
    }
}