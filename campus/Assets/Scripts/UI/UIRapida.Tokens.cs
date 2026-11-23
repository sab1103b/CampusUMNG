using UnityEngine;

/// <summary>
/// UIRapida (tokens): paleta, escala tipográfica, radios, espaciado y medidas fijas de componentes.
/// Las fábricas (Panel, Boton, Campo…) están en UIRapida.cs; la tipografía en UIRapida.Tipografia.cs;
/// los sprites e íconos en UIRapida.Iconos.cs.
/// </summary>
public static partial class UIRapida
{
    // Paleta (contraste WCAG calculado entre paréntesis). Todos los colores en Hex: un solo formato para toda la app.
    public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
    /// <summary>El mismo color con otra opacidad (selecciones, halos).</summary>
    public static Color ConAlfa(Color c, float alfa) { c.a = alfa; return c; }

    public static readonly Color Verde = Hex("#1c7126");        // verde oscuro de la marca (blanco encima: 6,1:1)
    /// <summary>Texto e íconos SOBRE colores sólidos (Verde 6,1:1, Rojo 5,6:1, RutaColor 4,9:1). No es fondo: para fondos, SuperficieOpaca.</summary>
    public static readonly Color SobreColor = Hex("#ffffff");
    public static readonly Color Oscuro = Hex("#3d5245");       // texto principal (8,5:1 sobre blanco)
    public static readonly Color Menta = Hex("#adc1ad");        // verde claro de la marca (botones secundarios, etiquetas)
    // Texto/íconos SOBRE Menta: 5,9:1 en reposo y 5,3:1 presionado (Menta × PresionadoClaro); el verde normal solo da 3,2:1
    public static readonly Color SobreMenta = Hex("#0f4416");
    public static readonly Color Gris = Hex("#5f6963");         // SOLO texto secundario (5,6:1 sobre blanco); nunca fondo de botón
    public static readonly Color Rojo = Hex("#b83d2e");         // errores y acciones destructivas (5,6:1 sobre blanco)
    public static readonly Color RojoSobreMenta = Hex("#7a1f15");   // rojo para íconos/texto sobre Menta (5,4:1; 4,8:1 presionado)
    public static readonly Color FondoCampo = Hex("#f0f5f0");   // campos de texto, filas y botón de cerrar sobre fondo claro
    /// <summary>
    /// Contorno de los campos de texto en reposo (4,52:1 sobre blanco y 3,63:1 sobre el campo presionado, WCAG 1.4.11).
    /// Con foco el contorno pasa a Verde y <see cref="GrosorFocoCampo"/>; con error, a Rojo. Ver <see cref="BordeDe"/> e <see cref="IndicadorFoco"/>.
    /// </summary>
    public static readonly Color BordeCampo = Hex("#6b7a70");
    public const float GrosorBordeCampo = 2f;   // grosor del contorno de los campos (unidades de canvas)
    public const float GrosorFocoCampo = 3f;    // grosor del contorno con foco o con error (se nota sin depender solo del color)
    public static readonly Color Superficie = Hex("#fffffff5"); // hojas/tarjetas blancas flotando sobre el mapa (96 %)
    public static readonly Color SuperficieOpaca = Hex("#ffffff");  // hojas que tapan el mapa por completo (ficha, perfil, formularios)
    public static readonly Color Transparente = Hex("#ffffff00");
    /// <summary>Naranja de las PUERTAS en el editor del mapa (nodos que son puerta de un lugar). Símbolo, no texto (4,2:1 sobre blanco).</summary>
    public static readonly Color Acento = Hex("#b36b00");
    public static readonly Color Seleccion = Hex("#ffd23f");    // elemento seleccionado en el editor (no es texto)
    public static readonly Color Presionado = Hex("#d9d9d9");   // tinte al presionar botones de color sólido
    /// <summary>
    /// Tinte al presionar botones/chips claros (blanco, FondoCampo, Menta). Con #edf2ed el texto sigue legible al presionar:
    /// Gris sobre FondoCampo 4,6:1, Gris sobre blanco 5,0:1, SobreMenta sobre Menta 5,3:1, Oscuro 6,8:1.
    /// </summary>
    public static readonly Color PresionadoClaro = Hex("#edf2ed");
    /// <summary>
    /// Tinte de respaldo (ColorBlock.disabledColor) de botones desactivados SIN <see cref="EstadoDeshabilitado"/>:
    /// opaco y distinto de <see cref="Presionado"/>. Para desactivar un botón usa EstadoDeshabilitado, que además
    /// cambia el fondo a <see cref="FondoDeshabilitado"/> y el texto a <see cref="TextoDeshabilitado"/> (≥4,5:1).
    /// </summary>
    public static readonly Color Deshabilitado = Hex("#c9d3cc");
    /// <summary>Fondo plano de un botón desactivado (gris verdoso claro, se distingue de Menta y de Presionado).</summary>
    public static readonly Color FondoDeshabilitado = Hex("#d6ded8");
    /// <summary>Texto/ícono de un botón desactivado: Oscuro sobre FondoDeshabilitado (6,1:1).</summary>
    public static readonly Color TextoDeshabilitado = Oscuro;
    /// <summary>Fondo del aviso flotante (toast): Oscuro al 92 % (texto blanco encima: 8,4:1 sobre el tono pleno).</summary>
    public static readonly Color FondoAviso = ConAlfa(Oscuro, 0.92f);
    public static readonly Color Separador = Hex("#e0e6e3");    // líneas divisorias
    public static readonly Color Sombra = Hex("#00000029");     // sombra de tarjetas y botones flotantes (16 %)
    public static readonly Color Velo = Hex("#0000004d");       // fondo oscuro detrás de modales (30 %)
    /// <summary>RutaColor = la ruta (línea y barra de ruta) y tu posición (halo). Destaca sobre el verde del mapa.</summary>
    public static readonly Color RutaColor = Hex("#1f6fd1");
    /// <summary>DestinoColor = el pin del destino (Verde con borde blanco: es un "lugar del campus", no la ruta).</summary>
    public static readonly Color DestinoColor = Verde;
    /// <summary>Color de la selección de texto en todos los campos (Verde al 25 %).</summary>
    public static readonly Color SeleccionTexto = ConAlfa(Verde, 0.25f);
    public const float DuracionFundido = 0.08f;                 // fadeDuration de los botones
    /// <summary>Aparición de modales (<see cref="Aparecer"/>): escala 0,96→1 y alfa 0→1.</summary>
    public const float DuracionAparicion = 0.18f;
    /// <summary>Cierre de modales (<see cref="Desaparecer"/>): más corto que la aparición (salir se siente rápido).</summary>
    public const float DuracionDesaparicion = 0.12f;

    // Selección (UN solo estilo para chips de categoría, pestañas de tipo del formulario y opciones de lista):
    // elegido = Verde con texto blanco (6,1:1); no elegido = Menta con texto SobreMenta (5,9:1). Se eligió Menta (y no
    // Superficie) porque se distingue tanto sobre el mapa como sobre hojas blancas. Aplícalo con UIRapida.MarcarSeleccion.
    public static readonly Color SeleccionadoFondo = Verde;
    public static readonly Color SeleccionadoTexto = SobreColor;
    public static readonly Color NoSeleccionadoFondo = Menta;
    public static readonly Color NoSeleccionadoTexto = SobreMenta;

    // Microcopia: TODOS los botones e ítems de menú van en tipo oración ("Guardar", "Agregar lugar", "Cerrar sesión").
    // La acción principal se distingue por color (Verde) y peso (Bold), no por mayúsculas.

    /// <summary>Tamaño mínimo de un área táctil (≈ 48 dp a la resolución de referencia 1080×1920).</summary>
    public const float ToqueMinimo = 96f;

    // Escala tipográfica (unidades de canvas, referencia 1080×1920). Nada de contenido por debajo de TamMinimo.
    // Seis pasos: 48 · 44 · 36 · 32 · 26 · 22. Las etiquetas en MAYÚSCULAS y los chips usan TamSecundario (26)
    // con EspaciadoMayusculas: el paso de 24 se eliminó por estar a 2 unidades del 26 (no se distinguían).
    /// <summary>
    /// 44 NO es un paso propio de la escala: es el MÍNIMO del autoajuste del nombre en la ficha
    /// (el nombre se muestra en TamTitulo y solo baja hasta 44 si no cabe).
    /// </summary>
    public const float TamNombreFicha = 44f;
    public const float TamTitulo = 48f;      // títulos de barra / pantalla
    public const float TamSubtitulo = 36f;   // nombre de lugar, encabezados de tarjeta, títulos de modal
    public const float TamCuerpo = 32f;      // texto de contenido y campos
    public const float TamSecundario = 26f;  // textos de apoyo, botones secundarios, etiquetas en mayúsculas, chips
    public const float TamMinimo = 22f;      // mínimo del autoajuste

    // Radios de esquina
    /// <summary>
    /// Radio de píldora para un alto de <see cref="ToqueMinimo"/>. Para que un botón sea píldora con CUALQUIER alto
    /// (p. ej. AltoBotonPrincipal) usa <see cref="RedondearPildora"/>, que toma la mitad del alto real.
    /// </summary>
    public const float RadioPildora = 48f;   // botones principales
    public const float RadioTarjeta = 36f;   // tarjetas, hojas, modales
    public const float RadioControl = 24f;   // campos, botones secundarios, listas
    public const float RadioEtiqueta = 14f;  // chips y etiquetas
    public const float RadioMinimo = 6f;     // barras finas y asas

    // Espaciado e íconos
    // Escala de espaciado: Esp1..Esp4 = 8 / 16 / 24 / 32 (pasos de 8).
    public const float Esp1 = 8f, Esp2 = 16f, Esp3 = 24f, Esp4 = 32f;
    /// <summary>Margen = 36: excepción a la escala de 8, solo para el borde exterior de tarjetas y pantallas.</summary>
    public const float Margen = 36f;
    public const float AltoBarraSuperior = 150f;
    // Escala de íconos: 44 (chico) · 56 (botón/acción) · 64 (grande).
    public const float TamIconoChico = 44f;      // íconos en selectores y filas secundarias
    public const float TamIconoBoton = 56f;      // ícono dentro de un botón y acciones de fila (editar/eliminar)
    public const float TamIconoGrande = 64f;     // íconos destacados (encabezados, barra de ruta)
    public const float TamAvatar = 92f;          // círculo de avatar / ícono de encabezado de tarjeta
    public const float AltoEncabezadoTarjeta = 116f;
    public const float AltoBotonPrincipal = 112f;
    public const float SombraY = 8f, SombraExtra = 12f;   // desplazamiento y crecimiento de las sombras de tarjeta
    public const float EspaciadoMayusculas = 4f;           // characterSpacing solo para etiquetas en MAYÚSCULAS
    public const float TamBotonMapa = 130f;                // botones redondos sobre el mapa (zoom, ubicación, 2D/3D)
    public const float MargenControlesMapa = Esp4;         // separación de los controles del mapa al borde derecho

    // Medidas fijas de componentes (antes eran literales sueltos)
    public const float AnchoModal = 880f;          // ancho de los modales centrados (confirmar, formularios de admin)
    /// <summary>Alto de los modales de lista (UIRapida.ModalLista) como fracción del alto de la pantalla.</summary>
    public const float AltoRelModalLista = 0.6f;
    public const float AltoSeparador = 2f;         // grosor de las líneas divisorias
    public const float TamPinMapa = 110f;          // pin arrastrable / marcador de lugar sobre el mapa
    public const float AltoFilaMenu = ToqueMinimo; // filas de menú y listas táctiles
    public const float AnchoAsa = 90f, AltoAsa = 10f;   // asa de arrastre de las hojas inferiores
    public const float AltoCampoMultilinea = 150f; // campo de texto de varias líneas
    public const float AltoEtiquetaCampo = 40f;    // etiqueta encima de un campo
    public const float AltoTituloModal = 90f;      // franja del título de un modal (hasta dos líneas: título + detalle)
    // Compartidas entre la ejecución (InterfazCampus) y el integrador del Editor (IntegradorInterfaz)
    public const float AltoMaxFicha = 820f;        // alto máximo de la ficha de lugar
    public const float AltoAviso = 120f;           // alto del aviso flotante (toast)
    public const float AnchoBotonTerminar = 2f * ToqueMinimo + Margen;   // "Terminar" de la barra de ruta (TamCuerpo con aire)
    public const float AltoBarraRuta = 190f;       // barra de ruta sobre el mapa (indicación + "Terminar"), en ejecución y en el integrador
    public const float AltoMaxResultados = 900f;   // alto máximo de la lista de resultados de búsqueda
}
