using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Emergency lever -> power cut. The kitchen goes dark, the compressors die and the
// laundry lock lets go. The cook grabs a flashlight and comes looking; the prisoner
// has to lift the dead shutter and get under it before the beam reaches him.
public sealed partial class FreezerEscape
{
    // Tuning lives in code so the Kitchen scene needs no new serialized data.
    private const float BlackoutLevel = 0.06f;
    private const float FlashlightDelay = 1f;
    private const float CookRunSpeed = 4.2f;     // kitchen leg, off screen
    private const float CookSearchSpeed = 3f;    // inside the cold store, beam sweeping
    private const float CookRushSpeed = 6f;      // after the shutter slams he runs at the noise
    private const float CatchDistance = 0.7f;
    private const float ShutterLiftSeconds = 0.75f;
    private const float SlamHoldSeconds = 2.6f;  // hero catches his breath while the cook hits the door
    private const float SlamStandOff = 0.6f;
    // In front of the crates and shelves so the cook and his beam stay readable.
    private const float CookLaneZ = -0.7f;

    private readonly struct DimmedLight
    {
        public readonly Light Light;
        public readonly float Intensity;

        public DimmedLight(Light light)
        {
            Light = light;
            Intensity = light.intensity;
        }
    }

    private readonly List<DimmedLight> dimmed = new List<DimmedLight>();
    private readonly List<Renderer> glowing = new List<Renderer>();
    private readonly List<Vector3> cookPath = new List<Vector3>();
    private MaterialPropertyBlock darkGlow;
    private bool powerOut;
    private bool laundryOpen;
    private bool slammed;
    private float blackoutTime;
    private float cookDistance;
    private float cookRunLength;
    private float cookPathLength;
    private Transform cook;
    private Quaternion cookRestRotation;
    private Quaternion armRestRotation;
    private Vector3 cookFloor;
    private Light flashlight;
    private Light lensGlow;
    private Light goalLamp;
    private Coroutine shutterLift;
    private Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor;
    private float ambientIntensity;
    private float ambientScale = 1f;

    private bool CookHasFlashlight => powerOut && blackoutTime >= FlashlightDelay;

    private void BeginBlackout()
    {
        powerOut = true;
        blackoutTime = 0f;
        laundryOpen = slammed = false;
        float laundrySide = laundryDoor.position.x + 0.25f;

        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!light.enabled || light.transform.position.x > laundrySide)
                continue;
            // The hero's own fill stays on (and lifts a little) so he still reads as a silhouette.
            bool heroLight = light.GetComponentInParent<SideViewPlayer>() != null ||
                light.name.IndexOf("Prisoner", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (heroLight)
            {
                light.intensity *= 1.8f;
                continue;
            }
            dimmed.Add(new DimmedLight(light));
        }

        darkGlow = new MaterialPropertyBlock();
        darkGlow.SetColor("_EmissionColor", Color.black);
        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer.bounds.center.x > laundrySide || renderer is ParticleSystemRenderer)
                continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.IsKeywordEnabled("_EMISSION"))
                {
                    glowing.Add(renderer);
                    break;
                }
            }
        }

        ambientLight = RenderSettings.ambientLight;
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;
        ambientIntensity = RenderSettings.ambientIntensity;
        fogColor = RenderSettings.fogColor;

        goalLamp = new GameObject("Released lock lamp").AddComponent<Light>();
        goalLamp.type = LightType.Point;
        goalLamp.color = new Color(1f, 0.66f, 0.32f);
        goalLamp.range = 2.8f;
        goalLamp.intensity = 0f;
        goalLamp.shadows = LightShadows.None;
        goalLamp.transform.position = new Vector3(laundryDoor.position.x - 0.15f, 2.35f, -0.45f);

        PrepareCook();
        StartCoroutine(PowerFailure());
    }

    private IEnumerator PowerFailure()
    {
        // A few dying flickers, then dark.
        float[] steps = { 0.25f, 1f, 0.12f, 0.8f, 0.05f, 0.45f, 0f };
        float[] holds = { 0.06f, 0.09f, 0.07f, 0.12f, 0.1f, 0.06f, 0f };
        for (int i = 0; i < steps.Length; i++)
        {
            SetSceneLight(steps[i]);
            if (holds[i] > 0f)
                yield return new WaitForSeconds(holds[i]);
        }

        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            goalLamp.intensity = Mathf.Lerp(0f, 1.4f, t / 0.6f);
            yield return null;
        }
        goalLamp.intensity = 1.4f;
    }

    private void SetSceneLight(float power)
    {
        foreach (DimmedLight entry in dimmed)
        {
            if (entry.Light == null)
                continue;
            entry.Light.intensity = entry.Intensity * Mathf.Lerp(BlackoutLevel, 1f, power);
        }

        foreach (Renderer renderer in glowing)
        {
            if (renderer != null)
                renderer.SetPropertyBlock(power > 0.5f ? null : darkGlow);
        }

        ambientScale = Mathf.Lerp(0.32f, 1f, power);
        ApplyAmbient(ambientScale);
    }

    private void ApplyAmbient(float scale)
    {
        RenderSettings.ambientLight = ambientLight * scale;
        RenderSettings.ambientSkyColor = ambientSky * scale;
        RenderSettings.ambientEquatorColor = ambientEquator * scale;
        RenderSettings.ambientGroundColor = ambientGround * scale;
        RenderSettings.ambientIntensity = ambientIntensity * scale;
        RenderSettings.fogColor = fogColor * Mathf.Lerp(0.5f, 1f, scale);
    }

    private void PrepareCook()
    {
        if (cookArm == null)
            return;

        cook = cookArm.parent;
        cookRestRotation = cook.rotation;
        armRestRotation = cookArm.localRotation;
        cookFloor = cook.position;

        float freezerX = freezerDoor.position.x;
        cookPath.Clear();
        cookPath.Add(cookFloor);
        cookPath.Add(new Vector3(freezerX - 1f, cookFloor.y, cookFloor.z));
        cookPath.Add(new Vector3(freezerX + 0.4f, cookFloor.y, CookLaneZ));
        cookPath.Add(new Vector3(laundryDoor.position.x - 0.6f, cookFloor.y, CookLaneZ));
        cookRunLength = Vector3.Distance(cookPath[0], cookPath[1]) + Vector3.Distance(cookPath[1], cookPath[2]);
        cookPathLength = cookRunLength + Vector3.Distance(cookPath[2], cookPath[3]);
        cookDistance = 0f;

        Transform spoon = cookArm.Find("Wooden spoon");
        if (spoon != null)
            spoon.gameObject.SetActive(false);

        var torch = new GameObject("Cook flashlight").transform;
        torch.SetParent(cookArm, false);
        torch.localPosition = new Vector3(-0.42f, -0.17f, -0.075f);
        GameObject casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        casing.name = "Flashlight casing";
        Destroy(casing.GetComponent<Collider>());
        casing.transform.SetParent(torch, false);
        casing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        casing.transform.localScale = new Vector3(0.05f, 0.075f, 0.05f);
        var stemRenderer = leverHandle.GetComponentInChildren<Renderer>();
        if (stemRenderer != null)
            casing.GetComponent<Renderer>().sharedMaterial = stemRenderer.sharedMaterial;

        flashlight = new GameObject("Flashlight beam").AddComponent<Light>();
        flashlight.transform.SetParent(torch, false);
        flashlight.transform.localPosition = new Vector3(0f, 0f, 0.08f);
        flashlight.type = LightType.Spot;
        flashlight.color = new Color(0.86f, 0.92f, 1f);
        flashlight.range = 9f;
        flashlight.spotAngle = 40f;
        flashlight.innerSpotAngle = 16f;
        flashlight.intensity = 0f;
        flashlight.shadows = LightShadows.Soft;
        flashlight.shadowStrength = 0.85f;
        flashlight.renderMode = LightRenderMode.ForcePixel;
        lensGlow = new GameObject("Flashlight lens glow").AddComponent<Light>();
        lensGlow.transform.SetParent(torch, false);
        lensGlow.transform.localPosition = new Vector3(0f, 0f, 0.11f);
        lensGlow.type = LightType.Point;
        lensGlow.color = flashlight.color;
        lensGlow.range = 0.7f;
        lensGlow.intensity = 0f;
        lensGlow.shadows = LightShadows.None;
        torch.gameObject.SetActive(false);
    }

    private void UpdateBlackout(float delta)
    {
        if (!powerOut)
            return;

        blackoutTime += delta;
        cold = Mathf.MoveTowards(cold, 0f, delta * 0.35f);

        if (phase == Phase.Laundry || phase == Phase.Escaped)
        {
            // Back on a live circuit: the laundry side brings the ambience back.
            ambientScale = Mathf.MoveTowards(ambientScale, 1f, delta * 0.5f);
            ApplyAmbient(ambientScale);
        }

        if (cook == null)
            return;

        if (CookHasFlashlight && phase != Phase.Caught)
        {
            float speed = slammed ? CookRushSpeed : cookDistance < cookRunLength ? CookRunSpeed : CookSearchSpeed;
            // Once the shutter is down he pulls up a step short of it, clear of the door frame.
            float end = slammed ? Mathf.Max(cookDistance, cookPathLength - SlamStandOff) : cookPathLength;
            cookDistance = Mathf.Min(end, cookDistance + speed * delta);
        }
        cook.position = PointOnCookPath(cookDistance) + Vector3.up * CookBob();

        // The cook shoulders the dead freezer shutter open on his way in.
        if (cook.position.x > freezerDoor.position.x - 1.3f)
        {
            float open = closedDoorHeight + doorTravel;
            SetDoorHeight(freezerDoor, Mathf.MoveTowards(freezerDoor.position.y, open, delta * 7f));
        }

        if (phase == Phase.Blackout && CookHasFlashlight && cook.position.x >= body.position.x - CatchDistance)
            Caught();
    }

    private float CookBob()
    {
        bool walking = CookHasFlashlight && cookDistance < (slammed ? cookPathLength - SlamStandOff : cookPathLength) - 0.01f && phase != Phase.Caught;
        return walking ? Mathf.Abs(Mathf.Sin(blackoutTime * 9f)) * 0.05f : 0f;
    }

    private Vector3 PointOnCookPath(float distance)
    {
        for (int i = 1; i < cookPath.Count; i++)
        {
            float length = Vector3.Distance(cookPath[i - 1], cookPath[i]);
            if (distance <= length || i == cookPath.Count - 1)
                return Vector3.Lerp(cookPath[i - 1], cookPath[i], length > 0f ? Mathf.Clamp01(distance / length) : 1f);
            distance -= length;
        }
        return cookPath[cookPath.Count - 1];
    }

    private void PoseCook()
    {
        if (cook == null)
            return;

        // Turn from the range toward the cold store while reaching for the flashlight.
        float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((blackoutTime - 0.35f) / Mathf.Max(0.1f, FlashlightDelay - 0.35f)));
        bool walking = CookHasFlashlight && cookDistance < (slammed ? cookPathLength - SlamStandOff : cookPathLength) - 0.01f && phase != Phase.Caught;
        float sway = walking ? Mathf.Sin(blackoutTime * 4.5f) * 4f : 0f;
        cook.rotation = Quaternion.Euler(0f, -90f * reach, sway) * cookRestRotation;

        Transform torch = flashlight.transform.parent;
        if (CookHasFlashlight && !torch.gameObject.activeSelf)
            torch.gameObject.SetActive(true);

        Vector3 shoulder = cookArm.position;
        Vector3 aim = BeamTarget() - shoulder;
        Vector3 forearm = new Vector3(-0.37f, -0.16f, -0.07f).normalized;
        Quaternion parent = cookArm.parent.rotation;
        Quaternion pointed = Quaternion.FromToRotation(parent * forearm, aim.normalized) * parent;
        Quaternion rest = parent * armRestRotation;
        cookArm.rotation = Quaternion.Slerp(rest, pointed, reach);
        torch.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

        if (CookHasFlashlight)
        {
            // Click on with a stutter, then a steady beam.
            float on = blackoutTime - FlashlightDelay;
            float flicker = on < 0.25f ? (Mathf.Repeat(on, 0.08f) < 0.04f ? 0.3f : 1f) : 1f;
            flashlight.intensity = Mathf.MoveTowards(flashlight.intensity, 3.2f * flicker, Time.deltaTime * 18f);
            lensGlow.intensity = flashlight.intensity * 0.6f;
        }
    }

    private Vector3 BeamTarget()
    {
        Vector3 chest = body.position + Vector3.up * 0.35f;
        if (phase == Phase.Caught)
            return chest;
        if (slammed)
            return new Vector3(laundryDoor.position.x - 0.2f, 0.9f + Mathf.Sin(blackoutTime * 2.3f) * 0.45f, -0.45f);

        Vector3 ahead = cook.position + new Vector3(2.8f, 0.2f, -cook.position.z);
        ahead.x += Mathf.Sin(blackoutTime * 1.9f) * 1.3f;
        ahead.y += (Mathf.Sin(blackoutTime * 1.3f) * 0.5f + 0.5f) * 0.9f;
        float gap = body.position.x - cook.position.x;
        float drawn = gap > 0f ? Mathf.InverseLerp(8f, 3f, gap) * 0.55f : 0f;
        return Vector3.Lerp(ahead, chest, drawn);
    }

    private void TryLiftLaundryShutter()
    {
        if (laundryOpen || shutterLift != null || !NearLaundryShutter())
            return;
        shutterLift = StartCoroutine(LiftLaundryShutter());
    }

    private bool NearLaundryShutter() => player.IsGrounded && Feet < 0.2f &&
        Mathf.Abs(body.position.x - (laundryDoor.position.x - 0.45f)) < 0.85f;

    private IEnumerator LiftLaundryShutter()
    {
        player.SetDoorwayWalk(true);
        Transform actor = player.transform;
        Vector3 start = actor.position;
        Vector3 stance = start;
        stance.x = laundryDoor.position.x - 0.42f;
        float step = Mathf.Abs(stance.x - start.x) / 2.6f;
        for (float t = 0f; t < step; t += Time.deltaTime)
        {
            actor.position = Vector3.Lerp(start, stance, Mathf.SmoothStep(0f, 1f, t / step));
            yield return null;
        }
        actor.position = stance;

        motion.Face(1f);
        motion.PlayLibraryGesture(CellAnimationLibrary.Motion.Interact, 0.18f);
        float floor = Feet;
        mechanismSound.pitch = 0.8f;
        mechanismSound.Play();
        float closed = closedLaundryHeight;
        float open = closedLaundryHeight + doorTravel;
        for (float t = 0f; t < ShutterLiftSeconds; t += Time.deltaTime)
        {
            float lift = Mathf.SmoothStep(0f, 1f, t / ShutterLiftSeconds);
            SetDoorHeight(laundryDoor, Mathf.Lerp(closed, open, lift));
            motion.SetHandTarget(new Vector3(laundryDoor.position.x - 0.12f, floor + 1.0f + lift * 0.75f, body.position.z - 0.14f), 1f);
            yield return null;
        }
        SetDoorHeight(laundryDoor, open);
        Physics.SyncTransforms();
        laundryOpen = true;
        motion.EndAction();
        player.SetDoorwayWalk(false);
        mechanismSound.pitch = 1f;
        shutterLift = null;
    }

    private void SlamLaundryShutter()
    {
        slammed = true;
        StartCoroutine(Slam());
    }

    private IEnumerator Slam()
    {
        // The prisoner stops with his back to the shutter while the cook arrives on the other side.
        player.SetDoorwayWalk(true);
        motion.Face(-1f);
        float from = laundryDoor.position.y;
        const float duration = 0.16f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float fall = t / duration;
            SetDoorHeight(laundryDoor, Mathf.Lerp(from, closedLaundryHeight, fall * fall));
            yield return null;
        }
        SetDoorHeight(laundryDoor, closedLaundryHeight);
        Physics.SyncTransforms();
        mechanismSound.pitch = 0.62f;
        mechanismSound.PlayOneShot(mechanismSound.clip, 1f);
        if (goalLamp != null)
            goalLamp.intensity = 0f;
        yield return new WaitForSeconds(0.4f);
        mechanismSound.pitch = 1f;

        float held = 0.4f;
        while (held < SlamHoldSeconds && (held < 1f || cookDistance < cookPathLength - SlamStandOff - 0.01f))
        {
            held += Time.deltaTime;
            yield return null;
        }
        player.SetDoorwayWalk(false);
    }

    private void Caught()
    {
        phase = Phase.Caught;
        if (shutterLift != null)
        {
            StopCoroutine(shutterLift);
            shutterLift = null;
            player.SetDoorwayWalk(false);
            mechanismSound.pitch = 1f;
        }
        if (climb != null)
            StopCoroutine(climb);
        motion.EndAction();
        SetPlayerActive(false);
        if (GameManager.Instance != null)
            StartCoroutine(ReportCaught());
    }

    private IEnumerator ReportCaught()
    {
        yield return new WaitForSeconds(1.3f);
        GameManager.Instance.ReportPlayerDetected();
    }

    private string BlackoutHint()
    {
        if (Time.time < feedbackUntil)
            return feedback;
        if (shutterLift != null || climb != null)
            return string.Empty;
        if (laundryOpen)
            return "Get through!";
        if (NearLaundryShutter())
            return "E  Lift the laundry shutter";
        return CookHasFlashlight ? "The cook is coming   /   Run to the laundry" : "Run to the laundry";
    }
}
