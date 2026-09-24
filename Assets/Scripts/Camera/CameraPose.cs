using UnityEngine;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Additive offsets contributed by camera modifiers. The rig starts from the base pose
    /// (body position + eye height, look yaw/pitch) and applies these on top.
    /// </summary>
    public struct CameraPose
    {
        /// <summary>Offset in look-local space: x right, y up, z forward.</summary>
        public Vector3 PositionOffset;
        /// <summary>Rotation offset in degrees: x pitch (positive looks down, Unity convention), y yaw, z roll.</summary>
        public Vector3 EulerOffset;
        /// <summary>Added to the base vertical field of view.</summary>
        public float FovOffset;
    }

    /// <summary>Anything that nudges the camera each frame: bob, breathing, landing dips, shakes, cutscene blends.</summary>
    public interface ICameraModifier
    {
        /// <summary>Lower runs first.</summary>
        int Order { get; }
        void Modify(ref CameraPose pose, float deltaTime);
    }
}
