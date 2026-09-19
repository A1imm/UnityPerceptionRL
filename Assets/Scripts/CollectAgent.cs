using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(SimpleCharacterController))]
public class CollectAgent : Agent
{
    [Header("Referencje")]
    [Tooltip("Arena, w której działa agent - potrzebna do zresetowania " +
             "pozycji obiektu do zebrania na początku każdego epizodu.")]
    [SerializeField] private CollectArea area;

    [Tooltip("Obiekt do zebrania - subskrybujemy jego zdarzenie, żeby " +
             "przyznać nagrodę i zakończyć epizod po kontakcie agenta.")]
    [SerializeField] private Collectible collectible;

    [Tooltip("Punkt, z którego agent startuje na początku każdego epizodu.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("Maksymalna dopuszczalna odległość od pozycji startowej. Po " +
             "przekroczeniu agent otrzymuje karę i epizod jest kończony - " +
             "zabezpiecza przed utknięciem poza areną (np. po zeskoczeniu " +
             "z platformy w pustkę).")]
    [SerializeField] private float maxDistanceFromStart = 15f;

    [Header("Metryki (tag ścian do liczenia kolizji)")]
    [Tooltip("Tag przypisany do obiektów ścian - używany do zliczania " +
             "kolizji agenta z granicami areny.")]
    [SerializeField] private string wallTag = "wall";

    private Vector3 startPosition;
    private SimpleCharacterController characterController;
    private Rigidbody rb;

    private Vector3 previousPosition;
    private float episodeDistanceTraveled;
    private int episodeCollisionCount;
    private int currentEpisodeSteps;
    private bool statsLoggedThisEpisode;
    private bool hasCompletedFirstEpisode;

    protected override void Awake()
    {
        base.Awake();
        characterController = GetComponent<SimpleCharacterController>();
        rb = GetComponent<Rigidbody>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (collectible != null)
        {
            collectible.onAgentCollected.AddListener(HandleCollected);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (collectible != null)
        {
            collectible.onAgentCollected.RemoveListener(HandleCollected);
        }
    }

    public override void OnEpisodeBegin()
    {

        if (hasCompletedFirstEpisode && !statsLoggedThisEpisode)
        {
            LogEpisodeStats("timeout");
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Quaternion randomRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        transform.SetPositionAndRotation(spawnPoint.position, randomRotation);

        startPosition = transform.position;
        previousPosition = transform.position;

        episodeDistanceTraveled = 0f;
        episodeCollisionCount = 0;
        currentEpisodeSteps = 0;
        statsLoggedThisEpisode = false;
        hasCompletedFirstEpisode = true;

        area.ResetArea();
    }

    public override void CollectObservations(VectorSensor sensor)
    {

    }

    public override void OnActionReceived(ActionBuffers actions)
    {

        if (Vector3.Distance(startPosition, transform.position) > maxDistanceFromStart)
        {
            AddReward(-1f);
            LogEpisodeStats("stray");
            EndEpisode();
            return;
        }

        float forward = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        bool jump = actions.DiscreteActions[0] > 0;

        characterController.ForwardInput = forward;
        characterController.TurnInput = turn;
        characterController.JumpInput = jump;

        currentEpisodeSteps++;
        episodeDistanceTraveled += Vector3.Distance(previousPosition, transform.position);
        previousPosition = transform.position;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Vertical");
        continuousActions[1] = Input.GetAxis("Horizontal");

        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag(wallTag))
        {
            episodeCollisionCount++;
        }
    }

    private void HandleCollected(Collider other)
    {
        AddReward(1f);
        LogEpisodeStats("success");
        EndEpisode();
    }

    private void LogEpisodeStats(string outcome)
    {
        var stats = Academy.Instance.StatsRecorder;
        stats.Add("Custom/SuccessRate", outcome == "success" ? 1f : 0f);
        stats.Add("Custom/EpisodeSteps", currentEpisodeSteps);
        stats.Add("Custom/DistanceTraveled", episodeDistanceTraveled);
        stats.Add("Custom/Collisions", episodeCollisionCount);

        statsLoggedThisEpisode = true;
    }
}