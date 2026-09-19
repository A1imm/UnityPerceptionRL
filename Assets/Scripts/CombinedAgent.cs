using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(SimpleCharacterController))]
public class CombinedAgent : Agent
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

        collectible.transform.parent.gameObject.SetActive(false);
    }

    private void HandleGoalReached(Collider other)
    {
        if (!hasGatheredCollectible)
        {

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

        statsLoggedThisEpisode = true;
    }
}
