using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StairGame.Api
{
    /// <summary>
    /// Core contract between the host application and Unity.
    /// This file mirrors the supplied contract. Transport is intentionally not defined here.
    /// </summary>
    public interface IStairGameApi
    {
        Task<GameStartResponse> StartGameAsync(
            GameStartRequest request,
            CancellationToken cancellationToken = default);

        Task StopGameAsync(
            CancellationToken cancellationToken = default);

        Task ResetGameAsync(
            CancellationToken cancellationToken = default);

        Task<GameState> GetGameStateAsync(
            CancellationToken cancellationToken = default);

        Task SendMovementAsync(
            MovementCommand movement,
            CancellationToken cancellationToken = default);

        Task<StairConfiguration> GetStairConfigurationAsync(
            CancellationToken cancellationToken = default);

        Task<GameStatistics> GetStatisticsAsync(
            CancellationToken cancellationToken = default);

        event EventHandler<StairReachedEventArgs> StairReached;
        event EventHandler<GameStateChangedEventArgs> GameStateChanged;
        event EventHandler<GameFinishedEventArgs> GameFinished;
    }

    public enum Foot
    {
        Left,
        Right
    }

    public enum GameState
    {
        Idle,
        Starting,
        Running,
        Paused,
        Finished,
        Error
    }

    public sealed class GameStartRequest
    {
        public string PlayerId { get; set; }
        public int TotalStairs { get; set; }
        public Foot StartingFoot { get; set; }
    }

    public sealed class GameStartResponse
    {
        public bool Success { get; set; }
        public string GameId { get; set; }
        public DateTime StartedAt { get; set; }
        public StairConfiguration StairConfiguration { get; set; }
    }

    public sealed class MovementCommand
    {
        public long Timestamp { get; set; }
        public Foot ActiveFoot { get; set; }
        public JointPosition Hip { get; set; }
        public JointPosition Knee { get; set; }
        public JointPosition Ankle { get; set; }
    }

    public sealed class JointPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class StairConfiguration
    {
        public int Count { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
        public double Gap { get; set; }
    }

    public sealed class GameStatistics
    {
        public int CurrentStair { get; set; }
        public int StairsCompleted { get; set; }
        public TimeSpan TotalElapsedTime { get; set; }
        public TimeSpan? CurrentStairElapsedTime { get; set; }
        public IReadOnlyList<StairStatistics> Stairs { get; set; }
    }

    public sealed class StairStatistics
    {
        public int StairNumber { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public Foot Foot { get; set; }
        public TimeSpan StepDuration { get; set; }
    }

    public sealed class StairReachedEventArgs : EventArgs
    {
        public int StairNumber { get; set; }
        public Foot Foot { get; set; }
        public TimeSpan StepDuration { get; set; }
        public TimeSpan TotalElapsedTime { get; set; }
    }

    public sealed class GameStateChangedEventArgs : EventArgs
    {
        public GameState PreviousState { get; set; }
        public GameState CurrentState { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class GameFinishedEventArgs : EventArgs
    {
        public int StairsCompleted { get; set; }
        public TimeSpan TotalElapsedTime { get; set; }
        public GameStatistics Statistics { get; set; }
    }
}
