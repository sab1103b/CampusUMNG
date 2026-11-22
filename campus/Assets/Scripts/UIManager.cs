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
    [SerializeField] private string pantallaPublica = "Usuario";     // a donde se vuelve si se cierra o vence la sesión
    [SerializeField] private string pantallaLogin = "AdminLogin";

    [Header("Referencias")]
    [SerializeField] private AdminService admin;
    [Tooltip("Texto para avisos cortos. Ponlo en la raíz del Canvas, fuera de los paneles.")]
    [SerializeField] private TMP_Text textoAviso;
    [SerializeField] private float segundosAviso = 3f;

    private readonly List<Pantalla> pila = new List<Pantalla>();
    private Coroutine avisoActivo;

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
        Pantalla p = pila.Find(x => string.Equals(x.nombre, nombre, StringComparison.OrdinalIgnoreCase));
        return p != null && p.panel != null && p.panel.activeSelf;
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
        if (teclado != null && teclado.escapeKey.wasPressedThisFrame) Atras();
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

        if (pila.Count > 0 && pila[pila.Count - 1] == destino) return;   // ya estás ahí

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
    public void Avisar(string mensaje)
    {
        if (textoAviso == null) return;
        if (avisoActivo != null) StopCoroutine(avisoActivo);
        avisoActivo = StartCoroutine(MostrarAviso(mensaje));
    }

    private IEnumerator MostrarAviso(string mensaje)
    {
        textoAviso.text = mensaje;
        yield return new WaitForSeconds(segundosAviso);
        textoAviso.text = "";
        avisoActivo = null;
    }

    // Si la sesión de administrador se cierra o vence mientras estás en una pantalla de admin,
    // se vuelve a la pantalla pública.
    private void AlCambiarSesion(bool activa)
    {
        if (activa) return;

        bool enZonaAdmin = pila.Exists(p => p.requiereAdmin) ||
                           (pila.Count > 0 && pila[pila.Count - 1].nombre == pantallaLogin);
        if (!enZonaAdmin) return;

        IrRaiz(pantallaPublica);
        Avisar("Sesión de administrador cerrada.");
    }

    private Pantalla Buscar(string nombre)
    {
        Pantalla p = pantallas.Find(x => string.Equals(x.nombre, nombre, StringComparison.OrdinalIgnoreCase));

        if (p == null || p.panel == null)
        {
            Debug.LogWarning($"UIManager: la pantalla '{nombre}' no existe o no tiene panel asignado.");
            return null;
        }
        return p;
    }
}
