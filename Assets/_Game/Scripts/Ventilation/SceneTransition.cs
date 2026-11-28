using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Leaves the ventilation shaft for the upper floor.
// UpperFloor in Build Settings -> load it. Otherwise hand over to the GameManager route
// (which ends the jam build), and with no GameManager show "To be continued" with a replay key.
public sealed class SceneTransition : MonoBehaviour
{
    [SerializeField] private string nextScene = "UpperFloor";
    [SerializeField] private VentilationHud hud;
    [SerializeField, Min(0.1f)] private float fadeSeconds = 1.4f;
    [SerializeField] private string fallbackMessage = "To be continued";

    public bool Started { get; private set; }
    private bool waitingForReplay;

    public void Go()
    {
        if (Started) return;
        Started = true;
        StartCoroutine(Leave());
    }

    private IEnumerator Leave()
    {
        if (hud != null && hud.FadeLevel < 0.99f)
            yield return hud.FadeTo(1f, fadeSeconds);

        if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
        {
            SceneManager.LoadScene(nextScene);
            yield break;
        }

        var game = GameManager.Instance;
        if (game != null && game.State == GameState.Playing)
        {
            // Hold black under the GameManager's own fade, then let the route continue or finish.
            game.CompleteCurrentLevel();
            yield break;
        }

        if (hud != null)
        {
            hud.ShowCentre(fallbackMessage + "\n\n<size=22>R  Replay</size>");
            hud.SetHint(string.Empty);
        }
        waitingForReplay = true;
    }

    private void Update()
    {
        if (waitingForReplay && Input.GetKeyDown(KeyCode.R))
        {
            waitingForReplay = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().name : SceneManager.GetActiveScene().path);
        }
    }
}
