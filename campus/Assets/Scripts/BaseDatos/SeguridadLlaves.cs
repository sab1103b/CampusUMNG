using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Hash de las llaves de administrador (PBKDF2-SHA256 con sal aleatoria).
/// La llave nunca se guarda en texto plano. Formato guardado:
///   pbkdf2-sha256$iteraciones$salBase64$hashBase64
/// </summary>
public static class SeguridadLlaves
{
    private const string Prefijo = "pbkdf2-sha256";
    // Hashes nuevos. Cada hash guarda sus propias iteraciones: los viejos (60 000) siguen sirviendo.
    // El login y el registro calculan PBKDF2 en otro hilo, así que el costo no congela la pantalla.
    private const int Iteraciones = 120000;
    // Rango aceptado al verificar: un hash manipulado con 1 iteración (débil) o con miles de millones
    // (congelaría el celular) se rechaza.
    private const int IteracionesMin = 10000;
    private const int IteracionesMax = 1000000;
    private const int BytesSal = 16;
    private const int BytesHash = 32;

    public static string Hashear(string llave)
    {
        byte[] sal = new byte[BytesSal];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(sal);

        byte[] hash = Derivar(llave, sal, Iteraciones);
        return $"{Prefijo}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string llave, string guardado)
    {
        if (string.IsNullOrEmpty(llave) || string.IsNullOrEmpty(guardado)) return false;

        string[] partes = guardado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo) return false;   // p. ej. un hash bcrypt viejo de PostgreSQL

        if (!int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iter) ||
            iter < IteracionesMin || iter > IteracionesMax)
            return false;

        try
        {
            byte[] sal = Convert.FromBase64String(partes[2]);
            byte[] esperado = Convert.FromBase64String(partes[3]);
            if (sal.Length == 0 || esperado.Length == 0) return false;
            byte[] calculado = Derivar(llave, sal, iter);
            return IgualesTiempoConstante(esperado, calculado);
        }
        catch (Exception)
        {
            // Base64 inválido, sal demasiado corta para el proveedor, etc.: nunca da acceso
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Códigos de registro de administradores
    // ------------------------------------------------------------------

    // Sin letras que se confunden (O/0, I/1, L)
    private const string AlfabetoCodigo = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Código aleatorio tipo "K7MX2-QP9TB" (unos 49 bits de azar).</summary>
    public static string GenerarCodigo()
    {
        // Muestreo con rechazo: solo bytes < 248 (8 × 31), así cada letra tiene la misma probabilidad
        int limite = 256 - 256 % AlfabetoCodigo.Length;
        byte[] azar = new byte[16];
        StringBuilder sb = new StringBuilder(11);
        int escritos = 0;

        using (var rng = RandomNumberGenerator.Create())
        {
            while (escritos < 10)
            {
                rng.GetBytes(azar);
                for (int i = 0; i < azar.Length && escritos < 10; i++)
                {
                    if (azar[i] >= limite) continue;
                    if (escritos == 5) sb.Append('-');
                    sb.Append(AlfabetoCodigo[azar[i] % AlfabetoCodigo.Length]);
                    escritos++;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Mayúsculas, sin espacios ni guiones: "k7mx2 qp9tb" = "K7MX2-QP9TB".</summary>
    public static string NormalizarCodigo(string codigo)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char ch in codigo ?? "")
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// Hash del código para guardar en la base: PBKDF2 con sal propia (mismo formato que las llaves),
    /// así una copia del APK no permite probar códigos fuera de línea.
    /// </summary>
    public static string HashCodigo(string codigo) => Hashear(NormalizarCodigo(codigo));

    /// <summary>
    /// Compara un código con su hash guardado. Acepta el formato nuevo (PBKDF2) y el viejo
    /// (SHA-256 hex de 64 caracteres) de bases anteriores.
    /// </summary>
    public static bool VerificarCodigo(string codigo, string guardado)
    {
        string normal = NormalizarCodigo(codigo);
        if (normal.Length == 0 || string.IsNullOrEmpty(guardado)) return false;
        if (EsHashCodigoViejo(guardado))
            return IgualesTiempoConstante(Encoding.ASCII.GetBytes(guardado.ToLowerInvariant()),
                                          Encoding.ASCII.GetBytes(HashCodigoViejo(normal)));
        return Verificar(normal, guardado);
    }

    /// <summary>Verdadero si el hash es del formato viejo (SHA-256 en hex, 64 caracteres).</summary>
    public static bool EsHashCodigoViejo(string guardado)
    {
        if (guardado == null || guardado.Length != 64) return false;
        foreach (char ch in guardado)
            if (!Uri.IsHexDigit(ch)) return false;
        return true;
    }

    /// <summary>
    /// Huella protegida del hash viejo (SHA-256) de un código: PBKDF2 del hex SHA-256. Se guarda en
    /// meta.codigos_migrados_pbkdf2 para reconocer, al actualizar el APK, la cuenta del celular que se
    /// registró con ESE código (y no con otro espacio que reutilizó el mismo id). Se guarda con PBKDF2
    /// y no el SHA-256 directo para no abrir una vía rápida de adivinar códigos libres desde el APK.
    /// </summary>
    public static string HuellaCodigoViejo(string codigo) => Hashear(HashCodigoViejo(NormalizarCodigo(codigo)));

    /// <summary>Verdadero si 'hashViejo' (SHA-256 hex de una base anterior) corresponde a la huella guardada.</summary>
    public static bool CoincideHuellaCodigoViejo(string hashViejo, string huella) =>
        EsHashCodigoViejo(hashViejo) && Verificar(hashViejo.ToLowerInvariant(), huella);

    // Formato viejo: SHA-256("campus-conecta:" + código normalizado). Solo para verificar.
    // SHA256Managed directo (no SHA256.Create, que busca la clase por nombre con CryptoConfig y puede
    // fallar si IL2CPP la eliminó); además se preserva en link.xml.
    private static string HashCodigoViejo(string codigoNormalizado)
    {
#pragma warning disable 0618, SYSLIB0021   // obsoleto solo en .NET 6+; en el perfil de Unity es la implementación administrada
        using (SHA256 sha = new SHA256Managed())
#pragma warning restore 0618, SYSLIB0021
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("campus-conecta:" + codigoNormalizado));
            StringBuilder sb = new StringBuilder(h.Length * 2);
            foreach (byte b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    private static byte[] Derivar(string llave, byte[] sal, int iteraciones)
    {
        using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(llave), sal, iteraciones, HashAlgorithmName.SHA256))
            return kdf.GetBytes(BytesHash);
    }

    // Compara sin salir antes de tiempo, para no revelar cuántos bytes coinciden
    private static bool IgualesTiempoConstante(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
