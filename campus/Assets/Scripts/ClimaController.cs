using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Consulta el clima actual de Cajicá en OpenWeatherMap y actualiza el widget.
/// Si la consulta sale bien, se actualiza cada 10 minutos (configurable).
/// Si falla, muestra "--°C" (nunca un valor viejo) y reintenta al minuto.
/// </summary>
public class ClimaController : MonoBehaviour
{
    private const string SinDato = "--°C";

    /// <summary>
    /// Se lanza cada vez que cambia el texto de la temperatura o de la descripción (p. ej. para que la
    /// píldora del clima se ajuste al contenido sin revisarlo en cada frame). Quien se suscriba debe
    /// desuscribirse en OnDisable/OnDestroy.
    /// </summary>
    public static event Action TextoActualizado;

    [Header("OpenWeatherMap")]
    [SerializeField] private string apiKey = "PEGA_AQUI_TU_API_KEY";
    [SerializeField] private double latitud = 4.942334;     // Cajicá (aprox.)
    [SerializeField] private double longitud = -74.011848;
    [SerializeField] private float actualizarCadaSegundos = 600f;
    [Tooltip("Si la consulta falla, se vuelve a intentar tras estos segundos")]
    [SerializeField] private float reintentarCadaSegundos = 60f;

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

    private void Awake()
    {
        // El texto que trae la escena ("New Text", "21*"...) es solo de diseño: nunca es un dato real
        PonerTextos(SinDato, prefijoDescripcion + "Cargando clima…");
    }

    private void Start()
    {
        // "14°C" siempre en una sola línea: si no cabe, se achica en vez de partirse
        if (textoTemperatura != null)
        {
            textoTemperatura.textWrappingMode = TextWrappingModes.NoWrap;
            textoTemperatura.enableAutoSizing = true;
            textoTemperatura.fontSizeMax = Mathf.Max(textoTemperatura.fontSize, UIRapida.TamSubtitulo);
            textoTemperatura.fontSizeMin = UIRapida.TamMinimo;   // autoajuste nunca por debajo del mínimo legible de la escala
        }

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("PEGA_AQUI", StringComparison.Ordinal))
        {
            Debug.LogWarning("Clima: falta la API key de OpenWeatherMap en el inspector.");
            MostrarError();
            return;   // sin clave no tiene sentido consultar ni reintentar
        }

        // Esperas reutilizadas (no se crea un objeto nuevo en cada vuelta del ciclo)
        esperaActualizar = new WaitForSecondsRealtime(actualizarCadaSegundos);
        esperaReintento = new WaitForSecondsRealtime(reintentarCadaSegundos);
        iniciado = true;
        ciclo = StartCoroutine(Ciclo());
    }

    // Desactivar el objeto detiene la corrutina sin pasar por sus finally: se deja el estado limpio
    private void OnDisable()
    {
        ciclo = null;
        consultando = false;
    }

    // Al reactivarlo, el ciclo sigue (Start no se vuelve a llamar)
    private void OnEnable()
    {
        if (!iniciado || ciclo != null || claveInvalida) return;
        ReiniciarEsperas();
        ciclo = StartCoroutine(Ciclo());
    }

    // Una espera cortada a la mitad guarda su hora de fin: Reset la deja lista para volver a contar completa
    private void ReiniciarEsperas()
    {
        esperaActualizar?.Reset();
        esperaReintento?.Reset();
    }

    private IEnumerator Ciclo()
    {
        while (true)
        {
            yield return ObtenerClima();
            if (claveInvalida) { ciclo = null; yield break; }   // 401/403: reintentar no sirve hasta cambiar la API key
            yield return ultimoOk ? esperaActualizar : esperaReintento;
        }
    }

    // Al volver a la app (estuvo en segundo plano): si la última consulta tiene más de 60 s, se consulta ya
    // en vez de esperar el resto del intervalo (el dato pudo quedar viejo mientras el celular dormía)
    private void OnApplicationPause(bool pausada)
    {
        if (pausada || consultando || ciclo == null || claveInvalida || !isActiveAndEnabled) return;
        if (Time.realtimeSinceStartup - ultimaConsulta < SegundosMinimosAlVolver) return;
        StopCoroutine(ciclo);   // estaba esperando (no consultando): se puede reiniciar sin cortar una petición
        ReiniciarEsperas();
        ciclo = StartCoroutine(Ciclo());
    }

    private bool ultimoOk;
    private bool claveInvalida;
    private const float SegundosMinimosAlVolver = 60f;
    private WaitForSecondsRealtime esperaActualizar;
    private WaitForSecondsRealtime esperaReintento;
    private Coroutine ciclo;
    private bool consultando;
    private bool iniciado;
    private float ultimaConsulta = float.NegativeInfinity;

    private IEnumerator ObtenerClima()
    {
        consultando = true;
        try { yield return Consultar(); }
        finally
        {
            consultando = false;
            ultimaConsulta = Time.realtimeSinceStartup;
        }
    }

    private IEnumerator Consultar()
    {
        ultimoOk = false;
        // InvariantCulture: evita que el celular escriba 4,918 (con coma) y rompa la URL
        string lat = latitud.ToString(CultureInfo.InvariantCulture);
        string lon = longitud.ToString(CultureInfo.InvariantCulture);

        string url = "https://api.openweathermap.org/data/2.5/weather" +
                     $"?lat={lat}&lon={lon}&appid={apiKey}&units=metric&lang=es";

        using (UnityWebRequest peticion = UnityWebRequest.Get(url))
        {
            peticion.timeout = 10;
            yield return peticion.SendWebRequest();

            if (peticion.responseCode == 401 || peticion.responseCode == 403)
            {
                claveInvalida = true;
                Debug.LogWarning($"Clima: API key de clima inválida (HTTP {peticion.responseCode}). No se reintenta; revisa la clave en el inspector.");
                MostrarError();
                yield break;
            }

            if (peticion.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Clima: error {peticion.responseCode} - {peticion.error}. Reintento en {reintentarCadaSegundos:0} s.");
                MostrarError();
                yield break;
            }

            RespuestaClima datos = null;
            try { datos = JsonUtility.FromJson<RespuestaClima>(peticion.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning("Clima: JSON inválido. " + e.Message); }

            if (datos == null || datos.main == null || datos.weather == null || datos.weather.Length == 0)
            {
                Debug.LogWarning("Clima: respuesta inesperada del servidor.");
                MostrarError();
                yield break;
            }

            PonerTextos($"{Mathf.RoundToInt(datos.main.temp)}°C",
                        prefijoDescripcion + PrimeraMayuscula(datos.weather[0].description));
            ultimoOk = true;
        }
    }

    // Sin dato: nunca se deja en pantalla una temperatura vieja
    private void MostrarError()
    {
        PonerTextos(SinDato, prefijoDescripcion + "Clima no disponible");
    }

    // Escribe ambos textos y avisa (TextoActualizado) solo si alguno cambió
    private void PonerTextos(string temperatura, string descripcion)
    {
        bool cambio = Poner(textoTemperatura, temperatura);
        cambio |= Poner(textoDescripcion, descripcion);
        if (cambio) TextoActualizado?.Invoke();
    }

    // Asigna solo si cambia (evita reconstruir la malla del texto sin necesidad); true si cambió
    private static bool Poner(TMP_Text texto, string valor)
    {
        if (texto == null || texto.text == valor) return false;
        texto.text = valor;
        return true;
    }

    private static string PrimeraMayuscula(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        return char.ToUpper(texto[0]) + texto.Substring(1);
    }
}
