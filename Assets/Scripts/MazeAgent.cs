using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(SimpleCharacterController))]
public class MazeAgent : Agent
{
    [Header("Referencje")]
    [Tooltip("Generator labiryntu odpowiedzialny za proceduralne " +
             "tworzenie ścian oraz wyznaczanie pozycji startowej i celu.")]
    [SerializeField] private MazeGenerator maze;

    [Tooltip("Obiekt celu - subskrybujemy jego zdarzenie, żeby przyznać " +
             "nagrodę i zakończyć epizod po dotarciu agenta.")]
    [SerializeField] private Collectible goal;

    [Tooltip("Maksymalna dopuszczalna odległość od pozycji startowej w " +
             "danym epizodzie - zabezpieczenie na wypadek nietypowego " +
             "zachowania fizyki, nie powinno normalnie być osiągane, " +
             "skoro agent jest ograniczony ścianami labiryntu.")]
    [SerializeField] private float maxDistanceFromStart = 20f;

    [Header("Metryki (tag ścian do liczenia kolizji)")]
    [SerializeField] private string wallTag = "wall";

    [Tooltip("Kara przyznawana za każdą kolizję ze ścianą - zniechęca do " +
             "częstego dotykania ścian podczas nawigacji. W przeciwieństwie " +
             "do środowiska 1, gdzie liczba kolizji pozostaje czystą " +
             "metryką wynikową, tutaj jest ona częściowo bezpośrednio " +
             "optymalizowanym celem - warto to uwzględnić przy " +
             "interpretacji tej metryki w analizie wyników.")]
    [SerializeField] private float wallCollisionPenalty = 0.01f;

    [Header("Bonus eksploracyjny")]
    [Tooltip("Nagroda przyznawana jednorazowo za pierwsze odwiedzenie " +
             "danej komórki labiryntu w danym epizodzie. Zachęca do " +
             "systematycznej eksploracji i zniechęca do bezproduktywnego " +
             "powracania w te same miejsca. W przeciwieństwie do nagrody " +
             "opartej na dystansie, nie da się jej \"farmić\" przez " +
             "oscylowanie - każda komórka daje nagrodę tylko raz.")]
    [SerializeField] private float newCellExplorationBonus = 0.02f;

    private readonly HashSet<Vector2Int> visitedCells = new HashSet<Vector2Int>();

    private SimpleCharacterController characterController;
    private Rigidbody rb;
    private Vector3 episodeStartPosition;

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
        if (goal != null)
        {
            goal.onAgentCollected.AddListener(HandleGoalReached);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (goal != null)
        {
            goal.onAgentCollected.RemoveListener(HandleGoalReached);
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

        maze.GenerateMaze();

        Quaternion randomRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        Vector3 worldStartPosition = maze.transform.TransformPoint(maze.StartLocalPosition);
        transform.SetPositionAndRotation(worldStartPosition, randomRotation);

        episodeStartPosition = transform.position;
        previousPosition = transform.position;

        visitedCells.Clear();
        visitedCells.Add(maze.GetCellCoordinates(transform.position));

        episodeDistanceTraveled = 0f;
        episodeCollisionCount = 0;
        currentEpisodeSteps = 0;
        statsLoggedThisEpisode = false;
        hasCompletedFirstEpisode = true;
    }

    public override void CollectObservations(VectorSensor sensor)
    {

    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (Vector3.Distance(episodeStartPosition, transform.position) > maxDistanceFromStart)
        {
            AddReward(-1f);
            LogEpisodeStats("stray");
            EndEpisode();
            return;
        }

        float forward = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        characterController.ForwardInput = forward;
        characterController.TurnInput = turn;
        characterController.JumpInput = false;

        if (MaxStep > 0)
        {
            AddReward(-1f / MaxStep);
        }

        episodeDistanceTraveled += Vector3.Distance(previousPosition, transform.position);
        previousPosition = transform.position;
        currentEpisodeSteps++;

        Vector2Int currentCell = maze.GetCellCoordinates(transform.position);
        if (visitedCells.Add(currentCell))
        {
            AddReward(newCellExplorationBonus);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Vertical");
        continuousActions[1] = Input.GetAxis("Horizontal");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag(wallTag))
        {
            episodeCollisionCount++;
            AddReward(-wallCollisionPenalty);
        }
    }

    private void HandleGoalReached(Collider other)
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