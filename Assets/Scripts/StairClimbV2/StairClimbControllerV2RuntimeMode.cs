using UnityEngine;


public sealed partial class StairClimbControllerV2
{
    
    public bool CanChangeActivationModeWithoutReset =>
        !isAnimating &&
        !waitingForOppositeFoot &&
        rightFootStepIndex == leftFootStepIndex;

    /// <summary>
    /// Changes BothFeet / RightOnly / LeftOnly while preserving:
    /// - current character/root position
    /// - current stair indexes
    /// - current session progress
    /// - current planted-foot state
    ///
    /// The change is rejected while an animation is playing or while one
    /// foot is still ahead of the other in BothFeet mode.
    /// </summary>
    public bool TrySetActivationModeWithoutReset(LegActivationMode mode)
    {
        if (activationMode == mode)
            return true;

        if (isAnimating)
        {
            Debug.LogWarning(
                "Stair Climb V2: Finish the current animation before changing leg mode.",
                this
            );
            return false;
        }

        if (waitingForOppositeFoot || rightFootStepIndex != leftFootStepIndex)
        {
            Debug.LogWarning(
                "Stair Climb V2: Finish the current stair with the trailing foot before changing leg mode.",
                this
            );
            return false;
        }

        activationMode = mode;

        // Do NOT call ResetSession().
        // We only clear transient request state that should not survive a mode switch.
        waitingForOppositeFoot = false;
        pendingTargetStepIndex = -1;
        requiredNextFoot = FootSide.Right;

        Debug.Log(
            $"Stair Climb V2 mode changed without reset | Mode: {activationMode} | Completed step index: {rightFootStepIndex}",
            this
        );

        return true;
    }

    public bool TrySetBothFeetModeWithoutReset() =>
        TrySetActivationModeWithoutReset(LegActivationMode.BothFeet);

    public bool TrySetRightOnlyModeWithoutReset() =>
        TrySetActivationModeWithoutReset(LegActivationMode.RightOnly);

    public bool TrySetLeftOnlyModeWithoutReset() =>
        TrySetActivationModeWithoutReset(LegActivationMode.LeftOnly);
}