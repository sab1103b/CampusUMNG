using System;
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
    private const int Iteraciones = 60000;   // suficiente para un celular sin que el login se sienta lento
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

        try
        {
            int iter = int.Parse(partes[1]);
            byte[] sal = Convert.FromBase64String(partes[2]);
            byte[] esperado = Convert.FromBase64String(partes[3]);
            byte[] calculado = Derivar(llave, sal, iter);
            return IgualesTiempoConstante(esperado, calculado);
        }
        catch (FormatException)
        {
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
        byte[] azar = new byte[10];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(azar);

        StringBuilder sb = new StringBuilder(11);
        for (int i = 0; i < azar.Length; i++)
        {
            if (i == 5) sb.Append('-');
            sb.Append(AlfabetoCodigo[azar[i] % AlfabetoCodigo.Length]);
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

    /// <summary>En la base solo se guarda este hash: el código nunca queda escrito en la app.</summary>
    public static string HashCodigo(string codigo)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("campus-conecta:" + NormalizarCodigo(codigo)));
            StringBuilder sb = new StringBuilder(h.Length * 2);
            foreach (byte b in h) sb.Append(b.ToString("x2"));
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
