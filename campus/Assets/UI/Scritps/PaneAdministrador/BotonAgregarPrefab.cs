using System.IO;
using UnityEngine;

public class GestorLugares : MonoBehaviour
{
    [SerializeField] private GameObject prefabLugar;
    [SerializeField] private Transform contenedor;

    public BaseDatosLugares baseDatos =
        new BaseDatosLugares();

    private string rutaArchivo;

    private void Awake()
    {
        rutaArchivo =
            Application.persistentDataPath +
            "/lugares.json";
    }

    private void Start()
    {
        CargarDatos();
        GenerarListaVisual();
    }

    public void AgregarLugar()
    {
        LugarInteres nuevo = new LugarInteres();

        nuevo.Nombre = "Nuevo Lugar";
        nuevo.Categoria = "Sin Categoría";

        baseDatos.lugares.Add(nuevo);

        GuardarDatos();

        GenerarListaVisual();
    }

    public void GuardarDatos()
    {
        string json =
            JsonUtility.ToJson(
                baseDatos,
                true);

        File.WriteAllText(
            rutaArchivo,
            json);

        Debug.Log("Datos guardados");
    }

    public void CargarDatos()
    {
        if (File.Exists(rutaArchivo))
        {
            string json =
                File.ReadAllText(rutaArchivo);

            baseDatos =
                JsonUtility.FromJson<BaseDatosLugares>(
                    json);
        }
    }

    public void GenerarListaVisual()
    {
        foreach (Transform hijo in contenedor)
        {
            Destroy(hijo.gameObject);
        }

        foreach (LugarInteres lugar in baseDatos.lugares)
        {
            Instantiate(
                prefabLugar,
                contenedor);
        }
    }
}