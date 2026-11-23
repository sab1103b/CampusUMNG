using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Modificar mapa: construcción de la barra superior, la ayuda, los controles del mapa, las herramientas y el panel de puertas.</summary>
public partial class EditorMapa
{
    private void ConstruirInterfaz()
    {
        // ---------- Barra superior ----------
        // Misma barra que el formulario de lugar: los cambios se guardan al instante, la X solo sale
        UIRapida.BarraSuperior(transform, "Modificar mapa", fuente, Salir, out TMP_Text _);

        // ---------- Ayuda y estado ----------
        Image ayuda = UIRapida.Panel("Ayuda", transform, UIRapida.Menta);   // opaca: el contraste de SobreMenta se calcula sobre Menta
        UIRapida.Anclar(ayuda.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, -UIRapida.AltoBarraSuperior), new Vector2(0, AltoAyuda));
        ayuda.raycastTarget = false;
        textoAyuda = UIRapida.Texto(ayuda.transform, "TextoAyuda", "", UIRapida.TamSecundario, UIRapida.SobreMenta, fuente, false, TextAlignmentOptions.TopLeft);
        UIRapida.Estirar((RectTransform)textoAyuda.transform, UIRapida.Margen, AltoLineaEstado, UIRapida.Margen, UIRapida.Esp2);   // encima del estado, sin solaparse
        // Ayuda legible sobre Menta: Medium y SobreMenta opaco (el Regular a 22–26 se veía tenue y difícil de leer)
        textoAyuda.enableAutoSizing = true;
        textoAyuda.fontSizeMin = UIRapida.TamSecundario;
        textoAyuda.fontSizeMax = UIRapida.TamCuerpo;
        textoAyuda.color = UIRapida.SobreMenta;
        UIRapida.PesoMedio(textoAyuda);
        textoEstado = UIRapida.Texto(ayuda.transform, "TextoEstado", "", UIRapida.TamSecundario, UIRapida.SobreMenta, fuente, true, TextAlignmentOptions.BottomLeft);
        UIRapida.Estirar((RectTransform)textoEstado.transform, UIRapida.Margen, UIRapida.Esp2, UIRapida.Margen, AltoAyuda - AltoLineaEstado);
        textoEstado.enableAutoSizing = true;
        textoEstado.fontSizeMin = UIRapida.TamSecundario;
        textoEstado.fontSizeMax = UIRapida.TamCuerpo;
        textoEstado.color = UIRapida.SobreMenta;

        // ---------- Zoom y perspectiva (a la derecha, como en la pantalla de usuario) ----------
        // Mismo grupo de controles (UIRapida.GrupoControlesMapa) que en el mapa del usuario y en el formulario
        if (camara != null)
        {
            RectTransform zoom = UIRapida.GrupoControlesMapa(transform);
            UIRapida.BotonMapa(zoom, "BotonZoomMas", "mas", camara.ZoomMas);
            UIRapida.BotonMapa(zoom, "BotonZoomMenos", "menos", camara.ZoomMenos);
            Button p = UIRapida.BotonMapaTexto(zoom, "BotonPerspectiva", camara.Es3D ? "2D" : "3D", fuente, camara.AlternarPerspectiva);
            TMP_Text tp = UIRapida.TextoDe(p);
            alCambiarPerspectiva = tresD => { if (tp != null) tp.text = tresD ? "2D" : "3D"; };
            camara.PerspectivaCambiada += alCambiarPerspectiva;
        }

        // ---------- Herramientas (abajo): tarjeta redondeada con dos grupos ----------
        // Esp2 + (oculto + Esp2) de relleno + sección de arriba (red o modelo) + sección de edificaciones + Esp1 de espacio.
        // La barra mide lo justo para la red de caminos y crece solo mientras se edita un modelo (AjustarAltoBarra).
        const float altoBarra = AltoBarraNormal;

        Image herramientas = UIRapida.Panel("Herramientas", transform, UIRapida.SuperficieOpaca);   // hoja fija: tapa el mapa
        UIRapida.Redondear(herramientas, UIRapida.RadioTarjeta);
        UIRapida.Anclar(herramientas.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -OcultoBarra), new Vector2(0, altoBarra));
        sombraHerramientas = UIRapida.SombraDe(herramientas, UIRapida.RadioTarjeta);
        rtHerramientas = herramientas.rectTransform;
        VerticalLayoutGroup vh = herramientas.gameObject.AddComponent<VerticalLayoutGroup>();
        vh.padding = new RectOffset((int)UIRapida.Margen, (int)UIRapida.Margen, (int)UIRapida.Esp2, (int)(OcultoBarra + UIRapida.Esp2));
        vh.spacing = UIRapida.Esp1;
        vh.childControlWidth = vh.childControlHeight = true;
        vh.childForceExpandWidth = true;
        vh.childForceExpandHeight = false;

        // Red de caminos: se oculta mientras se edita un modelo 3D (su lugar lo toma la edición del modelo)
        Transform filaRed = SeccionHerramientas(herramientas.transform, "RED DE CAMINOS", out TMP_Text _);
        seccionRed = filaRed.parent.gameObject;
        AgregarHerramienta(filaRed, Herramienta.Mover, "Mover", "mover");
        AgregarHerramienta(filaRed, Herramienta.Nodo, "Nodo", "nodo");
        AgregarHerramienta(filaRed, Herramienta.Conectar, "Conectar", "conectar");
        AgregarHerramienta(filaRed, Herramienta.Puerta, "Puerta", "puerta");
        AgregarHerramienta(filaRed, Herramienta.Eliminar, "Eliminar", "eliminar");
        textoBotonEliminar = UIRapida.TextoDe(botones[Herramienta.Eliminar]);

        // ---------- Edición del modelo seleccionado (mismo espacio que la red de caminos) ----------
        // Ajustes en una rejilla de controles (RadioControl); Guardar/Descartar en su propia columna, en píldora
        Transform filaModelo1 = SeccionModelo(herramientas.transform, out Transform filaModelo2, out Transform accionesModelo);
        panelModelo = filaModelo1.parent.parent.parent.gameObject;   // Fila > Rejilla > Cuerpo > Sección

        BotonModelo(filaModelo1, "Girar -15°", () => CambiarModelo(rot: -15f));
        BotonModelo(filaModelo1, "Girar +15°", () => CambiarModelo(rot: 15f));
        BotonModelo(filaModelo1, "Más alto", () => CambiarModelo(alto: 2f));
        BotonModelo(filaModelo1, "Menos alto", () => CambiarModelo(alto: -2f));

        BotonModelo(filaModelo2, "Más ancho", () => CambiarModelo(ancho: 2f));
        BotonModelo(filaModelo2, "Menos ancho", () => CambiarModelo(ancho: -2f));
        BotonModelo(filaModelo2, "Más largo", () => CambiarModelo(largo: 2f));
        BotonModelo(filaModelo2, "Menos largo", () => CambiarModelo(largo: -2f));

        BotonAccionModelo(accionesModelo, "Guardar", () => { GuardarModelo(true); TerminarEdicionModelo(); });
        // Con cambios sin guardar pide confirmar en el modal (Rojo), como el formulario de lugar
        UIRapida.BotonSecundario(accionesModelo, "Descartar", "Descartar", PedirDescartarModelo, UIRapida.TamSecundario, fuente);
        panelModelo.SetActive(false);

        Transform filaEdif = SeccionHerramientas(herramientas.transform, "EDIFICACIONES 3D", out TMP_Text _);
        AgregarHerramienta(filaEdif, Herramienta.Cubo, "Cubo", "cubo");
        AgregarHerramienta(filaEdif, Herramienta.Rectangulo, "Rectángulo", "rectangulo");
        AgregarHerramienta(filaEdif, Herramienta.Cilindro, "Cilindro", "cilindro");

        // ---------- Elegir de qué lugar es la puerta ----------
        // Modal de lista compartido (UIRapida.ModalLista): mismo diseño que la lista de opciones del formulario de lugar,
        // con "Cancelar" abajo. Velo en la raíz del canvas (fuera de la zona segura; tocarlo cierra)
        modalPuerta = UIRapida.ModalLista(transform, "¿De qué lugar es esta puerta?", CerrarPuerta,
                                          fuente, "Cancelar", "PanelPuerta");
        panelPuerta = modalPuerta.gameObject;
        grupoPuerta = modalPuerta.Grupo;
        cajaPuerta = modalPuerta.Caja;
        tituloPuerta = modalPuerta.Titulo;
        listaPuertas = modalPuerta.Lista;
        panelPuerta.SetActive(false);

        // ---------- Modal de confirmación (descartar cambios del modelo / eliminar un modelo) ----------
        // Fábrica compartida: velo FueraDeZona + TituloModal + texto + secundario + acción en Rojo; Atrás = cancelar
        modalConfirmar = UIRapida.ModalConfirmar(transform, "", "", "Cancelar", "Eliminar", true, null, null, fuente, "ModalConfirmarEditor");
    }

    // Medidas de la barra, todas derivadas de los tokens de UIRapida (sin literales sueltos)
    private const float AltoBotonModelo = UIRapida.ToqueMinimo;                 // 96
    private const float LineaSecundaria = UIRapida.TamSecundario * 1.2f;        // una línea de TamSecundario (interlineado TMP ≈ 1,2)
    private const float LineaCuerpo = UIRapida.TamCuerpo * 1.2f;                // una línea de TamCuerpo (ayuda y estado)
    private const float AltoTituloSeccion = UIRapida.AltoEtiquetaCampo;         // 40: título en MAYÚSCULAS, como la etiqueta de un campo
    // Tarjeta de herramienta compacta: Esp1 + ícono chico + nombre (una línea) + Esp1 ≈ 100 (≥ ToqueMinimo)
    private const float AltoFilaHerramientas = UIRapida.Esp1 + UIRapida.TamIconoChico + UIRapida.Esp1 + LineaSecundaria + UIRapida.Esp1;
    // Franja Menta: línea de estado abajo (hasta dos líneas, ≈78) y tres líneas de ayuda encima (≈188 en total)
    private const float AltoLineaEstado = UIRapida.Esp2 + 2f * LineaCuerpo;
    private const float AltoAyuda = UIRapida.Esp2 + 3f * LineaCuerpo + AltoLineaEstado;
    // Ajustes del modelo: título + Esp1 + dos filas de botones separadas por Esp1 (= 248)
    private const float AltoSeccionModelo = AltoTituloSeccion + UIRapida.Esp1 + 2f * AltoBotonModelo + UIRapida.Esp1;
    private const float AltoSeccion = AltoTituloSeccion + UIRapida.Esp1 + AltoFilaHerramientas;              // ≈148
    private const float OcultoBarra = UIRapida.RadioTarjeta;   // la parte baja (esquinas redondeadas de abajo) queda fuera de pantalla
    private const float AltoBarraNormal = UIRapida.Esp2 + OcultoBarra + UIRapida.Esp2 + AltoSeccion + AltoSeccion + UIRapida.Esp1;
    private const float AltoBarraModelo = UIRapida.Esp2 + OcultoBarra + UIRapida.Esp2 + AltoSeccionModelo + AltoSeccion + UIRapida.Esp1;

    private RectTransform rtHerramientas;
    private Image sombraHerramientas;

    /// <summary>La barra crece mientras se edita un modelo (sus ajustes ocupan dos filas) y vuelve a su alto normal al terminar.</summary>
    private void AjustarAltoBarra(bool editandoModelo)
    {
        if (rtHerramientas == null) return;
        float alto = editandoModelo ? AltoBarraModelo : AltoBarraNormal;
        rtHerramientas.sizeDelta = new Vector2(rtHerramientas.sizeDelta.x, alto);
        if (sombraHerramientas != null)
        {
            RectTransform rs = sombraHerramientas.rectTransform;
            rs.sizeDelta = new Vector2(rs.sizeDelta.x, alto + 2f * UIRapida.SombraExtra);
        }
    }

    // Sección de la barra: título pequeño + una fila de herramientas (o dos filas de 96 para editar un modelo).
    // "Red de caminos" y la edición del modelo miden lo mismo, así cambiar entre ellas no mueve el resto de la barra.
    private Transform SeccionHerramientas(Transform padre, string titulo, out TMP_Text textoTitulo, bool dosFilas = false, float alto = AltoSeccion)
    {
        RectTransform seccion = UIRapida.Rect("Seccion " + (titulo.Length > 0 ? titulo : "Modelo"), padre);
        seccion.gameObject.AddComponent<LayoutElement>().preferredHeight = alto;
        VerticalLayoutGroup v = seccion.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.LowerCenter;   // el espacio sobrante queda arriba, como separación
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        textoTitulo = UIRapida.Texto(seccion, "Titulo", titulo, UIRapida.TamSecundario, UIRapida.Gris, fuente, true, TextAlignmentOptions.BottomLeft);
        textoTitulo.characterSpacing = UIRapida.EspaciadoMayusculas;   // títulos en MAYÚSCULAS
        textoTitulo.textWrappingMode = TextWrappingModes.NoWrap;
        textoTitulo.overflowMode = TextOverflowModes.Ellipsis;
        textoTitulo.gameObject.AddComponent<LayoutElement>().preferredHeight = AltoTituloSeccion;

        Transform primera = Fila(seccion, dosFilas ? AltoBotonModelo : AltoFilaHerramientas);
        if (dosFilas) Fila(seccion, AltoBotonModelo);
        return primera;
    }

    // Sección de edición del modelo: título (medidas del modelo) y un cuerpo con la rejilla de ajustes (dos filas)
    // a la izquierda y la columna de acciones (Guardar / Descartar) a la derecha. Devuelve la primera fila.
    private Transform SeccionModelo(Transform padre, out Transform fila2, out Transform acciones)
    {
        RectTransform seccion = UIRapida.Rect("Seccion Modelo", padre);
        seccion.gameObject.AddComponent<LayoutElement>().preferredHeight = AltoSeccionModelo;
        VerticalLayoutGroup v = seccion.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.LowerCenter;
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        textoModelo = UIRapida.Texto(seccion, "Titulo", "", UIRapida.TamSecundario, UIRapida.Oscuro, fuente, true, TextAlignmentOptions.BottomLeft);
        textoModelo.textWrappingMode = TextWrappingModes.NoWrap;
        textoModelo.overflowMode = TextOverflowModes.Ellipsis;
        textoModelo.gameObject.AddComponent<LayoutElement>().preferredHeight = AltoTituloSeccion;

        RectTransform cuerpo = UIRapida.Rect("Cuerpo", seccion);
        cuerpo.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f * AltoBotonModelo + UIRapida.Esp1;
        HorizontalLayoutGroup h = cuerpo.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UIRapida.Esp3;   // separa la rejilla de las acciones
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        Transform rejilla = Columna(cuerpo, "Rejilla", 4f);
        Transform fila1 = Fila(rejilla, AltoBotonModelo);
        fila2 = Fila(rejilla, AltoBotonModelo);
        acciones = Columna(cuerpo, "Acciones", 1.3f);
        return fila1;
    }

    private static Transform Columna(Transform padre, string nombre, float peso)
    {
        RectTransform col = UIRapida.Rect(nombre, padre);
        col.gameObject.AddComponent<LayoutElement>().flexibleWidth = peso;
        VerticalLayoutGroup v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = UIRapida.Esp1;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = true;
        return col;
    }

    private static Transform Fila(Transform padre, float alto)
    {
        RectTransform fila = UIRapida.Rect("Fila", padre);
        fila.gameObject.AddComponent<LayoutElement>().preferredHeight = alto;
        HorizontalLayoutGroup h = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = UIRapida.Esp1;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        return fila;
    }

    // Botón de herramienta: tarjeta redondeada con ícono dibujado y nombre
    private void AgregarHerramienta(Transform padre, Herramienta h, string texto, string icono)
    {
        Image fondo = UIRapida.Panel("Herramienta_" + h, padre, UIRapida.Menta);
        UIRapida.Redondear(fondo, UIRapida.RadioControl);
        Button b = fondo.gameObject.AddComponent<Button>();
        b.targetGraphic = fondo;
        b.onClick.AddListener(() => ElegirHerramienta(h));
        UIRapida.EstadosBotonClaro(b);   // tarjeta Menta: al presionar PresionadoClaro (Presionado dejaba el texto a 3,6:1)

        Image img = UIRapida.Panel("Icono", fondo.transform, UIRapida.SobreMenta);
        img.sprite = UIRapida.Icono(icono);
        img.preserveAspect = true;
        img.raycastTarget = false;
        UIRapida.Anclar(img.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -UIRapida.Esp1), new Vector2(UIRapida.TamIconoChico, UIRapida.TamIconoChico));

        TMP_Text t = UIRapida.Texto(fondo.transform, "Texto", texto, UIRapida.TamSecundario, UIRapida.SobreMenta, fuente, true, TextAlignmentOptions.Center);
        UIRapida.Anclar((RectTransform)t.transform, Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, UIRapida.Esp1), new Vector2(-UIRapida.Esp2, LineaSecundaria));
        t.enableAutoSizing = true;
        t.fontSizeMin = UIRapida.TamMinimo;
        t.fontSizeMax = UIRapida.TamSecundario;

        botones[h] = b;
        iconosHerramienta[h] = img;
    }

    // Acción principal de la edición del modelo (Guardar): verde, en píldora y Bold, como el resto de acciones principales
    private void BotonAccionModelo(Transform padre, string texto, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, texto, texto, UIRapida.TamSecundario, UIRapida.Verde, UIRapida.SobreColor, fuente, accion);
        UIRapida.RedondearPildora((Image)b.targetGraphic);   // radio = mitad del alto real (lo decide la columna)
    }

    // Ajuste del modelo (Girar, Más alto...): control Menta de la cuadrícula
    private void BotonModelo(Transform padre, string texto, UnityEngine.Events.UnityAction accion)
    {
        Button b = UIRapida.Boton(padre, texto, texto, UIRapida.TamSecundario, UIRapida.Menta, UIRapida.SobreMenta, fuente, accion);
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        UIRapida.EstadosBotonClaro(b);   // sobre Menta: PresionadoClaro mantiene el contraste del texto
    }
}
