using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Agent ewaluacyjny realizujący zadanie łączące nawigację w proceduralnie generowanym
/// labiryncie, zebranie obiektu oraz dotarcie do celu końcowego, przy
/// jednoczesnym unikaniu przeszkód. W odróżnieniu od MazeAgent (środowisko
/// 2), tutaj dostępny jest skok - potrzebny do wejścia na platformę z
/// obiektem do zebrania (styl środowiska 1), a dodatkowo teoretycznie
/// umożliwiający przeskoczenie niższych, kwadratowych przeszkód zamiast
/// ich omijania.
///
/// Detekcja odbywa się dwutorowo, zgodnie ze stylem poszczególnych
/// elementów:
///  - obiekt do zebrania i cel: strefy wyzwalające (trigger) + zdarzenia
///    Collectible.onAgentCollected, tak jak w środowiskach 1 i 2,
///  - ściany i przeszkody: fizyczna kolizja (OnCollisionEnter) po tagu,
///    tak jak dotychczas.
///
/// System nagród:
///  - +0,3 za zebranie obiektu
///  - +0,7 za dotarcie do celu PO zebraniu obiektu (razem +1,0, koniec
///    epizodu - sukces)
///  - dotarcie do celu PRZED zebraniem obiektu nie daje żadnej reakcji
///  - -1,0 i koniec epizodu za dotknięcie przeszkody
///  - -1,0 i koniec epizodu za oddalenie się poza maxDistanceFromStart
///  - kara czasowa -1/MaxStep za każdy krok
///  - +0,04 za odwiedzenie nowej komórki labiryntu (bonus eksploracyjny)
///  - -0,01 za każdą kolizję ze zwykłą ścianą
///
/// Uwaga: Custom/EpisodeSteps liczone jest przez własny licznik
/// currentEpisodeSteps, a nie przez wbudowane StepCount z klasy Agent -
/// to drugie jest resetowane do zera, zanim jeszcze zdąży wykonać się
/// OnEpisodeBegin() nowego epizodu, więc dla epizodów kończonych
/// timeoutem StepCount w tym miejscu zawsze pokazywałoby błędnie 0.
/// </summary>
[RequireComponent(typeof(SimpleCharacterController))]
public class CombinedAgentEvaluation : Agent
{
    [Header("Referencje")]
    [SerializeField] private CombinedAreaGenerator area;
    [SerializeField] private Collectible collectible;
    [SerializeField] private Collectible goal;

    [Tooltip("Maksymalna dopuszczalna odległość od pozycji startowej.")]
    [SerializeField] private float maxDistanceFromStart = 20f;

    [Header("Tagi ścian i przeszkód")]
    [SerializeField] private string wallTag = "wall";
    [SerializeField] private string hazardTag = "hazard";

    [Header("Kary i nagrody pomocnicze")]
    [SerializeField] private float newCellExplorationBonus = 0.07f;
    [SerializeField] private float wallCollisionPenalty = 0.01f;

    private SimpleCharacterController characterController;
    private Rigidbody rb;
    private Vector3 episodeStartPosition;
    private bool hasGatheredCollectible;

    private readonly HashSet<Vector2Int> visitedCells = new HashSet<Vector2Int>();

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
        if (collectible != null) collectible.onAgentCollected.AddListener(HandleCollectibleGathered);
        if (goal != null) goal.onAgentCollected.AddListener(HandleGoalReached);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (collectible != null) collectible.onAgentCollected.RemoveListener(HandleCollectibleGathered);
        if (goal != null) goal.onAgentCollected.RemoveListener(HandleGoalReached);
    }

    public override void OnEpisodeBegin()
    {
        if (hasCompletedFirstEpisode && !statsLoggedThisEpisode)
        {
            LogEpisodeStats("timeout");
        }

        // W trybie ewaluacji ustaw seed przed jakimkolwiek losowaniem
        // pozycji, orientacji lub układu środowiska.
        EvaluationManager.Instance?.PrepareNextEpisode();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (collectible != null)
        {
            collectible.transform.parent.gameObject.SetActive(true);
        }

        area.GenerateArea();

        Quaternion randomRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        Vector3 worldStartPosition = area.transform.TransformPoint(area.StartLocalPosition);
        transform.SetPositionAndRotation(worldStartPosition, randomRotation);

        episodeStartPosition = transform.position;
        previousPosition = transform.position;
        hasGatheredCollectible = false;

        visitedCells.Clear();
        visitedCells.Add(area.GetCellCoordinates(transform.position));

        episodeDistanceTraveled = 0f;
        episodeCollisionCount = 0;
        currentEpisodeSteps = 0;
        statsLoggedThisEpisode = false;
        hasCompletedFirstEpisode = true;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Celowo puste - obserwacje pochodzą z osobno skonfigurowanego
        // sensora (Ray Perception lub kamera).
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
        bool jump = actions.DiscreteActions[0] > 0;

        characterController.ForwardInput = forward;
        characterController.TurnInput = turn;
        characterController.JumpInput = jump;

        if (MaxStep > 0)
        {
            AddReward(-1f / MaxStep);
        }

        currentEpisodeSteps++;
        episodeDistanceTraveled += Vector3.Distance(previousPosition, transform.position);
        previousPosition = transform.position;

        Vector2Int currentCell = area.GetCellCoordinates(transform.position);
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

        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    private void OnCollisionEnter(Collision collision)
    {
        string tag = collision.collider.tag;

        if (tag == wallTag)
        {
            episodeCollisionCount++;
            AddReward(-wallCollisionPenalty);
        }
        else if (tag == hazardTag)
        {
            AddReward(-1f);
            LogEpisodeStats("hazard");
            EndEpisode();
        }
    }

    private void HandleCollectibleGathered(Collider other)
    {
        if (hasGatheredCollectible)
        {
            return;
        }

        hasGatheredCollectible = true;
        AddReward(0.3f);

        // Ukrycie platformy z obiektem - sygnalizuje wizualnie, że ta
        // część zadania została ukończona, i zapobiega ponownemu
        // wywołaniu triggera.
        collectible.transform.parent.gameObject.SetActive(false);
    }

    private void HandleGoalReached(Collider other)
    {
        if (!hasGatheredCollectible)
        {
            // Dotarcie do celu przed zebraniem obiektu - brak reakcji,
            // agent musi wrócić po zebranie obiektu.
            return;
        }

        AddReward(0.7f);
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
        stats.Add("Custom/HazardHit", outcome == "hazard" ? 1f : 0f);
        stats.Add("Custom/StrayHit", outcome == "stray" ? 1f : 0f);
        stats.Add("Custom/TimeoutHit", outcome == "timeout" ? 1f : 0f);

        EvaluationManager.Instance?.ReportEpisode(
            outcome,
            currentEpisodeSteps,
            episodeDistanceTraveled,
            episodeCollisionCount,
            hasGatheredCollectible);

        statsLoggedThisEpisode = true;
    }
}