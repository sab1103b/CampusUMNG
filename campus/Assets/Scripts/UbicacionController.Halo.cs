using UnityEngine;

/// <summary>"Tú estás aquí": halo de precisión (disco en el suelo bajo el marcador).</summary>
public partial class UbicacionController
{
    // Halo de precisión: disco plano en el suelo, RutaColor a AlfaHalo con borde más opaco (AlfaBordeHalo).
    // Radio en metros reales: la precisión del GPS (real) o RadioAproximadoM (aproximada).
    // El relleno es muy tenue (0,14) para no confundirse con la línea de la ruta (mismo azul). El borde
    // pulsa suave (PeriodoPulso s, Time.unscaledTime) entre AlfaBordeMin y AlfaBordeHalo, pero SOLO
    // cuando la app ya dibuja a 60 fps por otro motivo (CamaraMapa.Ahorrando = false): el pulso nunca
    // despierta la cámara; en ahorro el borde queda fijo en AlfaBordeHalo.
    // Ubicación aproximada: el marcador no cambia de color; el halo es más grande (RadioAproximadoM) y su
    // borde queda tenue y fijo (AlfaBordeAproximado, sin pulso): "no sabemos exactamente dónde estás".
    private const float AlfaHalo = 0.14f;
    private const float AlfaBordeHalo = 0.6f;
    private const float AlfaBordeMin = 0.3f;
    private const float AlfaBordeAproximado = 0.22f;
    private const float PeriodoPulso = 1.8f;
    private const int LadosHalo = 48;
    private static Color[] coloresHalo;     // colores de la malla (el borde se reescribe al pulsar)
    private static float alfaBordeEscrito = -1f;
    private const float RadioAproximadoM = 40f;
    private const float RadioMinimoM = 5f, RadioMaximoM = 150f;
    private Transform halo;
    private float zoomHalo = float.NaN;
    private float unidadesPorMetroHalo = 1f;
    private Vector3 posHaloEscrita;
    private float escalaHaloEscrita = -1f;
    private static Mesh mallaHalo;          // compartidos por todas las instancias
    private static Material materialHalo;

    // Disco en el suelo bajo el marcador. Sin colisionador (los toques siguen yendo al mapa/marcador).
    // El transform solo se escribe si cambió la posición o el tamaño.
    private void ActualizarHalo(float ySuelo)
    {
        float radioM = estado == Estado.Aproximada ? RadioAproximadoM : PrecisionM;
        if (radioM <= 0f)   // simulada (precisión 0): sin halo
        {
            if (halo != null && halo.gameObject.activeSelf) halo.gameObject.SetActive(false);
            return;
        }

        if (halo == null) CrearHalo();
        if (!halo.gameObject.activeSelf) halo.gameObject.SetActive(true);

        float zoom = mapa.Zoom;
        if (zoom != zoomHalo)
        {
            zoomHalo = zoom;
            unidadesPorMetroHalo = GeoMapa.UnidadesPorMetro(mapa);
        }

        float escala = Mathf.Clamp(radioM, RadioMinimoM, RadioMaximoM) * unidadesPorMetroHalo;
        Vector3 m = marcador.position;
        Vector3 pos = new Vector3(m.x, ySuelo + 0.02f * unidadesPorMetroHalo, m.z);   // apenas sobre el suelo
        if (!((pos - posHaloEscrita).sqrMagnitude <= 1e-6f)) { halo.position = pos; posHaloEscrita = pos; }
        if (Mathf.Abs(escala - escalaHaloEscrita) > 1e-4f)
        {
            halo.localScale = new Vector3(escala, 1f, escala);
            escalaHaloEscrita = escala;
        }

        PulsarBordeHalo(estado == Estado.Aproximada);
    }

    // Reescribe el alfa del anillo exterior solo si cambió de forma perceptible (≥ 0,02).
    // Lo llama Update a su ritmo normal; nunca llama CamaraMapa.Despertar.
    private static void PulsarBordeHalo(bool aproximada)
    {
        if (mallaHalo == null || coloresHalo == null) return;
        bool fijo = aproximada || CamaraMapa.Ahorrando;
        float alfa = aproximada ? AlfaBordeAproximado : AlfaBordeHalo;
        if (!fijo)
        {
            float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PeriodoPulso));
            alfa = Mathf.Lerp(AlfaBordeMin, AlfaBordeHalo, s);
        }
        // (un valor fijo se escribe exacto aunque la diferencia con el anterior sea pequeña)
        if (alfa == alfaBordeEscrito || (!fijo && Mathf.Abs(alfa - alfaBordeEscrito) < 0.02f)) return;
        alfaBordeEscrito = alfa;

        Color borde = UIRapida.ConAlfa(UIRapida.RutaColor, alfa);
        for (int i = 1 + LadosHalo; i < coloresHalo.Length; i++) coloresHalo[i] = borde;
        mallaHalo.colors = coloresHalo;
    }

    private void CrearHalo()
    {
        GameObject go = new GameObject("HaloUbicacion");
        halo = go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = MallaHalo();
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = MaterialHalo();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        posHaloEscrita = new Vector3(float.NaN, 0f, 0f);   // fuerza la primera escritura
        escalaHaloEscrita = -1f;
    }

    // Material transparente compartido. Sprites/Default está en "Always Included Shaders" (viaja en el APK),
    // usa el color de vértice (relleno y borde en una sola malla) y URP lo dibuja como unlit transparente.
    private static Material MaterialHalo()
    {
        if (materialHalo != null) return materialHalo;
        Shader s = Shader.Find("Sprites/Default");
        if (s != null) materialHalo = new Material(s);
        else
        {
            // Respaldo: material de color de la escena (puede verse opaco si su shader no es transparente)
            Material respaldo = GeoMapa.Material(UIRapida.RutaColor);
            if (respaldo == null) return null;   // sin ningún shader disponible: el halo no se dibuja
            materialHalo = new Material(respaldo);
            GeoMapa.PonerColor(materialHalo, UIRapida.ConAlfa(UIRapida.RutaColor, AlfaHalo));
        }
        materialHalo.name = "HaloUbicacion";
        return materialHalo;
    }

    // Disco de radio 1 en el plano XZ: relleno RutaColor a AlfaHalo y anillo exterior (8 % del radio) a AlfaBordeHalo
    private static Mesh MallaHalo()
    {
        if (mallaHalo != null) return mallaHalo;
        const int lados = LadosHalo;
        const float radioBorde = 0.92f;
        Color relleno = UIRapida.ConAlfa(UIRapida.RutaColor, AlfaHalo);
        Color borde = UIRapida.ConAlfa(UIRapida.RutaColor, AlfaBordeHalo);

        // 0 = centro · 1..lados = anillo del relleno · luego anillos interior y exterior del borde
        int n = 1 + lados * 3;
        Vector3[] v = new Vector3[n];
        Color[] c = new Color[n];
        int[] t = new int[lados * 9];
        v[0] = Vector3.zero; c[0] = relleno;
        for (int i = 0; i < lados; i++)
        {
            float a = i * Mathf.PI * 2f / lados;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[1 + i] = dir * radioBorde;          c[1 + i] = relleno;
            v[1 + lados + i] = dir * radioBorde;  c[1 + lados + i] = borde;
            v[1 + lados * 2 + i] = dir;           c[1 + lados * 2 + i] = borde;
        }
        int k = 0;
        for (int i = 0; i < lados; i++)
        {
            int j = (i + 1) % lados;
            t[k++] = 0; t[k++] = 1 + j; t[k++] = 1 + i;
            int a0 = 1 + lados + i, a1 = 1 + lados + j, b0 = 1 + lados * 2 + i, b1 = 1 + lados * 2 + j;
            t[k++] = a0; t[k++] = a1; t[k++] = b0;
            t[k++] = a1; t[k++] = b1; t[k++] = b0;
        }
        mallaHalo = new Mesh { name = "HaloUbicacion" };
        mallaHalo.MarkDynamic();   // el borde cambia de color al pulsar
        mallaHalo.vertices = v;
        mallaHalo.colors = c;
        coloresHalo = c;
        alfaBordeEscrito = AlfaBordeHalo;
        mallaHalo.triangles = t;
        mallaHalo.RecalculateBounds();
        return mallaHalo;
    }

    // Al destruir el controlador: el halo y su malla/material (creados por código) no quedan huérfanos.
    // Solo hay un "Tú estás aquí" en la escena; si hubiera otro, MallaHalo/MaterialHalo los recrean.
    private void DestruirHalo()
    {
        if (halo != null) Destroy(halo.gameObject);
        halo = null;
        if (mallaHalo != null) { Destroy(mallaHalo); mallaHalo = null; }
        coloresHalo = null;
        alfaBordeEscrito = -1f;
        if (materialHalo != null) { Destroy(materialHalo); materialHalo = null; }
    }
}
