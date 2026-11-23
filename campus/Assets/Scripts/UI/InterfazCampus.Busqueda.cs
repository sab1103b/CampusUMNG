using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary> InterfazCampus: buscador, categorías y lista de resultados. </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Usuario: buscador y resultados
    // ==================================================================
    private const float EsperaBusqueda = 0.15f;   // segundos sin escribir antes de buscar
    private bool busquedaPendiente;
    private float busquedaEn;

    // Textos normalizados (minúsculas, sin tildes) calculados una sola vez por carga de datos
    private struct EntradaLugar
    {
        public LugaresService.Lugar lugar;
        public string nombre, categoria, raizCategoria, detalle;
    }

    private struct EntradaServicio
    {
        public LugaresService.Servicio servicio;
        public LugaresService.Lugar dueño;
        public string nombre, categoria, raizCategoria, detalle, categoriaDueño, raizCategoriaDueño;
    }

    private readonly List<EntradaLugar> indiceLugares = new List<EntradaLugar>();
    private readonly List<EntradaServicio> indiceServicios = new List<EntradaServicio>();
    private bool indiceBusquedaListo;
    private object listaLugaresIndexada, listaServiciosIndexada;

    // Filas de resultado reutilizables (no se destruyen ni se crean en cada tecla)
    private readonly List<Button> filasResultados = new List<Button>();
    private readonly List<TMP_Text> textosFilas = new List<TMP_Text>();
    private readonly List<LugaresService.Lugar> lugaresFilas = new List<LugaresService.Lugar>();
    private readonly List<LugaresService.Servicio> serviciosFilas = new List<LugaresService.Servicio>();
    private int filasUsadas;

    private static readonly string HexGris = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Gris);
    // Segunda línea de cada resultado: tamaño absoluto (TamSecundario), no relativo al de la fila
    private static readonly string TamSegundaLinea =
        Mathf.RoundToInt(UIRapida.TamSecundario).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void AlEscribirBusqueda(string texto)
    {
        if (texto.Length > 0) categoriaFiltro = null;
        if (texto.Trim().Length == 0)
        {
            busquedaPendiente = false;
            ActualizarResultados();   // borrar el texto cierra la lista enseguida
            return;
        }
        busquedaPendiente = true;
        busquedaEn = Time.unscaledTime + EsperaBusqueda;
    }

    private void FiltrarCategoria(string categoria)
    {
        string nuevo = Normalizar(categoria) == "todos" ? "*" : categoria;

        // Tocar otra vez la misma categoría cierra la lista
        if (panelResultados != null && panelResultados.activeSelf && categoriaFiltro == nuevo)
        {
            OcultarResultados();
            return;
        }

        categoriaFiltro = nuevo;
        busquedaPendiente = false;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        ActualizarResultados();
    }

    private void OcultarResultados()
    {
        categoriaFiltro = null;
        busquedaPendiente = false;
        if (buscador != null) buscador.SetTextWithoutNotify("");
        if (panelResultados != null) panelResultados.SetActive(false);
        ActualizarChips();
    }

    private void PrepararIndiceBusqueda()
    {
        if (lugares == null) return;
        if (indiceBusquedaListo && ReferenceEquals(listaLugaresIndexada, lugares.Lugares)
                                && ReferenceEquals(listaServiciosIndexada, lugares.Servicios)) return;

        indiceLugares.Clear();
        foreach (LugaresService.Lugar l in lugares.Lugares)
        {
            string cat = Normalizar(l.categoria);
            indiceLugares.Add(new EntradaLugar
            {
                lugar = l,
                nombre = Normalizar(l.nombre),
                categoria = cat,
                raizCategoria = cat.TrimEnd('s'),
                detalle = Normalizar(l.descripcion) + "\n" + Normalizar(l.indicacion),
            });
        }

        indiceServicios.Clear();
        foreach (LugaresService.Servicio s in lugares.Servicios)
        {
            LugaresService.Lugar dueño = lugares.BuscarPorId(s.lugar_id);
            if (dueño == null) continue;
            string cat = Normalizar(s.categoria), catDueño = Normalizar(dueño.categoria);
            indiceServicios.Add(new EntradaServicio
            {
                servicio = s,
                dueño = dueño,
                nombre = Normalizar(s.nombre),
                categoria = cat,
                raizCategoria = cat.TrimEnd('s'),
                detalle = Normalizar(s.descripcion) + "\n" + Normalizar(s.indicacion),
                categoriaDueño = catDueño,
                raizCategoriaDueño = catDueño.TrimEnd('s'),
            });
        }

        listaLugaresIndexada = lugares.Lugares;
        listaServiciosIndexada = lugares.Servicios;
        indiceBusquedaListo = true;
    }

    private void ActualizarResultados()
    {
        if (panelResultados == null || lugares == null) return;

        string q = Normalizar(buscador != null ? buscador.text : "");
        if (q.Length == 0 && categoriaFiltro == null)
        {
            panelResultados.SetActive(false);
            ActualizarChips();
            return;
        }

        PrepararIndiceBusqueda();
        bool todas = categoriaFiltro == null || categoriaFiltro == "*";
        string filtro = todas ? null : Normalizar(categoriaFiltro);
        string raizFiltro = filtro != null ? filtro.TrimEnd('s') : null;

        filasUsadas = 0;
        int n = 0, total = 0;   // n = filas mostradas; total = todas las coincidencias (para el aviso de corte)
        foreach (EntradaLugar e in indiceLugares)
        {
            if (!CoincideCategoria(e.categoria, e.raizCategoria, filtro, raizFiltro)) continue;
            if (q.Length > 0 && !Coincide(e.nombre, e.categoria, e.detalle, q)) continue;
            total++;
            if (n >= maxResultados) continue;   // se sigue contando, sin crear filas

            AgregarFila($"<b>{TextoSeguro(e.lugar.nombre)}</b>\n<size={TamSegundaLinea}><color={HexGris}>{TextoSeguro(e.lugar.categoria)}</color></size>", e.lugar, null);
            n++;
        }

        // Servicios (aulas, biblioteca, laboratorios, oficinas...): al escribir, o al elegir una categoría
        // que coincida con la suya (por ejemplo "Biblioteca" muestra la biblioteca del Camacho Leiva)
        bool porCategoria = q.Length == 0 && filtro != null;
        if (q.Length > 0 || porCategoria)
        {
            foreach (EntradaServicio e in indiceServicios)
            {
                bool coincideServicio = CoincideCategoria(e.categoria, e.raizCategoria, filtro, raizFiltro);
                if (porCategoria) { if (!coincideServicio) continue; }
                else if (!coincideServicio && !CoincideCategoria(e.categoriaDueño, e.raizCategoriaDueño, filtro, raizFiltro)) continue;
                if (q.Length > 0 && !Coincide(e.nombre, e.categoria, e.detalle, q)) continue;
                total++;
                if (n >= maxResultados) continue;

                LugaresService.Servicio s = e.servicio;
                string piso = string.IsNullOrEmpty(s.piso) ? "" : $" · piso {s.piso}";
                string segunda = TextoSeguro($"{s.categoria} en {e.dueño.nombre}{piso}");
                AgregarFila($"<b>{TextoSeguro(s.nombre)}</b>\n<size={TamSegundaLinea}><color={HexGris}>{segunda}</color></size>",
                            e.dueño, s);
                n++;
            }
        }

        // Las filas que sobran se esconden (quedan para la próxima búsqueda)
        for (int i = filasUsadas; i < filasResultados.Count; i++)
            if (filasResultados[i].gameObject.activeSelf) filasResultados[i].gameObject.SetActive(false);

        if (textoSinResultados != null)
        {
            // Sin resultados: explica por qué. Lista cortada: lo dice al final
            bool cortado = total > n;
            textoSinResultados.gameObject.SetActive(n == 0 || cortado);
            textoSinResultados.text = cortado
                ? (q.Length == 0
                    ? $"Mostrando {n} de {total}; escribe un nombre para afinar."
                    : $"Mostrando {n} de {total}; escribe más para afinar.")
                : q.Length > 0 && !todas
                ? $"No hay «{TextoSeguro(buscador != null ? buscador.text.Trim() : q)}» en «{TextoSeguro(categoriaFiltro)}». Prueba con «Todos»."
                : q.Length > 0
                ? "No encontramos coincidencias. Prueba con otra palabra o una categoría."
                : !todas
                    ? $"Aún no hay lugares en la categoría «{TextoSeguro(categoriaFiltro)}»."
                    : "Aún no hay lugares registrados.";
            textoSinResultados.transform.SetAsLastSibling();
        }

        CerrarCapasExcepto(Capa.Resultados);
        panelResultados.SetActive(true);
        panelResultados.transform.SetAsLastSibling();   // por encima de todo lo del mapa
        AcomodarEnFranja((RectTransform)panelResultados.transform, UIRapida.AltoMaxResultados, pegadoArriba: true);
        ActualizarChips();
    }

    private void AgregarFila(string texto, LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
        int i = filasUsadas++;
        if (i >= filasResultados.Count)
        {
            Button nueva = Instantiate(plantillaResultado, contenedorResultados);
            int indice = i;
            nueva.onClick.RemoveAllListeners();
            nueva.onClick.AddListener(() => ElegirFila(indice));   // una sola vez por fila
            filasResultados.Add(nueva);
            textosFilas.Add(nueva.GetComponentInChildren<TMP_Text>(true));
            lugaresFilas.Add(null);
            serviciosFilas.Add(null);
        }

        Button fila = filasResultados[i];
        if (!fila.gameObject.activeSelf) fila.gameObject.SetActive(true);
        if (textosFilas[i] != null) textosFilas[i].text = texto;
        lugaresFilas[i] = lugar;
        serviciosFilas[i] = servicio;
    }

    // Nombre o categoría desde 1 letra; descripción e indicaciones desde 3 letras
    private static bool Coincide(string nombre, string categoria, string detalle, string q) =>
        nombre.IndexOf(q, StringComparison.Ordinal) >= 0 || categoria.IndexOf(q, StringComparison.Ordinal) >= 0 ||
        (q.Length >= 3 && detalle.IndexOf(q, StringComparison.Ordinal) >= 0);

    private void ElegirFila(int indice)
    {
        if (indice < 0 || indice >= filasUsadas || lugaresFilas[indice] == null) return;
        ElegirResultado(lugaresFilas[indice], serviciosFilas[indice]);
    }

    /// <summary>¿La categoría coincide con el filtro activo? Una categoría vacía no coincide con ningún filtro.</summary>
    private bool CoincideCategoria(string categoria)
    {
        if (categoriaFiltro == null || categoriaFiltro == "*") return true;
        string a = Normalizar(categoria), b = Normalizar(categoriaFiltro);
        return CoincideCategoria(a, a.TrimEnd('s'), b, b.TrimEnd('s'));
    }

    // a / b ya normalizados; "Aulas" coincide con "Aula", "Cafetería" con "Cafeteria"...
    private static bool CoincideCategoria(string a, string raizA, string b, string raizB)
    {
        if (b == null) return true;          // sin filtro o "Todos"
        if (a.Length == 0) return false;     // sin categoría: no entra en ningún filtro
        if (a == b || raizA == raizB) return true;   // singular/plural ("aula" = "aulas")
        // Prefijo solo con raíces de 3 letras o más: una raíz corta ("a", "ba") no debe abarcar otras categorías
        return (raizB.Length >= 3 && a.StartsWith(raizB, StringComparison.Ordinal)) ||
               (raizA.Length >= 3 && b.StartsWith(raizA, StringComparison.Ordinal));
    }

    private void ElegirResultado(LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
        OcultarResultados();

        // Igual que el buscador: registra la búsqueda y, si hay una ruta, la cambia al nuevo destino
        if (comoLlegar != null)
        {
            if (servicio != null) comoLlegar.SeleccionarServicio(servicio);
            else comoLlegar.SeleccionarLugar(lugar);
            // Pin de destino y cámara centrada en el lugar (después de Seleccionar: un LimpiarRuta ocultaría el pin)
            LugaresService.Lugar destino = servicio != null && lugares != null ? lugares.BuscarPorId(servicio.lugar_id) : lugar;
            if (destino != null) comoLlegar.MostrarDestino(destino);
        }

        MostrarFicha(lugar, servicio);
    }

    // Minúsculas y sin tildes: "Cafetería" = "cafeteria"
    private static string Normalizar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim().ToLowerInvariant())
        {
            switch (ch)
            {
                case 'á': case 'à': case 'ä': sb.Append('a'); break;
                case 'é': case 'è': case 'ë': sb.Append('e'); break;
                case 'í': case 'ì': case 'ï': sb.Append('i'); break;
                case 'ó': case 'ò': case 'ö': sb.Append('o'); break;
                case 'ú': case 'ù': case 'ü': sb.Append('u'); break;
                case 'ñ': sb.Append('n'); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
