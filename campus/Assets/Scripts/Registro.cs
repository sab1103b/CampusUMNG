using System.Diagnostics;

/// <summary>
/// Mensajes informativos de consola que solo existen en el Editor y en builds de desarrollo.
/// En el APK de entrega las llamadas a <see cref="Info"/> desaparecen al compilar (no cuestan nada).
/// Las advertencias y errores siguen usando Debug.LogWarning / Debug.LogError.
/// </summary>
public static class Registro
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Info(string mensaje) => UnityEngine.Debug.Log(mensaje);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Info(string mensaje, UnityEngine.Object contexto) => UnityEngine.Debug.Log(mensaje, contexto);
}
