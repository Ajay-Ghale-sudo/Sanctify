using System;
using UnityEngine;

namespace Sanctify.Characters
{
    /// <summary>
    /// Distance-driven gait cycle shared by head bob and footsteps. One full cycle (2π) is
    /// two steps. Footfalls land at π/2 and 3π/2, so phase 0 and π are the neutral
    /// "between steps" poses: a bob built from sin(φ) and sin²(φ) is zero there.
    ///
    /// <see cref="Phase"/> is unwrapped (it only grows) so it can be interpolated between
    /// fixed steps without wrap handling; sample it with sin/cos or <c>Mathf.Repeat</c>.
    ///
    /// On <see cref="Reset"/> the phase eases to the nearest neutral pose instead of jumping,
    /// and the next footstep needs a full half stride again.
    /// </summary>
    public sealed class StrideCycle
    {
        const float HalfPi = Mathf.PI * 0.5f;

        /// <summary>Phase at the end of the last tick, radians, unwrapped.</summary>
        public float Phase { get; private set; }
        /// <summary>Phase at the start of the last tick, for interpolation.</summary>
        public float PreviousPhase { get; private set; }
        public int StepCount { get; private set; }
        public bool IsSettling => _settling;

        /// <summary>Raised on each footfall. Argument is the foot: 0 = left, 1 = right.</summary>
        public event Action<int> Step;

        /// <summary>Radians per second the phase eases toward neutral after a stop.</summary>
        public float SettleRate { get; set; } = 8f;

        float _lastStepPhase = -HalfPi;
        bool _settling;
        float _settleTarget;

        /// <summary>Advances by a travelled distance using the stride length that applies right now.</summary>
        public void Advance(float distance, float strideLength)
        {
            PreviousPhase = Phase;
            _settling = false;

            if (distance <= 0f || strideLength <= 0f)
                return;

            Phase += distance / strideLength * (Mathf.PI * 2f);

            while (Phase - _lastStepPhase >= Mathf.PI)
            {
                _lastStepPhase += Mathf.PI;
                Step?.Invoke(StepCount & 1);
                StepCount++;
            }
        }

        /// <summary>Call on ticks with no ground travel. Eases toward neutral if a reset is pending.</summary>
        public void Settle(float deltaTime)
        {
            PreviousPhase = Phase;
            if (!_settling)
                return;

            Phase = Mathf.MoveTowards(Phase, _settleTarget, SettleRate * deltaTime);
            if (Mathf.Approximately(Phase, _settleTarget))
            {
                Phase = _settleTarget;
                _settling = false;
                _lastStepPhase = _settleTarget - HalfPi;
            }
        }

        /// <summary>
        /// Called when the character has fully stopped on the ground. The phase eases to the
        /// nearest neutral pose and the next footstep requires a full half stride again.
        /// </summary>
        public void Reset()
        {
            _settleTarget = Mathf.Round(Phase / Mathf.PI) * Mathf.PI;
            _settling = true;
            StepCount = 0;
        }

        public float GetInterpolatedPhase(float alpha) => Mathf.LerpUnclamped(PreviousPhase, Phase, Mathf.Clamp01(alpha));
    }
}
