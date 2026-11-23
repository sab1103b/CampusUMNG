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
        limiteDescartar = 0f;
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
        limiteDescartar = 0f;
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

    private void PedirEliminarModelo(ModeloCampus mc)
    {
        if (mc == null || mc.datos == null) return;
        int id = mc.datos.id;
        if (modeloPorEliminar != id)
        {
            CancelarEliminar();
            modeloPorEliminar = id;
            ArmarConfirmacion();
            Estado($"<color={ColorError}>Toca otra vez el modelo (en rojo) para eliminarlo.</color>");
            return;
        }

        CancelarEliminar();
        if (modeloSel == mc) DeseleccionarModelo();
        Operar("Eliminando modelo…");
        admin.EliminarModelo(id, (ok, resp) => Terminar(ok, resp, ok ? "Modelo eliminado." : null));
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
