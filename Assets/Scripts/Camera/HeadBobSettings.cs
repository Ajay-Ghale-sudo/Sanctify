using UnityEngine;

namespace Sanctify.Cameras
{
    [CreateAssetMenu(fileName = "SO_HeadBob", menuName = "Sanctify/Camera/Head Bob Settings")]
    public sealed class HeadBobSettings : ScriptableObject
    {
        [Header("Walking (metres / degrees)")]
        [Min(0f)] public float walkVertical = 0.02f;
        [Min(0f)] public float walkLateral = 0.012f;
        public float walkRollDegrees = 0.6f;
        public float walkPitchDegrees = 0.3f;

        [Header("Running (metres / degrees)")]
        [Min(0f)] public float runVertical = 0.05f;
        [Min(0f)] public float runLateral = 0.03f;
        public float runRollDegrees = 1.5f;
        public float runPitchDegrees = 0.8f;

        [Header("Blending")]
        [Tooltip("Horizontal speed at which locomotion bob reaches full strength. Below it the bob fades in proportionally.")]
        [Min(0.01f)] public float speedForFullBob = 0.6f;
        [Tooltip("How quickly bob strength follows speed changes. Higher = snappier.")]
        [Min(0.1f)] public float blendSharpness = 10f;

        [Header("Breathing (idle)")]
        [Min(0f)] public float breathsPerMinute = 14f;
        [Min(0f)] public float breathVertical = 0.006f;
        [Min(0f)] public float breathLateral = 0.002f;
        public float breathPitchDegrees = 0.25f;
    }
}
