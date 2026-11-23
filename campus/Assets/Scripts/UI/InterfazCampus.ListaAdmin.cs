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
    // Pools de filas (rendimiento): lugares, desplegables y filas de espacios interiores se reutilizan por índice;
    // sus botones se conectan una sola vez y leen el dato actual de la fila. Los objetos de los pools se anotan
    // en un HashSet para que LimpiarSalvo no los destruya (búsqueda O(1)).
    private readonly List<GameObject> poolFilasLugar = new List<GameObject>();
    private readonly List<LugaresService.Lugar> lugarDeFila = new List<LugaresService.Lugar>();
    private readonly List<BloqueServicios> poolBloques = new List<BloqueServicios>();
    private readonly List<FilaServicioUI> poolServicios = new List<FilaServicioUI>();
    private readonly HashSet<GameObject> conservarLugares = new HashSet<GameObject>();
    private readonly List<GameObject> poolFilasReporte = new List<GameObject>();
    private readonly HashSet<GameObject> conservarReportes = new HashSet<GameObject>();
    private TMP_Text avisoVersion;

    // Botón "N espacios interiores" de un lugar y las filas que despliega (cambian en cada refresco)
    private sealed class BloqueServicios
    {
        public GameObject go;
        public TMP_Text texto;
        public RectTransform flecha;
        public int lugarId;
        public readonly List<GameObject> filas = new List<GameObject>();
    }

    private sealed class FilaServicioUI
    {
        public GameObject go;
        public TMP_Text nombre, detalle;
        public LugaresService.Servicio servicio;
    }

    private void RefrescarLugaresAdmin()
    {
        if (lugares == null) return;
        if (textoCantidadLugares != null) textoCantidadLugares.text = Contar(lugares.Lugares.Count, "lugar", "lugares");
        if (contenedorLugares == null || prefabEspacio == null) return;

        CerrarConfirmacion(animar: false);   // la fila que pedía confirmación se va a reconstruir
        LimpiarSalvo(contenedorLugares, conservarLugares);
        int n = 0, nBloques = 0, nServicios = 0;
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
            if (servicios.Count > 0)
                MostrarListaServicios(l, servicios, ((RectTransform)fila.transform).sizeDelta.x, nBloques++, ref nServicios);
        }
        for (int i = n; i < poolFilasLugar.Count; i++)
        {
            lugarDeFila[i] = null;
            if (poolFilasLugar[i] != null && poolFilasLugar[i].activeSelf) poolFilasLugar[i].SetActive(false);
        }
        for (int i = nBloques; i < poolBloques.Count; i++)
        {
            poolBloques[i].filas.Clear();
            if (poolBloques[i].go.activeSelf) poolBloques[i].go.SetActive(false);
        }
        for (int i = nServicios; i < poolServicios.Count; i++)
        {
            poolServicios[i].servicio = null;
            if (poolServicios[i].go.activeSelf) poolServicios[i].go.SetActive(false);
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
        conservarLugares.Add(fila);
        return fila;
    }

    // Destruye los hijos que no son de un pool (las filas de ejemplo del diseño)
    private static void LimpiarSalvo(Transform contenedor, HashSet<GameObject> conservar)
    {
        for (int i = contenedor.childCount - 1; i >= 0; i--)
        {
            GameObject h = contenedor.GetChild(i).gameObject;
            if (!conservar.Contains(h)) Destroy(h);
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
        conservarLugares.Add(t.gameObject);
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

    // Desplegable "N espacios interiores" del lugar (bloque i del pool) y sus filas debajo (desde nServicios)
    private void MostrarListaServicios(LugaresService.Lugar lugar, List<LugaresService.Servicio> servicios, float ancho,
                                       int indiceBloque, ref int nServicios)
    {
        BloqueServicios bloque = BloqueDeServicios(indiceBloque, ancho);
        bloque.lugarId = lugar.id;
        bloque.texto.text = Contar(servicios.Count, "espacio interior", "espacios interiores");
        AnchoFila(bloque.go, ancho);
        bloque.go.transform.SetAsLastSibling();
        if (!bloque.go.activeSelf) bloque.go.SetActive(true);

        bloque.filas.Clear();
        foreach (LugaresService.Servicio s in servicios)
        {
            FilaServicioUI f = FilaDeServicio(nServicios++, ancho);
            f.servicio = s;
            string detalle = string.IsNullOrEmpty(s.piso) ? (s.categoria ?? "") : $"{s.categoria} · piso {s.piso}";
            f.nombre.text = TextoSeguro(s.nombre);
            f.detalle.text = TextoSeguro(detalle);
            AnchoFila(f.go, ancho);
            f.go.transform.SetAsLastSibling();
            bloque.filas.Add(f.go);
        }
        AplicarDesplegado(lugaresDesplegados.Contains(lugar.id), bloque.filas, bloque.flecha);
    }

    private static void AnchoFila(GameObject go, float ancho)
    {
        RectTransform rt = (RectTransform)go.transform;
        if (!Mathf.Approximately(rt.sizeDelta.x, ancho)) rt.sizeDelta = new Vector2(ancho, rt.sizeDelta.y);
    }

    // Bloque i del pool: se crea, estiliza y conecta solo la primera vez
    private BloqueServicios BloqueDeServicios(int i, float ancho)
    {
        if (i < poolBloques.Count) return poolBloques[i];
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        BloqueServicios bloque = new BloqueServicios();

        // Botón desplegable (fila propia debajo del lugar)
        Image fondo = UIRapida.Panel("EspaciosInteriores", contenedorLugares, UIRapida.FondoCampo);
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        FijarAlto(fondo.rectTransform, ancho, UIRapida.ToqueMinimo);
        Button desplegar = fondo.gameObject.AddComponent<Button>();
        desplegar.targetGraphic = fondo;
        UIRapida.EstadosBotonClaro(desplegar);   // fondo claro (FondoCampo): tinte PresionadoClaro al presionar

        Image icono = UIRapida.Panel("Icono", fondo.transform, UIRapida.Verde);
        icono.sprite = UIRapida.Icono("capas");
        icono.preserveAspect = true;
        icono.raycastTarget = false;
        UIRapida.Anclar(icono.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                        new Vector2(UIRapida.Margen, 0), new Vector2(UIRapida.TamIconoBoton, UIRapida.TamIconoBoton));

        bloque.texto = UIRapida.Texto(fondo.transform, "Texto", "", UIRapida.TamSecundario,
                                      UIRapida.Verde, fuente, true, TextAlignmentOptions.MidlineLeft);
        UIRapida.Estirar((RectTransform)bloque.texto.transform, UIRapida.Margen + UIRapida.TamIconoBoton + UIRapida.Esp3, 0, UIRapida.ToqueMinimo, 0);

        Image flecha = UIRapida.Panel("Flecha", fondo.transform, UIRapida.Verde);
        flecha.sprite = UIRapida.Icono("abajo");
        flecha.preserveAspect = true;
        flecha.raycastTarget = false;
        UIRapida.Anclar(flecha.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                        new Vector2(-UIRapida.Margen, 0), new Vector2(UIRapida.TamIconoChico, UIRapida.TamIconoChico));
        bloque.flecha = flecha.rectTransform;
        bloque.go = fondo.gameObject;
        UIRapida.AplicarFuentes(fondo.transform);

        desplegar.onClick.AddListener(() =>
        {
            bool ver = !lugaresDesplegados.Contains(bloque.lugarId);
            if (ver) lugaresDesplegados.Add(bloque.lugarId); else lugaresDesplegados.Remove(bloque.lugarId);
            AplicarDesplegado(ver, bloque.filas, bloque.flecha);
        });
        poolBloques.Add(bloque);
        conservarLugares.Add(bloque.go);
        return bloque;
    }

    private static void AplicarDesplegado(bool ver, List<GameObject> filas, RectTransform flecha)
    {
        foreach (GameObject f in filas) if (f != null && f.activeSelf != ver) f.SetActive(ver);
        if (flecha != null) flecha.localEulerAngles = new Vector3(0, 0, ver ? 180f : 0f);
    }

    // Fila i del pool de servicios: nombre y detalle a la izquierda (con sangría), Editar y Eliminar a la derecha.
    // Se crea y conecta una sola vez; los botones leen el servicio actual de la fila.
    private FilaServicioUI FilaDeServicio(int i, float ancho)
    {
        if (i < poolServicios.Count) return poolServicios[i];
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        FilaServicioUI f = new FilaServicioUI();

        Image fondo = UIRapida.Panel("Servicio", contenedorLugares, UIRapida.SuperficieOpaca);
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        fondo.raycastTarget = false;
        FijarAlto(fondo.rectTransform, ancho, AltoFilaServicio);

        // Línea menta de sangría: deja claro que pertenece al lugar de arriba
        Image guia = UIRapida.Panel("Guia", fondo.transform, UIRapida.Menta);
        guia.raycastTarget = false;
        UIRapida.Anclar(guia.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
                        new Vector2(UIRapida.Esp3, 0), new Vector2(AnchoGuia, -UIRapida.Margen));

        TMP_Text nombre = UIRapida.Texto(fondo.transform, "Nombre", "", UIRapida.TamCuerpo, UIRapida.Oscuro, fuente, true, TextAlignmentOptions.BottomLeft);
        nombre.enableAutoSizing = true;
        nombre.fontSizeMin = UIRapida.TamSecundario;
        nombre.fontSizeMax = UIRapida.TamCuerpo;
        nombre.textWrappingMode = TextWrappingModes.NoWrap;
        nombre.overflowMode = TextOverflowModes.Ellipsis;
        UIRapida.Estirar((RectTransform)nombre.transform, SangriaServicio, AltoFilaServicio * 0.5f, AnchoAccionServicio * 2 + UIRapida.Esp2, UIRapida.Esp2);
        TMP_Text sub = UIRapida.Texto(fondo.transform, "Detalle", "", UIRapida.TamSecundario, UIRapida.Gris, fuente, false, TextAlignmentOptions.TopLeft);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;
        UIRapida.Estirar((RectTransform)sub.transform, SangriaServicio, UIRapida.Esp2, AnchoAccionServicio * 2 + UIRapida.Esp2, AltoFilaServicio * 0.5f + MedioRenglon);

        Button editar = AccionServicio(fondo.transform, "Editar", "lapiz", UIRapida.Verde, fuente, -(AnchoAccionServicio + UIRapida.Esp1), AnchoAccionServicio);
        Button eliminar = AccionServicio(fondo.transform, "Eliminar", "basura", UIRapida.Rojo, fuente, -UIRapida.Esp1, AnchoAccionServicio);
        editar.onClick.AddListener(() => { if (f.servicio != null) AbrirServicioAdmin(f.servicio); });
        eliminar.onClick.AddListener(() => { if (f.servicio != null) PedirEliminarServicio(f.servicio); });
        UIRapida.AplicarFuentes(fondo.transform);

        f.go = fondo.gameObject;
        f.nombre = nombre;
        f.detalle = sub;
        f.go.SetActive(false);   // oculta hasta desplegar
        poolServicios.Add(f);
        conservarLugares.Add(f.go);
        return f;
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
        UIRapida.EstadosTransparente(b);   // PresionadoClaro: perceptible y el texto rojo/verde sigue legible

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
        if (textoResumenFecha != null) textoResumenFecha.text = $"Periodo: {Meses[hoy.Month - 1]} de {hoy.Year}";
        if (contenedorReportes == null || prefabEstadistica == null) return;

        // Vía AdminService: exige sesión (y cuenta como actividad); null = sin sesión o error (ya registrado).
        // Mismo periodo que el PDF (los primeros días del mes o sin búsquedas incluye el mes anterior)
        if (admin == null) return;
        List<RepositorioCampus.EstadisticaLugar> datos = admin.EstadisticasDelMes(hoy, out AdminService.PeriodoReporte periodo);
        if (datos == null) return;
        if (textoResumenFecha != null && !string.IsNullOrEmpty(periodo.texto)) textoResumenFecha.text = periodo.texto;

        LimpiarSalvo(contenedorReportes, conservarReportes);
        int n = 0;
        foreach (RepositorioCampus.EstadisticaLugar d in datos)
        {
            GameObject fila = FilaReporte(n++);
            if (!fila.activeSelf) fila.SetActive(true);
            fila.transform.SetAsLastSibling();
            PonerTexto(fila.transform, "Nombre", TextoSeguro(d.nombre));
            PonerTexto(fila.transform, "Categoria", TextoSeguro(d.categoria));
            PonerTexto(fila.transform, "Busquedas(mes)", d.enPeriodo.ToString());   // como el PDF
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
        conservarReportes.Add(fila);
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
            t.textWrappingMode = TextWrappingModes.NoWrap;   // una línea: lo largo termina en "…"
            t.overflowMode = TextOverflowModes.Ellipsis;
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
    private static readonly string HexGris = "#" + ColorUtility.ToHtmlStringRGB(UIRapida.Gris);
}
