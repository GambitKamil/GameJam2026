using UnityEngine;

// Tiny status LEDs on wall boxes and sensors: emission only, no real light, so they cost nothing.
public sealed class IndicatorBlink : MonoBehaviour
{
    [SerializeField] private Renderer[] leds;
    [SerializeField] private Color color = new Color(0.3f, 1f, 0.45f);
    [SerializeField, Min(0.1f)] private float period = 1.6f;
    [SerializeField, Range(0.05f, 1f)] private float duty = 0.12f;
    [SerializeField, Min(0f)] private float brightness = 2.2f;

    private MaterialPropertyBlock block;
    private float offset;
    private bool lastOn = true;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        offset = Random.value * period;
        Apply(false);
    }

    private void Update()
    {
        bool on = Mathf.Repeat(Time.time + offset, period) < period * duty;
        if (on != lastOn) Apply(on);
    }

    private void Apply(bool on)
    {
        lastOn = on;
        block.Clear();
        block.SetColor("_EmissionColor", color * (on ? brightness : 0.04f));
        block.SetColor("_Color", on ? color : color * 0.25f);
        foreach (Renderer led in leds)
            if (led != null) led.SetPropertyBlock(block);
    }
}
