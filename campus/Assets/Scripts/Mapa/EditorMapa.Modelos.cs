using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Modificar mapa: crear, seleccionar, ajustar, guardar y eliminar las edificaciones 3D.</summary>
public partial class EditorMapa
{
    // ==================================================================
    // Modelos 3D
    // ==================================================================
    private static string TipoDe(Herramienta h) =>
        h == Herramienta.Cubo ? "cubo" : h == Herramienta.Rectangulo ? "rectangulo" : "cilindro";

    private static string NombreTipo(string tipo) =>
        tipo == "cubo" ? "cubo" : tipo == "rectangulo" ? "rectángulo" : "cilindro";

    private void CrearModelo(string tipo, Vector3 suelo)
    {
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        AdminService.ModeloEditable m = new AdminService.ModeloEditable
        {
            tipo = tipo,
            latitud = geo.x,
            longitud = geo.y,
            rotacion = 0f,
            ancho = tipo == "rectangulo" ? 24f : tipo == "cilindro" ? 10f : 12f,
            largo = tipo == "rectangulo" ? 12f : tipo == "cilindro" ? 10f : 12f,
            alto = tipo == "rectangulo" ? 8f : 12f,
        };

        Operar($"Agregando {NombreTipo(tipo)}…");
        admin.CrearModelo(m, (ok, resp) =>
        {
            Terminar(ok, resp, ok ? $"{char.ToUpper(NombreTipo(tipo)[0])}{NombreTipo(tipo).Substring(1)} agregado. Ajústalo abajo." : null);
            if (!ok) return;
            int nuevo = LeerId(resp);
            ElegirHerramienta(Herramienta.Mover);
            // La recarga ya pudo redibujar los modelos: si el nuevo ya está, se selecciona; si no, al aparecer
            ModeloCampus creado = modelos != null && nuevo != 0 ? modelos.Buscar(nuevo) : null;
            if (creado != null) SeleccionarModelo(creado, guardarAnterior: false);
            else seleccionarModeloAlRedibujar = nuevo;
        });
    }

    private void AlRedibujarModelos()
    {
        if (!isActiveAndEnabled) return;
        // Cambios sin guardar del modelo en edición: se conservan sobre el objeto nuevo
        LugaresService.Modelo pendiente = modeloCambiado ? trabajoModelo : null;
        int id = seleccionarModeloAlRedibujar != 0 ? seleccionarModeloAlRedibujar : idModeloSel;
        seleccionarModeloAlRedibujar = 0;
        modeloSel = null;   // el objeto anterior se destruyó al redibujar
        idModeloSel = 0;
        originalModelo = trabajoModelo = null;
        modeloCambiado = false;
        if (id != 0)
        {
            ModeloCampus mc = modelos != null ? modelos.Buscar(id) : null;
            if (mc != null)
            {
                SeleccionarModelo(mc, guardarAnterior: false);
                if (pendiente != null && pendiente.id == id)
                {
                    trabajoModelo = pendiente;
                    mc.datos = pendiente;
                    modeloCambiado = true;
                    modelos.Ubicar(mc);
                    ActualizarTextoModelo();
                }
            }
            else MostrarSeccionModelo(false);
        }
        ColorearModelosEliminar();
        RestaurarBorradorModelo();
    }

    private void SeleccionarModelo(ModeloCampus mc, bool guardarAnterior = true)
    {
        if (mc == null || mc.datos == null || modeloSel == mc) return;
        int id = mc.datos.id;
        if (guardarAnterior && modeloSel != null)
        {
            GuardarModelo(false);
            // Guardar recarga la base y redibuja los modelos: hay que buscar otra vez el que se tocó
            if (modelos != null) mc = modelos.Buscar(id);
            if (mc == null || mc.datos == null) return;
        }
        DeseleccionarModelo();

        // Se edita una copia: los datos compartidos solo cambian si Guardar sale bien
        modeloSel = mc;
        idModeloSel = id;
        originalModelo = mc.datos;
        trabajoModelo = Copia(originalModelo);
        mc.datos = trabajoModelo;
        modeloCambiado = false;
        mc.Resaltar(true, ColorModeloSel);
        MostrarSeccionModelo(true);
        ActualizarTextoModelo();
        Ayuda("Ajusta el modelo con los botones. Toca el mapa para <b>moverlo</b> ahí. Toca <b>Guardar</b> para dejar los cambios en la base de datos.");
    }

    private void DeseleccionarModelo()
    {
        if (modeloSel != null)
        {
            // Lo que no se guardó se pierde: el modelo vuelve a sus datos originales
            if (originalModelo != null)
            {
                modeloSel.datos = originalModelo;
                if (modeloCambiado && modelos != null) modelos.Ubicar(modeloSel);
            }
            modeloSel.Resaltar(false, ColorModeloSel);
        }
        modeloSel = null;
        idModeloSel = 0;
        originalModelo = trabajoModelo = null;
        modeloCambiado = false;
        MostrarSeccionModelo(false);
    }

    // En la barra: edición del modelo en lugar de "Red de caminos", o al revés
    private void MostrarSeccionModelo(bool editando)
    {
        if (panelModelo != null) panelModelo.SetActive(editando);
        if (seccionRed != null) seccionRed.SetActive(!editando);
        AjustarAltoBarra(editando);
    }

    // Guardar o Descartar: se suelta el modelo y vuelve la red de caminos
    private void TerminarEdicionModelo()
    {
        DeseleccionarModelo();
        ElegirHerramienta(Herramienta.Mover);
    }

    private void CambiarModelo(float rot = 0f, float ancho = 0f, float largo = 0f, float alto = 0f)
    {
        if (modeloSel == null || trabajoModelo == null) return;
        LugaresService.Modelo d = trabajoModelo;

        d.rotacion = Mathf.Repeat(d.rotacion + rot, 360f);
        if (d.tipo == "cubo" || d.tipo == "cilindro")
        {
            // El cubo y el cilindro conservan la base pareja (cuadrada o redonda)
            float lado = Mathf.Clamp(d.ancho + ancho + largo, 2f, 300f);
            d.ancho = d.largo = lado;
        }
        else
        {
            d.ancho = Mathf.Clamp(d.ancho + ancho, 2f, 300f);
            d.largo = Mathf.Clamp(d.largo + largo, 2f, 300f);
        }
        d.alto = Mathf.Clamp(d.alto + alto, 2f, 200f);

        modeloCambiado = true;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
    }

    private void MoverModeloA(Vector3 suelo)
    {
        if (modeloSel == null || trabajoModelo == null) return;
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        trabajoModelo.latitud = geo.x;
        trabajoModelo.longitud = geo.y;
        modeloCambiado = true;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
    }

    private void ActualizarTextoModelo()
    {
        if (modeloSel == null || trabajoModelo == null || textoModelo == null) return;
        LugaresService.Modelo d = trabajoModelo;
        string medidas = d.tipo == "cilindro" ? $"Ø {d.ancho:0} m" : $"{d.ancho:0} × {d.largo:0} m";
        textoModelo.text = $"{char.ToUpper(NombreTipo(d.tipo)[0])}{NombreTipo(d.tipo).Substring(1)} · {medidas} · {d.alto:0} m de alto · {d.rotacion:0}°" +
                           (modeloCambiado ? $"  <color={ColorSinGuardar}>(sin guardar)</color>" : "");
    }

    // Guarda la copia en edición. Si falla, el modelo vuelve a como estaba y se avisa.
    // "avisoGlobal": mensaje para el UIManager si se guardó (al salir del editor el texto de estado ya no se ve).
    private void GuardarModelo(bool avisar, string avisoGlobal = null)
    {
        if (modeloSel == null || trabajoModelo == null) return;
        if (!modeloCambiado) { if (avisar) Estado("No hay cambios por guardar."); return; }

        LugaresService.Modelo d = trabajoModelo;
        LugaresService.Modelo original = originalModelo;
        int id = d.id;
        modeloCambiado = false;
        ActualizarTextoModelo();

        Operar("Guardando modelo…");
        admin.ActualizarModelo(id, new AdminService.ModeloEditable
        {
            tipo = d.tipo, nombre = d.nombre, latitud = d.latitud, longitud = d.longitud,
            rotacion = d.rotacion, ancho = d.ancho, largo = d.largo, alto = d.alto,
        }, (ok, resp) =>
        {
            if (ok)
            {
                if (original != null) CopiarValores(d, original);   // ya está en la base de datos
                Terminar(true, resp, "Modelo guardado.");
                if (avisoGlobal != null && ui != null) ui.Avisar(avisoGlobal);
                return;
            }

            // Falló: se restaura el original (en el objeto que esté en el mapa ahora).
            // Si fue porque venció la sesión, los cambios se conservan para recuperarlos al volver.
            ocupado = false;
            if (admin != null && !admin.EstaAutenticado)
            {
                borradorModelo = Copia(d);
                if (ui != null) ui.Avisar(MensajeSesionModelo, true);
            }
            ModeloCampus vivo = modelos != null ? modelos.Buscar(id) : null;
            if (vivo != null && original != null)
            {
                if (vivo == modeloSel)
                {
                    trabajoModelo = Copia(original);
                    vivo.datos = trabajoModelo;
                }
                else vivo.datos = original;
                modelos.Ubicar(vivo);
            }
            ActualizarTextoModelo();
            Estado($"<color={ColorError}>No se pudo guardar el modelo: {UIRapida.TextoSeguro(SinPunto(resp))}. Se dejó como estaba.</color>");
        });
    }

    private void DescartarModelo()
    {
        if (modeloSel == null || originalModelo == null) return;
        trabajoModelo = Copia(originalModelo);
        modeloSel.datos = trabajoModelo;
        modeloCambiado = false;
        modelos.Ubicar(modeloSel);
        ActualizarTextoModelo();
        Estado("Cambios descartados.");
    }

    // "Descartar" de la barra o Atrás durante la edición: sin cambios termina al instante; con cambios pregunta
    // con el modal "¿Descartar los cambios?" (como el formulario de lugar)
    private void PedirDescartarModelo()
    {
        if (modeloSel == null) return;
        if (!modeloCambiado) { TerminarEdicionModelo(); Estado(""); return; }
        MostrarModal("¿Descartar los cambios?", "Los ajustes de este modelo que no guardaste se perderán.",
                     "Seguir editando", "Descartar", () =>
                     {
                         DescartarModelo();
                         TerminarEdicionModelo();
                     }, null);
    }

    // Eliminar un modelo 3D: se pinta en Rojo y se confirma en el modal (Cancelar, el velo o Atrás no borran)
    private void PedirEliminarModelo(ModeloCampus mc)
    {
        if (mc == null || mc.datos == null) return;
        int id = mc.datos.id;
        string tipo = NombreTipo(mc.datos.tipo);
        CancelarEliminar();
        modeloPorEliminar = id;
        limiteEliminar = float.MaxValue;   // sin plazo: lo decide el modal
        if (textoBotonEliminar != null) textoBotonEliminar.text = "¿Eliminar?";
        ColorearModelosEliminar();
        Estado("");
        MostrarModal($"¿Eliminar este {tipo}?", $"El {tipo} (en rojo) se borrará del mapa. Esta acción no se puede deshacer.",
                     "Cancelar", "Eliminar", () =>
                     {
                         CancelarEliminar();
                         if (idModeloSel == id) DeseleccionarModelo();
                         Operar("Eliminando modelo…");
                         admin.EliminarModelo(id, (ok, resp) => Terminar(ok, resp, ok ? "Modelo eliminado." : null));
                     }, () => { CancelarEliminar(); Estado("No se eliminó el modelo."); });
    }

    // Modal de confirmación único del editor (se crea en ConstruirInterfaz); la acción siempre es destructiva (Rojo).
    // Sin modal no se borra ni se descarta nada: se toma como "Cancelar" (lo seguro) y se avisa en la consola.
    private void MostrarModal(string titulo, string texto, string cancelar, string confirmar,
                              UnityEngine.Events.UnityAction alConfirmar, UnityEngine.Events.UnityAction alCancelar)
    {
        if (modalConfirmar == null)
        {
            Debug.LogWarning("EditorMapa: no hay modal de confirmación; \"" + titulo + "\" se cancela sin cambios.");
            alCancelar?.Invoke();
            return;
        }
        modalConfirmar.Configurar(cancelar, confirmar, true, false);
        modalConfirmar.Mostrar(titulo, texto, alConfirmar, alCancelar ?? (() => { }));
    }

    // ---------- Borrador del modelo (sesión vencida) ----------
    private void ConservarBorradorModelo()
    {
        if (trabajoModelo == null) return;
        borradorModelo = Copia(trabajoModelo);
        if (ui != null) ui.Avisar(MensajeSesionModelo, true);
    }

    // Vuelve a seleccionar el modelo y le aplica los cambios que no se alcanzaron a guardar
    private void RestaurarBorradorModelo()
    {
        if (borradorModelo == null || modelos == null || modeloSel != null || !isActiveAndEnabled) return;
        if (admin != null && !admin.EstaAutenticado) return;
        ModeloCampus mc = modelos.Buscar(borradorModelo.id);
        if (mc == null)
        {
            if (!ExisteModelo(borradorModelo.id)) borradorModelo = null;   // lo borraron: no hay nada que recuperar
            return;                                                         // si no, aún no se dibuja: se intenta al redibujar
        }
        LugaresService.Modelo b = borradorModelo;
        borradorModelo = null;
        SeleccionarModelo(mc, guardarAnterior: false);
        if (modeloSel != mc || trabajoModelo == null) return;
        CopiarValores(b, trabajoModelo);
        modeloCambiado = true;
        modelos.Ubicar(mc);
        ActualizarTextoModelo();
        Estado("Se recuperaron los cambios sin guardar del modelo. Toca Guardar para conservarlos.");
    }

    private bool ExisteModelo(int id)
    {
        if (lugares == null || lugares.Modelos == null) return false;
        foreach (LugaresService.Modelo m in lugares.Modelos) if (m.id == id) return true;
        return false;
    }

    private void ColorearModelosEliminar()
    {
        if (modelos == null || lugares == null || lugares.Modelos == null) return;
        foreach (LugaresService.Modelo d in lugares.Modelos)
        {
            ModeloCampus mc = modelos.Buscar(d.id);
            if (mc == null) continue;
            if (d.id == modeloPorEliminar) mc.Resaltar(true, ColorEliminar);
            else mc.Resaltar(mc == modeloSel, ColorModeloSel);
        }
    }

    // ---------- Confirmación de "Eliminar" en dos toques (SegundosConfirmar) ----------
    private void ArmarConfirmacion()
    {
        limiteEliminar = Time.unscaledTime + SegundosConfirmar;
        if (textoBotonEliminar != null) textoBotonEliminar.text = "¿Eliminar?";
        ColorearNodos();
        ColorearModelosEliminar();
    }

    private void CancelarEliminar()
    {
        bool habia = nodoPorEliminar != 0 || modeloPorEliminar != 0;
        nodoPorEliminar = modeloPorEliminar = 0;
        if (textoBotonEliminar != null) textoBotonEliminar.text = "Eliminar";
        if (habia) { ColorearNodos(); ColorearModelosEliminar(); }
    }

    private void RevisarConfirmacion()
    {
        if ((nodoPorEliminar != 0 || modeloPorEliminar != 0) && Time.unscaledTime > limiteEliminar)
        {
            CancelarEliminar();
            Estado($"No se eliminó nada (pasaron {SegundosConfirmar:0} segundos sin confirmar).");
        }
    }

    // ---------- Marco del modelo seleccionado / por eliminar ----------
    // La selección no se distingue solo por el tono: una base plana (Oscuro; Rojo si se va a eliminar) un poco más
    // grande que la planta del modelo lo contornea a ras de suelo. Sigue al modelo mientras se mueve o se ajusta.
    private Transform marcoModelo;
    private bool marcoCilindro;
    private Renderer marcoRender;
    // Último estado aplicado al marco: solo se reasigna material/posición/escala cuando algo cambia
    private bool marcoEliminar;
    private Vector3 marcoPos, marcoEscala;
    private Quaternion marcoRot;
    private float marcoBorde = -1f;

    private void ActualizarMarcoModelo()
    {
        // Salida temprana (cada frame): sin selección, sin modelo por eliminar y sin marco visible no hay nada que hacer
        if (modeloSel == null && modeloPorEliminar == 0 && (marcoModelo == null || !marcoModelo.gameObject.activeSelf)) return;
        ModeloCampus mc = modeloSel;
        bool eliminar = false;
        if (modeloPorEliminar != 0 && modelos != null)
        {
            ModeloCampus pe = modelos.Buscar(modeloPorEliminar);
            if (pe != null) { mc = pe; eliminar = true; }
        }
        if (mc == null || mc.datos == null || raizVisual == null || cam == null)
        {
            if (marcoModelo != null && marcoModelo.gameObject.activeSelf) marcoModelo.gameObject.SetActive(false);
            return;
        }

        bool cilindro = mc.datos.tipo == "cilindro";
        if (marcoModelo == null || marcoCilindro != cilindro)
        {
            if (marcoModelo != null) Destroy(marcoModelo.gameObject);
            GameObject go = GameObject.CreatePrimitive(cilindro ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = "MarcoModelo";
            Collider col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Destroy(col); }   // los toques llegan al modelo
            Renderer r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(raizVisual, false);
            marcoModelo = go.transform;
            marcoRender = r;
            marcoCilindro = cilindro;
            marcoBorde = -1f;   // objeto nuevo: hay que aplicar todo
            marcoRender.sharedMaterial = GeoMapa.Material(eliminar ? ColorEliminar : UIRapida.Oscuro);
            marcoEliminar = eliminar;
        }
        if (!marcoModelo.gameObject.activeSelf) marcoModelo.gameObject.SetActive(true);
        if (marcoEliminar != eliminar)
        {
            marcoRender.sharedMaterial = GeoMapa.Material(eliminar ? ColorEliminar : UIRapida.Oscuro);
            marcoEliminar = eliminar;
        }

        // Borde visible a cualquier zoom: proporcional a la altura de la cámara (como los nodos)
        float borde = Mathf.Max(5f, cam.transform.position.y) * 0.012f;
        Transform t = mc.transform;
        Vector3 s = t.localScale, p = t.position;
        Quaternion q = t.rotation;
        if (Mathf.Approximately(borde, marcoBorde) && p == marcoPos && s == marcoEscala && q == marcoRot) return;
        marcoBorde = borde; marcoPos = p; marcoEscala = s; marcoRot = q;
        marcoModelo.SetPositionAndRotation(new Vector3(p.x, 0.12f, p.z), q);
        marcoModelo.localScale = new Vector3(s.x + 2f * borde, cilindro ? 0.05f : 0.1f, s.z + 2f * borde);
    }

    private static LugaresService.Modelo Copia(LugaresService.Modelo d) => new LugaresService.Modelo
    {
        id = d.id, tipo = d.tipo, nombre = d.nombre, latitud = d.latitud, longitud = d.longitud,
        rotacion = d.rotacion, ancho = d.ancho, largo = d.largo, alto = d.alto,
    };

    private static void CopiarValores(LugaresService.Modelo de, LugaresService.Modelo a)
    {
        a.latitud = de.latitud; a.longitud = de.longitud; a.rotacion = de.rotacion;
        a.ancho = de.ancho; a.largo = de.largo; a.alto = de.alto;
    }
}
