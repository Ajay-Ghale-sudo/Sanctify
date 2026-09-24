using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Base for states that work on one prop (Amnesia's iLuxPlayerState_Interact). Reads the
    /// context the prop wrote, marks the prop as in use, and leaves if the prop is destroyed.
    /// Subclasses call base.Enter first and base.Exit last.
    /// </summary>
    public abstract class HeldState : InteractionState
    {
        protected HeldState(PlayerInteractor interactor) : base(interactor) { }

        protected Interactable Prop { get; private set; }
        /// <summary>The body to work on. Null for props without a rigidbody.</summary>
        protected Rigidbody Body { get; private set; }
        protected Vector3 HitPoint { get; private set; }
        /// <summary>Set when the prop was destroyed while in use. Exit must not touch it then.</summary>
        protected bool PropDestroyed { get; private set; }

        public override void Enter()
        {
            InteractionContext context = Interactor.Context;
            Prop = context.Prop;
            Body = context.Body;
            HitPoint = context.HitPoint;
            PropDestroyed = false;
            Prop.BeginUse(Interactor);
        }

        public override void Exit()
        {
            if (!PropDestroyed && Prop != null)
                Prop.EndUse();
            Prop = null;
            Body = null;
        }

        public override void OnPropDestroyed(Interactable prop)
        {
            if (!ReferenceEquals(prop, Prop))
                return;
            PropDestroyed = true;
            ReturnToPrevious();
        }
    }
}
