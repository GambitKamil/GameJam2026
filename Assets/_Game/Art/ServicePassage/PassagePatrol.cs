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

    private const int Samples = 20;
    private readonly Color32[] visibility = new Color32[Samples * Samples];
    private Texture2D shadowMap;
    private float nextSample;
    private float distance;
    private float phase;
    private Vector3 lane;
    private float direction;

    private void Awake()
    {
        lane = transform.position;
        shadowMap = new Texture2D(Samples, Samples, TextureFormat.RGBA32, false, true);
        shadowMap.wrapMode = TextureWrapMode.Clamp;
        var properties = new MaterialPropertyBlock();
        properties.SetTexture("_Visibility", shadowMap);
        haze.GetComponent<Renderer>().SetPropertyBlock(properties);
        if (Camera.main != null) Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        RestartAt(lane.x);
    }

    public void RestartAt(float x)
    {
        distance = Mathf.Clamp(rightEnd - x, 0f, rightEnd - leftEnd);
        phase = 0f;
        nextSample = 0f;
        Pose();
    }

    private void Update()
    {
        distance += speed * Time.deltaTime;
        phase += Time.deltaTime;
        Pose();
    }

    private void Pose()
    {
        float span = rightEnd - leftEnd;
        float leg = Mathf.Repeat(distance, span * 2f);
        direction = leg < span ? -1f : 1f;
        transform.position = new Vector3(rightEnd - Mathf.PingPong(distance, span), lane.y, lane.z);
        transform.rotation = Quaternion.Euler(0f, direction < 0f ? 180f : 0f, 0f);
        float step = Mathf.Sin(distance * 5.5f);
        body.localPosition = Vector3.up * (0.022f * Mathf.Abs(step));
        nearLeg.localRotation = Quaternion.Euler(0, 0, step * 23f);
        farLeg.localRotation = Quaternion.Euler(0, 0, -step * 23f);

        // For most of a stride the wrist points down the hall. Only the wrist scans across it.
        float time = Mathf.Repeat(phase, 6.4f);
        float scan = time < 2.0f ? Mathf.Pow(Mathf.Sin(time * Mathf.PI / 2f), 2f) : 0f;
        float back = time > 3.9f && time < 5.1f ? Mathf.Pow(Mathf.Sin((time - 3.9f) * Mathf.PI / 1.2f), 2f) : 0f;
        Vector3 ahead = Quaternion.AngleAxis(direction * (2f + 15f * scan - 12f * back), Vector3.up)
            * (Vector3.right * direction);
        torch.transform.rotation = Quaternion.LookRotation(ahead + Vector3.down * (0.14f + 0.014f * step));
        if (phase >= nextSample)
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
