using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A loose physics object the player can carry and, if its data allows, throw (Amnesia's
    /// LuxProp_Object). Push and drawer modes will join Grab here as a mode switch, as in
    /// Amnesia, rather than as separate classes.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicsProp : Interactable
    {
        [Tooltip("How it's held and whether it can be thrown. Empty uses stock values, which can't be thrown.")]
        [SerializeField] GrabData grab;

        Rigidbody _body;

        public GrabData Grab => grab != null ? grab : GrabData.Default;

        void Awake() => _body = GetComponent<Rigidbody>();

        // Prefer the body the ray hit, so a prop made of jointed parts is held by the part you grabbed.
        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
            => Begin(interactor, InteractionStateId.Grab, body != null ? body : _body, hitPoint);
    }
}
