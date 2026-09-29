using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>What an interactor needs from the pawn carrying it: the walking player or the Mage Hand.</summary>
    public interface IInteractorBody
    {
        /// <summary>Eye pose at the latest physics step, no bob. Held objects aim from here.</summary>
        Pose ViewPose { get; }
        /// <summary>Held objects are damped against this, so they travel with the pawn.</summary>
        Vector3 Velocity { get; }
        /// <summary>How fast the view is turning, in degrees per second. Interact mode's leash tightens while it turns.</summary>
        float TurnSpeed { get; }
        /// <summary>Top-speed scale while carrying. 1 = normal.</summary>
        float SpeedMultiplier { set; }
        /// <summary>How far past the pawn's surface a point is: the reach needed to touch it.</summary>
        float ReachTo(Vector3 point);
        /// <summary>Moves a hold goal outside the pawn, so a held object can't be pulled into the camera.</summary>
        Vector3 KeepOut(Vector3 goal, float margin);
        /// <summary>What the pawn stands on, which it can't hold. Null for the hand.</summary>
        Collider Ground { get; }
        /// <summary>The pawn's own collider, for overlap tests.</summary>
        Collider Shape { get; }
        /// <summary>Stops or restores collision with the pawn, in physics and in any movement queries.</summary>
        void SetIgnored(Collider collider, bool ignored);
    }
}
