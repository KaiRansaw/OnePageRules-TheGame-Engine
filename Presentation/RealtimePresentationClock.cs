using System;
using System.Threading;
using System.Threading.Tasks;

namespace FDG.Presentation
{
    /// <summary>
    /// Real wall-clock pacing for interactive (GUI) play: <see cref="Wait"/> delays for the
    /// nominal duration scaled by <see cref="Scale"/>. Used on the host so the battle unfolds
    /// at a presentable tempo and clients receive beats already spaced in real time.
    /// </summary>
    public class RealtimePresentationClock : IPresentationClock
    {
        public float Scale { get; }

        /// <summary>Extra multiplier set by the game screen every frame (Options > Game speed): 2 = half speed.</summary>
        public static volatile float PaceScale = 1f;

        /// <param name="scale">
        /// Duration multiplier. <c>1</c> is real time; <c>2</c> is half speed; <c>0</c> makes
        /// every wait instant. The user-facing "slow mode" knob feeds this.
        /// </param>
        public RealtimePresentationClock(float scale = 1f)
        {
            Scale = scale < 0f ? 0f : scale;
        }

        public Task Wait(TimeSpan nominalDuration, CancellationToken ct = default)
        {
            double ms = nominalDuration.TotalMilliseconds * Scale * PaceScale;
            if (ms <= 0d)
            {
                return Task.CompletedTask;
            }

            return Task.Delay(TimeSpan.FromMilliseconds(ms), ct);
        }
    }
}
