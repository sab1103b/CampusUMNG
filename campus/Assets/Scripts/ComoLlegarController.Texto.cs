using System.Globalization;
using UnityEngine;

/// <summary>Cómo llegar: texto de la ruta (destino, distancia, tiempo y avisos).</summary>
public partial class ComoLlegarController
{
    private void ActualizarTexto(RedCaminos.Ruta ruta)
    {
        if (textoRuta == null) return;

        // Texto: si es un servicio, se agrega su piso e indicación para llegar dentro del lugar
        string titulo = Seguro(seleccionado.nombre);
        string detalle = "";
        if (servicioSel != null)
        {
            titulo = $"{Seguro(servicioSel.nombre)} · {Seguro(seleccionado.nombre)}";
            if (!string.IsNullOrEmpty(servicioSel.piso)) detalle = $"Piso {Seguro(servicioSel.piso)}";
            if (!string.IsNullOrEmpty(servicioSel.indicacion))
                detalle += (detalle.Length > 0 ? " · " : "") + Seguro(Recortar(servicioSel.indicacion, MaxIndicacion));
            if (detalle.Length > 0) detalle = "\n" + detalle;
        }

        double metros = ruta.metrosTotal;
        // Origen anclado en la entrada principal: sin ubicación, o fuera del campus (con usarPuntoFijoFuera)
        string avisoUbicacion = ubicacion.UsandoUbicacionAproximada
            ? "\nUbicación no disponible: la ruta sale de la entrada principal."
            : ubicacion.OrigenAnclado
                ? "\nEstás fuera del campus: la ruta sale de la entrada principal."
                : "";

        if (Llegaste(ruta))
        {
            PonerTexto($"<b>Has llegado</b>\n{Seguro(seleccionado.nombre)}{Pequeno(detalle)}");
            return;
        }

        // Línea 1: destino en negrita · Línea 2: distancia y tiempo · Línea 3 (pequeña): avisos y piso
        int minutos = Mathf.Max(1, Mathf.CeilToInt((float)(metros / velocidadCaminarMs) / 60f));
        string resumen = $"<b>{titulo}</b>\n{Distancia(metros)}  ·  {minutos} min a pie";

        string aviso = "";
        if (!ruta.porCaminos && ruta.motivoRecta != "el destino está muy cerca") aviso = "\nEn línea recta (fuera de los caminos).";
        else if (ruta.porCaminos && ruta.metrosHastaCamino > maxDistanciaAlosCaminosM)
            aviso = $"\nCamina {Distancia(ruta.metrosHastaCamino)} hasta el camino más cercano.";

        PonerTexto(resumen + Pequeno(avisoUbicacion + aviso + detalle));
    }

    // Con el origen anclado en la entrada (ubicación aproximada o fuera del campus) no se puede afirmar que llegaste
    private bool Llegaste(RedCaminos.Ruta ruta) =>
        ruta != null && ruta.metrosTotal <= distanciaLlegadaM && !ubicacion.OrigenAnclado;

    // Nombres e indicaciones vienen de la base de datos: se escapan para que TMP no interprete etiquetas
    private static string Seguro(string s) => UIRapida.TextoSeguro(s);

    // Asigna el texto solo si cambió
    private void PonerTexto(string valor)
    {
        if (textoRuta != null && textoRuta.text != valor) textoRuta.text = valor;
    }

    // "350 m" o, desde 1000 m, "1,2 km" (coma decimal, como se escribe en Colombia)
    private static string Distancia(double metros)
    {
        if (metros < 999.5) return $"{metros:0} m";
        return (metros / 1000.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";
    }

    // La barra de ruta muestra como máximo MaxLineasTexto líneas (el resto con "…") y la indicación
    // del servicio se recorta a MaxIndicacion caracteres: la barra no crece ni desborda
    private const int MaxLineasTexto = 4;
    private const int MaxIndicacion = 60;

    private static string Recortar(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";

    // Texto secundario (avisos, piso, indicación) más pequeño. Sin transparencia: blanco opaco sobre
    // RutaColor da 4,94:1 (con alfa bajaba a 4,33:1, por debajo de WCAG AA)
    private static string Pequeno(string texto) =>
        string.IsNullOrEmpty(texto) ? "" : $"<size=90%>{texto}</size>";
}
