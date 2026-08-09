using StairGame.Api;
using UnityEngine;

namespace StairGame.Api.Unity
{
    /// <summary>
    /// Converts a stream of Kinect joint samples into a one-shot stair movement request.
    /// The host reports body pose only; Unity remains responsible for deciding whether
    /// the movement satisfies the current stair rule.
    /// </summary>
    public sealed class StairMovementEvaluator : MonoBehaviour
    {
        public enum ForwardAxis
        {
            X,
            Z
        }

        private sealed class FootTrackingState
        {
            public bool HasBaseline;
            public double BaselineY;
            public double BaselineForward;
            public double LastY;
            public double LastForward;
            public long LastTimestamp;
            public bool LiftSeen;
            public bool AwaitingNewBaseline;
            public int StableSamples;
            public long AcceptedTimestamp;
        }

        [Header("Kinect Coordinate Convention")]
        [SerializeField] private ForwardAxis forwardAxis = ForwardAxis.Z;
        [SerializeField] private bool forwardIsPositive = true;

        [Header("Step Completion Rule")]
        [Tooltip("Minimum ankle lift in Kinect units. Use meters when Kinect positions are in meters.")]
        [SerializeField, Min(0.01f)] private float minimumAnkleLift = 0.07f;

        [Tooltip("Required lift as a fraction of Unity stair rise.")]
        [SerializeField, Range(0.1f, 1.5f)] private float stairHeightLiftFraction = 0.55f;

        [Tooltip("Minimum forward ankle travel in Kinect units.")]
        [SerializeField, Min(0.01f)] private float minimumForwardTravel = 0.06f;

        [Tooltip("Required forward travel as a fraction of Unity tread depth.")]
        [SerializeField, Range(0.05f, 1.5f)] private float stairDepthForwardFraction = 0.25f;

        [Header("Noise / Re-arm")]
        [SerializeField, Min(1)] private int stableSamplesToRebaseline = 5;
        [SerializeField, Min(0.01f)] private float settledVerticalSpeed = 0.10f;
        [SerializeField, Min(0.01f)] private float settledForwardSpeed = 0.12f;
        [SerializeField, Min(100)] private int forceRebaselineAfterMilliseconds = 1500;

        [Header("Debug")]
        [SerializeField] private bool logAcceptedThresholds = false;

        private readonly FootTrackingState rightState = new FootTrackingState();
        private readonly FootTrackingState leftState = new FootTrackingState();

        public void ResetAll()
        {
            ResetState(rightState);
            ResetState(leftState);
        }

        /// <summary>
        /// Observes one movement sample. When allowTrigger is false the sample is still
        /// used for baseline/re-arm tracking but cannot advance the game.
        /// </summary>
        public bool Evaluate(
            MovementCommand movement,
            StairConfiguration stair,
            bool allowTrigger)
        {
            if (!IsValid(movement))
            {
                return false;
            }

            FootTrackingState state = GetState(movement.ActiveFoot);
            double ankleY = movement.Ankle.Y;
            double ankleForward = GetForwardValue(movement.Ankle);

            if (!state.HasBaseline)
            {
                SetBaseline(state, ankleY, ankleForward, movement.Timestamp);
                return false;
            }

            if (state.AwaitingNewBaseline)
            {
                UpdateRebaselineState(
                    state,
                    ankleY,
                    ankleForward,
                    movement.Timestamp
                );

                return false;
            }

            double lift = ankleY - state.BaselineY;
            double forward = ankleForward - state.BaselineForward;

            float requiredLift = Mathf.Max(
                minimumAnkleLift,
                stair != null
                    ? (float)stair.Height * stairHeightLiftFraction
                    : minimumAnkleLift
            );

            float requiredForward = Mathf.Max(
                minimumForwardTravel,
                stair != null
                    ? (float)stair.Depth * stairDepthForwardFraction
                    : minimumForwardTravel
            );

            if (lift >= requiredLift * 0.45f)
            {
                state.LiftSeen = true;
            }

            bool passed =
                allowTrigger &&
                state.LiftSeen &&
                lift >= requiredLift &&
                forward >= requiredForward;

            state.LastY = ankleY;
            state.LastForward = ankleForward;
            state.LastTimestamp = movement.Timestamp;

            if (passed && logAcceptedThresholds)
            {
                Debug.Log(
                    $"Stair movement threshold passed | Foot: {movement.ActiveFoot} | " +
                    $"Lift: {lift:F3}/{requiredLift:F3} | " +
                    $"Forward: {forward:F3}/{requiredForward:F3}",
                    this
                );
            }

            return passed;
        }

        public void NotifyAccepted(Foot foot, long timestamp)
        {
            FootTrackingState state = GetState(foot);
            state.AwaitingNewBaseline = true;
            state.StableSamples = 0;
            state.AcceptedTimestamp = timestamp;
            state.LiftSeen = false;
        }

        private void UpdateRebaselineState(
            FootTrackingState state,
            double y,
            double forward,
            long timestamp)
        {
            double deltaSeconds = GetDeltaSeconds(state.LastTimestamp, timestamp);

            if (deltaSeconds > 0.0001)
            {
                double verticalSpeed = System.Math.Abs(y - state.LastY) / deltaSeconds;
                double forwardSpeed = System.Math.Abs(forward - state.LastForward) / deltaSeconds;

                if (verticalSpeed <= settledVerticalSpeed &&
                    forwardSpeed <= settledForwardSpeed)
                {
                    state.StableSamples++;
                }
                else
                {
                    state.StableSamples = 0;
                }
            }

            bool timedOut =
                state.AcceptedTimestamp > 0 &&
                timestamp > state.AcceptedTimestamp &&
                timestamp - state.AcceptedTimestamp >= forceRebaselineAfterMilliseconds;

            if (state.StableSamples >= stableSamplesToRebaseline || timedOut)
            {
                SetBaseline(state, y, forward, timestamp);
                return;
            }

            state.LastY = y;
            state.LastForward = forward;
            state.LastTimestamp = timestamp;
        }

        private double GetForwardValue(JointPosition position)
        {
            double value = forwardAxis == ForwardAxis.X
                ? position.X
                : position.Z;

            return forwardIsPositive ? value : -value;
        }

        private FootTrackingState GetState(Foot foot)
        {
            return foot == Foot.Right ? rightState : leftState;
        }

        private static bool IsValid(MovementCommand movement)
        {
            return movement != null &&
                   movement.Hip != null &&
                   movement.Knee != null &&
                   movement.Ankle != null;
        }

        private static double GetDeltaSeconds(long previousTimestamp, long currentTimestamp)
        {
            if (previousTimestamp <= 0 || currentTimestamp <= previousTimestamp)
            {
                return 0d;
            }

            return (currentTimestamp - previousTimestamp) / 1000d;
        }

        private static void SetBaseline(
            FootTrackingState state,
            double y,
            double forward,
            long timestamp)
        {
            state.HasBaseline = true;
            state.BaselineY = y;
            state.BaselineForward = forward;
            state.LastY = y;
            state.LastForward = forward;
            state.LastTimestamp = timestamp;
            state.LiftSeen = false;
            state.AwaitingNewBaseline = false;
            state.StableSamples = 0;
            state.AcceptedTimestamp = 0;
        }

        private static void ResetState(FootTrackingState state)
        {
            state.HasBaseline = false;
            state.BaselineY = 0d;
            state.BaselineForward = 0d;
            state.LastY = 0d;
            state.LastForward = 0d;
            state.LastTimestamp = 0;
            state.LiftSeen = false;
            state.AwaitingNewBaseline = false;
            state.StableSamples = 0;
            state.AcceptedTimestamp = 0;
        }
    }
}
