using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Zarządza powtarzalną ewaluacją wytrenowanego modelu ML-Agents.
/// Każdy epizod otrzymuje deterministyczny seed, a wynik jest zapisywany
/// jako osobny wiersz CSV. Dla porównywanych wariantów Ray i Visual należy
/// ustawić tę samą liczbę epizodów i ten sam baseSeed.
/// </summary>
public class EvaluationManager : MonoBehaviour
{
    public static EvaluationManager Instance { get; private set; }

    [Header("Identyfikacja eksperymentu")]
    [SerializeField] private string modelName = "CollectRay";
    [SerializeField] private string taskName = "Collect";
    [SerializeField] private string perception = "Ray";

    [Header("Procedura ewaluacji")]
    [Min(1)]
    [SerializeField] private int episodeCount = 500;
    [SerializeField] private int baseSeed = 12345;
    [Min(0.1f)]
    [SerializeField] private float timeScale = 20f;
    [SerializeField] private bool stopWhenFinished = true;

    [Header("Zapis")]
    [Tooltip("Nazwa podfolderu tworzonego w katalogu głównym projektu Unity.")]
    [SerializeField] private string outputDirectoryName = "EvaluationResults";

    private StreamWriter writer;
    private int completedEpisodes;
    private int currentSeed;
    private double episodeStartTime;
    private bool episodePrepared;
    private bool finished;
    private string outputPath;

    public int CompletedEpisodes => completedEpisodes;
    public int CurrentSeed => currentSeed;
    public string OutputPath => outputPath;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("W scenie może znajdować się tylko jeden EvaluationManager.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Time.timeScale = timeScale;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDirectory = Path.Combine(projectRoot, outputDirectoryName);
        Directory.CreateDirectory(outputDirectory);

        string safeModelName = SanitizeFileName(modelName);
        string fileName = $"evaluation_{safeModelName}_seed{baseSeed}_n{episodeCount}.csv";
        outputPath = Path.Combine(outputDirectory, fileName);

        writer = new StreamWriter(outputPath, false);
        writer.WriteLine(
            "model,task,perception,episode,seed,outcome,success,steps," +
            "distance,collisions,subgoal_reached,hazard_hit,stray_hit,timeout,wall_time_s");
        writer.Flush();

        Debug.Log($"[Evaluation] Start: {modelName}, epizody={episodeCount}, " +
                  $"baseSeed={baseSeed}. CSV: {outputPath}");
    }

    /// <summary>
    /// Wywoływane dokładnie raz na początku każdego nowego epizodu,
    /// przed pierwszym użyciem UnityEngine.Random przez agenta lub generator.
    /// </summary>
    public void PrepareNextEpisode()
    {
        if (finished)
        {
            return;
        }

        currentSeed = baseSeed + completedEpisodes;
        UnityEngine.Random.InitState(currentSeed);
        episodeStartTime = Time.realtimeSinceStartupAsDouble;
        episodePrepared = true;
    }

    /// <summary>
    /// Zapisuje wynik zakończonego epizodu. subgoalReached jest używane
    /// przede wszystkim w zadaniu Combined do informacji, czy przed
    /// niepowodzeniem agent zdążył zebrać obiekt pośredni.
    /// </summary>
    public void ReportEpisode(
        string outcome,
        int steps,
        float distance,
        int collisions,
        bool subgoalReached = false)
    {
        if (finished || !episodePrepared)
        {
            return;
        }

        bool success = outcome == "success";
        bool hazardHit = outcome == "hazard";
        bool strayHit = outcome == "stray";
        bool timeout = outcome == "timeout";
        double wallTime = Time.realtimeSinceStartupAsDouble - episodeStartTime;

        int episodeNumber = completedEpisodes + 1;

        writer.WriteLine(string.Join(",",
            Csv(modelName),
            Csv(taskName),
            Csv(perception),
            episodeNumber.ToString(CultureInfo.InvariantCulture),
            currentSeed.ToString(CultureInfo.InvariantCulture),
            Csv(outcome),
            Bool01(success),
            steps.ToString(CultureInfo.InvariantCulture),
            distance.ToString("G9", CultureInfo.InvariantCulture),
            collisions.ToString(CultureInfo.InvariantCulture),
            Bool01(subgoalReached),
            Bool01(hazardHit),
            Bool01(strayHit),
            Bool01(timeout),
            wallTime.ToString("G9", CultureInfo.InvariantCulture)));
        writer.Flush();

        completedEpisodes++;
        episodePrepared = false;

        if (completedEpisodes % 25 == 0 || completedEpisodes == episodeCount)
        {
            Debug.Log($"[Evaluation] {modelName}: {completedEpisodes}/{episodeCount} epizodów.");
        }

        if (completedEpisodes >= episodeCount)
        {
            FinishEvaluation();
        }
    }

    private void FinishEvaluation()
    {
        if (finished)
        {
            return;
        }

        finished = true;
        writer?.Flush();
        writer?.Close();
        writer = null;

        Debug.Log($"[Evaluation] Zakończono {modelName}. Wyniki: {outputPath}");

        if (!stopWhenFinished)
        {
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        writer?.Flush();
        writer?.Close();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private static string Bool01(bool value) => value ? "1" : "0";

    private static string Csv(string value)
    {
        if (value == null)
        {
            return "";
        }

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
