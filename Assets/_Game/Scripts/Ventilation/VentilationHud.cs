using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Minimal UI for the ventilation shaft: a key prompt at the bottom, a black fade and a centred line.
public sealed class VentilationHud : MonoBehaviour
{
    [SerializeField] private Text hint;
    [SerializeField] private Image fade;
    [SerializeField] private Text centre;

    public float FadeLevel => fade != null ? fade.color.a : 0f;

    private void Awake()
    {
        SetHint(string.Empty);
        SetFade(0f);
        if (centre != null) centre.gameObject.SetActive(false);
    }

    public void SetHint(string text)
    {
        if (hint == null || hint.text == text) return;
        hint.text = text;
        hint.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void SetFade(float alpha)
    {
        if (fade == null) return;
        Color c = fade.color;
        c.a = Mathf.Clamp01(alpha);
        fade.color = c;
        fade.raycastTarget = false;
        fade.enabled = c.a > 0.001f;
    }

    public IEnumerator FadeTo(float target, float seconds)
    {
        float from = FadeLevel;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(from, target, t / seconds));
            yield return null;
        }
        SetFade(target);
    }

    public void ShowCentre(string text)
    {
        if (centre == null) return;
        centre.text = text;
        centre.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }
}
