using UnityEngine;


public sealed class PassagePatrol : MonoBehaviour
{
    [SerializeField] private Transform body;
    [SerializeField] private Transform nearLeg;
    [SerializeField] private Transform farLeg;
    [SerializeField] private Light torch;
    [SerializeField] private MeshFilter haze;
    [SerializeField] private float leftEnd = -2f;
    [SerializeField] private float rightEnd = 65f;
    [SerializeField] private float speed = 2.95f;
    [SerializeField] private float endPause = 1.4f;
    [SerializeField] private float searchDuration = 2.6f;
    // Off for background guards: the haze keeps a full cone and no rays are cast.
    [SerializeField] private bool sampleBeam = true;

    private const int Samples = 20;
    private const float TurnSpeed = 140f;   // Torch turn limit, degrees per second.
    private const float AimSmooth = 0.22f;
    private const float HallPitch = 7.97f;  // atan(0.14): the beam drops slightly down the hall.
    private const float WalkEase = 0.4f;
    private readonly Color32[] visibility = new Color32[Samples * Samples];
    private Texture2D shadowMap;
    private float nextSample;
    private float distance;
    private float phase;
    private Vector3 lane;
    private float direction;
    private float facing;
    private float nextTurn;
    private float pace;
    private float gait;
    private float clock;
    private bool locked;
    private Vector3 suspect;
    private float yaw;
    private float pitch;
    private float yawSpeed;
    private float pitchSpeed;
    private Quaternion beam;

    public bool Searching { get; private set; }
    public bool Paused { get; private set; }
    public Vector3 Forward => torch.transform.forward;
    public Transform Torch => torch.transform;

    private void Awake()
    {
        lane = transform.position;
        shadowMap = new Texture2D(Samples, Samples, TextureFormat.RGBA32, false, true);
        shadowMap.wrapMode = TextureWrapMode.Clamp;
        // Start with an open beam, so a guard that never samples still shows the whole cone.
        for (int i = 0; i < visibility.Length; i++) visibility[i] = new Color32(255, 0, 0, 255);
        shadowMap.SetPixels32(visibility);
        shadowMap.Apply(false);
        var properties = new MaterialPropertyBlock();
        properties.SetTexture("_Visibility", shadowMap);
        haze.GetComponent<Renderer>().SetPropertyBlock(properties);
        if (Camera.main != null) Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        RestartAt(lane.x);
    }

    public void RestartAt(float x)
    {
        float span = rightEnd - leftEnd;
        distance = Mathf.Clamp(rightEnd - x, 0f, span);
        direction = facing = distance < span ? -1f : 1f;
        nextTurn = distance < span ? span : span * 2f;
        phase = 0f;
        nextSample = 0f;
        clock = 0f;
        pace = gait = 1f;
        Searching = false;
        Paused = false;
        locked = false;
        HallAim(out yaw, out pitch);
        yawSpeed = pitchSpeed = 0f;
        beam = Quaternion.Euler(pitch, yaw, 0f);
        Pose();
    }

    public void Investigate(Vector3 point)
    {
        suspect = point;
        facing = point.x >= transform.position.x ? 1f : -1f;
        Searching = true;
        Paused = false;
        locked = false;
        clock = 0f;
        pace = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        phase += dt;
        clock += dt;
        if (Searching)
        {
            if (locked && clock >= searchDuration) EndSearch();
        }
        else if (Paused)
        {
            if (clock >= endPause) EndPause();
        }
        else Walk(dt);
        gait = Mathf.MoveTowards(gait, Searching || Paused ? 0f : 1f, dt / 0.3f);
        Steer(dt);
        Pose();
    }

    private void Walk(float dt)
    {
        pace = Mathf.MoveTowards(pace, 1f, dt / WalkEase);
        distance += speed * Mathf.SmoothStep(0f, 1f, pace) * dt;
        if (distance < nextTurn) return;
        // Hold at the end of the lane and look back before turning.
        distance = nextTurn;
        pace = 0f;
        clock = 0f;
        Paused = true;
    }

    private void EndPause()
    {
        Paused = false;
        direction = -direction;
        facing = direction;
        nextTurn += rightEnd - leftEnd;
    }

    private void EndSearch()
    {
        Searching = false;
        locked = false;
        facing = direction;
    }

    private void Steer(float dt)
    {
        float goalYaw, goalPitch;
        if (Searching) SearchAim(out goalYaw, out goalPitch);
        else if (Paused) LookBack(out goalYaw, out goalPitch);
        else HallAim(out goalYaw, out goalPitch);
        // Yaw runs from -160 to 160 through 0 (the rear wall), so big swings never cross the camera.
        yaw = Mathf.SmoothDamp(yaw, goalYaw, ref yawSpeed, AimSmooth, TurnSpeed, dt);
        pitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchSpeed, AimSmooth, TurnSpeed * 0.5f, dt);
        beam = Quaternion.RotateTowards(beam, Quaternion.Euler(pitch, yaw, 0f), TurnSpeed * dt);
        // The body follows the beam round once it has swung past the rear wall.
        if (Paused && yaw * direction < 0f) facing = -direction;
    }

    private void HallAim(out float y, out float p)
    {
        // For most of a stride the wrist points down the hall. Only the wrist scans across it.
        float time = Mathf.Repeat(phase, 6.4f);
        float scan = time < 2.0f ? Mathf.Pow(Mathf.Sin(time * Mathf.PI / 2f), 2f) : 0f;
        float back = time > 3.9f && time < 5.1f ? Mathf.Pow(Mathf.Sin((time - 3.9f) * Mathf.PI / 1.2f), 2f) : 0f;
        y = direction * (92f + 15f * scan - 12f * back);
        p = HallPitch;
    }

    private void LookBack(out float y, out float p)
    {
        // Swing the beam across the rear wall onto the way it came, dipping as it passes.
        float s = Mathf.SmoothStep(0f, 1f, endPause > 0f ? clock / (endPause * 0.85f) : 1f);
        y = direction * Mathf.Lerp(92f, -94f, s);
        p = HallPitch + 10f * Mathf.Sin(s * Mathf.PI);
    }

    private void SearchAim(out float y, out float p)
    {
        Vector3 to = suspect - torch.transform.position;
        float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
        y = facing * Mathf.Clamp(Mathf.Atan2(Mathf.Abs(to.x), to.z) * Mathf.Rad2Deg, 0f, 160f);
        p = Mathf.Clamp(Mathf.Atan2(-to.y, flat) * Mathf.Rad2Deg, -35f, 75f);
        if (!locked)
        {
            if (Quaternion.Angle(beam, Quaternion.Euler(p, y, 0f)) > 4f && clock < 2f) return;
            locked = true;
            clock = 0f;
        }
        // Slow search around the point: a side arc with a smaller up and down drift.
        float w = Mathf.SmoothStep(0f, 1f, clock / 0.6f);
        y += w * 8f * Mathf.Sin(clock * 2.2f);
        p += w * 4f * Mathf.Sin(clock * 3.1f + 0.5f);
    }

    private void Pose()
    {
        float span = rightEnd - leftEnd;
        transform.position = new Vector3(rightEnd - Mathf.PingPong(distance, span), lane.y, lane.z);
        transform.rotation = Quaternion.Euler(0f, facing < 0f ? 180f : 0f, 0f);
        float step = Mathf.Sin(distance * 5.5f) * gait;
        body.localPosition = Vector3.up * (0.022f * Mathf.Abs(step));
        nearLeg.localRotation = Quaternion.Euler(0, 0, step * 23f);
        farLeg.localRotation = Quaternion.Euler(0, 0, -step * 23f);
        // The stride nods the beam a little, as before.
        torch.transform.rotation = beam * Quaternion.Euler(0.78f * step, 0f, 0f);
        if (sampleBeam && phase >= nextSample)
        {
            SampleBeam();
            nextSample = phase + 0.07f;
        }
    }

    private void SampleBeam()
    {
        Transform aim = torch.transform;
        float width = 2f * Mathf.Tan(torch.spotAngle * 0.5f * Mathf.Deg2Rad);
        for (int y = 0; y < Samples; y++)
        for (int x = 0; x < Samples; x++)
        {
            Vector3 ray = new Vector3(((x + 0.5f) / Samples - 0.5f) * width,
                ((y + 0.5f) / Samples - 0.5f) * width, 1f);
            float end = 1f;
            if (Physics.Raycast(aim.position, aim.TransformDirection(ray.normalized), out RaycastHit hit,
                torch.range * ray.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                end = hit.distance / (torch.range * ray.magnitude);
            visibility[y * Samples + x] = new Color32((byte)(Mathf.Clamp01(end) * 255f), 0, 0, 255);
        }
        shadowMap.SetPixels32(visibility);
        shadowMap.Apply(false);
    }

    public bool LightsPoint(Vector3 point)
    {
        Vector3 ray = point - torch.transform.position;
        return ray.magnitude <= torch.range
            && Vector3.Dot(torch.transform.forward, ray.normalized) >= Mathf.Cos(torch.spotAngle * 0.5f * Mathf.Deg2Rad)
            && !Physics.Linecast(torch.transform.position, point, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void OnDestroy()
    {
        if (shadowMap != null) Destroy(shadowMap);
    }
}
