using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Base for anything the player can focus and use (Amnesia's iLuxProp). The prop owns its
    /// data and decides which interaction state handles it: its handler writes the context and
    /// asks the player's <see cref="PlayerInteractor"/> for that state. Props never read input.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("Farthest the player can be and still focus this, in metres. The interactor's range caps it too.")]
        [SerializeField, Min(0.1f)] float maxFocusDistance = 2f;
        [Tooltip("Shown under the crosshair while focused.")]
        [SerializeField] string focusText;
        [SerializeField] bool interactionDisabled;
        [Tooltip("Raised every time the player interacts, after the prop has started its interaction.")]
        [SerializeField] UnityEvent onInteract;

        PlayerInteractor _user;

        public float MaxFocusDistance => maxFocusDistance;
        public virtual string FocusText => focusText;
        public bool InteractionDisabled
        {
            get => interactionDisabled;
            set => interactionDisabled = value;
        }
        /// <summary>True while an interaction state (pickup, grab, ...) is working on this prop.</summary>
        public bool IsInteractedWith => _user != null;

        public bool CanFocus(PlayerInteractor interactor, float distance)
            => !interactionDisabled && !IsInteractedWith && distance <= maxFocusDistance && IsUsableBy(interactor);

        /// <summary>Whether the prop can be used right now, e.g. only in the cursor mode. Props that can't aren't focused.</summary>
        protected virtual bool IsUsableBy(PlayerInteractor interactor) => true;

        /// <param name="body">The rigidbody the focus ray hit, if any.</param>
        /// <param name="hitPoint">Where the focus ray hit, in world space.</param>
        public void Interact(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
        {
            HandleInteract(interactor, body, hitPoint);
            onInteract?.Invoke();
        }

        protected abstract void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint);

        /// <summary>Hands this prop to an interaction state. The state reads the context on entry.</summary>
        protected void Begin(PlayerInteractor interactor, InteractionStateId state, Rigidbody body, Vector3 hitPoint)
        {
            interactor.BeginInteraction(state, new InteractionContext(this, body, hitPoint));
        }

        internal void BeginUse(PlayerInteractor user) => _user = user;
        internal void EndUse() => _user = null;

        protected virtual void OnDestroy()
        {
            if (_user != null)
                _user.NotifyPropDestroyed(this);
        }
    }
}
