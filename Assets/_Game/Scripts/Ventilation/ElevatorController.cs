using System.Collections;
using UnityEngine;

// Freight lift at the end of the shaft.
// Lever A (power) feeds it and steadies the shaft lights; lever B (launch) starts a countdown.
// When the countdown ends with the prisoner aboard, the lift rises into the closing cinematic;
// otherwise it buzzes, stays down and lever B can be pulled again.
public sealed class ElevatorController : MonoBehaviour
{
    public enum State { Unpowered, Powered, Countdown, Riding }

    [Header("Lift")]
    [SerializeField] private Rigidbody platform;
    [SerializeField, Min(0.5f)] private float platformHalfWidth = 1.7f;
    [SerializeField, Min(1f)] private float riseHeight = 6.9f;
    [SerializeField, Min(0.1f)] private float riseSpeed = 0.65f;
    [SerializeField, Min(1f)] private float launchDelay = 5f;
    [Tooltip("Share of the climb after which the screen starts to fade.")]
    [SerializeField, Range(0.2f, 1f)] private float fadeFrom = 0.55f;

    [Header("Controls")]
    [SerializeField] private LeverController powerLever;
    [SerializeField] private LeverController launchLever;

    [Header("Links")]
    [SerializeField] private FlickeringLightController shaftLights;
    [SerializeField] private SecurityCameraController[] cameras;
    [SerializeField] private AlarmController alarm;
    [SerializeField] private CartController cart;
    [SerializeField] private CartHidingController hiding;
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private VentilationPlayerActions actions;
    [SerializeField] private VentilationHud hud;
    [SerializeField] private SceneTransition transition;

    [Header("Indicators")]
    [SerializeField] private Light powerLamp;
    [SerializeField] private Renderer powerLens;
    [SerializeField] private Light statusLamp;
    [SerializeField] private Renderer statusLens;
    [SerializeField] private Light launchPanelLamp;

    [Header("Sound")]
    [SerializeField] private AudioSource motor;
    [SerializeField] private AudioSource panel;

    public State Current { get; private set; }
    public float PlatformTop => platform.position.y;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private Vector3 bottom;
    private float countdown;
    private float powerIntensity, statusIntensity, panelIntensity;
    private MaterialPropertyBlock block;
    private Coroutine ride;
    private float deniedFlash;

    private void Awake()
    {
        body = player.GetComponent<Rigidbody>();
        capsule = player.GetComponent<CapsuleCollider>();
        bottom = platform.position;
        platform.isKinematic = true;
        platform.interpolation = RigidbodyInterpolation.Interpolate;
        block = new MaterialPropertyBlock();
        powerIntensity = powerLamp != null ? powerLamp.intensity : 0f;
        statusIntensity = statusLamp != null ? statusLamp.intensity : 0f;
        panelIntensity = launchPanelLamp != null ? launchPanelLamp.intensity : 0f;
        powerLever.Switched += OnPower;
        launchLever.Switched += OnLaunch;
        Current = State.Unpowered;
    }

    private void OnDestroy()
    {
        if (powerLever != null) powerLever.Switched -= OnPower;
        if (launchLever != null) launchLever.Switched -= OnLaunch;
    }

    private void OnPower(LeverController lever, bool on)
    {
        if (Current is State.Countdown or State.Riding) return;
        Current = on ? State.Powered : State.Unpowered;
        shaftLights.SetSteady(on);
        if (motor != null && on)
        {
            motor.pitch = 0.5f;
            motor.volume = 0.12f;
            if (!motor.isPlaying) motor.Play();
        }
        else if (motor != null)
            motor.Stop();
    }

    private void OnLaunch(LeverController lever, bool on)
    {
        if (!on) return;
        if (Current != State.Powered)
        {
            // No power: a dead clunk and a short red flash on the launch panel.
            deniedFlash = 0.6f;
            Play(VentilationAudio.Cue.Buzzer, 0.5f, 0.7f);
            return;
        }
        Current = State.Countdown;
        countdown = launchDelay;
        powerLever.Locked = true;
        launchLever.Locked = true;
        StartCoroutine(Countdown());
    }

    private IEnumerator Countdown()
    {
        int lastBeep = -1;
        while (countdown > 0f && Current == State.Countdown)
        {
            int second = Mathf.CeilToInt(countdown);
            if (second != lastBeep)
            {
                lastBeep = second;
                Play(VentilationAudio.Cue.Beep, second <= 1 ? 1.25f : 1f, 0.8f);
            }
            countdown -= Time.deltaTime;
            yield return null;
        }
        if (Current != State.Countdown) yield break;

        if (PlayerAboard())
            ride = StartCoroutine(Ride());
        else
        {
            Play(VentilationAudio.Cue.Buzzer, 1f, 0.9f);
            Current = State.Powered;
            powerLever.Locked = false;
            launchLever.Locked = false;
        }
    }

    private bool OnPlatform(float x) => Mathf.Abs(x - platform.position.x) <= platformHalfWidth;

    private bool PlayerAboard()
    {
        if (hiding.IsHidden)
            return OnPlatform(cart.X) && Mathf.Abs(cart.CurrentPosition.y - PlatformTop) < 0.2f;
        float feet = body.position.y + capsule.center.y - capsule.height * 0.5f;
        return OnPlatform(body.position.x) && Mathf.Abs(feet - PlatformTop) < 0.25f;
    }

    private IEnumerator Ride()
    {
        Current = State.Riding;
        actions.Freeze(true);
        bool riderVisible = !hiding.IsHidden;
        if (riderVisible)
            player.SetDoorwayWalk(true);

        foreach (SecurityCameraController camera in cameras)
            camera.Track(platform.transform);
        alarm.StartStoryAlarm();
        if (motor != null)
        {
            motor.pitch = 0.75f;
            motor.volume = 0.45f;
            if (!motor.isPlaying) motor.Play();
        }

        float riderOffset = body.position.y - platform.position.y;
        float risen = 0f;
        bool fading = false;
        while (risen < riseHeight)
        {
            float step = riseSpeed * Mathf.SmoothStep(0.25f, 1f, Mathf.Clamp01(risen / 0.6f)) * Time.deltaTime;
            risen = Mathf.Min(riseHeight, risen + step);
            Vector3 p = bottom + Vector3.up * risen;
            platform.MovePosition(p);
            if (riderVisible)
            {
                Vector3 rider = body.position;
                rider.y = p.y + riderOffset;
                body.position = rider;
                player.transform.position = rider;
            }
            if (!fading && risen >= riseHeight * fadeFrom)
            {
                fading = true;
                transition.Go();
            }
            yield return null;
        }
        if (!fading) transition.Go();
    }

    private void Update()
    {
        bool powered = Current != State.Unpowered;
        SetLamp(powerLamp, powerLens, powerIntensity, powered ? 1f : 0f, new Color(0.3f, 1f, 0.45f));

        // Status: blinks amber through the countdown, steady during the ride.
        float status = 0f;
        if (Current == State.Countdown)
            status = Mathf.Repeat(Time.time, 0.5f) < 0.25f ? 1f : 0.15f;
        else if (Current == State.Riding)
            status = 1f;
        SetLamp(statusLamp, statusLens, statusIntensity, status, new Color(1f, 0.62f, 0.25f));

        if (launchPanelLamp != null)
        {
            deniedFlash = Mathf.Max(0f, deniedFlash - Time.deltaTime);
            bool denied = deniedFlash > 0f && Mathf.Repeat(deniedFlash, 0.3f) > 0.15f;
            launchPanelLamp.color = denied ? new Color(0.9f, 0.12f, 0.08f) : new Color(1f, 0.62f, 0.25f);
            launchPanelLamp.intensity = denied ? panelIntensity : powered ? panelIntensity * 0.6f : 0f;
        }
    }

    private void SetLamp(Light lamp, Renderer lens, float fullIntensity, float level, Color color)
    {
        if (lamp != null) lamp.intensity = fullIntensity * level;
        if (lens == null) return;
        block.Clear();
        block.SetColor("_EmissionColor", color * (level > 0f ? 0.4f + 1.6f * level : 0f));
        block.SetColor("_Color", level > 0f ? color : new Color(0.12f, 0.12f, 0.12f));
        lens.SetPropertyBlock(block);
    }

    private void Play(VentilationAudio.Cue cue, float pitch, float volume)
    {
        if (panel == null) return;
        panel.pitch = pitch;
        panel.PlayOneShot(VentilationAudio.Get(cue), volume);
    }

    // Checkpoint restore.
    public void ResetToBottom(bool powered)
    {
        StopAllCoroutines();
        ride = null;
        platform.position = bottom;
        platform.transform.position = bottom;
        Current = powered ? State.Powered : State.Unpowered;
        powerLever.Locked = false;
        launchLever.Locked = false;
        launchLever.SetState(false);
        if (motor != null)
        {
            if (powered)
            {
                motor.pitch = 0.5f;
                motor.volume = 0.12f;
                if (!motor.isPlaying) motor.Play();
            }
            else motor.Stop();
        }
    }
}
