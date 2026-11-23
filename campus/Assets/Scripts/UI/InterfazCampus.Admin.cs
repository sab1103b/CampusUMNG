using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary> InterfazCampus: estilo del panel de administración, reporte PDF, formulario y editor del mapa. </summary>
public partial class InterfazCampus
{
    // ==================================================================
    // Panel de administración: encabezado, pestañas, botones y filas de lugares
    // ==================================================================
    private GameObject panelGestion;
    private TMP_Text textoTabLugares, textoTabReportes;
    private Image barraTabLugares, barraTabReportes;

    private void EstilizarPanelAdmin()
    {
        if (btnSalirAdmin == null) return;
        Canvas canvas = btnSalirAdmin.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Transform raiz = canvas.rootCanvas.transform;
        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;

        // El encabezado y el título "Panel de administración UMNG" se dejan como en el diseño original

        // Salir: círculo blanco con ícono rojo
        Image imgSalir = btnSalirAdmin.targetGraphic as Image;
        if (imgSalir == null) imgSalir = btnSalirAdmin.GetComponent<Image>();
        if (imgSalir != null && btnSalirAdmin.transform.Find("IconoSalir") == null)
        {
            imgSalir.sprite = UIRapida.Circulo();
            imgSalir.color = Color.white;
            imgSalir.preserveAspect = true;
            Image ic = UIRapida.Panel("IconoSalir", btnSalirAdmin.transform, UIRapida.Rojo);
            ic.sprite = UIRapida.Icono("salir");
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIRapida.Estirar(ic.rectTransform, UIRapida.Esp2, UIRapida.Esp2, UIRapida.Esp2, UIRapida.Esp2);
        }

        // ---------- Pestañas: fondo blanco; la activa en verde con línea redondeada ----------
        if (tabLugares != null)
        {
            Transform barraTabs = tabLugares.transform.parent != null ? tabLugares.transform.parent.parent : null;
            Image fondoTabs = barraTabs != null ? barraTabs.GetComponent<Image>() : null;
            if (fondoTabs != null) fondoTabs.color = Color.white;

            textoTabLugares = UIRapida.TextoDe(tabLugares);
            barraTabLugares = BarraDePestana(tabLugares);
        }
        if (tabReportes != null)
        {
            textoTabReportes = UIRapida.TextoDe(tabReportes);
            barraTabReportes = BarraDePestana(tabReportes);
        }
        foreach (Image b in new[] { barraTabLugares, barraTabReportes })
        {
            if (b == null) continue;
            UIRapida.Redondear(b, UIRapida.RadioMinimo);
            b.color = UIRapida.Verde;
        }
        if (tabLugares != null) tabLugares.onClick.AddListener(ActualizarPestanas);
        if (tabReportes != null) tabReportes.onClick.AddListener(ActualizarPestanas);
        if (contenedorLugares != null)
        {
            Transform t = contenedorLugares;
            while (t != null && t.name != "GestionLugaresPanel") t = t.parent;
            panelGestion = t != null ? t.gameObject : null;
        }
        ActualizarPestanas();

        // ---------- Botones: agregar (principal) y modificar mapa (secundario) ----------
        if (btnAgregarLugar != null)
        {
            EstiloBotonPrincipal(btnAgregarLugar, fuente);
            TMP_Text t = UIRapida.TextoDe(btnAgregarLugar);
            if (t != null) t.text = "Agregar lugar";   // tipo oración, como todos los botones
            IconoEnBoton(btnAgregarLugar, "mas", Color.white);
        }
        if (btnModificarMapa != null)
        {
            EstiloBotonPrincipal(btnModificarMapa, fuente);
            ((Image)btnModificarMapa.targetGraphic).color = UIRapida.Menta;
            TMP_Text t = UIRapida.TextoDe(btnModificarMapa);
            if (t != null) t.color = UIRapida.SobreMenta;   // sobre Menta
            PesoBotonSecundario(btnModificarMapa);
            IconoEnBoton(btnModificarMapa, "mapa", UIRapida.SobreMenta);

            // Más cerca de "Agregar": su fila deja de crecer y el botón se pega arriba
            Transform fila = btnModificarMapa.transform.parent != null ? btnModificarMapa.transform.parent.parent : null;
            LayoutElement le = fila != null ? fila.GetComponent<LayoutElement>() : null;
            if (le != null)
            {
                le.preferredHeight = UIRapida.ToqueMinimo + UIRapida.Esp3;   // botón táctil + su margen inferior
                le.flexibleHeight = 0f;
            }
            RectTransform rb = (RectTransform)btnModificarMapa.transform;
            rb.offsetMin = new Vector2(rb.offsetMin.x, UIRapida.Esp3);    // margen abajo
            rb.offsetMax = new Vector2(rb.offsetMax.x, 0f);               // sin margen arriba (pegado a "Agregar")
        }

        // ---------- "Lugares del campus" (CorregirTextosEscena cambia "Espacios") + conteo como etiqueta ----------
        Transform guion = BuscarHijo(raiz, "NumeroLugares");
        if (guion != null) guion.gameObject.SetActive(false);   // el "-" suelto del diseño
        foreach (TMP_Text t in raiz.GetComponentsInChildren<TMP_Text>(true))
            if (t.text.Trim() == "Lugares del campus") { t.fontStyle = FontStyles.Bold; t.color = UIRapida.Oscuro; }

        if (textoCantidadLugares != null && textoCantidadLugares.transform.parent.Find("FondoConteo") == null)
        {
            Image fondoConteo = UIRapida.Panel("FondoConteo", textoCantidadLugares.transform.parent, UIRapida.Menta);
            UIRapida.Redondear(fondoConteo, UIRapida.RadioEtiqueta);
            fondoConteo.raycastTarget = false;
            RectTransform rc = fondoConteo.rectTransform, rt = (RectTransform)textoCantidadLugares.transform;
            rc.anchorMin = rt.anchorMin; rc.anchorMax = rt.anchorMax; rc.pivot = rt.pivot;
            rc.anchoredPosition = rt.anchoredPosition; rc.sizeDelta = rt.sizeDelta;
            fondoConteo.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            fondoConteo.transform.SetSiblingIndex(textoCantidadLugares.transform.GetSiblingIndex());
            textoCantidadLugares.color = UIRapida.SobreMenta;
            textoCantidadLugares.fontStyle = FontStyles.Bold;
            textoCantidadLugares.alignment = TextAlignmentOptions.Center;
        }

        // ---------- Lista: sin fondo gris, filas separadas ----------
        if (contenedorLugares != null)
        {
            ScrollRect sr = contenedorLugares.GetComponentInParent<ScrollRect>(true);
            Image fondoLista = sr != null ? sr.GetComponent<Image>() : null;
            if (fondoLista != null) fondoLista.color = UIRapida.Transparente;
            VerticalLayoutGroup vl = contenedorLugares.GetComponent<VerticalLayoutGroup>();
            if (vl != null) { vl.spacing = UIRapida.Esp2; vl.padding = Relleno(UIRapida.Esp1, UIRapida.Esp1, UIRapida.Esp1, UIRapida.Esp2); }
        }
    }

    // Línea inferior de una pestaña (hermana "Barra" del botón)
    private static Image BarraDePestana(Button tab)
    {
        Transform padre = tab != null ? tab.transform.parent : null;
        Transform barra = padre != null ? padre.Find("Barra") : null;
        return barra != null ? barra.GetComponent<Image>() : null;
    }

    // Texto dentro de un hijo con ese nombre (o null)
    private static TMP_Text TextoEnHijo(Transform raiz, string hijo)
    {
        Transform t = BuscarHijo(raiz, hijo);
        return t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static Button BotonEnHijo(Transform raiz, string hijo)
    {
        Transform t = BuscarHijo(raiz, hijo);
        return t != null ? t.GetComponent<Button>() : null;
    }

    // La pestaña visible en verde y negrita con su línea; la otra en gris
    private void ActualizarPestanas()
    {
        bool lugares = panelGestion == null || panelGestion.activeSelf;
        if (textoTabLugares != null)
        {
            textoTabLugares.color = lugares ? UIRapida.Verde : UIRapida.Gris;
            UIRapida.Peso(textoTabLugares, lugares);   // una sola familia: solo cambia FontStyles.Bold
        }
        if (textoTabReportes != null)
        {
            textoTabReportes.color = lugares ? UIRapida.Gris : UIRapida.Verde;
            UIRapida.Peso(textoTabReportes, !lugares);
        }
    }

    // Fila de un lugar: tarjeta redondeada, ícono, nombre en negrita, categoría como etiqueta, íconos de acción
    private static void EstilizarFilaLugar(Transform fila)
    {
        Image fondo = fila.GetComponent<Image>();
        if (fondo != null) { UIRapida.Redondear(fondo, UIRapida.RadioControl); fondo.color = UIRapida.SuperficieOpaca; }

        // Sin imagen decorativa a la izquierda: el nombre y la categoría usan ese espacio
        Transform deco = fila.Find("Image");
        if (deco != null) deco.gameObject.SetActive(false);

        TMP_Text nombre = TextoEnHijo(fila, "Nombre");
        if (nombre != null)
        {
            nombre.fontStyle = FontStyles.Bold;
            nombre.color = UIRapida.Oscuro;
            nombre.enableAutoSizing = true;
            nombre.fontSizeMin = UIRapida.TamSecundario;
            nombre.fontSizeMax = UIRapida.TamSubtitulo;
        }
        TMP_Text categoria = TextoEnHijo(fila, "Categoria");
        if (categoria != null)
        {
            // El texto ya llega en MAYÚSCULAS y con TextoSeguro (RefrescarLugaresAdmin)
            categoria.fontStyle = FontStyles.Bold;
            categoria.color = UIRapida.Verde;
            categoria.characterSpacing = UIRapida.EspaciadoMayusculas;   // etiqueta en MAYÚSCULAS
            categoria.enableAutoSizing = true;
            categoria.fontSizeMin = UIRapida.TamMinimo;
            categoria.fontSizeMax = UIRapida.TamSecundario;
        }

        AccionFila(BuscarHijo(fila, "Editar"), "lapiz", UIRapida.Verde);
        AccionFila(BuscarHijo(fila, "Eliminar"), "basura", UIRapida.Rojo);
        UIRapida.AplicarFuentes(fila);   // fila creada después del Start
    }

    // Editar / Eliminar: ícono de acción (56, decisión del dueño), centrado en su espacio, con el color de su texto
    private const float TamanoIconoAccion = UIRapida.TamIconoBoton;

    private static void AccionFila(Transform accion, string icono, Color color)
    {
        if (accion == null) return;
        Transform espacio = accion.Find("Imagen");
        Image img = espacio != null ? espacio.GetComponent<Image>() : null;
        if (img != null && espacio.Find("Icono") == null)
        {
            img.sprite = null;
            img.color = UIRapida.Transparente;   // el espacio del diseño queda, pero vacío
            Image ic = UIRapida.Panel("Icono", espacio, color);
            ic.sprite = UIRapida.Icono(icono);
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIRapida.Anclar(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            Vector2.zero, new Vector2(TamanoIconoAccion, TamanoIconoAccion));
        }
        TMP_Text t = accion.GetComponentInChildren<TMP_Text>(true);
        if (t != null)
        {
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true;
            t.fontSizeMin = UIRapida.TamMinimo;
            t.fontSizeMax = UIRapida.TamSecundario;
        }
    }

    private bool generandoPdf;   // bloquea el doble toque mientras se genera (se libera en OnDisable)
    private string textoBotonPdf;

    private IEnumerator GenerarPdf()
    {
        if (generandoPdf || admin == null) yield break;
        generandoPdf = true;
        textoBotonPdf = PonerProgreso(btnPdf, "Generando reporte…");
        if (textoEstadoPdf != null) textoEstadoPdf.text = "Generando reporte…";
        yield return null;   // se dibuja el estado antes del trabajo (es síncrono)

        try
        {
            admin.GenerarReportePdf((ok, resultado) =>
            {
                // AdminService devuelve un texto corto y amigable (dónde quedó o qué falló): se muestra tal cual
                if (textoEstadoPdf != null) textoEstadoPdf.text = resultado;
                if (ui != null) ui.Avisar(resultado, !ok);
            });
        }
        finally
        {
            QuitarProgreso(btnPdf, textoBotonPdf);
            textoBotonPdf = null;
            generandoPdf = false;
        }
    }

    // ==================================================================
    // Formulario de lugar
    // ==================================================================
    private void AbrirFormulario(LugaresService.Lugar l)
    {
        // Formulario nuevo (con mapa). El de texto queda solo como respaldo si no se pudo crear.
        if (formularioMapa != null)
        {
            formularioMapa.Abrir(l);
            ui.Ir(PFormulario);
            return;
        }

        lugarEditando = l != null ? l.id : 0;
        if (tituloFormulario != null) tituloFormulario.text = l != null ? "Editar lugar" : "Nuevo lugar";

        Poner(fNombre, l?.nombre);
        Poner(fCategoria, l?.categoria);
        Poner(fDescripcion, l?.descripcion);
        Poner(fIndicacion, l?.indicacion);
        Poner(fHorario, l?.horario);
        Poner(fLatitud, l != null ? l.latitud.ToString("0.000000", CultureInfo.InvariantCulture) : "");
        Poner(fLongitud, l != null ? l.longitud.ToString("0.000000", CultureInfo.InvariantCulture) : "");
        Mostrar(formError, false);

        ui.Ir(PFormulario);
    }

    private void UsarMiUbicacion()
    {
        if (ubicacion == null || !ubicacion.TieneUbicacion)
        {
            Error(formError, ubicacion != null && !string.IsNullOrEmpty(ubicacion.MensajeEstado) ? ubicacion.MensajeEstado : "Todavía no hay ubicación del GPS.");
            return;
        }
        Poner(fLatitud, ubicacion.UbicacionReal.x.ToString("0.000000", CultureInfo.InvariantCulture));
        Poner(fLongitud, ubicacion.UbicacionReal.y.ToString("0.000000", CultureInfo.InvariantCulture));
        Mostrar(formError, false);
    }

    private void GuardarLugar()
    {
        if (!LeerNumero(fLatitud, out double lat) || !LeerNumero(fLongitud, out double lon))
        {
            Error(formError, "Escribe la latitud y la longitud como números (ej. 4.942603 y -74.010816).");
            return;
        }

        AdminService.LugarEditable datos = new AdminService.LugarEditable
        {
            nombre = Texto(fNombre),
            categoria = Texto(fCategoria),
            descripcion = Texto(fDescripcion),
            indicacion = Texto(fIndicacion),
            horario = Texto(fHorario),
            latitud = lat,
            longitud = lon,
        };

        if (admin == null) { Error(formError, "No se pudo guardar el lugar."); return; }
        UIRapida.EstadoDeshabilitado(btnGuardarLugar, true);   // estado deshabilitado de la app mientras guarda
        Action<bool, string> alTerminar = (ok, mensaje) =>
        {
            UIRapida.EstadoDeshabilitado(btnGuardarLugar, false);
            if (!ok) { Error(formError, mensaje); return; }
            if (ui == null) return;
            ui.Cerrar(PFormulario);
            ui.Avisar($"«{TextoSeguro(datos.nombre)}» guardado.");
        };

        if (lugarEditando > 0) admin.ActualizarLugar(lugarEditando, datos, alTerminar);
        else admin.CrearLugar(datos, alTerminar);
    }

    // ==================================================================
    // Modificar mapa (administrador): nodos, puertas y edificaciones 3D
    // ==================================================================
    private void PrepararEditorMapa()
    {
        if (ui == null || admin == null || lugares == null) return;

        Mapbox.Unity.Map.AbstractMap mapa = FindFirstObjectByType<Mapbox.Unity.Map.AbstractMap>();
        CamaraMapa camara = FindFirstObjectByType<CamaraMapa>();
        if (mapa == null || camara == null) { Debug.LogWarning("Modificar mapa: falta el mapa o la cámara del mapa."); return; }

        TMP_FontAsset fuente = fichaNombre != null ? fichaNombre.font : TMP_Settings.defaultFontAsset;
        EditorMapa editor = EditorMapa.Crear(transform, ui, admin, lugares, mapa, modelosMapa, camara, fuente,
                                             ubicacion != null && ubicacion.MapaListo);

        ui.RegistrarPantalla(new UIManager.Pantalla
        {
            nombre = EditorMapa.NombrePantalla,
            panel = editor.gameObject,
            requiereAdmin = true,
        });

        // "Nuevo lugar / Editar lugar" con el mapa de fondo: reemplaza el formulario de texto del diseño
        formularioMapa = FormularioLugarMapa.Crear(transform, PFormulario, ui, admin, lugares, mapa, camara, fuente,
                                                   ubicacion != null && ubicacion.MapaListo);
        ui.RegistrarPantalla(new UIManager.Pantalla
        {
            nombre = PFormulario,
            panel = formularioMapa.gameObject,
            requiereAdmin = true,   // no es superpuesta: oculta el panel de administración para que se vea el mapa
        });

        // Botón "Modificar mapa" en Gestión de lugares, con el mismo estilo que "Agregar lugar"
        if (btnAgregarLugar == null) return;
        Transform filaAgregar = btnAgregarLugar.transform.parent != null ? btnAgregarLugar.transform.parent.parent : null;
        if (filaAgregar == null || filaAgregar.parent == null) return;

        GameObject fila = Instantiate(filaAgregar.gameObject, filaAgregar.parent);
        fila.name = "OpcionModificarMapa";
        fila.transform.SetSiblingIndex(filaAgregar.GetSiblingIndex() + 1);
        btnModificarMapa = fila.GetComponentInChildren<Button>(true);
        if (btnModificarMapa == null) { Destroy(fila); return; }

        btnModificarMapa.onClick.RemoveAllListeners();
        TMP_Text t = btnModificarMapa.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = "Modificar mapa";
        btnModificarMapa.onClick.AddListener(() => ui.Ir(EditorMapa.NombrePantalla));
    }
}
