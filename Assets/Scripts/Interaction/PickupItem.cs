using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Sanctify.Interaction
{
    /// <summary>
    /// An item the player picks up into the inventory. Interacting starts the short
    /// <see cref="PickupState"/>, which calls <see cref="Take"/> on the grab frame.
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

        // Use the item's own body, not the one the ray reports: an item without a body that sits
        // under another rigidbody would otherwise freeze that parent.
        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
            => Begin(interactor, InteractionStateId.Pickup, _body, hitPoint);

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
