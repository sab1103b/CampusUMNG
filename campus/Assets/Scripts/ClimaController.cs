using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Consulta el clima actual de Cajicá en OpenWeatherMap y actualiza el widget.
/// Se actualiza solo cada 10 minutos (configurable).
/// </summary>
public class ClimaController : MonoBehaviour
{
    [Header("OpenWeatherMap")]
    [SerializeField] private string apiKey = "PEGA_AQUI_TU_API_KEY";
    [SerializeField] private double latitud = 4.942334;     // Cajicá (aprox.)
    [SerializeField] private double longitud = -74.011848;
    [SerializeField] private float actualizarCadaSegundos = 600f;

    [Header("UI del widget")]
    [SerializeField] private TMP_Text textoTemperatura;
    [SerializeField] private TMP_Text textoDescripcion;
    [Tooltip("Texto antes de la descripción, por ejemplo \"Cajicá: \"")]
    [SerializeField] private string prefijoDescripcion = "";

    // Clases que reflejan la parte del JSON que nos interesa
    [Serializable] public class Principal { public float temp; }
    [Serializable] public class Descripcion { public string description; }
    [Serializable] public class RespuestaClima
    {
        public Principal main;
        public Descripcion[] weather;
    }

    private IEnumerator Start()
    {
        // "14°C" siempre en una sola línea: si no cabe, se achica en vez de partirse
        if (textoTemperatura != null)
        {
            textoTemperatura.textWrappingMode = TextWrappingModes.NoWrap;
            textoTemperatura.enableAutoSizing = true;
            textoTemperatura.fontSizeMin = 14;
            textoTemperatura.fontSizeMax = Mathf.Max(textoTemperatura.fontSize, 24);
        }

        while (true)
        {
            yield return ObtenerClima();
            yield return new WaitForSeconds(actualizarCadaSegundos);
        }
    }

    private IEnumerator ObtenerClima()
    {
        // InvariantCulture: evita que el celular escriba 4,918 (con coma) y rompa la URL
        string lat = latitud.ToString(CultureInfo.InvariantCulture);
        string lon = longitud.ToString(CultureInfo.InvariantCulture);

        string url = "https://api.openweathermap.org/data/2.5/weather" +
                     $"?lat={lat}&lon={lon}&appid={apiKey}&units=metric&lang=es";

        using (UnityWebRequest peticion = UnityWebRequest.Get(url))
        {
            peticion.timeout = 10;
            yield return peticion.SendWebRequest();

            if (peticion.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Clima: error {peticion.responseCode} - {peticion.error}");
                if (textoDescripcion != null) textoDescripcion.text = prefijoDescripcion + "Clima no disponible";
                yield break;
            }

            RespuestaClima datos = JsonUtility.FromJson<RespuestaClima>(peticion.downloadHandler.text);

            if (datos == null || datos.main == null || datos.weather == null || datos.weather.Length == 0)
            {
                Debug.LogWarning("Clima: respuesta inesperada del servidor.");
                yield break;
            }

            if (textoTemperatura != null)
                textoTemperatura.text = $"{Mathf.Round(datos.main.temp)}°C";

            if (textoDescripcion != null)
                textoDescripcion.text = prefijoDescripcion + PrimeraMayuscula(datos.weather[0].description);
        }
    }

    private static string PrimeraMayuscula(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        return char.ToUpper(texto[0]) + texto.Substring(1);
    }
}
