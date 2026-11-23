using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Buscador de lugares de la pantalla de usuario (extraído de InterfazCampus): espera corta al escribir,
/// filtro por categoría, lista de resultados con filas reutilizables y textos de "sin resultados".
/// InterfazCampus lo añade en su Awake, le pasa las referencias de la escena (Configurar) y escucha sus eventos:
/// ResultadoElegido (abrir ficha/destino), Mostrando (cerrar las demás capas), Mostrada (ubicar el panel en la
/// franja libre) y EstadoCambiado (pintar los chips).
/// </summary>
public class BusquedaLugares : MonoBehaviour
{
    private const float EsperaBusqueda = 0.15f;   // segundos sin escribir antes de buscar
    /// <summary>Mínimo de filas visibles: con los espacios interiores, una categoría puede pasar de 30.</summary>
    public const int MinResultados = 80;

    /// <summary>Se tocó una fila: un lugar (servicio = null) o un espacio interior con su lugar dueño.</summary>
    public event Action<LugaresService.Lugar, LugaresService.Servicio> ResultadoElegido;
    /// <summary>La lista va a aparecer (o a actualizarse): cerrar las demás capas del mapa.</summary>
    public event Action Mostrando;
    /// <summary>La lista ya está activa y encima de todo: ubicarla en la franja libre.</summary>
    public event Action Mostrada;
    /// <summary>Cambió la categoría activa o la visibilidad de la lista (para pintar los chips).</summary>
    public event Action EstadoCambiado;

    /// <summary>null = sin filtro; "*" = todas; otro = la categoría del chip tocado.</summary>
    public string CategoriaFiltro { get; private set; }
    public bool Abierta => panelResultados != null && panelResultados.activeSelf;
    public int MaxResultados { get; set; } = 30;

    private LugaresService lugares;
    private TMP_InputField buscador;
    private GameObject panelResultados;
    private RectTransform contenedorResultados;
    private Button plantillaResultado;
    private TMP_Text textoSinResultados;

    private bool busquedaPendiente;
    private float busquedaEn;

    // Índice de búsqueda (textos normalizados una vez por carga de datos): ver IndiceBusqueda
    private readonly IndiceBusqueda indice = new IndiceBusqueda();
    private readonly List<IndiceBusqueda.Resultado> resultados = new List<IndiceBusqueda.Resultado>();

    // Filas de resultado reutilizables (no se destruyen ni se crean en cada tecla)
    private readonly List<Button> filas = new List<Button>();
    private readonly List<TMP_Text> textosFilas = new List<TMP_Text>();
    private readonly List<LugaresService.Lugar> lugaresFilas = new List<LugaresService.Lugar>();
    private readonly List<LugaresService.Servicio> serviciosFilas = new List<LugaresService.Servicio>();
    private int filasUsadas;

    private static readonly string HexGris = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Gris);
    // Segunda línea de cada resultado: tamaño absoluto (TamSecundario), no relativo al de la fila
    private static readonly string TamSegundaLinea =
        Mathf.RoundToInt(UIRapida.TamSecundario).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Referencias de la escena (las mismas que tiene InterfazCampus). Se llama una vez, en su Awake.</summary>
    public void Configurar(LugaresService datos, TMP_InputField campo, GameObject panel, RectTransform contenedor,
                           Button plantilla, TMP_Text textoVacio, int maxResultados)
    {
        lugares = datos;
        buscador = campo;
        panelResultados = panel;
        contenedorResultados = contenedor;
        plantillaResultado = plantilla;
        textoSinResultados = textoVacio;
        MaxResultados = Mathf.Max(maxResultados, MinResultados);
        if (buscador != null) { buscador.onValueChanged.RemoveListener(AlEscribir); buscador.onValueChanged.AddListener(AlEscribir); }
    }

    // Sin búsqueda pendiente no hace falta Update: el componente se apaga hasta la próxima tecla
    private void Awake() => enabled = false;

    private void OnDestroy()
    {
        if (buscador != null) buscador.onValueChanged.RemoveListener(AlEscribir);
    }

    // Búsqueda con espera corta: no se recalcula en cada tecla
    private void Update()
    {
        if (busquedaPendiente && Time.unscaledTime >= busquedaEn)
        {
            busquedaPendiente = false;
            enabled = false;
            Actualizar();
        }
    }

    private void AlEscribir(string texto)
    {
        // Con texto (no solo espacios) siempre se busca en todas las categorías; los chips se actualizan al instante
        if (texto.Trim().Length > 0 && CategoriaFiltro != null) { CategoriaFiltro = null; EstadoCambiado?.Invoke(); }
        if (texto.Trim().Length == 0)
        {
            busquedaPendiente = false;
            Actualizar();   // borrar el texto cierra la lista enseguida
            return;
        }
        busquedaPendiente = true;
        busquedaEn = Time.unscaledTime + EsperaBusqueda;
        enabled = true;   // Update revisa la espera solo mientras hay una búsqueda programada
    }

    /// <summary>Chip de categoría ("Todos" = todas). Tocar otra vez la misma categoría cierra la lista.</summary>
    public void FiltrarCategoria(string categoria)
    {
        string nuevo = IndiceBusqueda.Normalizar(categoria) == "todos" ? "*" : categoria;
        if (Abierta && CategoriaFiltro == nuevo)
        {
            Ocultar();
            return;
        }

        CategoriaFiltro = nuevo;
        busquedaPendiente = false;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        Actualizar();
    }

    /// <summary>Cierra la lista y olvida el texto y la categoría.</summary>
    public void Ocultar()
    {
        CategoriaFiltro = null;
        busquedaPendiente = false;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        if (panelResultados != null) panelResultados.SetActive(false);
        EstadoCambiado?.Invoke();
    }

    /// <summary>Datos nuevos: el índice se recalcula y una lista abierta se vuelve a llenar.</summary>
    public void RecargarDatos()
    {
        indice.Invalidar();
        if (Abierta) Actualizar();
    }

    private void Actualizar()
    {
        if (panelResultados == null || lugares == null) return;

        string q = IndiceBusqueda.Normalizar(buscador != null ? buscador.text : "");
        if (q.Length == 0 && CategoriaFiltro == null)
        {
            panelResultados.SetActive(false);
            EstadoCambiado?.Invoke();
            return;
        }

        indice.Preparar(lugares);
        bool todas = CategoriaFiltro == null || CategoriaFiltro == "*";
        string filtro = todas ? null : IndiceBusqueda.Normalizar(CategoriaFiltro);

        filasUsadas = 0;
        // total = todas las coincidencias (para el aviso de corte); se pintan hasta MaxResultados, ya por relevancia
        int total = indice.Buscar(q, filtro, MaxResultados, resultados);
        int n = resultados.Count;
        foreach (IndiceBusqueda.Resultado r in resultados)
        {
            if (r.servicio == null)
            {
                AgregarFila($"<b>{UIRapida.TextoSeguro(r.lugar.nombre)}</b>\n<size={TamSegundaLinea}><color={HexGris}>{UIRapida.TextoSeguro(r.lugar.categoria)}</color></size>", r.lugar, null);
                continue;
            }
            LugaresService.Servicio s = r.servicio;
            string piso = string.IsNullOrEmpty(s.piso) ? "" : $" · piso {s.piso}";
            string segunda = UIRapida.TextoSeguro($"{s.categoria} en {r.lugar.nombre}{piso}");
            AgregarFila($"<b>{UIRapida.TextoSeguro(s.nombre)}</b>\n<size={TamSegundaLinea}><color={HexGris}>{segunda}</color></size>",
                        r.lugar, s);
        }
        resultados.Clear();   // las filas ya guardan sus referencias

        // Las filas que sobran se esconden (quedan para la próxima búsqueda)
        for (int i = filasUsadas; i < filas.Count; i++)
        {
            if (filas[i].gameObject.activeSelf) filas[i].gameObject.SetActive(false);
            lugaresFilas[i] = null;
            serviciosFilas[i] = null;
        }

        if (textoSinResultados != null)
        {
            // Sin resultados: explica por qué. Lista cortada: lo dice al final
            bool cortado = total > n;
            textoSinResultados.gameObject.SetActive(n == 0 || cortado);
            textoSinResultados.text = cortado
                ? (q.Length == 0 ? $"Mostrando {n} de {total}. Escribe un nombre para afinar."
                                 : $"Mostrando {n} de {total}. Escribe más para afinar.")
                // (escribir quita el filtro de categoría: con texto siempre se busca en todas)
                : q.Length > 0
                ? "No encontramos coincidencias. Prueba con otra palabra o una categoría."
                : !todas
                    ? $"Aún no hay lugares en la categoría «{UIRapida.TextoSeguro(CategoriaFiltro)}»."
                    : "Aún no hay lugares registrados.";
            textoSinResultados.transform.SetAsLastSibling();
        }

        Mostrando?.Invoke();   // InterfazCampus cierra las demás capas (ficha, menú de perfil)
        panelResultados.SetActive(true);
        panelResultados.transform.SetAsLastSibling();   // por encima de todo lo del mapa
        Mostrada?.Invoke();    // InterfazCampus la ubica en la franja libre
        EstadoCambiado?.Invoke();
    }

    private void AgregarFila(string texto, LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
        int i = filasUsadas++;
        if (i >= filas.Count)
        {
            Button nueva = Instantiate(plantillaResultado, contenedorResultados);
            int indiceFila = i;
            nueva.onClick.RemoveAllListeners();
            nueva.onClick.AddListener(() => ElegirFila(indiceFila));   // una sola vez por fila
            filas.Add(nueva);
            textosFilas.Add(nueva.GetComponentInChildren<TMP_Text>(true));
            lugaresFilas.Add(null);
            serviciosFilas.Add(null);
        }

        Button fila = filas[i];
        if (!fila.gameObject.activeSelf) fila.gameObject.SetActive(true);
        if (textosFilas[i] != null) textosFilas[i].text = texto;
        lugaresFilas[i] = lugar;
        serviciosFilas[i] = servicio;
    }

    private void ElegirFila(int indiceFila)
    {
        if (indiceFila < 0 || indiceFila >= filasUsadas || lugaresFilas[indiceFila] == null) return;
        LugaresService.Lugar lugar = lugaresFilas[indiceFila];
        LugaresService.Servicio servicio = serviciosFilas[indiceFila];
        Ocultar();
        ResultadoElegido?.Invoke(lugar, servicio);
    }
}
