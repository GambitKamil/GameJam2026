using System.Collections;
using UnityEngine;

public sealed class BreadSupply : MonoBehaviour
{
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private Transform loaf;
    [SerializeField] private GameObject crumbTemplate;
    [SerializeField] private CellEscape escape;
    [SerializeField, Min(1)] private int pieces = 4;
    [SerializeField, Min(0.1f)] private float interactionReach = 0.9f;
    [SerializeField, Min(0.1f)] private float throwSpeed = 8.5f;

    private const float FloorHeight = 0.075f;
    private const float PreviewStep = 0.035f;
    private static readonly Color NormalGuide = new Color(1f, 0.85f, 0.55f, 0.9f);
    private static readonly Color KeyGuide = new Color(0.53f, 0.94f, 0.63f, 0.95f);

    private int remaining;
    private Vector3 loafScale;
    private LineRenderer preview;
    private Material previewMaterial;
    private Camera levelCamera;
    private bool aiming;

    public bool IsHoldingPiece { get; private set; }
    public bool ThrowInProgress { get; private set; }
    public bool HasBread => remaining > 0;

    private void Awake()
    {
        remaining = pieces;
        loafScale = loaf.localScale;
        levelCamera = Camera.main;
    }

    private void Update()
    {
        if (!SideViewPlayer.CanReadInput)
        {
            EndAim();
            return;
        }

        if (Input.GetKeyDown(KeyCode.E) && IsNearTable())
            TrySpendPiece();
        if (Input.GetKeyDown(KeyCode.Q))
            TakePieceForThrow();
        if (!IsHoldingPiece)
            return;

        if (Input.GetMouseButtonDown(0))
            aiming = true;
        if (!aiming)
            return;

        Vector3 origin = player.transform.position + Vector3.up * 0.35f;
        var plane = new Plane(Vector3.forward, origin);
        Ray ray = levelCamera.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 velocity = (ray.GetPoint(distance) - origin).normalized * throwSpeed;
            if (Input.GetMouseButtonUp(0))
                Launch(origin, velocity);
            else
                DrawPreview(origin, velocity);
        }

        if (!Input.GetMouseButton(0))
            EndAim();
    }

    public bool IsNearTable()
    {
        return Mathf.Abs(player.transform.position.x - transform.position.x) <= interactionReach;
    }

    private void TakePieceForThrow()
    {
        if (!IsHoldingPiece && !ThrowInProgress && IsNearTable() && TrySpendPiece())
            IsHoldingPiece = true;
    }

    public bool TrySpendPiece()
    {
        if (!HasBread)
            return false;

        remaining--;
        loaf.localScale = new Vector3(loafScale.x * remaining / pieces, loafScale.y, loafScale.z);
        loaf.gameObject.SetActive(HasBread);
        return true;
    }

    private void DrawPreview(Vector3 origin, Vector3 velocity)
    {
        if (preview == null)
        {
            var guide = new GameObject("Bread Trajectory");
            guide.transform.SetParent(transform, false);
            preview = guide.AddComponent<LineRenderer>();
            previewMaterial = new Material(Shader.Find("Sprites/Default"));
            preview.sharedMaterial = previewMaterial;
            preview.startWidth = 0.035f;
            preview.endWidth = 0.018f;
        }

        preview.enabled = true;
        preview.positionCount = 52;
        preview.SetPosition(0, origin);
        Vector3 last = origin;
        bool hitsKey = false;
        for (int i = 1; i < preview.positionCount; i++)
        {
            Vector3 point = FlightPoint(origin, velocity, i * PreviewStep);
            bool landed = point.y <= FloorHeight;
            preview.SetPosition(i, point);
            hitsKey = !landed && escape.HitsKey(last, point);
            if (landed || hitsKey)
            {
                preview.positionCount = i + 1;
                break;
            }

            last = point;
        }

        preview.startColor = preview.endColor = hitsKey ? KeyGuide : NormalGuide;
    }

    private void Launch(Vector3 origin, Vector3 velocity)
    {
        IsHoldingPiece = false;
        ThrowInProgress = true;
        EndAim();
        StartCoroutine(Throw(origin, velocity));
    }

    private IEnumerator Throw(Vector3 origin, Vector3 velocity)
    {
        GameObject crumb = Instantiate(crumbTemplate, origin, Quaternion.identity);
        crumb.name = "Thrown Bread";
        crumb.SetActive(true);
        bool airborneCatch = false;
        Vector3 last = origin;
        for (float elapsed = 0f; ; elapsed += Time.deltaTime)
        {
            Vector3 point = FlightPoint(origin, velocity, elapsed);
            crumb.transform.position = point;
            if (point.y <= FloorHeight)
                break;

            if (escape.HitsKey(last, point))
            {
                airborneCatch = true;
                break;
            }

            last = point;
            yield return null;
        }

        yield return escape.ReactToBread(crumb.transform, airborneCatch);
        if (crumb != null)
            Destroy(crumb);
        ThrowInProgress = false;
    }

    private static Vector3 FlightPoint(Vector3 origin, Vector3 velocity, float time)
    {
        Vector3 point = origin + velocity * time + Physics.gravity * (0.5f * time * time);
        point.x = Mathf.Clamp(point.x, -7.1f, 7f);
        point.y = Mathf.Max(point.y, FloorHeight);
        return point;
    }

    private void EndAim()
    {
        aiming = false;
        if (preview != null)
            preview.enabled = false;
    }

    private void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}
