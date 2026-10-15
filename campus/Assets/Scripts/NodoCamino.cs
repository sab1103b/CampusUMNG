using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Un punto de la red de caminos. Por defecto es solo un punto de PASO: sirve para dar forma
/// a curvas, esquinas y cruces, y no significa que haya un lugar ahí.
/// Si además es una ENTRADA de un lugar, escribe el id de ese lugar (un lugar puede tener varias entradas).
/// </summary>
public class NodoCamino : MonoBehaviour
{
    [Tooltip("Solo si este punto es una ENTRADA de un lugar: escribe el id de ese lugar. " +
             "Un lugar puede tener varias entradas. 0 = punto de paso normal.")]
    [FormerlySerializedAs("lugarId")]
    public int entradaDeLugarId = 0;

    [Tooltip("Nodos a los que se puede ir desde este. Basta conectar en un solo sentido.")]
    public List<NodoCamino> conexiones = new List<NodoCamino>();

    [SerializeField] private float radioGizmo = 1.5f;

    public bool EsEntrada => entradaDeLugarId > 0;

    // Al agregar el componente, empieza como punto de paso normal
    private void Reset() => entradaDeLugarId = 0;

    private void OnValidate()
    {
        if (entradaDeLugarId < 0) entradaDeLugarId = 0;   // un id negativo no tiene sentido
    }

    private void OnDrawGizmos()
    {
        // Naranja y grande = entrada de un lugar. Azul y pequeño = punto de paso.
        bool entrada = EsEntrada;
        Gizmos.color = entrada ? new Color(1f, 0.6f, 0f) : new Color(0.1f, 0.6f, 1f);
        Gizmos.DrawSphere(transform.position, entrada ? radioGizmo : radioGizmo * 0.5f);

        Gizmos.color = new Color(0.1f, 0.6f, 1f);
        foreach (NodoCamino c in conexiones)
        {
            if (c != null) Gizmos.DrawLine(transform.position, c.transform.position);
        }

#if UNITY_EDITOR
        if (entrada)
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f,
                $"{name} (entrada del lugar {entradaDeLugarId})");
#endif
    }
}