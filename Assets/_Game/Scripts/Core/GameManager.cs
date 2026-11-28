using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    Menu,
    Intro,
    Playing,
    Paused,
    Transitioning,
    Finished
}

public sealed class GameManager : MonoBehaviour
{
    public const string MainScene = "Main";
    public const string CellScene = "Cell";
    public const string CorridorScene = "Corridor";
    public const string KitchenScene = "Kitchen";
    public const string KitchenVentilationScene = "Kitchen_Ventilation";

    public static GameManager Instance { get; private set; }

    public event Action<GameState> StateChanged;
    public event Action<int, int, string> ProgressChanged;
    public event Action<string> LevelCompleted;
    public event Action<string> PlayerDetected;
    public event Action<string> SceneLoadStarted;
    public event Action<string> SceneReady;
    public event Action<float> FadeChanged;

    [SerializeField]
    private string[] levelSceneNames = { CellScene, CorridorScene, KitchenScene, KitchenVentilationScene };

    [SerializeField, Min(0f)]
    private float sceneTransitionDelay = 0.2f;

    public GameState State { get; private set; } = GameState.Menu;
    public int CurrentLevelIndex { get; private set; } = -1;
    public int LevelCount => levelSceneNames.Length;
    public bool IsInputEnabled => State == GameState.Playing;
    public bool IsRouteAvailable => LevelCount > 0 && Array.TrueForAll(levelSceneNames, IsSceneAvailable);
    public float FadeOpacity { get; private set; }

    private bool sceneChangeRequested;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        Time.timeScale = 1f;
        Instance = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) &&
            (State == GameState.Playing || State == GameState.Paused))
        {
            TogglePause();
        }

        if (State == GameState.Playing && Input.GetKeyDown(KeyCode.R))
        {
            RestartCurrentScene();
        }

        if (State == GameState.Playing && Input.GetKeyDown(KeyCode.N))
        {
            CompleteCurrentLevel();
        }
    }

    public void StartGame()
    {
        if (State != GameState.Menu || sceneChangeRequested)
        {
            return;
        }

        SetState(GameState.Intro);
    }

    public void CompleteIntro()
    {
        if (State != GameState.Intro || sceneChangeRequested || LevelCount == 0)
        {
            return;
        }

        string firstScene = levelSceneNames[0];
        if (CanLoadScene(firstScene))
        {
            sceneChangeRequested = true;
            StartCoroutine(LoadScene(firstScene));
        }
    }

    public void SkipIntro()
    {
        CompleteIntro();
    }

    public void CompleteCurrentLevel()
    {
        if (State != GameState.Playing || sceneChangeRequested || CurrentLevelIndex < 0)
        {
            return;
        }

        int nextLevelIndex = CurrentLevelIndex + 1;
        bool hasNextLevel = nextLevelIndex < LevelCount;
        if (hasNextLevel && !CanLoadScene(levelSceneNames[nextLevelIndex]))
        {
            return;
        }

        sceneChangeRequested = true;
        LevelCompleted?.Invoke(SceneManager.GetActiveScene().name);

        if (!hasNextLevel)
        {
            StartCoroutine(FinishRoute());
            return;
        }

        StartCoroutine(LoadScene(levelSceneNames[nextLevelIndex]));
    }

    public void ReportPlayerDetected()
    {
        if (State != GameState.Playing || sceneChangeRequested ||
            SceneManager.GetActiveScene().name != KitchenScene)
        {
            return;
        }

        sceneChangeRequested = true;
        PlayerDetected?.Invoke(SceneManager.GetActiveScene().name);
        StartCoroutine(LoadScene(KitchenScene));
    }

    public void RestartCurrentScene()
    {
        if ((State != GameState.Playing && State != GameState.Paused) || sceneChangeRequested)
        {
            return;
        }

        sceneChangeRequested = true;
        StartCoroutine(LoadScene(SceneManager.GetActiveScene().name));
    }

    public void TogglePause()
    {
        SetPaused(State != GameState.Paused);
    }

    public void SetPaused(bool paused)
    {
        if (sceneChangeRequested || (paused && State != GameState.Playing))
        {
            return;
        }

        if (!paused && State != GameState.Paused)
        {
            return;
        }

        Time.timeScale = paused ? 0f : 1f;
        SetState(paused ? GameState.Paused : GameState.Playing);
    }

    public void ReturnToMenu()
    {
        if (State == GameState.Menu || State == GameState.Transitioning ||
            sceneChangeRequested || !CanLoadScene(MainScene))
        {
            return;
        }

        sceneChangeRequested = true;
        StartCoroutine(LoadScene(MainScene));
    }

    private static bool IsSceneAvailable(string sceneName) =>
        !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);

    private bool CanLoadScene(string sceneName)
    {
        if (IsSceneAvailable(sceneName))
        {
            return true;
        }

        Debug.LogError($"Scene '{sceneName}' is missing from Build Settings.", this);
        return false;
    }

    private IEnumerator LoadScene(string sceneName)
    {
        SetState(GameState.Transitioning);
        SceneLoadStarted?.Invoke(sceneName);
        yield return FadeTo(1f);
        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

        Time.timeScale = 1f;
        Scene scene = SceneManager.GetActiveScene();
        CurrentLevelIndex = Array.IndexOf(levelSceneNames, scene.name);
        LevelEntrance entrance = null;
        if (CurrentLevelIndex >= 0)
        {
            ProgressChanged?.Invoke(CurrentLevelIndex + 1, LevelCount, scene.name);
            entrance = LevelEntrance.FindIn(scene);
        }

        SceneReady?.Invoke(scene.name);
        yield return FadeTo(0f);

        if (entrance != null)
        {
            yield return entrance.WaitForCompletion();
        }

        sceneChangeRequested = false;
        SetState(CurrentLevelIndex >= 0 ? GameState.Playing : GameState.Menu);
    }

    private IEnumerator FinishRoute()
    {
        SetState(GameState.Transitioning);
        yield return FadeTo(1f);
        Time.timeScale = 0f;
        SetState(GameState.Finished);
        yield return FadeTo(0f);
        sceneChangeRequested = false;
    }

    private IEnumerator FadeTo(float target)
    {
        float from = FadeOpacity;
        float elapsed = 0f;
        while (elapsed < sceneTransitionDelay)
        {
            SetFade(Mathf.Lerp(from, target, elapsed / sceneTransitionDelay));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        SetFade(target);
    }

    private void SetFade(float opacity)
    {
        FadeOpacity = opacity;
        FadeChanged?.Invoke(opacity);
    }

    private void SetState(GameState newState)
    {
        if (State == newState)
        {
            return;
        }

        State = newState;
        StateChanged?.Invoke(State);
    }
}
