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

    public static GameManager Instance { get; private set; }

    public event Action<GameState> StateChanged;
    public event Action<int, int, string> ProgressChanged;
    public event Action<string> LevelCompleted;
    public event Action<string> PlayerDetected;
    public event Action<string> SceneLoadStarted;
    public event Action<string> SceneReady;

    [SerializeField]
    private string[] levelSceneNames = { CellScene, CorridorScene, KitchenScene };

    [SerializeField, Min(0f)]
    private float sceneTransitionDelay = 0.2f;

    public GameState State { get; private set; } = GameState.Menu;
    public int CurrentLevelIndex { get; private set; } = -1;
    public int LevelCount => levelSceneNames.Length;
    public bool IsInputEnabled => State == GameState.Playing;

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
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SyncWithActiveScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
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
        if (State == GameState.Intro && !sceneChangeRequested && LevelCount > 0)
        {
            string firstScene = levelSceneNames[0];
            if (CanLoadScene(firstScene))
            {
                sceneChangeRequested = true;
                StartCoroutine(LoadSceneAfterDelay(firstScene));
            }
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
            Time.timeScale = 0f;
            SetState(GameState.Finished);
            sceneChangeRequested = false;
            return;
        }

        StartCoroutine(LoadSceneAfterDelay(levelSceneNames[nextLevelIndex]));
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
        StartCoroutine(LoadSceneAfterDelay(KitchenScene));
    }

    public void RestartCurrentScene()
    {
        if ((State != GameState.Playing && State != GameState.Paused) || sceneChangeRequested)
        {
            return;
        }

        sceneChangeRequested = true;
        StartCoroutine(LoadSceneAfterDelay(SceneManager.GetActiveScene().name));
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
        StartCoroutine(LoadSceneAfterDelay(MainScene));
    }

    private bool CanLoadScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            return true;
        }

        Debug.LogError($"Scene '{sceneName}' is missing from Build Settings.", this);
        return false;
    }

    private IEnumerator LoadSceneAfterDelay(string sceneName)
    {
        SetState(GameState.Transitioning);
        SceneLoadStarted?.Invoke(sceneName);

        if (sceneTransitionDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(sceneTransitionDelay);
        }

        SceneManager.LoadScene(sceneName);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
        {
            return;
        }

        sceneChangeRequested = false;
        SyncWithActiveScene(scene);
        SceneReady?.Invoke(scene.name);
    }

    private void SyncWithActiveScene(Scene scene)
    {
        Time.timeScale = 1f;
        CurrentLevelIndex = Array.IndexOf(levelSceneNames, scene.name);

        if (scene.name == MainScene)
        {
            SetState(GameState.Menu);
            return;
        }

        if (CurrentLevelIndex >= 0)
        {
            SetState(GameState.Playing);
            ProgressChanged?.Invoke(CurrentLevelIndex + 1, levelSceneNames.Length, scene.name);
        }
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
