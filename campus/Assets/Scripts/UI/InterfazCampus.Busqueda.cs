using UnityEngine;

/// <summary>
/// InterfazCampus: conexión con el buscador (BusquedaLugares). La búsqueda, la espera al escribir y la lista de
/// resultados viven en BusquedaLugares; aquí solo se crea, se le pasan las referencias de la escena y se
/// responde a sus eventos (capas, ubicación en la franja, chips y elección de un resultado).
/// </summary>
public partial class InterfazCampus
{
    private BusquedaLugares busqueda;

    // null = sin filtro; "*" = todas (lo guarda BusquedaLugares; los chips lo leen)
    private string categoriaFiltro => busqueda != null ? busqueda.CategoriaFiltro : null;

    // Se llama en Awake: las referencias son los campos serializados de siempre (la escena no cambia)
    private void PrepararBusqueda()
    {
        busqueda = GetComponent<BusquedaLugares>();
        if (busqueda == null) busqueda = gameObject.AddComponent<BusquedaLugares>();
        busqueda.Configurar(lugares, buscador, panelResultados, contenedorResultados, plantillaResultado,
                            textoSinResultados, maxResultados);   // Configurar aplica el mínimo (MinResultados)
        busqueda.Mostrando += AlMostrarResultados;
        busqueda.Mostrada += AlUbicarResultados;
        busqueda.EstadoCambiado += ActualizarChips;
        busqueda.ResultadoElegido += ElegirResultado;
    }

    private void SoltarBusqueda()
    {
        if (busqueda == null) return;
        busqueda.Mostrando -= AlMostrarResultados;
        busqueda.Mostrada -= AlUbicarResultados;
        busqueda.EstadoCambiado -= ActualizarChips;
        busqueda.ResultadoElegido -= ElegirResultado;
    }

    private void AlMostrarResultados() => CerrarCapasExcepto(Capa.Resultados);

    private void AlUbicarResultados()
    {
        if (panelResultados != null)
            AcomodarEnFranja((RectTransform)panelResultados.transform, UIRapida.AltoMaxResultados, pegadoArriba: true);
    }

    private void FiltrarCategoria(string categoria)
    {
        if (busqueda != null) busqueda.FiltrarCategoria(categoria);
    }

    private void OcultarResultados()
    {
        if (busqueda != null) busqueda.Ocultar();
    }

    // La lista ya se cerró (BusquedaLugares.Ocultar antes de avisar)
    private void ElegirResultado(LugaresService.Lugar lugar, LugaresService.Servicio servicio)
    {
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
}
