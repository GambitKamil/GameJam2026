using UnityEngine;

// Read-only access for PrisonerAnimatorBridge. Nothing here changes how CellMotion behaves.
public sealed partial class CellMotion
{
    internal Transform Figure => figure;
    internal Transform LegacyHand => hand;
    internal GameObject HeldBread => heldBread;
    internal bool CarryingRod => carryingRod;
    internal Transform CarriedRod => wakeRod;
    internal Vector3 HandTarget => handTarget;
    internal float HandTargetWeight => handTargetWeight;
}
