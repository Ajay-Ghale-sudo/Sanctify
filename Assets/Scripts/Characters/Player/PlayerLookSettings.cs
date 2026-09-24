using UnityEngine;

namespace Sanctify.Characters.Player
{
    [CreateAssetMenu(fileName = "SO_PlayerLook", menuName = "Sanctify/Player/Look Settings")]
    public sealed class PlayerLookSettings : ScriptableObject
    {
        [Header("Top speed")]
        [Tooltip("Yaw top speed in degrees per second.")]
        [Min(0f)] public float yawSpeed = 100f;
        [Tooltip("Pitch top speed as a fraction of yaw speed.")]
        [Range(0.1f, 2f)] public float pitchToYawRatio = 0.667f;

        [Header("Ramping (seconds, eased)")]
        [Tooltip("Time from rest to top speed after input starts. Uses an S-curve, not a straight line.")]
        [Min(0.01f)] public float accelerationTime = 0.35f;
        [Tooltip("Time from top speed to rest after input stops, or before a reversal starts.")]
        [Min(0.01f)] public float decelerationTime = 0.3f;

        [Header("Input shaping")]
        [Tooltip("Stick response curve for yaw. 1 = linear; higher gives finer control near centre.")]
        [Range(1f, 3f)] public float stickCurve = 1.5f;
        [Tooltip("Treat the pitch triggers as buttons: any press past the threshold is full speed, like KF4 on PS2.")]
        public bool digitalPitch = true;
        [Range(0.05f, 0.95f)] public float digitalThreshold = 0.4f;

        [Header("Peek (right stick)")]
        [Tooltip("Furthest the view can shift from centre, in degrees, in any direction.")]
        [Range(0f, 20f)] public float peekMaxDegrees = 5f;
        [Tooltip("How quickly the view follows the stick outward. Higher = snappier.")]
        [Min(0.1f)] public float peekOutSharpness = 14f;
        [Tooltip("How quickly the view returns to centre when the stick eases off or is released. High values snap back.")]
        [Min(0.1f)] public float peekReturnSharpness = 35f;
        public bool invertPeekY = false;

        [Header("Pitch limits (degrees, positive = up)")]
        public float pitchMin = -55f;
        public float pitchMax = 55f;
        public bool invertPitch = false;
    }
}
