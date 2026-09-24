using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Free-roaming state (Amnesia's DefaultBase). Casts the focus ray through the centre of the
    /// view every frame and, on interact, hands the focused prop the hit; the prop picks the next
    /// state. Everything else passes through to the normal walk, look, attack and magic.
    /// </summary>
    public sealed class DefaultState : InteractionState
    {
        Rigidbody _focusBody;
        Vector3 _focusPoint;

        public DefaultState(PlayerInteractor interactor) : base(interactor) { }

        /// <summary>The prop under the crosshair, or null.</summary>
        public Interactable FocusProp { get; private set; }

        public override void Exit() => ClearFocus();

        public override void Tick(float deltaTime)
        {
            ClearFocus();
            if (!Interactor.CastFocusRay(out RaycastHit hit))
                return;

            var prop = hit.collider.GetComponentInParent<Interactable>();
            if (prop == null || !prop.CanFocus(Interactor, hit.distance))
                return;

            FocusProp = prop;
            _focusBody = hit.rigidbody;
            _focusPoint = hit.point;
        }

        public override bool OnAction(InteractionAction action, bool pressed)
        {
            if (action != InteractionAction.Interact || !pressed || FocusProp == null)
                return true;

            FocusProp.Interact(Interactor, _focusBody, _focusPoint);
            return false;
        }

        void ClearFocus()
        {
            FocusProp = null;
            _focusBody = null;
        }
    }
}
