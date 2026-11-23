using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary> InterfazCampus: lista de lugares y espacios interiores del administrador, y estadísticas del mes. </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Administrador: lugares
    // ==================================================================
    // Pool de filas (rendimiento): se reutilizan por índice; sus botones se conectan una sola vez y leen
    // el lugar actual de lugarDeFila[i]. Las listas de espacios interiores sí se reconstruyen (son pocas).
    private readonly List<GameObject> poolFilasLugar = new List<GameObject>();
    private readonly List<LugaresService.Lugar> lugarDeFila = new List<LugaresService.Lugar>();
    private readonly List<GameObject> poolFilasReporte = new List<GameObject>();
    private TMP_Text avisoVersion;

    private void RefrescarLugaresAdmin()
    {
        if (lugares == null) return;
        if (textoCantidadLugares != null) textoCantidadLugares.text = Contar(lugares.Lugares.Count, "lugar", "lugares");
        if (contenedorLugares == null || prefabEspacio == null) return;

        CerrarConfirmacion(animar: false);   // la fila que pedía confirmación se va a reconstruir
        LimpiarSalvo(contenedorLugares, poolFilasLugar, avisoVersion != null ? avisoVersion.gameObject : null);
        int n = 0;
        foreach (LugaresService.Lugar l in lugares.Lugares)
        {
            GameObject fila = FilaLugar(n);
            lugarDeFila[n] = l;
            n++;
            if (!fila.activeSelf) fila.SetActive(true);
            fila.transform.SetAsLastSibling();   // sus espacios interiores van justo debajo
            PonerTexto(fila.transform, "Nombre", TextoSeguro(l.nombre));
            PonerTexto(fila.transform, "Categoria", TextoSeguro((l.categoria ?? "").ToUpperInvariant()));   // etiqueta en MAYÚSCULAS

            List<LugaresService.Servicio> servicios = lugares.ServiciosDe(l.id);
            if (servicios.Count > 0) CrearListaServicios(l, servicios, (RectTransform)fila.transform);
        }
        for (int i = n; i < poolFilasLugar.Count; i++)
        {
            lugarDeFila[i] = null;
            if (poolFilasLugar[i] != null && poolFilasLugar[i].activeSelf) poolFilasLugar[i].SetActive(false);
        }
        AvisoVersionAdmin();
    }

    // Fila i del pool: se crea, estiliza y conecta solo la primera vez
    private GameObject FilaLugar(int i)
    {
        while (poolFilasLugar.Count <= i) { poolFilasLugar.Add(null); lugarDeFila.Add(null); }
        if (poolFilasLugar[i] != null) return poolFilasLugar[i];

        GameObject fila = Instantiate(prefabEspacio, contenedorLugares);
        EstilizarFilaLugar(fila.transform);
        int indice = i;
        Click(BotonEnHijo(fila.transform, "Editar"), () => { LugaresService.Lugar l = lugarDeFila[indice]; if (l != null) AbrirFormulario(l); });
        Click(BotonEnHijo(fila.transform, "Eliminar"), () => { LugaresService.Lugar l = lugarDeFila[indice]; if (l != null) PedirEliminar(l); });
        poolFilasLugar[i] = fila;
        return fila;
    }

    // Destruye los hijos que no son del pool (filas del diseño, listas de espacios interiores)
    private static void LimpiarSalvo(Transform contenedor, List<GameObject> pool, GameObject extra)
    {
        for (int i = contenedor.childCount - 1; i >= 0; i--)
        {
            GameObject h = contenedor.GetChild(i).gameObject;
            if (h != extra && !pool.Contains(h)) Destroy(h);
        }
    }

    // "1 lugar", "3 lugares", "0 lugares"
    private static string Contar(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    // Las ediciones del celular viven en su copia local de la base: una versión nueva de la app las reemplaza
    private void AvisoVersionAdmin()
    {
        if (avisoVersion != null) { avisoVersion.transform.SetAsLastSibling(); return; }   // se crea una vez
        TMP_Text t = UIRapida.Texto(contenedorLugares, "AvisoVersion",
                                    "Estos cambios se guardan solo en este celular; una actualización de la app los reemplaza.",
                                    UIRapida.TamSecundario, UIRapida.Gris, UIRapida.FuenteTexto, false, TextAlignmentOptions.Center);
        t.gameObject.AddComponent<LayoutElement>().minHeight = UIRapida.ToqueMinimo;
        UIRapida.AplicarFuentes(t.transform);
        avisoVersion = t;
    }

    // ==================================================================
    // Administrador: espacios interiores (servicios) de cada lugar
    // "Espacios interiores (N)" despliega la lista con Editar y Eliminar
    // ==================================================================
    private readonly HashSet<int> lugaresDesplegados = new HashSet<int>();   // se conserva al refrescar
    // Medidas de la fila de servicio con los tokens de UIRapida
    private const float AltoFilaServicio = UIRapida.ToqueMinimo + UIRapida.Margen;     // 132: dos renglones con aire
    private const float AnchoAccionServicio = UIRapida.ToqueMinimo + UIRapida.Esp4;    // 128: ícono + "Eliminar"
    private const float SangriaServicio = UIRapida.Esp4 + UIRapida.Esp3;               // 56: a la derecha de la línea guía
    private const float MedioRenglon = UIRapida.Esp1 / 2f;                             // separación entre nombre y detalle
    private const float AnchoGuia = UIRapida.RadioMinimo;   // línea de sangría: tan fina como una barra (RadioMinimo)

    private void CrearListaServicios(LugaresService.Lugar lugar, List<LugaresService.Servicio> servicios, RectTransform filaLugar)
    {
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        float ancho = filaLugar.sizeDelta.x;

        // Botón desplegable (fila propia debajo del lugar)
        Image fondo = UIRapida.Panel("EspaciosInteriores " + lugar.id, contenedorLugares, UIRapida.FondoCampo);
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        FijarAlto(fondo.rectTransform, ancho, UIRapida.ToqueMinimo);
        Button desplegar = fondo.gameObject.AddComponent<Button>();
        desplegar.targetGraphic = fondo;
        UIRapida.EstadosBoton(desplegar);   // fondo FondoCampo: al presionar se oscurece (sin menta)

        Image icono = UIRapida.Panel("Icono", fondo.transform, UIRapida.Verde);
        icono.sprite = UIRapida.Icono("capas");
        icono.preserveAspect = true;
        icono.raycastTarget = false;
        UIRapida.Anclar(icono.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                        new Vector2(UIRapida.Margen, 0), new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));

        TMP_Text texto = UIRapida.Texto(fondo.transform, "Texto", Contar(servicios.Count, "espacio interior", "espacios interiores"), UIRapida.TamSecundario,
                                        UIRapida.Verde, fuente, true, TextAlignmentOptions.MidlineLeft);
        UIRapida.Estirar((RectTransform)texto.transform, UIRapida.Margen + UIRapida.TamIconoBoton + UIRapida.Esp3, 0, UIRapida.ToqueMinimo, 0);

        Image flecha = UIRapida.Panel("Flecha", fondo.transform, UIRapida.Verde);
        flecha.sprite = UIRapida.Icono("abajo");
        flecha.preserveAspect = true;
        flecha.raycastTarget = false;
        UIRapida.Anclar(flecha.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                        new Vector2(-UIRapida.Margen, 0), new Vector2(UIRapida.TamIconoChico, UIRapida.TamIconoChico));

        // Filas de servicios (ocultas hasta desplegar)
        List<GameObject> filas = new List<GameObject>(servicios.Count);
        foreach (LugaresService.Servicio s in servicios)
            filas.Add(FilaServicio(s, ancho, fuente));
        UIRapida.AplicarFuentes(fondo.transform);
        foreach (GameObject f in filas) UIRapida.AplicarFuentes(f.transform);

        AplicarDesplegado(lugaresDesplegados.Contains(lugar.id), filas, flecha.rectTransform);
        desplegar.onClick.AddListener(() =>
        {
            bool ver = !lugaresDesplegados.Contains(lugar.id);
            if (ver) lugaresDesplegados.Add(lugar.id); else lugaresDesplegados.Remove(lugar.id);
            AplicarDesplegado(ver, filas, flecha.rectTransform);
        });
    }

    private static void AplicarDesplegado(bool ver, List<GameObject> filas, RectTransform flecha)
    {
        foreach (GameObject f in filas) if (f != null) f.SetActive(ver);
        if (flecha != null) flecha.localEulerAngles = new Vector3(0, 0, ver ? 180f : 0f);
    }

    // Una fila de servicio: nombre y detalle a la izquierda (con sangría), Editar y Eliminar a la derecha
    private GameObject FilaServicio(LugaresService.Servicio s, float ancho, TMP_FontAsset fuente)
    {
        Image fondo = UIRapida.Panel("Servicio " + s.id, contenedorLugares, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        fondo.raycastTarget = false;
        FijarAlto(fondo.rectTransform, ancho, AltoFilaServicio);

        // Línea menta de sangría: deja claro que pertenece al lugar de arriba
        Image guia = UIRapida.Panel("Guia", fondo.transform, UIRapida.Menta);
        guia.raycastTarget = false;
        UIRapida.Anclar(guia.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
                        new Vector2(UIRapida.Esp3, 0), new Vector2(AnchoGuia, -UIRapida.Margen));

        string detalle = string.IsNullOrEmpty(s.piso) ? (s.categoria ?? "") : $"{s.categoria} · piso {s.piso}";
        TMP_Text nombre = UIRapida.Texto(fondo.transform, "Nombre", TextoSeguro(s.nombre), UIRapida.TamCuerpo, UIRapida.Oscuro, fuente, true, TextAlignmentOptions.BottomLeft);
        nombre.enableAutoSizing = true;
        nombre.fontSizeMin = UIRapida.TamSecundario;
        nombre.fontSizeMax = UIRapida.TamCuerpo;
        nombre.textWrappingMode = TextWrappingModes.NoWrap;
        nombre.overflowMode = TextOverflowModes.Ellipsis;
        UIRapida.Estirar((RectTransform)nombre.transform, SangriaServicio, AltoFilaServicio * 0.5f, AnchoAccionServicio * 2 + UIRapida.Esp2, UIRapida.Esp2);
        TMP_Text sub = UIRapida.Texto(fondo.transform, "Detalle", TextoSeguro(detalle), UIRapida.TamSecundario, UIRapida.Gris, fuente, false, TextAlignmentOptions.TopLeft);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;
        UIRapida.Estirar((RectTransform)sub.transform, SangriaServicio, UIRapida.Esp2, AnchoAccionServicio * 2 + UIRapida.Esp2, AltoFilaServicio * 0.5f + MedioRenglon);

        Button editar = AccionServicio(fondo.transform, "Editar", "lapiz", UIRapida.Verde, fuente, -(AnchoAccionServicio + UIRapida.Esp1), AnchoAccionServicio);
        Button eliminar = AccionServicio(fondo.transform, "Eliminar", "basura", UIRapida.Rojo, fuente, -UIRapida.Esp1, AnchoAccionServicio);
        LugaresService.Servicio servicio = s;
        editar.onClick.AddListener(() => AbrirServicioAdmin(servicio));
        eliminar.onClick.AddListener(() => PedirEliminarServicio(servicio));
        return fondo.gameObject;
    }

    // Botón de acción con ícono de 56 arriba y texto abajo (área táctil AnchoAccionServicio × 116)
    private static Button AccionServicio(Transform padre, string texto, string icono, Color color, TMP_FontAsset fuente,
                                         float x, float ancho)
    {
        Image area = UIRapida.Panel(texto, padre, Color.white);   // invisible salvo al presionar (tinte)
        UIRapida.Anclar(area.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                        new Vector2(x, 0), new Vector2(ancho, AltoFilaServicio - UIRapida.Esp2));
        Button b = area.gameObject.AddComponent<Button>();
        b.targetGraphic = area;
        ColorBlock cb = b.colors;
        cb.normalColor = cb.highlightedColor = cb.selectedColor = UIRapida.Transparente;
        cb.pressedColor = UIRapida.PresionadoClaro;   // verde muy claro: perceptible y el texto rojo/verde sigue legible
        cb.colorMultiplier = 1f;
        cb.fadeDuration = UIRapida.DuracionFundido;
        b.colors = cb;

        Image ic = UIRapida.Panel("Icono", area.transform, color);
        ic.sprite = UIRapida.Icono(icono);
        ic.preserveAspect = true;
        ic.raycastTarget = false;
        UIRapida.Anclar(ic.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                        new Vector2(0, -UIRapida.Esp1), new Vector2(TamanoIconoAccion, TamanoIconoAccion));

        TMP_Text t = UIRapida.Texto(area.transform, "Texto", texto, UIRapida.TamSecundario, color, fuente, true, TextAlignmentOptions.Bottom);
        t.enableAutoSizing = true;
        t.fontSizeMin = UIRapida.TamMinimo;
        t.fontSizeMax = UIRapida.TamSecundario;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        UIRapida.Estirar((RectTransform)t.transform, 0, UIRapida.Esp1, 0, TamanoIconoAccion + UIRapida.Esp1);
        return b;
    }

    // Alto fijo que respetan tanto el layout de la lista como un contenedor sin layout
    private static void FijarAlto(RectTransform rt, float ancho, float alto)
    {
        LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = alto;
        rt.sizeDelta = new Vector2(ancho, alto);
    }

    private void AbrirServicioAdmin(LugaresService.Servicio s)
    {
        if (formularioMapa == null) { ui.Avisar("No se pudo abrir el formulario del espacio interior.", true); return; }
        CerrarConfirmacion(animar: false);
        formularioMapa.AbrirServicio(s);
        ui.Ir(PFormulario);
    }

    // Eliminar es irreversible: siempre con modal de confirmación (Atrás = Cancelar)
    private void PedirEliminarServicio(LugaresService.Servicio s)
    {
        if (admin == null || s == null) return;
        PedirConfirmacion($"¿Eliminar «{TextoSeguro(s.nombre)}»?",
                          "Se borra este espacio interior. No se puede deshacer.",
                          () => admin.EliminarServicio(s.id, (ok, msg) => { if (ui != null) ui.Avisar(ok ? $"«{TextoSeguro(s.nombre)}» eliminado." : msg, !ok); }));
    }

    private void PedirEliminar(LugaresService.Lugar lugar)
    {
        if (admin == null || lugar == null) return;
        PedirConfirmacion($"¿Eliminar «{TextoSeguro(lugar.nombre)}»?",
                          "También se borran sus espacios interiores y su historial de búsquedas. No se puede deshacer.",
                          () => admin.EliminarLugar(lugar.id, (ok, msg) => { if (ui != null) ui.Avisar(ok ? $"«{TextoSeguro(lugar.nombre)}» eliminado." : msg, !ok); }));
    }

    // ==================================================================
    // Administrador: reportes
    // ==================================================================
    private void RefrescarReportes()
    {
        if (!BaseDatosLocal.Lista) return;

        DateTime hoy = DateTime.Now;
        if (textoResumenFecha != null) textoResumenFecha.text = $"Resumen mensual · {Meses[hoy.Month - 1]} de {hoy.Year}";
        if (contenedorReportes == null || prefabEstadistica == null) return;

        // Vía AdminService: exige sesión (y cuenta como actividad); null = sin sesión o error (ya registrado)
        if (admin == null) return;
        List<RepositorioCampus.EstadisticaLugar> datos = admin.EstadisticasDelMes(hoy);
        if (datos == null) return;

        LimpiarSalvo(contenedorReportes, poolFilasReporte, null);
        int n = 0;
        foreach (RepositorioCampus.EstadisticaLugar d in datos)
        {
            GameObject fila = FilaReporte(n++);
            if (!fila.activeSelf) fila.SetActive(true);
            fila.transform.SetAsLastSibling();
            PonerTexto(fila.transform, "Nombre", TextoSeguro(d.nombre));
            PonerTexto(fila.transform, "Categoria", TextoSeguro(d.categoria));
            PonerTexto(fila.transform, "Busquedas(mes)", d.mesActual.ToString());
            PonerTexto(fila.transform, "Tendencias", Tendencia(d.mesActual, d.mesAnterior));
        }
        for (int i = n; i < poolFilasReporte.Count; i++)
            if (poolFilasReporte[i] != null && poolFilasReporte[i].activeSelf) poolFilasReporte[i].SetActive(false);
    }

    // Fila i del reporte: se crea y estiliza solo la primera vez
    private GameObject FilaReporte(int i)
    {
        while (poolFilasReporte.Count <= i) poolFilasReporte.Add(null);
        if (poolFilasReporte[i] != null) return poolFilasReporte[i];
        GameObject fila = Instantiate(prefabEstadistica, contenedorReportes);
        UIRapida.AplicarFuentes(fila.transform);
        EstilizarFilaReporte(fila.transform);
        poolFilasReporte[i] = fila;
        return fila;
    }

    // Fila del reporte (el prefab trae 40 y negro): nombre Oscuro TamCuerpo Bold; categoría, número y tendencia
    // Gris TamSecundario (la tendencia conserva su color en el texto enriquecido)
    private static void EstilizarFilaReporte(Transform fila)
    {
        TMP_Text nombre = TextoEnHijo(fila, "Nombre");
        if (nombre != null)
        {
            nombre.enableAutoSizing = false;
            nombre.fontSize = UIRapida.TamCuerpo;
            nombre.color = UIRapida.Oscuro;
            UIRapida.Peso(nombre, true);
            nombre.textWrappingMode = TextWrappingModes.NoWrap;
            nombre.overflowMode = TextOverflowModes.Ellipsis;
        }
        foreach (string hijo in new[] { "Categoria", "Busquedas(mes)", "Tendencias" })
        {
            TMP_Text t = TextoEnHijo(fila, hijo);
            if (t == null) continue;
            t.enableAutoSizing = false;
            t.fontSize = UIRapida.TamSecundario;
            t.color = UIRapida.Gris;
            UIRapida.Peso(t, false);
        }
    }

    private static string Tendencia(int actual, int anterior)
    {
        int dif = actual - anterior;
        if (anterior == 0 && actual > 0) return $"<color={HexVerde}>Nuevo</color>";
        if (dif > 0) return $"<color={HexVerde}>Sube +{dif}</color>";
        if (dif < 0) return $"<color={HexRojo}>Baja {dif}</color>";
        return $"<color={HexGris}>Igual</color>";
    }

    // Colores de la paleta (UIRapida) para texto enriquecido
    private static readonly string HexVerde = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Verde);
    private static readonly string HexRojo = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Rojo);
}
