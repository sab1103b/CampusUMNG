using System;
using System.Collections.Generic;
using System.Globalization;
using SQLite4Unity3d;

/// <summary>
/// Cuentas y espacios de administrador (login, registro, códigos).
///
/// Límite conocido (sin servidor): cada celular tiene su propia copia de la base, así que un código de
/// registro es de UN SOLO USO POR CELULAR, no global. El mismo código, todavía libre en el APK, puede
/// registrar una cuenta en dos celulares distintos (cada uno solo ve su base). Al actualizar el APK, la
/// cuenta del celular pasa a la base nueva solo si ese espacio sigue libre en la maestra
/// (BaseDatosLocal.ConservarDatosDelCelular); si no, se respalda la base y se avisa en el login
/// (BaseDatosLocal.AvisoPendiente). Mitigación hoy: el dueño entrega cada código a una sola persona.
/// Evolución futura: validar el registro (y marcar el código como usado) en un servidor.
/// </summary>
public static partial class RepositorioCampus
{
    // ==================================================================
    // Administradores
    // ==================================================================
    // ----- Login en dos pasos (AdminService hace el paso 2 fuera del hilo principal) -----
    // 1. LeerCuentaParaLogin: hilo principal (SQLite solo se usa ahí).
    // 2. VerificarLlave: solo PBKDF2, sin tocar la base; se puede ejecutar en otro hilo.

    /// <summary>Copia de la cuenta a verificar. 'llaveHash' null = el usuario no existe.</summary>
    public struct CuentaLogin
    {
        public string nickname;    // tal como quedó registrado
        public string llaveHash;
    }

    // Hash de relleno para cuando el usuario no existe: se verifica igual contra él, así el tiempo de
    // respuesta no revela si el usuario existe. Se calcula UNA vez (Lazy es seguro entre hilos); antes se
    // calculaba en cada intento y el usuario inexistente costaba dos PBKDF2 (más lento = se notaba).
    // Límite conocido: las cuentas con hash de 60 000 iteraciones responden algo más rápido que el relleno
    // (120 000); solo afecta a cuentas creadas antes de subir las iteraciones.
    // Limitación conocida (rehash): al iniciar sesión NO se vuelve a hashear la contraseña con las
    // iteraciones actuales, porque el trigger admin_inmutable bloquea cualquier UPDATE de una cuenta ya
    // registrada (intencional, no se cambia). Para subir una cuenta vieja a 120 000 iteraciones el dueño
    // elimina el espacio desde el Editor, crea uno nuevo y la persona se registra otra vez.
    private static readonly Lazy<string> HashRelleno =
        new Lazy<string>(() => SeguridadLlaves.Hashear("campus-relleno-" + Guid.NewGuid().ToString("N")));

    /// <summary>
    /// Busca la cuenta por usuario. El usuario no distingue mayúsculas ("Ana" = "ana"), igual que el registro.
    /// (Se usa lower() en la consulta y no COLLATE NOCASE en la columna: SQLite 3.6 no tiene ALTER COLUMN
    /// y reconstruir la tabla chocaría con el trigger admin_inmutable. lower() de SQLite solo cambia ASCII:
    /// por eso el registro solo admite usuarios ASCII; ver ValidarRegistro.)
    /// </summary>
    public static CuentaLogin LeerCuentaParaLogin(string nickname)
    {
        string buscado = (nickname ?? "").Trim();
        // Si una base vieja tuviera "Ana" y "ana", primero la coincidencia exacta
        List<FilaAdmin> filas = Db.Query<FilaAdmin>(
            @"SELECT nickname, llave_hash FROM administradores
              WHERE lower(nickname) = lower(?) AND llave_hash IS NOT NULL
              ORDER BY (nickname = ?) DESC, id LIMIT 1",
            buscado, buscado);
        return filas.Count > 0
            ? new CuentaLogin { nickname = filas[0].nickname, llaveHash = filas[0].llave_hash }
            : new CuentaLogin();
    }

    /// <summary>
    /// Compara la contraseña con la cuenta leída. Siempre hace exactamente UN PBKDF2 (contra el hash real o
    /// contra el de relleno). No usa la base de datos ni APIs de Unity: seguro en otro hilo.
    /// </summary>
    public static bool VerificarLlave(CuentaLogin cuenta, string llave)
    {
        bool existe = !string.IsNullOrEmpty(cuenta.llaveHash);
        bool ok = SeguridadLlaves.Verificar(llave, existe ? cuenta.llaveHash : HashRelleno.Value);
        return ok && existe;
    }

    public static bool VerificarAdmin(string nickname, string llave) => VerificarAdmin(nickname, llave, out _);

    /// <summary>Versión sincrónica (bloquea el hilo actual durante el PBKDF2). La app usa AdminService.IniciarSesion.</summary>
    public static bool VerificarAdmin(string nickname, string llave, out string nicknameGuardado)
    {
        CuentaLogin cuenta = LeerCuentaParaLogin(nickname);
        bool ok = VerificarLlave(cuenta, llave);
        nicknameGuardado = ok ? cuenta.nickname : null;
        return ok;
    }

    /// <summary>Un espacio de administrador: libre (sin usuario) o registrado.</summary>
    public struct EspacioAdmin
    {
        public int id;
        public string nickname;       // null = libre
        public string registradoEn;
        public bool Libre => string.IsNullOrEmpty(nickname);
    }

    public static List<EspacioAdmin> ListarEspaciosAdmin()
    {
        return Db.Query<FilaEspacio>("SELECT id, nickname, registrado_en FROM administradores ORDER BY id")
                 .ConvertAll(f => new EspacioAdmin { id = f.id, nickname = f.nickname, registradoEn = f.registrado_en });
    }

    public static int ContarAdminsRegistrados() =>
        Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname IS NOT NULL");

    /// <summary>
    /// Registro desde la app: el código debe existir y su espacio estar libre.
    /// Después de esto el usuario y la llave de ese espacio no se pueden cambiar (trigger en la base).
    /// </summary>
    internal static void RegistrarAdmin(string codigo, string nickname, string llave)
    {
        nickname = ValidarRegistro(codigo, nickname, llave);
        PreparacionRegistro p = PrepararRegistro(codigo, llave, LeerEspaciosParaRegistro());
        CompletarRegistro(p, nickname);
    }

    // ----- Registro en tres pasos (AdminService hace el paso 2 fuera del hilo principal) -----
    // 1. ValidarRegistro + LeerEspaciosParaRegistro: hilo principal (SQLite solo se usa ahí).
    // 2. PrepararRegistro: solo PBKDF2, sin tocar la base; se puede ejecutar en otro hilo.
    // 3. CompletarRegistro: hilo principal, escribe en una transacción.

    /// <summary>Copia de un espacio para probar el código sin consultar la base.</summary>
    public struct EspacioRegistro
    {
        public int id;
        public bool registrado;
        public string codigoHash;
    }

    /// <summary>Resultado del paso pesado del registro (PBKDF2).</summary>
    public struct PreparacionRegistro
    {
        public int espacioId;          // 0 = el código no coincide con ningún espacio
        public bool espacioRegistrado;
        public string hashLlave;       // null si no hace falta (código inválido o ya usado)
    }

    /// <summary>Valida los datos escritos y devuelve el usuario limpio. Lanza ErrorCampus si algo no sirve.</summary>
    public static string ValidarRegistro(string codigo, string nickname, string llave)
    {
        nickname = (nickname ?? "").Trim();
        if (SeguridadLlaves.NormalizarCodigo(codigo).Length == 0)
            throw new ErrorCampus("Escribe el código de verificación.", TipoErrorCampus.DatoInvalido);
        if (nickname.Length < 3 || nickname.Length > 50)
            throw new ErrorCampus("El usuario debe tener entre 3 y 50 caracteres.", TipoErrorCampus.DatoInvalido);
        // Solo ASCII: lower() de SQLite no cambia letras con tilde ni la ñ, así "José" y "JOSÉ" serían
        // usuarios distintos y el login sin mayúsculas fallaría. (Cuentas viejas con tilde siguen entrando
        // escribiendo el usuario exactamente como se registró.)
        foreach (char ch in nickname)
        {
            bool valido = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') ||
                          ch == '.' || ch == '_' || ch == '-';
            if (!valido)
                throw new ErrorCampus("El usuario solo puede tener letras sin tilde ni ñ, números, punto (.), guion (-) y guion bajo (_).",
                                      TipoErrorCampus.DatoInvalido);
        }
        ValidarLlaveNueva(llave);
        return nickname;
    }

    /// <summary>Espacios con el hash de su código: primero los libres, luego los registrados.</summary>
    public static List<EspacioRegistro> LeerEspaciosParaRegistro()
    {
        return Db.Query<FilaCodigo>(
                "SELECT id, nickname, codigo_hash FROM administradores ORDER BY (nickname IS NOT NULL), id")
            .ConvertAll(f => new EspacioRegistro
            {
                id = f.id, registrado = !string.IsNullOrEmpty(f.nickname), codigoHash = f.codigo_hash,
            });
    }

    /// <summary>
    /// Cada código tiene sal propia: no se puede buscar por hash, se prueba contra cada espacio.
    /// No usa la base de datos (seguro en otro hilo).
    /// </summary>
    public static PreparacionRegistro PrepararRegistro(string codigo, string llave, IList<EspacioRegistro> espacios)
    {
        PreparacionRegistro p = new PreparacionRegistro();
        foreach (EspacioRegistro e in espacios)
        {
            if (!SeguridadLlaves.VerificarCodigo(codigo, e.codigoHash)) continue;
            p.espacioId = e.id;
            p.espacioRegistrado = e.registrado;
            break;
        }
        if (p.espacioId > 0 && !p.espacioRegistrado) p.hashLlave = SeguridadLlaves.Hashear(llave);
        return p;
    }

    /// <summary>Escribe la cuenta en una transacción. Lanza ErrorCampus con un mensaje listo para mostrar.</summary>
    internal static void CompletarRegistro(PreparacionRegistro p, string nickname)
    {
        if (p.espacioId <= 0) throw new ErrorCampus("El código de verificación no es válido.", TipoErrorCampus.CodigoInvalido);
        if (p.espacioRegistrado || string.IsNullOrEmpty(p.hashLlave))
            throw new ErrorCampus("Este código ya fue usado para registrar una cuenta.", TipoErrorCampus.CodigoUsado);

        try
        {
            Escribir(() =>
            {
                // "Ana" y "ana" cuentan como el mismo usuario
                if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE lower(nickname) = lower(?)", nickname) > 0)
                    throw new ErrorCampus(MsgUsuarioExiste, TipoErrorCampus.UsuarioExiste);

                int n = Db.Execute(
                    @"UPDATE administradores
                      SET nickname = ?, llave_hash = ?, registrado_en = datetime('now', 'localtime')
                      WHERE id = ? AND nickname IS NULL",
                    nickname, p.hashLlave, p.espacioId);
                // Otro registro alcanzó a usar el espacio mientras se calculaba el hash (o se eliminó)
                if (n == 0) throw new ErrorCampus("Este código ya fue usado para registrar una cuenta.", TipoErrorCampus.CodigoUsado);
            });
        }
        catch (ErrorCampus e) when (e.Message == MsgUnico)
        {
            // Carrera: otro registro tomó el mismo usuario entre la revisión y el UPDATE, y lo frenó el índice
            // único idx_admin_nick_ci (o UNIQUE de nickname). El único UNIQUE que toca este UPDATE es el usuario.
            throw new ErrorCampus(MsgUsuarioExiste, TipoErrorCampus.UsuarioExiste);
        }
    }

    private const string MsgUsuarioExiste = "Ese usuario ya existe. Elige otro.";

    /// <summary>
    /// Texto de ayuda para la contraseña del registro (también es el mensaje de error si no cumple la regla).
    /// Riesgo conocido: el hash PBKDF2 de cada cuenta viaja dentro del APK (la base va empaquetada), así que
    /// alguien con el APK puede intentar adivinar contraseñas sin conexión. La regla mínima es 8 caracteres
    /// con letras y números; para la cuenta de entrega (y cualquier cuenta que se registre en la maestra antes
    /// de compilar) se recomienda una contraseña de 12 caracteres o más, sin palabras obvias.
    /// </summary>
    public const string AyudaLlave = "Usa al menos 8 caracteres con letras y números; mejor 12 o más.";

    /// <summary>Contraseña de una cuenta nueva: mínimo 8 caracteres, con al menos una letra y un número (ver AyudaLlave).</summary>
    private static void ValidarLlaveNueva(string llave)
    {
        bool letra = false, numero = false;
        foreach (char ch in llave ?? "")
        {
            if (char.IsLetter(ch)) letra = true;
            else if (char.IsDigit(ch)) numero = true;
        }
        if (string.IsNullOrEmpty(llave) || llave.Length < 8 || !letra || !numero)
            throw new ErrorCampus("La contraseña no es válida. " + AyudaLlave, TipoErrorCampus.DatoInvalido);
        if (llave.Length > 128)
            throw new ErrorCampus("La contraseña admite máximo 128 caracteres.", TipoErrorCampus.DatoInvalido);
    }

    /// <summary>Entero guardado en la tabla meta (0 si no existe o no es un número).</summary>
    private static int LeerEnteroMeta(string clave)
    {
        string valor = Db.ExecuteScalar<string>("SELECT valor FROM meta WHERE clave = ?", clave);
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    // Crear espacios solo existe en el Editor (el APK no debe poder generar códigos de registro).
#if UNITY_EDITOR
    /// <summary>
    /// Crea 'cantidad' espacios libres y devuelve sus códigos (solo se guardan como hash).
    /// En el Editor los códigos se anotan en CodigosRegistroAdmin.txt, fuera de Assets (no viaja en el APK).
    /// Los id nunca se reutilizan (meta.ultimo_id_admin): un espacio eliminado no "renace" con el mismo id,
    /// así una cuenta vieja de un celular no puede quedar emparejada por id con un espacio nuevo.
    /// </summary>
    public static List<(int id, string codigo)> CrearEspaciosAdmin(int cantidad)
    {
        List<(int, string)> creados = new List<(int, string)>();
        Db.RunInTransaction(() =>
        {
            int ultimo = Math.Max(Db.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM administradores"),
                                  LeerEnteroMeta(ClaveUltimoIdAdmin));
            for (int i = 0; i < cantidad; i++)
            {
                string codigo = SeguridadLlaves.GenerarCodigo();
                ultimo++;
                Db.Execute("INSERT INTO administradores (id, codigo_hash) VALUES (?, ?)", ultimo, SeguridadLlaves.HashCodigo(codigo));
                creados.Add((ultimo, codigo));
            }
            Db.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES (?, ?)",
                       ClaveUltimoIdAdmin, ultimo.ToString(CultureInfo.InvariantCulture));

            // Dentro de la transacción: si no se pueden anotar los códigos (disco, permisos), la excepción
            // revierte los espacios creados (un espacio cuyo código nadie conoce no serviría)
            string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            List<string> lineas = creados.ConvertAll(c => $"{c.Item1.ToString(CultureInfo.InvariantCulture)}|{c.Item2}|{fecha}");
            System.IO.File.AppendAllLines(RutaCodigos, lineas, new System.Text.UTF8Encoding(false));
        });
        BaseDatosLocal.MarcarCambio();
        return creados;
    }

    private const string ClaveUltimoIdAdmin = "ultimo_id_admin";

    /// <summary>
    /// Elimina un espacio (libre o registrado): esa cuenta pierde el acceso. Solo desde el Editor.
    /// También lo quita de meta.codigos_migrados_pbkdf2 (para que ninguna cuenta de un celular se
    /// empareje por id con él) y deja anotado el id más alto usado (los id no se reutilizan).
    /// </summary>
    public static void EliminarEspacioAdmin(int id)
    {
        Escribir(() =>
        {
            int maximo = Math.Max(Db.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM administradores"),
                                  LeerEnteroMeta(ClaveUltimoIdAdmin));
            if (Db.Execute("DELETE FROM administradores WHERE id = ?", id) == 0)
                throw new ErrorCampus("Espacio no encontrado.", TipoErrorCampus.NoEncontrado);
            Db.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES (?, ?)",
                       ClaveUltimoIdAdmin, maximo.ToString(CultureInfo.InvariantCulture));

            Dictionary<int, string> migrados = BaseDatosLocal.LeerCodigosMigrados(Db);
            if (migrados.Remove(id)) BaseDatosLocal.GuardarCodigosMigrados(Db, migrados);
        });
    }

    /// <summary>
    /// Completa meta.codigos_migrados_pbkdf2: los espacios anotados solo con su id (migración anterior, que
    /// no guardó el hash viejo) reciben la huella protegida de su hash SHA-256 original, reconstruida con el
    /// código de CodigosRegistroAdmin.txt SOLO si ese código coincide con el hash actual del espacio.
    /// Si el código del archivo no coincide, el espacio se quita de la lista (ya no es el mismo espacio).
    /// Si el archivo no tiene el código, se deja solo el id (ver BaseDatosLocal.PasarCuentasMigradas).
    /// Nada de esto se escribe en consola. Se llama al abrir la base en el Editor; sin pendientes no hace nada.
    /// </summary>
    public static void CompletarHuellasMigradas()
    {
        Dictionary<int, string> migrados = BaseDatosLocal.LeerCodigosMigrados(Db);
        if (!migrados.ContainsValue(null)) return;

        Dictionary<int, string> codigos = LeerCodigos();
        bool cambio = false;
        foreach (int id in new List<int>(migrados.Keys))
        {
            if (migrados[id] != null) continue;
            List<FilaCodigo> fila = Db.Query<FilaCodigo>("SELECT id, nickname, codigo_hash FROM administradores WHERE id = ?", id);
            if (fila.Count == 0) { migrados.Remove(id); cambio = true; continue; }   // el espacio ya no existe
            if (!codigos.TryGetValue(id, out string codigo)) continue;              // sin código: queda solo el id

            if (SeguridadLlaves.VerificarCodigo(codigo, fila[0].codigo_hash)) migrados[id] = SeguridadLlaves.HuellaCodigoViejo(codigo);
            else migrados.Remove(id);
            cambio = true;
        }
        if (cambio) Db.RunInTransaction(() => BaseDatosLocal.GuardarCodigosMigrados(Db, migrados));
    }

    /// <summary>Lugares sin ningún nodo-puerta (la ruta llega en línea recta o al punto más cercano).</summary>
    public static List<(int id, string nombre)> LugaresSinPuerta()
    {
        return Db.Query<FilaLugar>(
            @"SELECT l.id, l.nombre FROM lugares l
              WHERE NOT EXISTS (SELECT 1 FROM nodos n WHERE n.lugar_id = l.id)
              ORDER BY l.id")
            .ConvertAll(f => (f.id, f.nombre));
    }

    /// <summary>Archivo con los códigos generados (fuera de Assets: no se empaqueta).</summary>
    public static string RutaCodigos =>
        System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "CodigosRegistroAdmin.txt");

    /// <summary>id del espacio -> código, leído del archivo de códigos.</summary>
    public static Dictionary<int, string> LeerCodigos()
    {
        Dictionary<int, string> mapa = new Dictionary<int, string>();
        if (!System.IO.File.Exists(RutaCodigos)) return mapa;
        foreach (string linea in System.IO.File.ReadAllLines(RutaCodigos))
        {
            string[] p = linea.Split('|');
            if (p.Length >= 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) mapa[id] = p[1];
        }
        return mapa;
    }

    /// <summary>La primera vez (sin archivo de códigos ni espacios libres) crea los 5 espacios iniciales.</summary>
    public static void AsegurarEspaciosIniciales()
    {
        if (System.IO.File.Exists(RutaCodigos)) return;
        if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname IS NULL") > 0) return;

        CrearEspaciosAdmin(5);
        Registro.Info("Se crearon 5 espacios de administrador. Sus códigos están en: " + RutaCodigos);
    }
#endif
}
