using System.Threading;
using System.Threading.Tasks;

namespace StairGame.Api
{
    /// <summary>
    /// Optional extension for the exercise-mode UI used by this project.
    /// The original IStairGameApi remains unchanged for compatibility.
    /// </summary>
    public interface IStairGameExerciseApi
    {
        Task<bool> SetExerciseModeAsync(
            ExerciseMode mode,
            CancellationToken cancellationToken = default);

        Task<ExerciseMode> GetExerciseModeAsync(
            CancellationToken cancellationToken = default);
    }

    public enum ExerciseMode
    {
        BothFeet,
        RightOnly,
        LeftOnly
    }

    /// <summary>
    /// Optional power-data extension. Power is not present in the supplied core contract,
    /// so it is kept separate instead of silently changing IStairGameApi.
    /// </summary>
    public interface IStairGamePowerApi
    {
        Task SendPowerAsync(
            LegPowerSnapshot power,
            CancellationToken cancellationToken = default);

        Task<LegPowerSnapshot> GetPowerAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed class LegPowerSnapshot
    {
        public float Right { get; set; }
        public float Left { get; set; }
        public float Total { get; set; }
        public float MaximumValue { get; set; } = 100f;
    }
}
