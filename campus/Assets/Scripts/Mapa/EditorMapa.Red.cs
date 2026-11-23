using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

/// <summary>Modificar mapa: herramientas, toques sobre el mapa, nodos, conexiones, puertas y dibujo de la red de caminos.</summary>
public partial class EditorMapa
{
    // ==================================================================
    // Herramientas
    // ==================================================================
    private void ElegirHerramienta(Herramienta h)
    {
        if (h != Herramienta.Mover && modeloSel != null) { GuardarModelo(false); DeseleccionarModelo(); }

        actual = h;
        CancelarEliminar();
        if (h != Herramienta.Nodo && h != Herramienta.Conectar) nodoSeleccionado = 0;
        ColorearNodos();

        foreach (var par in botones)
        {
            bool activa = par.Key == h;
            Color colorActiva = par.Key == Herramienta.Eliminar ? UIRapida.Rojo : UIRapida.Verde;
            ((Image)par.Value.targetGraphic).color = activa ? colorActiva : UIRapida.Menta;
            // Inactiva: texto e ícono SobreMenta; "Eliminar" en RojoSobreMenta (texto e ícono iguales)
            Color colorInactiva = par.Key == Herramienta.Eliminar ? UIRapida.RojoSobreMenta : UIRapida.SobreMenta;
            UIRapida.TextoDe(par.Value).color = activa ? UIRapida.SobreColor : colorInactiva;
            if (iconosHerramienta.TryGetValue(par.Key, out Image icono))
                icono.color = activa ? UIRapida.SobreColor : colorInactiva;
        }

        switch (h)
        {
            case Herramienta.Mover:
                Ayuda("Arrastra para moverte y usa + / - o pellizca para el zoom. Toca un modelo 3D para editarlo."); break;
            case Herramienta.Nodo:
                Ayuda("Toca el mapa para crear un nodo: se une al <b>nodo elegido</b> (más grande, con aro). Toca un nodo para seguir desde él; tócalo otra vez para soltarlo."); break;
            case Herramienta.Conectar:
                Ayuda("Toca dos nodos para unirlos. Si ya estaban unidos, se separan."); break;
            case Herramienta.Puerta:
                Ayuda("Toca un nodo para marcarlo como <b>puerta</b> (entrada) de un lugar. Las puertas se ven como rombo naranja."); break;
            case Herramienta.Eliminar:
                Ayuda($"Nodo: tócalo y vuelve a tocarlo antes de {SegundosConfirmar:0} segundos para eliminarlo. Modelo 3D: tócalo y confirma."); break;
            default:
                Ayuda($"Toca el mapa donde quieras el <b>{NombreTipo(TipoDe(h))}</b>."); break;
        }
    }

    // ==================================================================
    // Toques sobre el mapa (un toque corto, sin arrastrar)
    // ==================================================================
    private void DetectarToque()
    {
        // Mientras se elige el lugar de una puerta o hay un modal de confirmación, los toques son del modal
        if (panelPuerta == null || panelPuerta.activeSelf || (modalConfirmar != null && modalConfirmar.Visible))
        { detector.Reiniciar(); return; }
        if (detector.Actualizar(out Vector2 pos)) Tocar(pos);
    }

    private void Tocar(Vector2 pos)
    {
        if (ocupado || !mapaListo || cam == null) return;

        ObjetoTocado(pos, out NodoVisual nodo, out ModeloCampus modelo);
        bool haySuelo = GeoMapa.PuntoEnSuelo(cam, pos, out Vector3 suelo);

        switch (actual)
        {
            case Herramienta.Mover:
                if (modelo != null) SeleccionarModelo(modelo);
                else if (modeloSel != null && haySuelo) MoverModeloA(suelo);
                break;

            case Herramienta.Nodo:
                if (nodo != null) { nodoSeleccionado = nodoSeleccionado == nodo.id ? 0 : nodo.id; ColorearNodos(); }
                else if (haySuelo) CrearNodo(suelo);
                break;

            case Herramienta.Conectar:
                if (nodo == null) { nodoSeleccionado = 0; ColorearNodos(); }
                else if (nodoSeleccionado == 0) { nodoSeleccionado = nodo.id; ColorearNodos(); Estado("Nodo elegido: toca el otro nodo."); }
                else if (nodoSeleccionado == nodo.id) { nodoSeleccionado = 0; ColorearNodos(); }
                else AlternarConexion(nodoSeleccionado, nodo.id);
                break;

            case Herramienta.Puerta:
                if (nodo != null) AbrirPuerta(nodo);
                break;

            case Herramienta.Eliminar:
                if (nodo != null) PedirEliminarNodo(nodo.id);
                else if (modelo != null) PedirEliminarModelo(modelo);
                else { CancelarEliminar(); Estado(""); }
                break;

            default:
                if (haySuelo) CrearModelo(TipoDe(actual), suelo);
                break;
        }
    }

    private void ObjetoTocado(Vector2 pos, out NodoVisual nodo, out ModeloCampus modelo)
    {
        nodo = null;
        modelo = null;
        int n = Physics.RaycastNonAlloc(cam.ScreenPointToRay(pos), impactos, 100000f);
        System.Array.Sort(impactos, 0, n, PorDistancia);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = impactos[i];
            nodo = h.collider.GetComponent<NodoVisual>();
            if (nodo != null) return;
            modelo = h.collider.GetComponent<ModeloCampus>();
            if (modelo != null) return;
        }
    }

    // ==================================================================
    // Nodos y conexiones
    // ==================================================================
    private void CrearNodo(Vector3 suelo)
    {
        Vector2d geo = mapa.WorldToGeoPosition(suelo);
        int unirCon = nodoSeleccionado;

        Operar("Creando nodo…");
        admin.CrearNodo(new AdminService.NodoEditable
        {
            latitud = geo.x,
            longitud = geo.y,
            conectar_con_nodo = unirCon,
        }, (ok, resp) =>
        {
            Terminar(ok, resp, ok ? (unirCon > 0 ? "Nodo creado y unido al anterior." : "Nodo creado.") : null);
            if (ok) { nodoSeleccionado = LeerId(resp); ColorearNodos(); }
        });
    }

    private void AlternarConexion(int a, int b)
    {
        bool existe = Conectados(a, b);
        Operar(existe ? "Separando nodos…" : "Uniendo nodos…");
        System.Action<bool, string> fin = (ok, resp) =>
        {
            Terminar(ok, resp, ok ? (existe ? "Nodos separados." : "Nodos unidos.") : null);
            if (ok) { nodoSeleccionado = b; ColorearNodos(); }   // se sigue desde el último nodo
        };
        if (existe) admin.EliminarConexion(a, b, fin);
        else admin.CrearConexion(a, b, fin);
    }

    private bool Conectados(int a, int b)
    {
        if (lugares.Red?.conexiones == null) return false;
        foreach (var c in lugares.Red.conexiones)
            if ((c.nodo_a == a && c.nodo_b == b) || (c.nodo_a == b && c.nodo_b == a)) return true;
        return false;
    }

    private void PedirEliminarNodo(int id)
    {
        if (nodoPorEliminar != id)
        {
            CancelarEliminar();
            nodoPorEliminar = id;
            ArmarConfirmacion();
            Estado($"<color={ColorError}>Toca otra vez el nodo (en rojo) para eliminarlo.</color> Sus conexiones también se borran.");
            return;
        }

        CancelarEliminar();
        if (nodoSeleccionado == id) nodoSeleccionado = 0;
        Operar("Eliminando nodo…");
        admin.EliminarNodo(id, (ok, resp) => Terminar(ok, resp, ok ? "Nodo eliminado." : null));
    }

    // ---------- Puertas ----------
    private void AbrirPuerta(NodoVisual nodo)
    {
        nodoPuerta = nodo.id;
        string actualLugar = "ninguno (punto de paso)";
        if (nodo.lugarId > 0)
        {
            LugaresService.Lugar l = lugares.BuscarPorId(nodo.lugarId);
            if (l != null) actualLugar = UIRapida.TextoSeguro(l.nombre);
        }
        tituloPuerta.text = $"¿De qué lugar es esta puerta?\n<size={UIRapida.TamSecundario:0}><color={ColorGris}>Ahora: {actualLugar}</color></size>";

        modalPuerta.Vaciar();   // apaga y destruye las filas anteriores, frena la inercia y vuelve arriba
        FilaPuerta("Ninguno (punto de paso)", 0, nodo.lugarId == 0);
        foreach (LugaresService.Lugar l in lugares.Lugares)
            FilaPuerta(string.IsNullOrEmpty(l.categoria) ? UIRapida.TextoSeguro(l.nombre)
                       : $"{UIRapida.TextoSeguro(l.nombre)}  <size={UIRapida.TamSecundario:0}>· {UIRapida.TextoSeguro(l.categoria)}</size>",
                       l.id, nodo.lugarId == l.id);

        panelPuerta.SetActive(true);
        panelPuerta.transform.SetAsLastSibling();
        if (grupoPuerta != null) grupoPuerta.interactable = grupoPuerta.blocksRaycasts = true;
        if (animPuerta != null) StopCoroutine(animPuerta);
        animPuerta = StartCoroutine(UIRapida.Aparecer(cajaPuerta, grupoPuerta));
    }

    private void FilaPuerta(string texto, int lugarId, bool actualSeleccionado)
    {
        Button b = UIRapida.Boton(listaPuertas, "Lugar_" + lugarId, texto, UIRapida.TamCuerpo,
                                  UIRapida.NoSeleccionadoFondo, UIRapida.NoSeleccionadoTexto, fuente, () => AsignarPuerta(lugarId));
        UIRapida.Redondear((Image)b.targetGraphic, UIRapida.RadioControl);
        UIRapida.MarcarSeleccion(b, actualSeleccionado);   // mismo estilo de selección que chips y pestañas
        TMP_Text t = UIRapida.TextoDe(b);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        ((RectTransform)t.transform).offsetMin = new Vector2(UIRapida.Esp3, UIRapida.Esp1 / 2f);
        LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = UIRapida.ToqueMinimo;
    }

    private void AsignarPuerta(int lugarId)
    {
        int id = nodoPuerta;
        CerrarPuerta();

        LugaresService.NodoDto dto = BuscarNodoDto(id);
        if (dto == null) { Estado("Ese nodo ya no existe."); return; }

        LugaresService.Lugar lugarPuerta = lugarId > 0 ? lugares.BuscarPorId(lugarId) : null;
        string nombreLugar = lugarPuerta != null ? UIRapida.TextoSeguro(lugarPuerta.nombre) : "ese lugar";
        Operar("Guardando puerta…");
        admin.ActualizarNodo(id, new AdminService.NodoEditable
        {
            nombre = dto.nombre,
            latitud = dto.latitud,
            longitud = dto.longitud,
            lugar_id = lugarId,
        }, (ok, resp) => Terminar(ok, resp, ok
            ? (lugarId > 0 ? $"Ese nodo ahora es puerta de {nombreLugar}." : "Ese nodo ahora es un punto de paso.")
            : null));
    }

    // Cierra el panel de puerta con fundido (UIRapida.Desaparecer); si la pantalla está apagada, al instante
    private void CerrarPuerta()
    {
        if (animPuerta != null) { StopCoroutine(animPuerta); animPuerta = null; }
        if (panelPuerta == null) return;
        if (panelPuerta.activeSelf && isActiveAndEnabled)
        {
            if (grupoPuerta != null) grupoPuerta.interactable = grupoPuerta.blocksRaycasts = false;   // sin toques dobles mientras se va
            animPuerta = StartCoroutine(UIRapida.Desaparecer(panelPuerta, cajaPuerta, grupoPuerta));
            return;
        }
        UIRapida.TerminarAparicion(cajaPuerta, grupoPuerta);
        panelPuerta.SetActive(false);
    }

    // El panel está abierto y no se está cerrando
    private bool PuertaAbierta => panelPuerta != null && panelPuerta.activeSelf && (grupoPuerta == null || grupoPuerta.interactable);

    private LugaresService.NodoDto BuscarNodoDto(int id)
    {
        if (lugares.Red?.nodos == null) return null;
        foreach (var n in lugares.Red.nodos) if (n.id == id) return n;
        return null;
    }

    // ==================================================================
    // Dibujo de la red mientras se edita
    // ==================================================================
    private void AlCambiarRed()
    {
        if (isActiveAndEnabled) DibujarRed();
    }

    // Pool por id: cada recarga de la red (crear un nodo, unir dos…) reubica las esferas y barras que ya existen
    // y solo crea las nuevas y destruye las que desaparecieron (antes se recreaba todo en cada cambio).
    private void DibujarRed()
    {
        if (raizVisual == null || lugares == null || lugares.Red == null || !mapaListo) return;

        posicionesRed.Clear();
        vistosNodos.Clear();
        vistasBarras.Clear();
        if (lugares.Red.nodos != null)
        {
            foreach (var n in lugares.Red.nodos)
            {
                Vector3 p = mapa.GeoToWorldPosition(new Vector2d(n.latitud, n.longitud), false);
                p.y = 0.5f;
                posicionesRed[n.id] = p;
                vistosNodos.Add(n.id);

                if (!nodos.TryGetValue(n.id, out NodoVisual nv) || nv == null)
                {
                    GameObject esfera = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    esfera.name = $"Nodo_{n.id}";
                    esfera.transform.SetParent(raizVisual, false);
                    esfera.GetComponent<Renderer>().sharedMaterial = GeoMapa.Material(ColorPaso);
                    nv = esfera.AddComponent<NodoVisual>();
                    nv.id = n.id;
                    nodos[n.id] = nv;
                }
                nv.transform.position = p;
                nv.lugarId = n.lugar_id;
            }
        }

        if (lugares.Red.conexiones != null)
        {
            foreach (var c in lugares.Red.conexiones)
            {
                if (!posicionesRed.TryGetValue(c.nodo_a, out Vector3 a) || !posicionesRed.TryGetValue(c.nodo_b, out Vector3 b)) continue;
                long clave = ClaveBarra(c.nodo_a, c.nodo_b);
                if (!vistasBarras.Add(clave)) continue;   // conexión repetida: una sola barra

                if (!barras.TryGetValue(clave, out Transform barra) || barra == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Camino_{c.nodo_a}_{c.nodo_b}";
                    Destroy(go.GetComponent<Collider>());   // los toques deben llegar a los nodos, no a las barras
                    go.transform.SetParent(raizVisual, false);
                    go.GetComponent<Renderer>().sharedMaterial = GeoMapa.Material(ColorPaso);
                    barra = go.transform;
                    barras[clave] = barra;
                }
                barra.position = (a + b) * 0.5f;
                Vector3 dir = b - a;
                if (dir.sqrMagnitude > 1e-6f) barra.rotation = Quaternion.LookRotation(dir);
                barra.localScale = new Vector3(1f, 0.2f, dir.magnitude);   // EscalarVisual ajusta el grosor
            }
        }

        // Lo que ya no está en la red se destruye
        sobrantesNodos.Clear();
        foreach (var par in nodos) if (!vistosNodos.Contains(par.Key)) sobrantesNodos.Add(par.Key);
        foreach (int id in sobrantesNodos)
        {
            if (nodos[id] != null) Destroy(nodos[id].gameObject);
            nodos.Remove(id);
        }
        sobrantesBarras.Clear();
        foreach (var par in barras) if (!vistasBarras.Contains(par.Key)) sobrantesBarras.Add(par.Key);
        foreach (long k in sobrantesBarras)
        {
            if (barras[k] != null) Destroy(barras[k].gameObject);
            barras.Remove(k);
        }

        if (nodoSeleccionado != 0 && !nodos.ContainsKey(nodoSeleccionado)) nodoSeleccionado = 0;
        visualSucio = true;   // barras nuevas o reubicadas: hay que reescalarlas
        ColorearNodos();
        EscalarVisual();
    }

    // Clave de una conexión sin orden (a-b = b-a)
    private static long ClaveBarra(int a, int b)
    {
        int menor = Mathf.Min(a, b), mayor = Mathf.Max(a, b);
        return ((long)menor << 32) | (uint)mayor;
    }

    // Destruye las esferas y barras del editor (al salir del editor)
    private void BorrarVisual()
    {
        if (raizVisual != null)
            for (int i = raizVisual.childCount - 1; i >= 0; i--) Destroy(raizVisual.GetChild(i).gameObject);
        nodos.Clear();
        barras.Clear();
        visualSucio = true;
    }

    private void ColorearNodos()
    {
        foreach (NodoVisual nv in nodos.Values)
        {
            if (nv == null) continue;
            Color c = nv.id == nodoPorEliminar ? ColorEliminar
                    : nv.id == nodoSeleccionado ? ColorSeleccion
                    : nv.lugarId > 0 ? ColorPuerta
                    : ColorPaso;
            nv.PonerColor(c);
            MarcarNodo(nv, nv.lugarId > 0, nv.id == nodoSeleccionado || nv.id == nodoPorEliminar,
                       nv.id == nodoPorEliminar ? ColorEliminar : UIRapida.Oscuro);
        }
        visualSucio = true;   // el seleccionado / por eliminar se dibuja más grande
    }

    // Forma además del color (el tono solo no basta sobre el suelo del mapa):
    //  · todos los nodos: disco Oscuro plano en el ecuador de la esfera = contorno oscuro visto desde arriba;
    //  · puertas: ese contorno es un ROMBO (cubo plano girado 45°) en vez de un círculo;
    //  · seleccionado / por eliminar: aro exterior (Oscuro o Rojo) bajo el contorno, además de la escala ×1,35
    //    (el aro blanco daba 1,05–1,33:1 contra el suelo del mapa).
    // Las piezas son hijas de la esfera (escalan con ella), sin collider (el toque llega a la esfera) ni sombras.
    private static void MarcarNodo(NodoVisual nv, bool puerta, bool resaltado, Color colorAro)
    {
        Transform t = nv.transform;
        Transform circulo = PiezaNodo(t, "Contorno", PrimitiveType.Cylinder, new Vector3(1.5f, 0.02f, 1.5f), 0f, 0f);
        Transform rombo = PiezaNodo(t, "ContornoPuerta", PrimitiveType.Cube, new Vector3(1.3f, 0.04f, 1.3f), 0f, 45f);
        Transform aro = PiezaNodo(t, "Aro", PrimitiveType.Cylinder, new Vector3(2.2f, 0.015f, 2.2f), -0.03f, 0f);
        circulo.gameObject.SetActive(!puerta);
        rombo.gameObject.SetActive(puerta);
        aro.gameObject.SetActive(resaltado);
        if (resaltado) aro.GetComponent<Renderer>().sharedMaterial = GeoMapa.Material(colorAro);
    }

    private static Transform PiezaNodo(Transform nodo, string nombre, PrimitiveType forma, Vector3 escala, float y, float giro)
    {
        Transform p = nodo.Find(nombre);
        if (p != null) return p;
        GameObject go = GameObject.CreatePrimitive(forma);
        go.name = nombre;
        Collider col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        Renderer r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = GeoMapa.Material(UIRapida.Oscuro);
        p = go.transform;
        p.SetParent(nodo, false);
        p.localPosition = new Vector3(0f, y, 0f);
        p.localRotation = Quaternion.Euler(0f, giro, 0f);
        p.localScale = escala;
        return p;
    }

    // Los nodos y caminos se ven del mismo tamaño en pantalla sin importar el zoom
    private void EscalarVisual()
    {
        if (cam == null || nodos.Count == 0) return;
        float altura = Mathf.Max(5f, cam.transform.position.y);
        // Solo cuando cambia el zoom (altura de la cámara) o la selección: no cada frame
        if (!visualSucio && Mathf.Abs(altura - ultimaAltura) < 0.01f) return;
        ultimaAltura = altura;
        visualSucio = false;
        float diametro = altura * 0.035f;
        float grosor = altura * 0.008f;

        foreach (NodoVisual nv in nodos.Values)
        {
            if (nv == null) continue;
            float d = nv.id == nodoSeleccionado || nv.id == nodoPorEliminar ? diametro * 1.35f : diametro;
            nv.transform.localScale = new Vector3(d, d, d);
        }
        foreach (Transform b in barras.Values)
        {
            if (b == null) continue;
            Vector3 s = b.localScale;
            b.localScale = new Vector3(grosor, grosor * 0.4f, s.z);
        }
    }
}
