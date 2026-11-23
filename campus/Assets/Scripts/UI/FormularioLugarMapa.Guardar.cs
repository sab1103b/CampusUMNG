using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Formulario de lugar: validación, guardado, borrador por sesión vencida y mensajes.</summary>
public partial class FormularioLugarMapa
{
    // ==================================================================
    // Guardar
    // ==================================================================
    private void Guardar()
    {
        if (ocupado) return;
        LimpiarErrores();

        // Recorta espacios y muestra el valor limpio en el campo
        string nombre = Limpio(campoNombre, true);
        campoNombre.SetTextWithoutNotify(nombre ?? "");

        bool valido = true;
        if (esInterior && edificioPadre <= 0) { MostrarError(errorEdificio, "Elige el edificio donde está este espacio."); valido = false; }
        if (string.IsNullOrEmpty(nombre)) { MostrarError(errorNombre, esInterior ? "Escribe el nombre del espacio." : "Escribe el nombre del lugar."); valido = false; }
        else if (nombre.Length < NombreMin) { MostrarError(errorNombre, $"El nombre debe tener al menos {NombreMin} caracteres."); valido = false; }
        else if (nombre.Length > NombreMax) { MostrarError(errorNombre, $"El nombre puede tener máximo {NombreMax} caracteres."); valido = false; }
        if (string.IsNullOrEmpty(categoria)) { MostrarError(errorCategoria, "Elige una categoría de la lista."); valido = false; }
        if (!valido)
        {
            Mensaje("Revisa los campos marcados en rojo.", true);
            scrollFormulario.verticalNormalizedPosition = 1f;   // los campos obligatorios están arriba
            return;
        }
        if (!esInterior && !hayUbicacion) { Mensaje("Ubica el lugar en el mapa: tócalo o arrastra el pin.", true); return; }

        // Edificio: ¿ya hay otro lugar en este mismo punto? Pide un segundo toque para confirmar
        if (!esInterior && !confirmarDuplicado)
        {
            LugaresService.Lugar otro = LugarCercano(RadioDuplicadoM);
            if (otro != null)
            {
                confirmarDuplicado = true;
                textoBotonGuardar.text = TextoGuardarIgual;
                Mensaje($"Ya hay un lugar en este punto: {UIRapida.TextoSeguro(otro.nombre)}. Toca de nuevo para guardarlo de todos modos.", true);
                return;
            }
        }
        AnularConfirmacion();

        ocupado = true;
        PonerGuardando(true);
        Mensaje("", false);
        string nombreSeguro = UIRapida.TextoSeguro(nombre);

        if (esInterior)
        {
            int padreId = edificioPadre;
            AdminService.ServicioEditable sv = new AdminService.ServicioEditable
            {
                lugar_id = padreId,
                nombre = nombre,
                categoria = categoria,
                piso = Limpio(campoPiso, true),
                descripcion = Limpio(campoDescripcion, false),
                indicacion = Limpio(campoIndicacion, false),
                horario = Limpio(campoHorario, true),
            };
            int servicio = servicioEditando;
            if (servicio > 0)
            {
                admin.ActualizarServicio(servicio, sv, (ok, resp) => Terminado(ok, resp, $"«{nombreSeguro}» actualizado."));
                return;
            }
            admin.CrearServicio(sv, (ok, resp) =>
            {
                LugaresService.Lugar p = ok ? lugares.BuscarPorId(padreId) : null;
                Terminado(ok, resp, $"«{nombreSeguro}» agregado dentro de {(p != null ? UIRapida.TextoSeguro(p.nombre) : "su edificio")}.");
            });
            return;
        }

        AdminService.LugarEditable datos = new AdminService.LugarEditable
        {
            nombre = nombre,
            categoria = categoria,
            descripcion = Limpio(campoDescripcion, false),
            indicacion = Limpio(campoIndicacion, false),
            horario = Limpio(campoHorario, true),
            latitud = Math.Round(ubicacion.x, 7),
            longitud = Math.Round(ubicacion.y, 7),
        };

        int puerta = nodoPuerta;
        int editando = lugarEditando;
        Action<bool, string> alGuardar = (ok, resp) =>
        {
            if (!ok) { Terminado(false, resp, null); return; }
            int id = editando > 0 ? editando : LeerId(resp);
            // El mensaje final se arma cuando responde la asignación de la puerta (sin suponer respuesta síncrona)
            AsignarPuerta(id, puerta, extra => Terminado(true, resp, $"«{nombreSeguro}» guardado.{extra}"));
        };

        if (editando > 0) admin.ActualizarLugar(editando, datos, alGuardar);
        else admin.CrearLugar(datos, alGuardar);
    }

    // Deja el nodo cercano como puerta del lugar (si el lugar todavía no tiene una). "listo" recibe el texto
    // que se añade al mensaje de guardado; se llama una sola vez, cuando AdminService responde (o al instante si no hay nada que hacer).
    private void AsignarPuerta(int lugarId, int nodoId, Action<string> listo)
    {
        if (lugarId <= 0 || nodoId <= 0 || lugares.Red == null || lugares.Red.nodos == null || TienePuerta(lugarId)) { listo(""); return; }
        LugaresService.NodoDto n = Array.Find(lugares.Red.nodos, x => x.id == nodoId);
        if (n == null) { listo(""); return; }

        bool respondio = false;
        admin.ActualizarNodo(nodoId, new AdminService.NodoEditable
        {
            nombre = n.nombre,
            latitud = n.latitud,
            longitud = n.longitud,
            lugar_id = lugarId,
        }, (ok, _) =>
        {
            if (respondio) return;
            respondio = true;
            listo(ok ? " Su puerta quedó en el camino más cercano."
                     : " No se pudo asignar la puerta automática: márcala en Modificar mapa > Puerta.");
        });
    }

    private void Terminado(bool ok, string respuesta, string mensajeOk)
    {
        ocupado = false;
        PonerGuardando(false);
        if (!ok)
        {
            // Sesión vencida: AdminService ya cerró la sesión (y el UIManager quizá cerró esta pantalla).
            // Se guarda lo escrito para restaurarlo al volver a abrir el formulario.
            if (admin != null && !admin.EstaAutenticado)
            {
                GuardarBorrador();
                firmaInicial = null;   // ya quedó en el borrador: OnDisable no lo vuelve a avisar
                Mensaje(MensajeSesion, true);
                if (ui != null) ui.Avisar("Tu sesión venció. " + MensajeBorrador, true);
                return;
            }
            Mensaje(string.IsNullOrEmpty(respuesta) ? "No se pudo guardar. Intenta de nuevo." : ConPunto(respuesta), true);
            return;
        }
        borrador = null;
        firmaInicial = null;   // guardado: al cerrar no queda nada pendiente
        if (ui != null) ui.Avisar(mensajeOk);
        Cancelar();
    }

    // "Guardar" mientras se guarda: estado deshabilitado de la app (texto ≥4,5:1) y "Guardando…"
    private void PonerGuardando(bool guardando)
    {
        UIRapida.EstadoDeshabilitado(btnGuardar, guardando);
        textoBotonGuardar.text = guardando ? TextoGuardando : confirmarDuplicado ? TextoGuardarIgual : TextoGuardar;
    }

    private void Cancelar()
    {
        CerrarOpciones(false);   // la pantalla se cierra: sin animación
        if (ui != null && ui.PantallaActual == nombrePantalla) ui.Atras();
    }

    // ==================================================================
    // Borrador (sesión vencida)
    // ==================================================================
    // Texto que resume lo que hay en el formulario (para saber si cambió desde que se abrió).
    // Edificio: incluye la ubicación del pin (7 decimales, como se guarda). Interior: la ubicación sale del
    // edificio elegido (edificioPadre), así que no se incluye.
    private string Firma(bool interior)
    {
        string lugar = interior ? "" : hayUbicacion
            ? ubicacion.x.ToString("F7", System.Globalization.CultureInfo.InvariantCulture) + "," +
              ubicacion.y.ToString("F7", System.Globalization.CultureInfo.InvariantCulture)
            : "sin ubicación";
        return string.Join("\u001f", interior ? "1" : "0", campoNombre.text, campoDescripcion.text, campoIndicacion.text,
                           campoHorario.text, campoPiso.text, categoria ?? "", edificioPadre.ToString(),
                           lugarEditando.ToString(), servicioEditando.ToString(), lugar);
    }

    private bool HayCambios() => firmaInicial != null && Firma(esInterior) != firmaInicial;

    // Solo cuando la sesión ya no es válida (si el admin cerró el formulario a propósito, lo escrito se descarta)
    private void GuardarBorradorSiHayCambios()
    {
        if (admin == null || admin.EstaAutenticado || ocupado || !HayCambios()) return;
        GuardarBorrador();
        firmaInicial = null;   // ya quedó en el borrador (y no se avisa dos veces)
        // Se avisa en el aviso global: la pantalla se cierra y el mensaje del formulario no se vería
        if (ui != null) ui.Avisar(MensajeBorrador, true);
    }

    private void GuardarBorrador()
    {
        borrador = new Borrador
        {
            lugarId = lugarEditando,
            servicioId = servicioEditando,
            esInterior = esInterior,
            nombre = campoNombre.text,
            descripcion = campoDescripcion.text,
            indicacion = campoIndicacion.text,
            horario = campoHorario.text,
            piso = campoPiso.text,
            categoria = categoria,
            edificioPadre = edificioPadre,
            ubicacionEdificio = esInterior ? ubicacionEdificio : ubicacion,
            hayUbicacionEdificio = esInterior ? hayUbicacionEdificio : hayUbicacion,
        };
    }

    // Pone lo escrito antes de que venciera la sesión. Devuelve si estaba en modo interior.
    private bool RestaurarBorrador()
    {
        Borrador b = borrador;
        borrador = null;   // si vuelve a fallar se guarda de nuevo

        campoNombre.SetTextWithoutNotify(b.nombre ?? "");
        campoDescripcion.text = b.descripcion ?? "";
        campoIndicacion.text = b.indicacion ?? "";
        campoHorario.text = b.horario ?? "";
        campoPiso.text = b.piso ?? "";
        categoria = b.categoria;
        edificioPadre = b.edificioPadre;
        if (b.hayUbicacionEdificio) { ubicacion = b.ubicacionEdificio; hayUbicacion = true; }

        Mensaje("Se recuperó lo que habías escrito.", false);
        return b.esInterior;
    }

    // ==================================================================
    // Mensajes y validación
    // ==================================================================
    // Todo mensaje al usuario termina en punto (las respuestas del servicio pueden venir sin él)
    private static string ConPunto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        char u = texto[texto.Length - 1];
        return u == '.' || u == '?' || u == '!' || u == '\u2026' ? texto : texto + ".";
    }

    private void Mensaje(string texto, bool esError)
    {
        textoError.color = esError ? UIRapida.Rojo : UIRapida.Gris;
        textoError.text = texto;
    }

    private static void MostrarError(TMP_Text t, string texto)
    {
        if (t == null) return;
        t.text = texto;
        t.gameObject.SetActive(true);
    }

    private static void OcultarError(TMP_Text t)
    {
        if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
    }

    private void LimpiarErrores()
    {
        OcultarError(errorNombre);
        OcultarError(errorCategoria);
        OcultarError(errorEdificio);
        Mensaje("", true);
    }

    // Quita la confirmación de "Guardar de todos modos" (cuando cambia el pin, el modo o se reabre)
    private void AnularConfirmacion()
    {
        if (!confirmarDuplicado) return;
        confirmarDuplicado = false;
        textoBotonGuardar.text = TextoGuardar;
        Mensaje("", true);
    }
}
