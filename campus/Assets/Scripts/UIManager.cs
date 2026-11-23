using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Controla qué pantalla (panel del Canvas) se ve. Todo ocurre en una sola escena:
/// el mapa, el GPS y los datos se mantienen y solo cambian los paneles.
///
/// - Ir("Nombre")         : abre una pantalla y recuerda de dónde vienes.
/// - Atras()              : vuelve a la pantalla anterior (también con el botón Atrás de Android).
/// - Reemplazar("Nombre") : como Ir, pero la pantalla actual no queda en el historial.
/// - IrRaiz("Nombre")     : borra el historial y deja solo esa pantalla.
///
/// Las pantallas marcadas como "Requiere Admin" solo se abren con sesión de administrador;
/// si no hay, se muestra el login. Si la sesión se cierra o vence, se vuelve a la pantalla pública.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Serializable]
    public class Pantalla
    {
        public string nombre;
        public GameObject panel;

        [Tooltip("Opcional: objeto padre (por ejemplo, el Canvas) que debe estar encendido para ver el panel. " +
                 "Se enciende y apaga solo según las pantallas visibles.")]
        public GameObject contenedor;

        [Tooltip("Se muestra ENCIMA de la pantalla actual sin ocultarla (por ejemplo, la ficha de un lugar)")]
        public bool superpuesta;

        [Tooltip("Solo se puede abrir con sesión de administrador; si no hay, pide el login")]
        public bool requiereAdmin;
    }

    [Header("Pantallas")]
    [SerializeField] private List<Pantalla> pantallas = new List<Pantalla>();

    [Header("Nombres especiales (deben coincidir con los de la lista)")]
    [SerializeField] private string pantallaInicial = "Menu";
    [Tooltip("Pantalla pública del mapa (referencia). Al cerrar o vencer la sesión se vuelve a la pantalla inicial.")]
    [SerializeField] private string pantallaPublica = "Usuario";
    public string PantallaPublica => pantallaPublica;
    [SerializeField] private string pantallaLogin = "AdminLogin";

    [Header("Referencias")]
    [SerializeField] private AdminService admin;
    [Tooltip("Texto para avisos cortos. Ponlo en la raíz del Canvas, fuera de los paneles.")]
    [SerializeField] private TMP_Text textoAviso;
    [Tooltip("Duración mínima de un aviso; los largos duran más (hasta 8 s) para alcanzar a leerlos")]
    [SerializeField] private float segundosAviso = 3f;

    private readonly List<Pantalla> pila = new List<Pantalla>();
    private readonly List<Func<bool>> manejadoresAtras = new List<Func<bool>>();
    private Coroutine avisoActivo;

    /// <summary>
    /// Registra una función que se consulta antes de cambiar de pantalla con el botón Atrás.
    /// Si devuelve true, "consumió" el Atrás (por ejemplo, cerró un menú o una lista) y no se cambia de pantalla.
    /// Los últimos en registrarse se consultan primero.
    /// </summary>
    public void RegistrarManejadorAtras(Func<bool> manejador)
    {
        if (manejador != null && !manejadoresAtras.Contains(manejador)) manejadoresAtras.Add(manejador);
    }

    public void QuitarManejadorAtras(Func<bool> manejador) => manejadoresAtras.Remove(manejador);

    /// <summary>Botón Atrás del sistema: primero las capas internas, luego la pantalla anterior.</summary>
    public void AtrasDelSistema()
    {
        for (int i = manejadoresAtras.Count - 1; i >= 0; i--)
        {
            try { if (manejadoresAtras[i]()) return; }
            catch (Exception e) { Debug.LogException(e); }
        }

        if (pila.Count > 1) { Atras(); return; }

        // En la pantalla inicial: como cualquier app Android, se va al fondo sin cerrarse
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var jugador = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var actividad = jugador.GetStatic<AndroidJavaObject>("currentActivity"))
                actividad.Call<bool>("moveTaskToBack", true);
        }
        catch (Exception e) { Debug.LogException(e); }
#endif
    }

    /// <summary>Nombre de la pantalla que está arriba (la que ve el usuario).</summary>
    public string PantallaActual => pila.Count > 0 ? pila[pila.Count - 1].nombre : null;

    /// <summary>Se dispara cada vez que cambia la pantalla visible (recibe su nombre).</summary>
    public event Action<string> PantallaCambiada;

    private void Start()
    {
        foreach (Pantalla p in pantallas)
        {
            if (p.panel != null) p.panel.SetActive(false);
        }
        ActualizarContenedores();
        if (textoAviso != null) textoAviso.text = "";
        PrepararAviso();   // tarjeta redondeada con sombra, oculta hasta el primer aviso

        if (admin != null) admin.SesionCambiada += AlCambiarSesion;

        Ir(pantallaInicial);
    }

    /// <summary>Agrega una pantalla creada por código (si ya existe una con ese nombre, la reemplaza).</summary>
    public void RegistrarPantalla(Pantalla p)
    {
        if (p == null || p.panel == null) return;
        pantallas.RemoveAll(x => string.Equals(x.nombre, p.nombre, StringComparison.OrdinalIgnoreCase));
        pantallas.Add(p);
        if (!pila.Contains(p)) p.panel.SetActive(false);
    }

    /// <summary>¿Está visible esa pantalla (arriba o debajo de una superpuesta)?</summary>
    public bool EstaVisible(string nombre)
    {
        for (int i = 0; i < pila.Count; i++)
        {
            Pantalla p = pila[i];
            if (string.Equals(p.nombre, nombre, StringComparison.OrdinalIgnoreCase))
                return p.panel != null && p.panel.activeSelf;
        }
        return false;
    }

    // Un contenedor queda encendido solo si alguna pantalla visible lo usa
    private void ActualizarContenedores()
    {
        HashSet<GameObject> usados = new HashSet<GameObject>();
        foreach (Pantalla p in pila)
            if (p.contenedor != null && p.panel != null && p.panel.activeSelf) usados.Add(p.contenedor);

        foreach (Pantalla p in pantallas)
            if (p.contenedor != null) p.contenedor.SetActive(usados.Contains(p.contenedor));

        PantallaCambiada?.Invoke(PantallaActual);
    }

    private void OnDestroy()
    {
        if (admin != null) admin.SesionCambiada -= AlCambiarSesion;
    }

    private void Update()
    {
        // El botón Atrás de Android (y la tecla Esc en el editor). En el Input System,
        // Android reporta el botón Atrás como la tecla Escape del teclado.
        var teclado = UnityEngine.InputSystem.Keyboard.current;
        if (teclado != null && teclado.escapeKey.wasPressedThisFrame) AtrasDelSistema();
    }

    /// <summary>Abre una pantalla. Conéctalo al OnClick de un botón escribiendo el nombre.</summary>
    public void Ir(string nombre)
    {
        Pantalla destino = Buscar(nombre);
        if (destino == null) return;

        // Pantallas de administrador: sin sesión se pide el login
        if (destino.requiereAdmin && (admin == null || !admin.EstaAutenticado))
        {
            Avisar("Esta sección es solo para administradores.");
            destino = Buscar(pantallaLogin);
            if (destino == null) return;
        }

        if (pila.Count > 0 && pila[pila.Count - 1] == destino)
        {
            // Ya estás ahí; si quedó oculta (por ejemplo, tras volver del login) se vuelve a mostrar
            if (!destino.panel.activeSelf)
            {
                destino.panel.SetActive(true);
                ActualizarContenedores();
            }
            return;
        }
        pila.Remove(destino);   // evita que la misma pantalla quede dos veces en el historial

        // Una pantalla normal oculta todo lo anterior; una superpuesta se pone encima
        if (!destino.superpuesta)
        {
            foreach (Pantalla p in pila) p.panel.SetActive(false);
        }

        destino.panel.SetActive(true);
        pila.Add(destino);
        ActualizarContenedores();
    }

    /// <summary>Vuelve a la pantalla anterior.</summary>
    public void Atras()
    {
        if (pila.Count <= 1) return;

        Pantalla actual = pila[pila.Count - 1];
        pila.RemoveAt(pila.Count - 1);
        actual.panel.SetActive(false);

        // Vuelve a mostrar lo de abajo: las superpuestas y la primera pantalla normal
        for (int i = pila.Count - 1; i >= 0; i--)
        {
            pila[i].panel.SetActive(true);
            if (!pila[i].superpuesta) break;
        }
        ActualizarContenedores();
    }

    /// <summary>Cierra la pantalla indicada si es la de arriba (útil para el botón "Cerrar" de una superpuesta).</summary>
    public void Cerrar(string nombre)
    {
        if (string.Equals(PantallaActual, nombre, StringComparison.OrdinalIgnoreCase)) Atras();
    }

    /// <summary>
    /// Saca una pantalla del historial aunque no esté arriba (p. ej. la ficha que se cerraba cuando otra pantalla la tapó).
    /// Si está arriba equivale a <see cref="Cerrar"/>.
    /// </summary>
    public void Quitar(string nombre)
    {
        if (string.Equals(PantallaActual, nombre, StringComparison.OrdinalIgnoreCase)) { Atras(); return; }
        int i = pila.FindIndex(p => string.Equals(p.nombre, nombre, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            // Ya no está en el historial (p. ej. IrRaiz durante un fundido): su panel no debe quedar encendido
            Pantalla fuera = Buscar(nombre);
            if (fuera != null && fuera.panel != null && fuera.panel.activeSelf) fuera.panel.SetActive(false);
            ActualizarContenedores();
            return;
        }
        Pantalla quitada = pila[i];
        pila.RemoveAt(i);
        if (quitada.panel != null) quitada.panel.SetActive(false);
        ActualizarContenedores();
    }

    /// <summary>Abre una pantalla sin dejar la actual en el historial (útil después del login).</summary>
    public void Reemplazar(string nombre)
    {
        if (pila.Count > 0)
        {
            Pantalla tope = pila[pila.Count - 1];
            tope.panel.SetActive(false);
            pila.RemoveAt(pila.Count - 1);
        }
        Ir(nombre);
    }

    /// <summary>Borra el historial y deja solo esa pantalla.</summary>
    public void IrRaiz(string nombre)
    {
        foreach (Pantalla p in pantallas)
        {
            if (p.panel != null) p.panel.SetActive(false);
        }
        pila.Clear();
        Ir(nombre);
    }

#if UNITY_EDITOR
    /// <summary>Lo usa la herramienta de integración del Editor para configurar las pantallas.</summary>
    public void ConfigurarDesdeEditor(List<Pantalla> lista, string inicial, string publica, string login,
                                      AdminService servicioAdmin, TMP_Text aviso)
    {
        pantallas = lista;
        pantallaInicial = inicial;
        pantallaPublica = publica;
        pantallaLogin = login;
        admin = servicioAdmin;
        textoAviso = aviso;
    }
#endif

    /// <summary>Muestra un mensaje corto en el texto de avisos.</summary>
    public void Avisar(string mensaje) => Avisar(mensaje, false);

    /// <summary>
    /// Igual que <see cref="Avisar(string)"/>; con error = true el aviso se distingue de uno de éxito:
    /// fondo Rojo e ícono de alerta a la izquierda.
    /// </summary>
    public void Avisar(string mensaje, bool error)
    {
        if (textoAviso == null) return;
        cuadroUltimoAviso = Time.frameCount;
        bool yaVisible = avisoActivo != null;   // otro aviso en pantalla (o desvaneciéndose): se reemplaza sin animar
        if (avisoActivo != null) StopCoroutine(avisoActivo);
        PrepararAviso();
        EstiloAviso(error);
        textoAviso.text = mensaje;

        bool animar = false;
        if (raizAviso != null)
        {
            if (!raizAviso.gameObject.activeSelf) { raizAviso.gameObject.SetActive(true); animar = true; }
            else if (!yaVisible) animar = true;
            if (!animar) UIRapida.TerminarAparicion(raizAviso, grupoAviso);
        }
        AvisoMostrado?.Invoke();   // InterfazCampus lo ubica según la pantalla antes de que se vea
        if (isActiveAndEnabled) avisoActivo = StartCoroutine(MostrarAviso(mensaje, animar));
    }

    /// <summary>Se dispara cada vez que aparece un aviso nuevo (para ubicarlo según la pantalla).</summary>
    public event Action AvisoMostrado;

    /// <summary>¿Hay un aviso en pantalla (incluso entrando o saliendo)?</summary>
    public bool AvisoVisible => avisoActivo != null;

    /// <summary>¿El aviso visible es de error?</summary>
    public bool AvisoEsError { get; private set; }

    /// <summary>¿Hay ahora mismo un aviso de error en pantalla?</summary>
    public bool HayAvisoDeError => avisoActivo != null && AvisoEsError;

    private int cuadroUltimoAviso = -1;      // cuadro del último Avisar (otro aviso del mismo cuadro es más específico)
    private bool silenciarCierreSesion;      // el próximo cierre de sesión no se anuncia

    /// <summary>
    /// El próximo cierre de sesión no muestra "Sesión de administrador cerrada.". Para cierres que el usuario
    /// no pidió ver (p. ej. un login que terminó bien cuando ya había salido de la pantalla de acceso).
    /// </summary>
    public void SilenciarProximoCierreDeSesion() => silenciarCierreSesion = true;

    // Aviso = tarjeta (patrón raíz + cara): la raíz (el padre del texto) queda invisible, la sombra suave es su
    // primer hijo y la "Cara" redondeada pinta FondoAviso (o Rojo si es error). El ícono se crea al primer error.
    private RectTransform raizAviso;
    private CanvasGroup grupoAviso;
    private UnityEngine.UI.Image fondoAviso, iconoAviso;   // fondoAviso = la cara
    private Vector4 margenTextoNormal;
    private TextAlignmentOptions alineacionTextoNormal = TextAlignmentOptions.Center;
    private bool estiloAvisoLeido;

    private void PrepararAviso()
    {
        if (estiloAvisoLeido || textoAviso == null) return;
        estiloAvisoLeido = true;
        margenTextoNormal = textoAviso.margin;
        alineacionTextoNormal = textoAviso.alignment;
        textoAviso.raycastTarget = false;
        UnityEngine.UI.Image raiz = textoAviso.transform.parent != null
            ? textoAviso.transform.parent.GetComponent<UnityEngine.UI.Image>() : null;
        if (raiz == null) return;   // sin fondo: solo texto (sin animación)

        raizAviso = raiz.rectTransform;
        Transform existente = raizAviso.Find("Cara");
        if (existente != null) fondoAviso = existente.GetComponent<UnityEngine.UI.Image>();
        if (fondoAviso == null)
        {
            fondoAviso = UIRapida.Panel("Cara", raizAviso, UIRapida.FondoAviso);
            UIRapida.Redondear(fondoAviso, UIRapida.RadioTarjeta);
            fondoAviso.raycastTarget = false;
            fondoAviso.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            UIRapida.Estirar(fondoAviso.rectTransform);
            fondoAviso.transform.SetAsFirstSibling();
            UIRapida.SombraComoHija(raizAviso, UIRapida.RadioTarjeta);   // queda detrás de la cara
        }
        raiz.sprite = null;
        raiz.color = UIRapida.Transparente;
        raiz.raycastTarget = false;

        grupoAviso = raizAviso.GetComponent<CanvasGroup>();
        if (grupoAviso == null) grupoAviso = raizAviso.gameObject.AddComponent<CanvasGroup>();
        grupoAviso.blocksRaycasts = false;   // el aviso nunca tapa toques
        grupoAviso.interactable = false;
        if (avisoActivo == null) raizAviso.gameObject.SetActive(false);
    }

    private void EstiloAviso(bool error)
    {
        AvisoEsError = error;
        if (fondoAviso == null) return;

        fondoAviso.color = error ? UIRapida.Rojo : UIRapida.FondoAviso;   // blanco sobre Rojo: 5,6:1
        if (error && iconoAviso == null)
        {
            iconoAviso = UIRapida.Panel("IconoError", fondoAviso.transform, UIRapida.SobreColor);
            iconoAviso.sprite = UIRapida.Icono("alerta");   // círculo con "!" (la X se lee como "cerrar")
            iconoAviso.preserveAspect = true;
            iconoAviso.raycastTarget = false;
            UIRapida.Anclar(iconoAviso.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                            new Vector2(UIRapida.Esp3, 0), new Vector2(UIRapida.TamIconoChico, UIRapida.TamIconoChico));
        }
        if (iconoAviso != null) iconoAviso.gameObject.SetActive(error);
        // Con ícono, el texto deja libre su espacio a la izquierda (más un respiro Esp2) y se alinea a la izquierda
        textoAviso.margin = error
            ? new Vector4(UIRapida.Esp3 + UIRapida.TamIconoChico + UIRapida.Esp2, margenTextoNormal.y, margenTextoNormal.z, margenTextoNormal.w)
            : margenTextoNormal;
        textoAviso.alignment = error ? TextAlignmentOptions.MidlineLeft : alineacionTextoNormal;
    }

    // Entra con UIRapida.Aparecer y sale con UIRapida.Desaparecer (anidadas: al reemplazar el aviso se detienen juntas)
    private IEnumerator MostrarAviso(string mensaje, bool animarEntrada)
    {
        if (animarEntrada && raizAviso != null) yield return UIRapida.Aparecer(raizAviso, grupoAviso);
        // ~0,06 s por carácter; tiempo real para que no dependa de timeScale
        int largo = mensaje != null ? mensaje.Length : 0;
        // Un error se lee con calma: al menos 6 s (los demás, al menos 3 s)
        float minimo = Mathf.Max(AvisoEsError ? 6f : 3f, segundosAviso);
        yield return new WaitForSecondsRealtime(Mathf.Clamp(2.5f + largo * 0.06f, minimo, Mathf.Max(8f, minimo)));
        if (raizAviso != null) yield return UIRapida.Desaparecer(raizAviso.gameObject, raizAviso, grupoAviso);
        textoAviso.text = "";
        avisoActivo = null;
    }

    // Las corrutinas se detienen al desactivar el objeto: el aviso no queda congelado a medias
    private void OnDisable()
    {
        if (avisoActivo == null) return;
        avisoActivo = null;
        if (textoAviso != null) textoAviso.text = "";
        if (raizAviso != null)
        {
            UIRapida.TerminarAparicion(raizAviso, grupoAviso);
            raizAviso.gameObject.SetActive(false);
        }
    }

    // Al cerrarse o vencer la sesión DENTRO de las pantallas de administrador el aviso genérico sale de aquí y
    // se vuelve al menú principal (raíz): así Atrás no manda la app al fondo desde el mapa. Fuera de ellas
    // (p. ej. en el mapa) no se avisa: el cierre que pide el usuario lo confirma InterfazCampus.
    // El genérico va ANTES de IrRaiz: al ocultarse, el formulario guarda el borrador y avisa algo más útil
    // ("Se guardó lo que escribiste…"), que queda encima. No pisa un error visible ni un aviso del mismo cuadro.
    private void AlCambiarSesion(bool activa)
    {
        if (activa) return;

        bool silenciar = silenciarCierreSesion;
        silenciarCierreSesion = false;
        bool enZonaAdmin = EnZonaAdmin();
        if (enZonaAdmin && !silenciar && !HayAvisoDeError && cuadroUltimoAviso != Time.frameCount)
            Avisar("Sesión de administrador cerrada.");

        if (enZonaAdmin) IrRaiz(pantallaInicial);
    }

    /// <summary>¿Un cierre de sesión ahora mismo lo anunciaría el UIManager? (solo dentro de las pantallas de admin)</summary>
    public bool AnunciaCierreDeSesion => EnZonaAdmin();

    private bool EnZonaAdmin()
    {
        bool enZona = pila.Count > 0 && pila[pila.Count - 1].nombre == pantallaLogin;
        for (int i = 0; i < pila.Count && !enZona; i++) enZona = pila[i].requiereAdmin;
        return enZona;
    }

    private Pantalla Buscar(string nombre)
    {
        Pantalla p = null;
        for (int i = 0; i < pantallas.Count; i++)
            if (string.Equals(pantallas[i].nombre, nombre, StringComparison.OrdinalIgnoreCase)) { p = pantallas[i]; break; }

        if (p == null || p.panel == null)
        {
            Debug.LogWarning($"UIManager: la pantalla '{nombre}' no existe o no tiene panel asignado.");
            return null;
        }
        return p;
    }
}
