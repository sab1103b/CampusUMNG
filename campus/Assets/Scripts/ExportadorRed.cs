#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mapbox.Unity.Map;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menú Campus > Exportar red a SQL
/// Convierte los nodos que dibujaste en la escena a latitud/longitud y genera
/// un archivo SQL (red_inicial.sql) para cargarlos en PostgreSQL.
/// No toca las tablas lugares, servicios ni busquedas_log.
/// </summary>
public static class ExportadorRed
{
    [MenuItem("Campus/Exportar red a SQL")]
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

        // Numeración de nodos: 1, 2, 3... en el orden de la jerarquía
        Dictionary<NodoCamino, int> ids = new Dictionary<NodoCamino, int>();
        for (int i = 0; i < nodos.Length; i++) ids[nodos[i]] = i + 1;

        List<string> filasNodos = new List<string>();
        int entradas = 0;

        foreach (NodoCamino n in nodos)
        {
            var geo = mapa.WorldToGeoPosition(n.transform.position);   // x = latitud, y = longitud
            string nombre = n.name.Replace("'", "''");
            string lugar = n.entradaDeLugarId > 0 ? n.entradaDeLugarId.ToString() : "NULL";
            if (n.entradaDeLugarId > 0) entradas++;

            filasNodos.Add($"({ids[n]}, '{nombre}', {Num(geo.x)}, {Num(geo.y)}, {lugar})");
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

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("-- Generado desde Unity (Campus > Exportar red a SQL)");
        sb.AppendLine("-- Reemplaza TODA la red actual (nodos y conexiones). No toca lugares ni servicios.");
        sb.AppendLine("-- Ejecutar con: \\i 'ruta/red_inicial.sql'");
        sb.AppendLine("-- Los nodos marcados como entrada necesitan que ese lugar exista en la tabla lugares.");
        sb.AppendLine();
        sb.AppendLine("DELETE FROM conexiones;");
        sb.AppendLine("DELETE FROM nodos;");
        sb.AppendLine();
        sb.AppendLine("INSERT INTO nodos (id, nombre, latitud, longitud, lugar_id) VALUES");
        sb.AppendLine(string.Join(",\n", filasNodos) + ";");
        sb.AppendLine();

        if (pares.Count > 0)
        {
            List<string> filasCaminos = new List<string>();
            foreach ((int a, int b) in pares) filasCaminos.Add($"({a}, {b})");
            sb.AppendLine("INSERT INTO conexiones (nodo_a, nodo_b) VALUES");
            sb.AppendLine(string.Join(",\n", filasCaminos) + ";");
            sb.AppendLine();
        }

        sb.AppendLine("SELECT setval(pg_get_serial_sequence('nodos', 'id'), (SELECT MAX(id) FROM nodos));");

        string ruta = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "red_inicial.sql");
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));

        Debug.Log($"Red exportada: {nodos.Length} nodos ({entradas} entradas de lugares), {pares.Count} caminos.\n{ruta}");
        EditorUtility.RevealInFinder(ruta);
    }

    private static string Num(double v) => v.ToString("0.00000000", CultureInfo.InvariantCulture);
}
#endif