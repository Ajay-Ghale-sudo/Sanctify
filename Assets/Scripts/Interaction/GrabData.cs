using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Per-prop tuning for how an object is carried and thrown (Amnesia's grab data). Share one
    /// asset between props that should feel the same. Player-wide tuning lives in
    /// <see cref="PlayerGrabSettings"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Grab", menuName = "Sanctify/Interaction/Grab Data")]
    public sealed class GrabData : ScriptableObject
    {
        [Header("Orientation")]
        [Tooltip("Keep the angle it had when grabbed, relative to the view, instead of swinging freely from the grab point. For things that should stay upright, like a lantern.")]
        public bool holdOrientation;

        [Header("Depth (metres from the eye)")]
        [Tooltip("Hold at Depth instead of where it was grabbed.")]
        public bool useFixedDepth;
        [Min(0.1f)] public float depth = 1f;
        [Tooltip("Nearest and farthest the object can be moved to while held.")]
        [Min(0.1f)] public float minDepth = 0.6f;
        [Min(0.1f)] public float maxDepth = 1.6f;

        [Header("Fixed pose")]
        [Tooltip("Hold by the centre at a set offset and angle relative to the view, instead of by the point grabbed. Implies Hold Orientation.")]
        public bool usePoseOffset;
        [Tooltip("Metres, view space (x right, y up), from the point under the cursor.")]
        public Vector3 positionOffset;
        [Tooltip("Degrees, view space.")]
        public Vector3 rotationOffset;

        [Header("Physics")]
        [Tooltip("Mass multiplier while held. Above 1 lets the held object shove other things harder.")]
        [Min(0.01f)] public float massMultiplier = 1f;
        [Min(0f)] public float forceMultiplier = 1f;
        [Tooltip("Only used with Hold Orientation.")]
        [Min(0f)] public float torqueMultiplier = 1f;

        [Header("Throw")]
        [Tooltip("Scales the player's throw strength. Launch speed is strength × this ÷ mass, so heavy props barely leave the hand unless this is raised (a brick, say). 0 = can't be thrown.")]
        [Min(0f)] public float throwMultiplier = 1f;

        public bool CanThrow => throwMultiplier > 0f;
        public bool HoldsOrientation => holdOrientation || usePoseOffset;

        static GrabData _default;

        /// <summary>Stock values, for props with no data assigned.</summary>
        public static GrabData Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<GrabData>();
                    _default.name = "GrabData (default)";
                    _default.hideFlags = HideFlags.HideAndDontSave;
                }
                return _default;
            }
        }
    }
}
