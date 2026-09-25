using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A loose physics object the player can carry and, if its data allows, throw, or drag along
    /// the floor if it's too heavy to lift (Amnesia's LuxProp_Object). One class with a handling
    /// switch, as in Amnesia, rather than a class per kind of handling.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicsProp : Interactable
    {
        public enum Handling
        {
            /// <summary>Dragged if heavier than the player's Max Lift Mass, lifted otherwise.</summary>
            ByMass,
            Lift,
            Drag,
        }

        [Tooltip("Lifted and carried, or dragged along the floor. By Mass drags it if it's heavier than the player's Max Lift Mass.")]
        [SerializeField] Handling handling = Handling.ByMass;
        [Tooltip("How it's held and whether it can be thrown, when lifted. Empty uses stock values, which can't be thrown.")]
        [SerializeField] GrabData grab;

        Rigidbody _body;

        public GrabData Grab => grab != null ? grab : GrabData.Default;

        void Awake() => _body = GetComponent<Rigidbody>();

        // Handling objects is part of the cursor mode: outside it, loose objects aren't focused.
        // In it, they're only focused within arm's reach, which is shorter for dragging than
        // for lifting (pickups reach as far as the focus ray).
        protected override bool IsUsableBy(PlayerInteractor interactor, in RaycastHit hit)
        {
            if (!interactor.CursorMode)
                return false;
            return Drags(interactor, Target(hit.rigidbody))
                ? DragState.CanTakeHold(interactor, hit.point)
                : GrabState.InReach(interactor, hit.point);
        }

        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
        {
            Rigidbody target = Target(body);
            InteractionStateId state = Drags(interactor, target) ? InteractionStateId.Drag : InteractionStateId.Grab;
            Begin(interactor, state, target, hitPoint);
        }

        // Prefer the body the ray hit, so a prop made of jointed parts is held by the part you grabbed.
        Rigidbody Target(Rigidbody hitBody) => hitBody != null ? hitBody : _body;

        bool Drags(PlayerInteractor interactor, Rigidbody target) => handling switch
        {
            Handling.Lift => false,
            Handling.Drag => true,
            _ => interactor.GrabSettings.IsTooHeavyToLift(target.mass),
        };
    }
}
