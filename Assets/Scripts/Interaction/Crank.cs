using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A crank, turned by circling the right stick while holding it (<see cref="CrankState"/>),
    /// in either mode. Clockwise winds it and anticlockwise unwinds it, between none and Max
    /// Turns, where it stops. Resistance makes it take more circles of the stick per turn. Let
    /// go, it turns itself back to none at Unwind Speed, unless that's 0, or it's wound all the
    /// way and Holds When Full.
    ///
    /// It can wind a <see cref="LiftGate"/>, which follows it. The gate is its load, so it can't
    /// be unwound any further than the gate can close: something under the gate stops the crank
    /// too, until it's moved. Winding the gate open is never stopped. Turned reports how far it's
    /// wound, 0..1, for anything else. This transform is what turns, about its forward axis,
    /// which should point out toward the player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Crank : Interactable
    {
        [Tooltip("How far it winds before it stops, in turns. 0.25 is a quarter turn.")]
        [SerializeField, Min(0.01f)] float maxTurns = 3f;
        [Tooltip("Circles of the stick per turn of the crank. 1 turns with the stick; higher is stiffer and slower.")]
        [SerializeField, Min(1f)] float resistance = 1f;
        [Tooltip("How fast it unwinds itself while nobody holds it, in turns per second. 0 = it stays where it's left.")]
        [SerializeField, Min(0f)] float unwindSpeed;
        [Tooltip("Once wound all the way, it stays there instead of unwinding itself.")]
        [SerializeField] bool holdsWhenFull;
        [Tooltip("Optional. The gate it winds open and shut. Anything under the gate stops the crank unwinding too.")]
        [SerializeField] LiftGate gate;
        [Tooltip("Raised whenever it turns, with how far it's wound: 0 at none, 1 at Max Turns.")]
        [SerializeField] UnityEvent<float> onTurned = new();

        Quaternion _rest;
        float _degrees;

        public UnityEvent<float> OnTurned => onTurned;
        /// <summary>How far it's wound: 0 at none, 1 at Max Turns.</summary>
        public float Wound => _degrees / MaxDegrees;
        float MaxDegrees => maxTurns * 360f;

        void Awake() => _rest = transform.localRotation;

        void Update()
        {
            bool held = IsInteractedWith || (holdsWhenFull && _degrees >= MaxDegrees);
            if (unwindSpeed > 0f && !held && _degrees > 0f)
                SetDegrees(_degrees - unwindSpeed * 360f * Time.deltaTime);
        }

        // In either mode, within the same reach as doors.
        protected override bool IsUsableBy(PlayerInteractor interactor, in RaycastHit hit)
            => DragState.CanTakeHold(interactor, hit.point);

        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
            => Begin(interactor, InteractionStateId.Crank, null, hitPoint);

        /// <summary>Turns it by a turn of the stick, in degrees, clockwise positive. Resistance scales it down.</summary>
        public void Turn(float stickDegrees) => SetDegrees(_degrees + stickDegrees / resistance);

        void SetDegrees(float degrees)
        {
            degrees = Mathf.Clamp(degrees, 0f, MaxDegrees);
            // Unwinding lowers the gate, so it goes no further than the gate can.
            if (gate != null && degrees < _degrees)
                degrees = Mathf.Max(degrees, gate.Reachable(degrees / MaxDegrees) * MaxDegrees);
            if (degrees == _degrees)
                return;
            _degrees = degrees;
            // Seen from in front, turning positively about forward is clockwise.
            transform.localRotation = _rest * Quaternion.AngleAxis(_degrees, Vector3.forward);
            if (gate != null)
                gate.SetOpenness(Wound);
            onTurned.Invoke(Wound);
        }
    }
}
