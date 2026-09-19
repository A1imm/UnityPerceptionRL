using UnityEngine;

public class CollectArea : MonoBehaviour
{
    [Header("Wymiary areny")]
    [Tooltip("Połowa szerokości/głębokości obszaru, w którym może pojawić się " +
             "platforma z obiektem do zebrania, liczona względem środka TrainingArea.")]
    [SerializeField] private float areaHalfExtent = 4.5f;

    [Tooltip("Margines odsunięcia od ścian, żeby platforma nie generowała się " +
             "dokładnie przy granicy areny.")]
    [SerializeField] private float wallMargin = 0.5f;

    [Header("Referencje")]
    [Tooltip("Transform platformy, na której stoi obiekt do zebrania. " +
             "Collectible powinien być jej dzieckiem w Hierarchy i nie " +
             "wymaga osobnego ustawiania pozycji.")]
    [SerializeField] private Transform platform;

    public void ResetArea()
    {
        PlacePlatformRandomly();
    }

    private void PlacePlatformRandomly()
    {
        float limit = areaHalfExtent - wallMargin;

        float x = Random.Range(-limit, limit);
        float z = Random.Range(-limit, limit);

        Vector3 localPosition = new Vector3(x, platform.localPosition.y, z);
        platform.localPosition = localPosition;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector3 size = new Vector3(areaHalfExtent * 2f, 0.02f, areaHalfExtent * 2f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);

        Gizmos.color = Color.red;
        float limit = areaHalfExtent - wallMargin;
        Vector3 innerSize = new Vector3(limit * 2f, 0.02f, limit * 2f);
        Gizmos.DrawWireCube(Vector3.zero, innerSize);
    }
}