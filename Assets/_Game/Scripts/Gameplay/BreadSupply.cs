using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
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
    private const float FlightStep = 1f / 120f;
    private const float CeilingHeight = 3.70f;
    private const float LeftWall = -7.27f;
    private const float RightWall = 7.08f;
    private static readonly Color NormalGuide = new Color(1f, 0.85f, 0.55f, 0.9f);
    private static readonly Color KeyGuide = new Color(0.53f, 0.94f, 0.63f, 0.95f);

    private int remaining;
    private Vector3 loafScale;
    private LineRenderer preview;
    private Material previewMaterial;
    private Camera levelCamera;
    private CellMotion motion;
    private readonly List<Vector3> guidePoints = new List<Vector3>(480);
    private bool aiming;
    private bool releaseAim;
    private Vector3 lastAimDirection;

    public bool IsHoldingPiece { get; private set; }
    public bool ThrowInProgress { get; private set; }
    public bool HasBread => remaining > 0;

    private void Awake()
    {
        remaining = pieces;
        loafScale = loaf.localScale;
        levelCamera = Camera.main;
        motion = player.GetComponent<CellMotion>();
    }

    private void Update()
    {
        if (!SideViewPlayer.CanReadInput || motion.Busy)
        {
            if (aiming)
                EndAim();
            return;
        }

        if (Input.GetKeyDown(KeyCode.E) && TryEatAtTable())
            return;
        if (Input.GetKeyDown(KeyCode.Q))
            TakePieceForThrow();
        if (!IsHoldingPiece || motion.Busy)
        {
            if (aiming)
                EndAim();
            return;
        }

        if (Input.GetMouseButtonDown(0))
            aiming = true;
        if (!aiming)
            return;

        bool released = Input.GetMouseButtonUp(0);
        if (!released && !Input.GetMouseButton(0))
        {
            EndAim();
            return;
        }

        Vector3 center = motion.AimPivot;
        var plane = new Plane(Vector3.forward, center);
        Ray ray = levelCamera.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 offset = ray.GetPoint(distance) - center;
            if (offset.sqrMagnitude > 0.000001f)
                lastAimDirection = offset.normalized;
        }

        if (lastAimDirection == Vector3.zero)
        {
            if (released)
                EndAim();
            return;
        }

        motion.SetAim(lastAimDirection, true);
        releaseAim = released;
    }

    private void LateUpdate()
    {
        if (!aiming || lastAimDirection == Vector3.zero)
            return;

        // The hand pose is resolved in CellMotion.LateUpdate before drawing or launching.
        Vector3 velocity = lastAimDirection * throwSpeed;
        Vector3 origin = motion.ThrowOrigin(velocity);
        if (releaseAim)
            Launch(origin, velocity);
        else
            DrawPreview(origin, velocity);
    }

    public bool IsNearTable()
    {
        return Mathf.Abs(player.transform.position.x - transform.position.x) <= interactionReach;
    }

    private bool TryEatAtTable()
    {
        if (IsHoldingPiece || !IsNearTable() || !TrySpendPiece())
            return false;

        motion.Eat(crumbTemplate);
        return true;
    }

    private void TakePieceForThrow()
    {
        if (!IsHoldingPiece && !ThrowInProgress && IsNearTable() && TrySpendPiece())
        {
            IsHoldingPiece = true;
            motion.Hold(crumbTemplate);
        }
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

        int catchFrame = Simulate(origin, velocity, guidePoints);
        preview.enabled = true;
        int count = catchFrame < 0 ? guidePoints.Count : catchFrame + 1;
        preview.positionCount = count;
        for (int i = 0; i < count; i++)
            preview.SetPosition(i, guidePoints[i]);
        preview.startColor = preview.endColor = catchFrame < 0 ? NormalGuide : KeyGuide;
    }

    private void Launch(Vector3 origin, Vector3 velocity)
    {
        IsHoldingPiece = false;
        ThrowInProgress = true;
        EndAim();
        StartCoroutine(motion.ThrowGesture(velocity, () => StartCoroutine(Throw(origin, velocity))));
    }

    private IEnumerator Throw(Vector3 origin, Vector3 velocity)
    {
        GameObject crumb = Instantiate(crumbTemplate, origin, Quaternion.identity);
        crumb.name = "Thrown Bread";
        crumb.SetActive(true);

        var path = new List<Vector3>(480);
        int catchFrame = Simulate(origin, velocity, path);
        int finalFrame = catchFrame < 0 ? path.Count - 1 : catchFrame;
        float duration = finalFrame * FlightStep;
        if (catchFrame >= 0)
            escape.StartAirCatch(path[catchFrame], duration);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            if (catchFrame >= 0)
                escape.UpdateAirCatch(elapsed);
            float frame = elapsed / FlightStep;
            int previous = Mathf.Min((int)frame, finalFrame - 1);
            crumb.transform.position = Vector3.Lerp(path[previous], path[previous + 1], frame - previous);
            crumb.transform.Rotate(0f, 0f, -velocity.x * Time.deltaTime * 24f);
            yield return null;
        }

        if (catchFrame >= 0)
            escape.UpdateAirCatch(duration);
        crumb.transform.position = path[finalFrame];
        yield return escape.ReactToBread(crumb.transform, catchFrame >= 0);
        if (crumb != null)
            Destroy(crumb);
        ThrowInProgress = false;
    }

    private int Simulate(Vector3 position, Vector3 velocity, List<Vector3> path)
    {
        path.Clear();
        path.Add(position);
        int catchFrame = -1;
        for (int i = 1; i < 480; i++)
        {
            Vector3 previous = position;
            velocity += Physics.gravity * FlightStep;
            position += velocity * FlightStep;

            if (position.x < LeftWall)
                Bounce(ref position.x, ref velocity.x, LeftWall, 0.62f);
            else if (position.x > RightWall)
                Bounce(ref position.x, ref velocity.x, RightWall, 0.62f);
            if (position.y > CeilingHeight)
                Bounce(ref position.y, ref velocity.y, CeilingHeight, 0.58f);

            bool settled = false;
            if (position.y <= FloorHeight)
            {
                Bounce(ref position.y, ref velocity.y, FloorHeight, 0.47f);
                velocity.x *= 0.68f;
                settled = velocity.y < 0.64f;
                if (settled)
                    position.y = FloorHeight;
            }

            path.Add(position);
            if (catchFrame < 0 && position.y > 0.55f && escape.HitsKey(previous, position))
                catchFrame = i;
            if (settled || catchFrame >= 0)
                break;
        }

        return catchFrame;
    }

    private static void Bounce(ref float position, ref float velocity, float surface, float restitution)
    {
        position = surface - (position - surface) * restitution;
        velocity *= -restitution;
    }

    private void EndAim()
    {
        aiming = releaseAim = false;
        lastAimDirection = Vector3.zero;
        if (motion != null)
            motion.SetAim(Vector3.zero, false);
        if (preview != null)
            preview.enabled = false;
    }

    private void OnDisable()
    {
        EndAim();
    }

    private void OnDestroy()
    {
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}
