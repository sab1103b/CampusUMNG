using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Formulario de lugar: pin sobre el mapa, toques y puerta automática (sin asignar memoria por frame).</summary>
public partial class FormularioLugarMapa
{
    // ==================================================================
    // Mapa: pin, toques y puerta automática (sin asignar memoria por frame)
    // ==================================================================
    private void Update()
    {
        if (panelOpciones.activeSelf || (modalDescartar != null && modalDescartar.Visible) || esInterior || arrastre.Arrastrando) { detector.Reiniciar(); return; }
        if (detector.Actualizar(out Vector2 p)) MoverPinA(p);   // un toque corto en el mapa lleva el pin ahí
    }

    // Última entrada con la que se ubicó el pin: si nada cambió, LateUpdate no recalcula (GeoToWorld + proyección)
    private int pinVersionPose = -1, pinPantallaAncho, pinPantallaAlto;
    private float pinZoom = float.NaN;
    private Vector2d pinUbicacion, pinCentro;
    private bool pinCalculado, pinVer;

    private void LateUpdate()
    {
        // El pin sigue su latitud/longitud aunque el mapa se mueva o haga zoom.
        // En modo interior solo se muestra si ya se eligió el edificio.
        bool ver = hayUbicacion && mapaListo && mapa != null && cam != null && (!esInterior || edificioPadre > 0);

        // Salida temprana: misma pose de cámara, mismo zoom/centro del mapa, misma ubicación del pin y misma pantalla
        float zoom = ver ? mapa.Zoom : 0f;
        Vector2d centro = ver ? mapa.CenterLatitudeLongitude : default(Vector2d);
        if (pinCalculado && pinVer == ver && pin.gameObject.activeSelf == ver && pinVersionPose == CamaraMapa.VersionPose
            && pinZoom == zoom && Igual(pinUbicacion, ubicacion) && Igual(pinCentro, centro)
            && pinPantallaAncho == Screen.width && pinPantallaAlto == Screen.height) return;
        pinCalculado = true;
        pinVersionPose = CamaraMapa.VersionPose;
        pinZoom = zoom;
        pinUbicacion = ubicacion;
        pinCentro = centro;
        pinPantallaAncho = Screen.width;
        pinPantallaAlto = Screen.height;

        if (ver)
        {
            Vector3 mundo = mapa.GeoToWorldPosition(ubicacion, false);
            Vector3 pantalla = cam.WorldToScreenPoint(mundo);
            ver = pantalla.z > 0;
            if (ver)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)pin.parent, pantalla, null, out Vector2 local);
                // Solo si se movió: escribir la posición ensucia el canvas en cada frame aunque el mapa esté quieto
                if ((pin.anchoredPosition - local).sqrMagnitude > 0.01f) pin.anchoredPosition = local;
            }
        }
        pinVer = ver;
        if (pin.gameObject.activeSelf != ver) pin.gameObject.SetActive(ver);
    }

    // Comparación exacta sin boxing (Vector2d.Equals(object) asigna memoria y su "==" de Mapbox siempre da false)
    private static bool Igual(Vector2d a, Vector2d b) => a.x == b.x && a.y == b.y;

    private void MoverPinA(Vector2 pantalla)
    {
        if (esInterior || !mapaListo || mapa == null || !GeoMapa.PuntoEnSuelo(cam, pantalla, out Vector3 suelo)) return;
        ubicacion = mapa.WorldToGeoPosition(suelo);
        hayUbicacion = true;
        AnularConfirmacion();
        ActualizarPuerta();
    }

    // Busca el nodo libre más cercano (a menos de 25 m) que quedaría como puerta
    private void ActualizarPuerta()
    {
        nodoPuerta = 0;
        metrosPuerta = 0f;

        if (esInterior) { PonerTextoPuerta(edificioPadre > 0 ? 1 : 0, 0, 0); return; }
        if (!hayUbicacion || lugares == null || lugares.Red == null || lugares.Red.nodos == null) { PonerTextoPuerta(0, 0, 0); return; }

        var nodos = lugares.Red.nodos;
        if (lugarEditando > 0 && TienePuerta(lugarEditando)) { PonerTextoPuerta(2, 0, 0); return; }

        float mejor = RadioPuertaM;
        for (int i = 0; i < nodos.Length; i++)
        {
            var n = nodos[i];
            if (n.lugar_id > 0 && n.lugar_id != lugarEditando) continue;   // ya es puerta de otro lugar
            float d = (float)RedCaminos.Metros(ubicacion, new Vector2d(n.latitud, n.longitud));
            if (d <= mejor) { mejor = d; nodoPuerta = n.id; metrosPuerta = d; }
        }
        if (nodoPuerta > 0) PonerTextoPuerta(3, nodoPuerta, Mathf.RoundToInt(metrosPuerta));
        else PonerTextoPuerta(4, 0, 0);
    }

    // Solo cambia el texto cuando cambia lo que dice (SetText con números no crea cadenas nuevas)
    private void PonerTextoPuerta(int estado, int nodo, int metros)
    {
        if (estado == puertaEstado && nodo == puertaNodo && metros == puertaMetros) return;
        puertaEstado = estado; puertaNodo = nodo; puertaMetros = metros;
        switch (estado)
        {
            case 1: textoPuerta.SetText("Usará la ruta y las puertas de su edificio."); break;
            case 2: textoPuerta.SetText("Este lugar ya tiene puerta (se conserva). Cámbiala en Modificar mapa > Puerta."); break;
            case 3: textoPuerta.SetText("Puerta automática: punto del camino a {0} m.", metros); break;
            case 4: textoPuerta.SetText("No hay un camino a menos de 25 m: la ruta llegará al punto del camino más cercano."); break;
            default: textoPuerta.SetText(""); break;
        }
    }

    private bool TienePuerta(int lugarId)
    {
        var nodos = lugares.Red.nodos;
        for (int i = 0; i < nodos.Length; i++) if (nodos[i].lugar_id == lugarId) return true;
        return false;
    }

    // Otro lugar (distinto del que se edita) a menos de "radio" metros del pin
    private LugaresService.Lugar LugarCercano(double radio)
    {
        if (lugares == null || lugares.Lugares == null) return null;
        List<LugaresService.Lugar> lista = lugares.Lugares;
        LugaresService.Lugar mejor = null;
        double mejorD = radio;
        for (int i = 0; i < lista.Count; i++)
        {
            LugaresService.Lugar l = lista[i];
            if (l == null || l.id == lugarEditando) continue;
            double d = RedCaminos.Metros(ubicacion, new Vector2d(l.latitud, l.longitud));
            if (d < mejorD) { mejorD = d; mejor = l; }
        }
        return mejor;
    }
}
