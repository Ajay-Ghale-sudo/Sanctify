using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Sanctify.Interaction
{
    /// <summary>
    /// An item the player picks up into the inventory. Interacting starts the short
    /// <see cref="PickupState"/>, which calls <see cref="Take"/> on the grab frame.
    ///
    /// A pawn that can't pick things up (the Mage Hand) grabs it like any loose object instead:
    /// only in interact mode and within reach, and dragged if it's too heavy for that pawn to
    /// lift. A pawn that can pick things up can pick it up out of that one's grip, so the hand
    /// fetches items and the player takes them from it.
    /// </summary>
    public sealed class PickupItem : Interactable
    {
        [Header("Item")]
        [SerializeField] string displayName = "Item";
        [SerializeField, Min(1)] int amount = 1;

        [Header("Pickup")]
        [Tooltip("Seconds from pressing interact until control returns.")]
        [SerializeField, Min(0.05f)] float duration = 0.6f;
        [Tooltip("Fraction of Duration at which the item reaches the hand and is taken. Interrupting after this point keeps the item.")]
        [SerializeField, Range(0.05f, 1f)] float takeAt = 0.5f;
        [Tooltip("Raised when the item is actually taken, on the grab frame.")]
        [SerializeField] UnityEvent onPickup;

        Rigidbody _body;

        public string DisplayName => displayName;
        public int Amount => amount;
        public float Duration => duration;
        public float TakeAt => takeAt;
        /// <summary>Falls back to the item's name when no focus text is set.</summary>
        public override string FocusText => string.IsNullOrEmpty(base.FocusText) ? displayName : base.FocusText;

        void Awake() => _body = GetComponent<Rigidbody>();

        // Picked up anywhere the focus ray reaches; grabbed as a loose object is (see PhysicsProp).
        protected override bool IsUsableBy(PlayerInteractor interactor, in RaycastHit hit)
        {
            if (interactor.HasState(InteractionStateId.Pickup))
                return true;
            if (!interactor.CursorMode)
                return false;
            return Drags(interactor) ? DragState.CanTakeHold(interactor, hit.point) : GrabState.InReach(interactor, hit.point);
        }

        // Use the item's own body, not the one the ray reports: an item without a body that sits
        // under another rigidbody would otherwise freeze that parent.
        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
        {
            if (interactor.HasState(InteractionStateId.Pickup))
            {
                Begin(interactor, InteractionStateId.Pickup, _body, hitPoint);
                return;
            }
            // ponytail: an item placed without a body gets a stock 1 kg one to be carried, and
            // stays loose after; give the item its own Rigidbody where the mass matters
            if (_body == null)
                _body = gameObject.AddComponent<Rigidbody>();
            Begin(interactor, Drags(interactor) ? InteractionStateId.Drag : InteractionStateId.Grab, _body, hitPoint);
        }

        // Taken out of the grip of a pawn that can only carry it.
        protected override bool CanTakeFrom(PlayerInteractor holder, PlayerInteractor taker)
            => taker.HasState(InteractionStateId.Pickup) && !holder.HasState(InteractionStateId.Pickup);

        bool Drags(PlayerInteractor interactor) => _body != null && interactor.GrabSettings.IsTooHeavyToLift(_body.mass);

        /// <summary>
        /// Adds the item to the inventory (Amnesia's item interact handler, moved to the grab frame).
        /// Returns false if it was refused, in which case the item stays in the world.
        /// </summary>
        public bool Take()
        {
            // No inventory yet, so everything is accepted. Refuse here once there is one and it's full.
            Debug.Log($"Picked up {displayName} x{amount}", this);
            onPickup?.Invoke();
            return true;
        }
    }
}
