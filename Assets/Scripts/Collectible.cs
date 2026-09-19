using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class Collectible : MonoBehaviour
{
    [Tooltip("Tag obiektu traktowanego jako agent zbierający przedmiot. " +
             "Musi zostać dodany w Tag Manager i przypisany do agenta.")]
    [SerializeField] private string agentTag = "agent";

    [Tooltip("Wywoływane, gdy obiekt z podanym tagiem wejdzie w kontakt " +
             "z obszarem triggera. Parametr przekazuje collider agenta.")]
    public UnityEvent<Collider> onAgentCollected;

    private void Reset()
    {

        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(agentTag))
        {
            return;
        }

        onAgentCollected?.Invoke(other);
    }
}