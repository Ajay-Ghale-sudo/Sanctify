using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A lever on a hinge, worked with the right stick as a door is (<see cref="HingeState"/>), in
    /// either mode: pushed slowly, it goes slowly; flicked, it's thrown across. An over-centre
    /// spring holds it at whichever end it's nearer, so once past halfway it carries on by itself,
    /// and it starts at its off end. Reaching the on end raises Switched On; back at the off end,
    /// Switched Off. With Lock When On, it stays on for good and can't be used again.
    ///
    /// Author it at the middle of its travel, with hinge limits the same either side (hinge
    /// angles count from the pose at load). On is the end the hinge axis turns it toward by
    /// Unity's rotation rule: with the axis along +x and the arm pointing up, on is toward +z.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(HingeJoint))]
    [DisallowMultipleComponent]
    public sealed class Lever : Interactable
    {
        [Tooltip("The over-centre spring: its push toward the nearer end, in N·m per radian from the middle. Enough to hold the lever there when knocked, little enough for a slow push to overcome.")]
        [SerializeField, Min(0f)] float detentStrength = 5f;
        [Tooltip("How near an end, in degrees, counts as reaching it.")]
        [SerializeField, Min(0f)] float switchMargin = 5f;
        [Tooltip("Stays on once switched on, and can't be used again.")]
        [SerializeField] bool lockWhenOn;
        [SerializeField] UnityEvent onSwitchedOn = new();
        [SerializeField] UnityEvent onSwitchedOff = new();

        // Just enough to tip it off the dead centre toward off at load, for the spring to finish.
        const float StartNudge = 0.5f; // rad/s

        Rigidbody _body;
        HingeJoint _hinge;
        Quaternion _rest;
        float _travel; // degrees either side of the middle

        public bool IsOn { get; private set; }
        public UnityEvent OnSwitchedOn => onSwitchedOn;
        public UnityEvent OnSwitchedOff => onSwitchedOff;

        Vector3 Axis => _hinge.transform.TransformDirection(_hinge.axis).normalized;

        /// <summary>
        /// Degrees from the middle, positive toward on. Worked out from the body's rotation, in
        /// the same terms as its angular velocity and torque, rather than from the hinge's own angle.
        /// </summary>
        float Angle
        {
            get
            {
                (_body.rotation * Quaternion.Inverse(_rest)).ToAngleAxis(out float degrees, out Vector3 axis);
                if (degrees > 180f)
                    degrees -= 360f;
                return Vector3.Dot(axis, Axis) < 0f ? -degrees : degrees;
            }
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _hinge = GetComponent<HingeJoint>();
            _rest = _body.rotation;
            JointLimits limits = _hinge.limits;
            _travel = Mathf.Min(-limits.min, limits.max);
            _body.angularVelocity = -Axis * StartNudge;
        }

        void FixedUpdate()
        {
            if (_hinge == null || _body.isKinematic)
                return;

            float angle = Angle;
            // Pushes away from the middle, so the lever rests at an end and snaps across once past halfway.
            _body.AddTorque(Axis * (detentStrength * angle * Mathf.Deg2Rad));

            if (!IsOn && angle >= _travel - switchMargin)
                Switch(true);
            else if (IsOn && angle <= -_travel + switchMargin)
                Switch(false);
        }

        // Both modes, within the same reach as doors.
        protected override bool IsUsableBy(PlayerInteractor interactor, in RaycastHit hit)
            => DragState.CanTakeHold(interactor, hit.point);

        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
            => Begin(interactor, InteractionStateId.Hinge, _body, hitPoint);

        void Switch(bool on)
        {
            IsOn = on;
            if (on && lockWhenOn)
            {
                _body.isKinematic = true; // frozen on; a hold on it ends
                InteractionDisabled = true;
            }
            (on ? onSwitchedOn : onSwitchedOff).Invoke();
        }
    }
}
