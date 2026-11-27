using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LevelEntrance : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    private float timeoutSeconds = 8f;

    private bool complete;

    public void CompleteEntrance()
    {
        complete = true;
    }

    internal IEnumerator WaitForCompletion()
    {
        float deadline = Time.unscaledTime + timeoutSeconds;
        while (this != null && isActiveAndEnabled && !complete && Time.unscaledTime < deadline)
        {
            yield return null;
        }

        if (this != null && isActiveAndEnabled && !complete)
        {
            Debug.LogWarning("Level entrance did not signal completion. Restoring player input.", this);
        }
    }

    internal static LevelEntrance FindIn(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var entrance = root.GetComponentInChildren<LevelEntrance>();
            if (entrance != null && entrance.isActiveAndEnabled)
            {
                return entrance;
            }
        }

        return null;
    }
}
