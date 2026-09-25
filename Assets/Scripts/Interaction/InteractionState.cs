using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    public enum InteractionStateId
    {
        Default,
        Pickup,
        Grab,
        Drag,
    }

    /// <summary>Buttons routed through the interaction state. Look and movement have their own hooks.</summary>
    public enum InteractionAction
    {
        Interact,
        Attack,
        Magic,
    }

    /// <summary>
    /// One mode of the player's interaction state machine (Amnesia's iLuxPlayerState).
    /// <see cref="PlayerInteractor"/> creates one instance of each state up front and routes
    /// input to whichever is current.
    ///
    /// Input hooks return true to let the default action (look, walk, attack, magic) run as
    /// well, false to swallow it. Every state restores in <see cref="Exit"/> whatever it changed
    /// in <see cref="Enter"/>, and states that hold something end themselves.
    /// </summary>
    public abstract class InteractionState
    {
        protected InteractionState(PlayerInteractor interactor) => Interactor = interactor;

        protected PlayerInteractor Interactor { get; }
        /// <summary>The state that was current before this one. Held states return here when they end.</summary>
        public InteractionState Previous { get; internal set; }

        /// <summary>Whether a prop may start this state with the given context. A refusal leaves the current state running.</summary>
        public virtual bool CanEnter(in InteractionContext context) => true;
        public virtual void Enter() { }
        public virtual void Exit() { }
        /// <summary>Update, after input has been routed.</summary>
        public virtual void Tick(float deltaTime) { }
        /// <summary>FixedUpdate, after player movement. Apply forces here.</summary>
        public virtual void FixedTick(float deltaTime) { }
        /// <summary>LateUpdate, after the camera rig. Place anything that follows the camera here.</summary>
        public virtual void LateTick(float deltaTime) { }

        public virtual bool OnAction(InteractionAction action, bool pressed) => true;
        /// <param name="look">Look rate input, x = yaw, y = pitch, -1..1. Not a mouse delta.</param>
        public virtual bool OnLook(Vector2 look) => true;
        /// <param name="move">x = strafe, y = forward, -1..1.</param>
        public virtual bool OnMove(Vector2 move) => true;
        public virtual void OnPropDestroyed(Interactable prop) { }

        protected void ReturnToPrevious() => Interactor.ChangeState(Previous);
    }
}
