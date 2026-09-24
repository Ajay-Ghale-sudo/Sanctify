using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Proportional-derivative controller on a vector error. Amnesia's controllers are PID, but
    /// every interaction sets I to 0, so the integral term is left out. Call <see cref="Reset"/>
    /// when a state starts so the first derivative isn't taken against a stale error.
    /// </summary>
    public sealed class PD3
    {
        Vector3 _lastError;
        bool _hasLastError;

        public PD3(float p, float d)
        {
            P = p;
            D = d;
        }

        public float P { get; set; }
        public float D { get; set; }

        public Vector3 Output(Vector3 error, float deltaTime)
        {
            Vector3 derivative = _hasLastError && deltaTime > 0f ? (error - _lastError) / deltaTime : Vector3.zero;
            _lastError = error;
            _hasLastError = true;
            return P * error + D * derivative;
        }

        /// <summary>
        /// Same law with the rate supplied, e.g. from a measured velocity. Taking the derivative
        /// from the measurement instead of the error means a moving target isn't matched
        /// instantly: the output lags it by roughly D / P seconds.
        /// </summary>
        public Vector3 Output(Vector3 error, Vector3 errorRate) => P * error + D * errorRate;

        public void Reset() => _hasLastError = false;
    }
}
