using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StairGame.Api;
using UnityEngine;

namespace StairGame.Api.Unity
{
    /// <summary>
    /// Transport-independent Unity implementation of the supplied Stair Game API.
    /// HTTP/WebSocket adapters can call this component without changing game logic.
    /// </summary>
    public sealed class StairGameApiBridge : MonoBehaviour,
        IStairGameApi,
        IStairGameExerciseApi,
        IStairGamePowerApi
    {
        [Header("Game References")]
        [SerializeField] private global::StairClimbControllerV2 stairController;
        [SerializeField] private global::StairPathV2 stairPath;
        [SerializeField] private global::StairGamePowerUI powerUI;
        [SerializeField] private StairMovementEvaluator movementEvaluator;

        [Header("Movement Input")]
        [Tooltip("When enabled, GameStartRequest.StartingFoot is enforced for the first API-driven movement in BothFeet mode.")]
        [SerializeField] private bool enforceStartingFoot = true;

        [Header("Debug")]
        [SerializeField] private bool logApiCalls = true;

        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly List<StairStatistics> stairStatistics = new List<StairStatistics>();

        private int mainThreadId;
        private GameState gameState = GameState.Idle;
        private string currentGameId = string.Empty;
        private string currentPlayerId = string.Empty;
        private DateTime startedAtUtc;
        private DateTime currentStairStartedAtUtc;
        private int requestedTotalStairs;
        private int observedCompletedSteps;
        private Foot configuredStartingFoot = Foot.Right;
        private bool firstApiMovementPending;
        private Foot lastAcceptedFoot = Foot.Right;
        private bool finishEventRaised;
        private StairConfiguration cachedStairConfiguration;

        private LegPowerSnapshot latestPower = new LegPowerSnapshot
        {
            Right = 0f,
            Left = 0f,
            Total = 0f,
            MaximumValue = 100f
        };

        public event EventHandler<StairReachedEventArgs> StairReached;
        public event EventHandler<GameStateChangedEventArgs> GameStateChanged;
        public event EventHandler<GameFinishedEventArgs> GameFinished;

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            ResolveReferences();
        }

        private void Update()
        {
            DrainMainThreadQueue();
            ObserveGameProgress();
        }

        public Task<GameStartResponse> StartGameAsync(
            GameStartRequest request,
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                ResolveReferences();

                if (stairController == null || stairPath == null)
                {
                    SetState(GameState.Error);
                    return new GameStartResponse
                    {
                        Success = false,
                        GameId = string.Empty,
                        StartedAt = DateTime.UtcNow,
                        StairConfiguration = BuildStairConfiguration(0, false)
                    };
                }

                SetState(GameState.Starting);

                currentPlayerId = request != null ? request.PlayerId ?? string.Empty : string.Empty;
                configuredStartingFoot = request != null ? request.StartingFoot : Foot.Right;
                currentGameId = Guid.NewGuid().ToString("N");
                startedAtUtc = DateTime.UtcNow;
                currentStairStartedAtUtc = startedAtUtc;
                stairStatistics.Clear();
                observedCompletedSteps = 0;
                finishEventRaised = false;
                firstApiMovementPending = true;

                stairPath.RefreshSteps();
                int availableSteps = Mathf.Max(0, stairPath.StepCount);
                if (availableSteps <= 0)
                {
                    SetState(GameState.Error);
                    return new GameStartResponse
                    {
                        Success = false,
                        GameId = currentGameId,
                        StartedAt = startedAtUtc,
                        StairConfiguration = BuildStairConfiguration(0, false)
                    };
                }

                int requested = request != null ? request.TotalStairs : 0;
                requestedTotalStairs = requested > 0
                    ? Mathf.Clamp(requested, 1, availableSteps)
                    : availableSteps;

                movementEvaluator?.ResetAll();
                stairController.ResetSession();

                SetState(GameState.Running);

                cachedStairConfiguration = BuildStairConfiguration(requestedTotalStairs, true);
                StairConfiguration configuration = CloneConfiguration(cachedStairConfiguration);

                if (logApiCalls)
                {
                    Debug.Log(
                        $"Stair API started | GameId: {currentGameId} | " +
                        $"PlayerId: {currentPlayerId} | Steps: {requestedTotalStairs} | " +
                        $"StartingFoot: {configuredStartingFoot}",
                        this
                    );
                }

                return new GameStartResponse
                {
                    Success = true,
                    GameId = currentGameId,
                    StartedAt = startedAtUtc,
                    StairConfiguration = configuration
                };
            }, cancellationToken);
        }

        public Task StopGameAsync(CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                if (stairController != null)
                {
                    stairController.StopSession();
                }

                SetState(GameState.Idle);

                if (logApiCalls)
                {
                    Debug.Log("Stair API stopped.", this);
                }
            }, cancellationToken);
        }

        public Task ResetGameAsync(CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                ResolveReferences();

                if (stairController == null)
                {
                    SetState(GameState.Error);
                    return;
                }

                stairStatistics.Clear();
                observedCompletedSteps = 0;
                finishEventRaised = false;
                firstApiMovementPending = true;
                startedAtUtc = DateTime.UtcNow;
                currentStairStartedAtUtc = startedAtUtc;
                movementEvaluator?.ResetAll();
                stairController.ResetSession();
                SetState(GameState.Running);
            }, cancellationToken);
        }

        public Task<GameState> GetGameStateAsync(CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() => gameState, cancellationToken);
        }

        public Task SendMovementAsync(
            MovementCommand movement,
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                if (movement == null ||
                    movement.Hip == null ||
                    movement.Knee == null ||
                    movement.Ankle == null)
                {
                    Debug.LogWarning("Stair API ignored an invalid MovementCommand.", this);
                    return;
                }

                if (gameState != GameState.Running || stairController == null)
                {
                    return;
                }

                StairConfiguration configuration = cachedStairConfiguration ?? BuildStairConfiguration(requestedTotalStairs, false);

                bool footAllowedByMode = IsFootAllowedByCurrentMode(movement.ActiveFoot);
                bool startingFootAllowed = IsStartingFootAllowed(movement.ActiveFoot);
                bool canTrigger =
                    footAllowedByMode &&
                    startingFootAllowed &&
                    stairController.SessionStarted &&
                    !stairController.IsAnimating;

                bool passed = movementEvaluator != null
                    ? movementEvaluator.Evaluate(movement, configuration, canTrigger)
                    : canTrigger;

                if (!passed)
                {
                    return;
                }

                global::StairClimbControllerV2.FootSide controllerFoot =
                    movement.ActiveFoot == Foot.Right
                        ? global::StairClimbControllerV2.FootSide.Right
                        : global::StairClimbControllerV2.FootSide.Left;

                bool accepted = stairController.TryRequestFootMovement(controllerFoot);
                if (!accepted)
                {
                    return;
                }

                lastAcceptedFoot = movement.ActiveFoot;
                firstApiMovementPending = false;
                movementEvaluator?.NotifyAccepted(movement.ActiveFoot, movement.Timestamp);

                if (logApiCalls)
                {
                    Debug.Log(
                        $"Stair API movement accepted | Foot: {movement.ActiveFoot} | " +
                        $"Timestamp: {movement.Timestamp}",
                        this
                    );
                }
            }, cancellationToken);
        }

        public Task<StairConfiguration> GetStairConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                ResolveReferences();
                int count = requestedTotalStairs > 0
                    ? requestedTotalStairs
                    : (stairPath != null ? stairPath.StepCount : 0);

                cachedStairConfiguration = BuildStairConfiguration(count, true);
                return CloneConfiguration(cachedStairConfiguration);
            }, cancellationToken);
        }

        public Task<GameStatistics> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(BuildStatistics, cancellationToken);
        }

        public Task<bool> SetExerciseModeAsync(
            ExerciseMode mode,
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                ResolveReferences();
                if (stairController == null)
                {
                    return false;
                }

                global::StairClimbControllerV2.LegActivationMode controllerMode;
                switch (mode)
                {
                    case ExerciseMode.RightOnly:
                        controllerMode = global::StairClimbControllerV2.LegActivationMode.RightOnly;
                        break;
                    case ExerciseMode.LeftOnly:
                        controllerMode = global::StairClimbControllerV2.LegActivationMode.LeftOnly;
                        break;
                    default:
                        controllerMode = global::StairClimbControllerV2.LegActivationMode.BothFeet;
                        break;
                }

                bool changed = stairController.TrySetActivationModeWithoutReset(controllerMode);

                if (changed && logApiCalls)
                {
                    Debug.Log($"Stair API exercise mode: {mode}", this);
                }

                return changed;
            }, cancellationToken);
        }

        public Task<ExerciseMode> GetExerciseModeAsync(
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                ResolveReferences();
                if (stairController == null)
                {
                    return ExerciseMode.BothFeet;
                }

                switch (stairController.ActivationMode)
                {
                    case global::StairClimbControllerV2.LegActivationMode.RightOnly:
                        return ExerciseMode.RightOnly;
                    case global::StairClimbControllerV2.LegActivationMode.LeftOnly:
                        return ExerciseMode.LeftOnly;
                    default:
                        return ExerciseMode.BothFeet;
                }
            }, cancellationToken);
        }

        public Task SendPowerAsync(
            LegPowerSnapshot power,
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() =>
            {
                if (power == null)
                {
                    return;
                }

                float maximum = Mathf.Max(1f, power.MaximumValue);
                latestPower = new LegPowerSnapshot
                {
                    Right = Mathf.Clamp(power.Right, 0f, maximum),
                    Left = Mathf.Clamp(power.Left, 0f, maximum),
                    Total = Mathf.Clamp(power.Total, 0f, maximum),
                    MaximumValue = maximum
                };

                if (powerUI != null)
                {
                    powerUI.SetMaximumValue(maximum);
                    powerUI.SetPowerValues(
                        latestPower.Right,
                        latestPower.Left,
                        latestPower.Total
                    );
                }
            }, cancellationToken);
        }

        public Task<LegPowerSnapshot> GetPowerAsync(
            CancellationToken cancellationToken = default)
        {
            return RunOnUnityThreadAsync(() => new LegPowerSnapshot
            {
                Right = latestPower.Right,
                Left = latestPower.Left,
                Total = latestPower.Total,
                MaximumValue = latestPower.MaximumValue
            }, cancellationToken);
        }

        private void ObserveGameProgress()
        {
            if (gameState != GameState.Running || stairController == null)
            {
                return;
            }

            int completed = GetCompletedSteps();
            if (completed > observedCompletedSteps)
            {
                DateTime now = DateTime.UtcNow;

                while (observedCompletedSteps < completed)
                {
                    observedCompletedSteps++;

                    TimeSpan totalElapsed = GetTotalElapsed(now);
                    TimeSpan stepDuration = now - currentStairStartedAtUtc;

                    stairStatistics.Add(new StairStatistics
                    {
                        StairNumber = observedCompletedSteps,
                        ElapsedTime = totalElapsed,
                        Foot = lastAcceptedFoot,
                        StepDuration = stepDuration
                    });

                    StairReached?.Invoke(this, new StairReachedEventArgs
                    {
                        StairNumber = observedCompletedSteps,
                        Foot = lastAcceptedFoot,
                        StepDuration = stepDuration,
                        TotalElapsedTime = totalElapsed
                    });

                    currentStairStartedAtUtc = now;
                }
            }

            bool reachedRequestedEnd =
                requestedTotalStairs > 0 &&
                completed >= requestedTotalStairs;

            bool controllerCompletedPath = !stairController.SessionStarted;

            if (reachedRequestedEnd || controllerCompletedPath)
            {
                FinishGame();
            }
        }

        private void FinishGame()
        {
            if (finishEventRaised)
            {
                return;
            }

            finishEventRaised = true;
            SetState(GameState.Finished);

            if (stairController != null && stairController.SessionStarted)
            {
                stairController.StopSession();
            }

            GameStatistics statistics = BuildStatistics();
            GameFinished?.Invoke(this, new GameFinishedEventArgs
            {
                StairsCompleted = statistics.StairsCompleted,
                TotalElapsedTime = statistics.TotalElapsedTime,
                Statistics = statistics
            });
        }

        private GameStatistics BuildStatistics()
        {
            DateTime now = DateTime.UtcNow;
            int completed = GetCompletedSteps();
            int total = requestedTotalStairs > 0
                ? requestedTotalStairs
                : (stairPath != null ? stairPath.StepCount : 0);

            int currentStair = total <= 0
                ? 0
                : Mathf.Clamp(completed + 1, 1, total);

            List<StairStatistics> copy = new List<StairStatistics>(stairStatistics.Count);
            for (int i = 0; i < stairStatistics.Count; i++)
            {
                StairStatistics source = stairStatistics[i];
                copy.Add(new StairStatistics
                {
                    StairNumber = source.StairNumber,
                    ElapsedTime = source.ElapsedTime,
                    Foot = source.Foot,
                    StepDuration = source.StepDuration
                });
            }

            return new GameStatistics
            {
                CurrentStair = currentStair,
                StairsCompleted = completed,
                TotalElapsedTime = GetTotalElapsed(now),
                CurrentStairElapsedTime = gameState == GameState.Running
                    ? now - currentStairStartedAtUtc
                    : (TimeSpan?)null,
                Stairs = copy
            };
        }

        private int GetCompletedSteps()
        {
            if (stairController == null)
            {
                return observedCompletedSteps;
            }

            int completedIndex = Mathf.Min(
                stairController.RightFootStepIndex,
                stairController.LeftFootStepIndex
            );

            int completed = Mathf.Max(0, completedIndex + 1);
            if (requestedTotalStairs > 0)
            {
                completed = Mathf.Min(completed, requestedTotalStairs);
            }

            return completed;
        }

        private bool IsFootAllowedByCurrentMode(Foot foot)
        {
            if (stairController == null)
            {
                return false;
            }

            switch (stairController.ActivationMode)
            {
                case global::StairClimbControllerV2.LegActivationMode.RightOnly:
                    return foot == Foot.Right;
                case global::StairClimbControllerV2.LegActivationMode.LeftOnly:
                    return foot == Foot.Left;
                default:
                    return true;
            }
        }

        private bool IsStartingFootAllowed(Foot foot)
        {
            if (!enforceStartingFoot || !firstApiMovementPending || stairController == null)
            {
                return true;
            }

            if (stairController.ActivationMode ==
                global::StairClimbControllerV2.LegActivationMode.RightOnly)
            {
                return foot == Foot.Right;
            }

            if (stairController.ActivationMode ==
                global::StairClimbControllerV2.LegActivationMode.LeftOnly)
            {
                return foot == Foot.Left;
            }

            return foot == configuredStartingFoot;
        }

        private StairConfiguration BuildStairConfiguration(int countOverride, bool refreshPath)
        {
            if (stairPath == null)
            {
                return new StairConfiguration
                {
                    Count = Mathf.Max(0, countOverride),
                    Width = 0d,
                    Height = 0d,
                    Depth = 0d,
                    Gap = 0d
                };
            }

            if (refreshPath)
            {
                stairPath.RefreshSteps();
            }

            int available = stairPath.StepCount;
            int sampleCount = Mathf.Min(available, 12);

            Vector3 climb = stairPath.ClimbWorldDirection;
            climb.y = 0f;
            if (climb.sqrMagnitude < 0.0001f)
            {
                climb = Vector3.forward;
            }
            climb.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, climb).normalized;

            double totalWidth = 0d;
            double totalDepth = 0d;
            double totalCenterSpacing = 0d;
            int geometrySamples = 0;
            int spacingSamples = 0;
            Vector3 previousCenter = Vector3.zero;
            double previousDepth = 0d;

            for (int i = 0; i < sampleCount; i++)
            {
                if (!stairPath.TryGetStep(i, out BoxCollider step) || step == null)
                {
                    continue;
                }

                double width = GetProjectedBoxSize(step, side);
                double depth = GetProjectedBoxSize(step, climb);

                totalWidth += width;
                totalDepth += depth;
                geometrySamples++;

                if (stairPath.TryGetStepTopCenter(i, out Vector3 center))
                {
                    if (i > 0)
                    {
                        double centerSpacing = Math.Abs(Vector3.Dot(center - previousCenter, climb));
                        double averagePairDepth = (previousDepth + depth) * 0.5d;
                        totalCenterSpacing += Math.Max(0d, centerSpacing - averagePairDepth);
                        spacingSamples++;
                    }

                    previousCenter = center;
                    previousDepth = depth;
                }
            }

            double averageWidth = geometrySamples > 0 ? totalWidth / geometrySamples : 0d;
            double averageDepth = geometrySamples > 0 ? totalDepth / geometrySamples : 0d;
            double averageGap = spacingSamples > 0 ? totalCenterSpacing / spacingSamples : 0d;

            int count = countOverride > 0
                ? Mathf.Min(countOverride, available)
                : available;

            return new StairConfiguration
            {
                Count = count,
                Width = averageWidth,
                Height = stairPath.InferredStepRise,
                Depth = averageDepth,
                Gap = averageGap
            };
        }

        private static StairConfiguration CloneConfiguration(StairConfiguration source)
        {
            if (source == null)
            {
                return null;
            }

            return new StairConfiguration
            {
                Count = source.Count,
                Width = source.Width,
                Height = source.Height,
                Depth = source.Depth,
                Gap = source.Gap
            };
        }

        private static double GetProjectedBoxSize(BoxCollider box, Vector3 direction)
        {
            Transform transform = box.transform;
            Vector3 scale = transform.lossyScale;

            double halfX = box.size.x * Math.Abs(scale.x) * 0.5d;
            double halfY = box.size.y * Math.Abs(scale.y) * 0.5d;
            double halfZ = box.size.z * Math.Abs(scale.z) * 0.5d;

            double extent =
                Math.Abs(Vector3.Dot(transform.right, direction)) * halfX +
                Math.Abs(Vector3.Dot(transform.up, direction)) * halfY +
                Math.Abs(Vector3.Dot(transform.forward, direction)) * halfZ;

            return extent * 2d;
        }

        private TimeSpan GetTotalElapsed(DateTime nowUtc)
        {
            if (startedAtUtc == default)
            {
                return TimeSpan.Zero;
            }

            TimeSpan elapsed = nowUtc - startedAtUtc;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        private void SetState(GameState newState)
        {
            if (gameState == newState)
            {
                return;
            }

            GameState previous = gameState;
            gameState = newState;

            GameStateChanged?.Invoke(this, new GameStateChangedEventArgs
            {
                PreviousState = previous,
                CurrentState = newState,
                Timestamp = DateTime.UtcNow
            });
        }

        private void ResolveReferences()
        {
            if (stairController == null)
            {
                stairController = FindFirstObjectByType<global::StairClimbControllerV2>();
            }

            if (stairPath == null)
            {
                stairPath = FindFirstObjectByType<global::StairPathV2>();
            }

            if (powerUI == null)
            {
                powerUI = FindFirstObjectByType<global::StairGamePowerUI>();
            }

            if (movementEvaluator == null)
            {
                movementEvaluator = GetComponent<StairMovementEvaluator>();
            }
        }

        private void DrainMainThreadQueue()
        {
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }
        }

        private Task RunOnUnityThreadAsync(
            Action action,
            CancellationToken cancellationToken)
        {
            return RunOnUnityThreadAsync(() =>
            {
                action();
                return true;
            }, cancellationToken);
        }

        private Task<T> RunOnUnityThreadAsync<T>(
            Func<T> action,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception exception)
                {
                    return Task.FromException<T>(exception);
                }
            }

            TaskCompletionSource<T> completion =
                new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    completion.TrySetCanceled(cancellationToken);
                });
            }

            mainThreadQueue.Enqueue(() =>
            {
                if (completion.Task.IsCompleted)
                {
                    registration.Dispose();
                    return;
                }

                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });

            return completion.Task;
        }
    }
}
