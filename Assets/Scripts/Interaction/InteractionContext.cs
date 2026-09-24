using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// What a prop hands to an interaction state (Amnesia's cLuxPlayerStateVars). Written by the
    /// prop just before the switch, read by the state on entry.
    /// </summary>
    public readonly struct InteractionContext
    {
        public readonly Interactable Prop;
        /// <summary>The body to work on. Null for props without physics.</summary>
        public readonly Rigidbody Body;
        /// <summary>Where the focus ray hit, in world space.</summary>
        public readonly Vector3 HitPoint;

        public InteractionContext(Interactable prop, Rigidbody body, Vector3 hitPoint)
        {
            Prop = prop;
            Body = body;
            HitPoint = hitPoint;
        }
    }
}
