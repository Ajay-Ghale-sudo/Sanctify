using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A door that slides straight up, like a sluice gate. It isn't handled directly: a
    /// <see cref="Lever"/> or <see cref="Crank"/> drives it through UnityEvents. It moves toward
    /// where it's told at Speed as a kinematic body, so it lifts whatever rests on it. Closing,
    /// it comes down onto anything with a body under it, a prop or the player, and stops there
    /// rather than crushing it (<see cref="IsBlocked"/>) until it's moved. A crank asks it how
    /// far it can close (<see cref="Reachable"/>), so the crank stops with it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class LiftGate : MonoBehaviour
    {
        // Stops this far above whatever's under it, and looks this far past each step down, so
        // something already touching the bottom edge counts.
        const float Skin = 0.02f;

        [Tooltip("How far it rises when fully open, in metres.")]
        [SerializeField, Min(0f)] float lift = 2f;
        [Tooltip("Fastest it moves, in m/s.")]
        [SerializeField, Min(0.01f)] float speed = 0.5f;

        Rigidbody _body;
        Vector3 _closed;
        float _height; // metres above closed
        float _target;

        /// <summary>Something with a body is under it, keeping it from closing.</summary>
        public bool IsBlocked { get; private set; }
        /// <summary>0 shut, 1 fully open.</summary>
        public float Openness => lift > 0f ? _height / lift : 0f;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _closed = _body.position;
        }

        /// <summary>0 shut, 1 fully open. For a crank's Turned.</summary>
        public void SetOpenness(float amount) => _target = Mathf.Clamp01(amount) * lift;

        /// <summary>For a lever's Switched On and Switched Off.</summary>
        public void SetOpen(bool open) => SetOpenness(open ? 1f : 0f);

        /// <summary>
        /// How far toward <paramref name="openness"/> it could close from where it is now before
        /// something under it stops it. Opening is never stopped.
        /// </summary>
        public float Reachable(float openness)
        {
            float wanted = Mathf.Clamp01(openness) * lift;
            if (wanted >= _height || lift <= 0f)
                return openness;
            return (_height - ClearanceBelow(_height - wanted)) / lift;
        }

        void FixedUpdate()
        {
            float step = Mathf.MoveTowards(_height, _target, speed * Time.fixedDeltaTime) - _height;
            IsBlocked = false;
            if (step < 0f)
            {
                float clear = ClearanceBelow(-step);
                IsBlocked = clear < -step;
                step = -clear;
            }
            if (step == 0f)
                return;
            _height += step;
            _body.MovePosition(_closed + Vector3.up * _height);
        }

        /// <summary>How far it can move down, up to <paramref name="distance"/>, before something with a body is under it. Walls and floors don't count.</summary>
        float ClearanceBelow(float distance)
        {
            float clear = distance;
            foreach (RaycastHit hit in _body.SweepTestAll(Vector3.down, distance + Skin, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null)
                    clear = Mathf.Min(clear, Mathf.Max(hit.distance - Skin, 0f));
            }
            return clear;
        }
    }
}
