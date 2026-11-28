using System.Reflection;
using UnityEngine;

internal static partial class CellPolishVerification
{
    private static Collider grateBlocker;
    private static float grateClosedAngle;
    private static Renderer[] prisonerParts, grateParts;
    private static FieldInfo crouchInput;
    private static float gateWalkStart, crouchProbeStart, closedGateStopX;
    private static float walkingGateDepth, crouchingGateDepth, reachGateDepth;
    private static string walkingGatePart, crouchingGatePart, reachGatePart;
    private static bool gateWalkFinished;

    private static void InitializeGrateProbe()
    {
        grateBlocker = gate.GetComponent<Collider>();
        grateClosedAngle = gate.eulerAngles.y;
        prisonerParts = figure.GetComponentsInChildren<Renderer>();
        grateParts = gate.GetComponentsInChildren<Renderer>();
        crouchInput = player.GetType().GetField("<Crouching>k__BackingField", Instance);
        gateWalkStart = crouchProbeStart = float.NaN;
        walkingGateDepth = crouchingGateDepth = reachGateDepth = 0f;
        walkingGatePart = crouchingGatePart = reachGatePart = "-";
        gateWalkFinished = false;
    }

    private static bool PushAgainstGrate()
    {
        if (gateWalkFinished)
            return true;

        Move(1f);
        if (player.transform.position.x < grateBlocker.bounds.min.x - 0.60f)
            return false;

        if (float.IsNaN(gateWalkStart))
            gateWalkStart = Time.time;
        if (Time.time - gateWalkStart < 0.55f)
        {
            SampleGrateDepth(ref walkingGateDepth, ref walkingGatePart);
            return false;
        }

        if (float.IsNaN(crouchProbeStart))
        {
            Capture("08-closed-grate-standing");
            closedGateStopX = body.position.x;
            crouchProbeStart = Time.time;
        }
        crouchInput.SetValue(player, true);
        SampleGrateDepth(ref crouchingGateDepth, ref crouchingGatePart);
        if (Time.time - crouchProbeStart < 0.55f)
            return false;

        Capture("09-closed-grate-crouching");
        crouchInput.SetValue(player, false);
        gateWalkFinished = true;
        return true;
    }

    private static void SampleGrateInteraction()
    {
        if (!gateWalkFinished || !motion.Busy || !grateBlocker.enabled ||
            Mathf.Abs(Mathf.DeltaAngle(grateClosedAngle, gate.eulerAngles.y)) > 1f)
            return;
        SampleGrateDepth(ref reachGateDepth, ref reachGatePart);
    }

    private static void SampleGrateDepth(ref float peak, ref string partName)
    {
        Bounds blocker = grateBlocker.bounds;
        foreach (Renderer part in prisonerParts)
        {
            if (!part.enabled || !part.bounds.Intersects(blocker))
                continue;

            Bounds character = part.bounds;
            foreach (Renderer bar in grateParts)
            {
                if (!bar.enabled || !bar.bounds.Intersects(character))
                    continue;

                Bounds grate = bar.bounds;
                float depth = Mathf.Min(character.max.x, grate.max.x) - Mathf.Max(character.min.x, grate.min.x);
                if (depth <= peak)
                    continue;
                peak = depth;
                partName = $"{part.name} / {bar.name}";
            }
        }
    }

    private static void ReportGrateProbe()
    {
        const float boundsTolerance = 0.0025f;
        Metric(gateWalkFinished && Mathf.Max(walkingGateDepth, crouchingGateDepth, reachGateDepth) <= boundsTolerance, $"Grate: physical stop x={closedGateStopX:F4}, standing approach overlap={walkingGateDepth:F4} m ({walkingGatePart}), crouched overlap={crouchingGateDepth:F4} m ({crouchingGatePart}), closed-gate interaction overlap={reachGateDepth:F4} m ({reachGatePart})");
    }
}
