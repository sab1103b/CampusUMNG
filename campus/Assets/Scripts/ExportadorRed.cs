#if UNITY_EDITOR
using System.Collections.Generic;
using Mapbox.Unity.Map;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menú Campus > Guardar red de la escena en la base de datos
/// Convierte los nodos que dibujaste dentro de RedCaminos a latitud/longitud y los guarda
/// directamente en la base local (Assets/StreamingAssets/campus.db). Reemplaza toda la red
/// (nodos y caminos); no toca lugares, servicios, administradores ni búsquedas.
/// </summary>
public static class ExportadorRed
{
    [MenuItem("Campus/Guardar red de la escena en la base de datos")]
    private static void Exportar()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Campus",
                "Presiona Play, espera a que se vea el mapa y vuelve a ejecutar esto.\n\n" +
                "La conversión a latitud/longitud solo funciona con el mapa cargado.", "OK");
            return;
        }

        AbstractMap mapa = Object.FindFirstObjectByType<AbstractMap>();
        RedCaminos red = Object.FindFirstObjectByType<RedCaminos>();
        if (mapa == null || red == null)
        {
            EditorUtility.DisplayDialog("Campus", "No encuentro el Map o el objeto RedCaminos en la escena.", "OK");
            return;
        }

        NodoCamino[] nodos = red.GetComponentsInChildren<NodoCamino>();
        if (nodos.Length == 0)
        {
            EditorUtility.DisplayDialog("Campus", "No hay nodos dentro de RedCaminos.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Campus",
                $"Se reemplazará TODA la red guardada en la base de datos por los {nodos.Length} nodos de la escena.\n\n" +
                "Lugares, servicios, administradores y búsquedas no se modifican.", "Guardar", "Cancelar"))
            return;

        // Numeración de nodos: 1, 2, 3... en el orden de la jerarquía
        Dictionary<NodoCamino, int> ids = new Dictionary<NodoCamino, int>();
        for (int i = 0; i < nodos.Length; i++) ids[nodos[i]] = i + 1;

        List<LugaresService.NodoDto> filas = new List<LugaresService.NodoDto>();
        int entradas = 0;

        foreach (NodoCamino n in nodos)
        {
            var geo = mapa.WorldToGeoPosition(n.transform.position);   // x = latitud, y = longitud
            if (n.entradaDeLugarId > 0) entradas++;

            filas.Add(new LugaresService.NodoDto
            {
                id = ids[n],
                nombre = n.name,
                latitud = System.Math.Round(geo.x, 8),
                longitud = System.Math.Round(geo.y, 8),
                lugar_id = n.entradaDeLugarId,
            });
        }

        // Caminos sin repetir (a < b)
        SortedSet<(int, int)> pares = new SortedSet<(int, int)>();
        foreach (NodoCamino n in nodos)
        {
            foreach (NodoCamino c in n.conexiones)
            {
                if (c == null || !ids.ContainsKey(c)) continue;
                int a = ids[n], b = ids[c];
                if (a == b) continue;
                pares.Add(a < b ? (a, b) : (b, a));
            }
        }

        try
        {
            RepositorioCampus.ReemplazarRed(filas, new List<(int, int)>(pares), out int sinLugar);

            string aviso = sinLugar > 0
                ? $"\n\nOjo: {sinLugar} entrada(s) apuntan a un lugar que no existe; quedaron como nodos de paso."
                : "";
            Debug.Log($"Red guardada: {nodos.Length} nodos ({entradas} entradas de lugares), {pares.Count} caminos.");
            EditorUtility.DisplayDialog("Campus",
                $"Red guardada en la base de datos:\n{nodos.Length} nodos, {pares.Count} caminos.{aviso}", "OK");

            Object.FindFirstObjectByType<LugaresService>()?.Recargar();
        }
        catch (System.Exception e)
        {
            Debug.LogError("Guardar red: " + e);
            EditorUtility.DisplayDialog("Campus", "No se pudo guardar la red:\n" + e.Message, "OK");
        }
    }
}
#endif
