using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Agent ewaluacyjny realizujący zadanie zbierania obiektu, sterujący postacią za
/// pośrednictwem SimpleCharacterController (ruch oparty na Rigidbody,
/// port z tutoriala Immersive Limit). Obserwacje pozyskiwane są wyłącznie
/// z RayPerceptionSensorComponent3D dodanego jako osobny komponent.
///
/// Przestrzeń akcji jest mieszana: dwie akcje ciągłe (ruch przód/tył,
/// obrót) oraz jedna gałąź dyskretna na skok (0 = brak skoku, 1 = skok),
/// zgodnie z zaleceniem z tutoriala dotyczącym integracji z ML-Agents.
///
/// System nagród odpowiada wersji z tutoriala: +1 za zebranie obiektu,
/// -1 i zakończenie epizodu przy oddaleniu się poza maxDistanceFromStart,
/// bez dodatkowej kary czasowej za pojedynczy krok.
///
/// Dodatkowo agent rejestruje własne metryki epizodu (skuteczność, liczbę
/// kroków, przebytą odległość, liczbę kolizji ze ścianami) za pomocą
/// Academy.Instance.StatsRecorder - trafiają one do tych samych wykresów
/// TensorBoard co standardowe statystyki treningu PPO, co ułatwia
/// późniejszą analizę wyników na potrzeby pracy.
/// </summary>
[RequireComponent(typeof(SimpleCharacterController))]
public class CollectAgentEvaluation : Agent
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

    // Śledzenie metryk bieżącego epizodu.
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
        // Jeśli poprzedni epizod zakończył się bez jawnego wywołania
        // EndEpisode() z naszej strony (czyli przez limit Max Step),
        // logujemy go teraz jako "timeout" - to jedyny moment, w którym
        // możemy to wykryć.
        if (hasCompletedFirstEpisode && !statsLoggedThisEpisode)
        {
            LogEpisodeStats("timeout");
        }

        // W trybie ewaluacji ustaw seed przed jakimkolwiek losowaniem
        // pozycji, orientacji lub układu środowiska.
        EvaluationManager.Instance?.PrepareNextEpisode();

        // Reset prędkości i pozycji. W przeciwieństwie do CharacterController,
        // tutaj nie trzeba nic wyłączać - wystarczy wyzerować prędkości
        // Rigidbody przed zmianą pozycji, żeby uniknąć przeniesienia starego
        // pędu na nowy epizod.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Losowa orientacja startowa - zgodnie z oryginalnym tutorialem.
        // Bez tego agent zawsze zaczyna zwrócony w tę samą stronę, co przy
        // losowej pozycji celu dookoła agenta uniemożliwia nauczenie się
        // aktywnego "poszukiwania" (obracania się) i sprzyja wyuczeniu
        // wąskiej strategii "idź prosto", skutecznej tylko wtedy, gdy cel
        // akurat trafi w ten sam, stały kierunek.
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
        // Celowo puste - jedynym źródłem obserwacji dla tego wariantu
        // agenta jest RayPerceptionSensorComponent3D.
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Kara i zakończenie epizodu, jeśli agent oddali się zbyt daleko
        // od pozycji startowej - zabezpieczenie przed utknięciem poza
        // areną, np. po zeskoczeniu z platformy w pustkę.
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

        // Aktualizacja przebytej odległości - liczona po każdym kroku
        // decyzyjnym na podstawie przemieszczenia względem poprzedniej klatki.
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

    /// <summary>
    /// Zapisuje metryki bieżącego epizodu do StatsRecorder, dzięki czemu
    /// trafiają do TensorBoard obok standardowych statystyk treningu PPO.
    /// Wywoływane dokładnie raz na epizod, niezależnie od przyczyny jego
    /// zakończenia (sukces, kara za oddalenie, timeout).
    /// </summary>
    private void LogEpisodeStats(string outcome)
    {
        var stats = Academy.Instance.StatsRecorder;
        stats.Add("Custom/SuccessRate", outcome == "success" ? 1f : 0f);
        stats.Add("Custom/EpisodeSteps", currentEpisodeSteps);
        stats.Add("Custom/DistanceTraveled", episodeDistanceTraveled);
        stats.Add("Custom/Collisions", episodeCollisionCount);

        EvaluationManager.Instance?.ReportEpisode(
            outcome,
            currentEpisodeSteps,
            episodeDistanceTraveled,
            episodeCollisionCount);

        statsLoggedThisEpisode = true;
    }
}