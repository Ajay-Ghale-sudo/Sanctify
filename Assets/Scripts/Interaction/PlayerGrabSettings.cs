using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Player-wide tuning for carrying objects: how hard and how loosely the hands pull, how
    /// throws scale with weight, what letting go leaves objects with, and how much weight slows
    /// the player. Per-prop tuning lives in <see cref="GrabData"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_PlayerGrab", menuName = "Sanctify/Player/Grab Settings")]
    public sealed class PlayerGrabSettings : ScriptableObject
    {
        [Header("Pull (acts on the point you grabbed, per kg)")]
        [Tooltip("Spring pulling the grabbed point toward the cursor. Lower = looser, laggier following.")]
        [Min(0f)] public float stiffness = 100f;
        [Tooltip("Damping on the grabbed point's velocity relative to the player. Walking carries objects without lag; dragging and turning leave them trailing by roughly Damping / Stiffness seconds. Keep Damping × fixed timestep well under 1, or objects grabbed near a corner buzz.")]
        [Min(0f)] public float damping = 12f;
        [Tooltip("Strongest pull, in newtons. Heavy objects hit this and trail further behind the cursor.")]
        [Min(0f)] public float maxPullForce = 80f;
        [Tooltip("Angular damping while held, so objects swinging from the grab point settle.")]
        [Min(0f)] public float heldAngularDamping = 2f;

        [Header("Orientation (Hold Orientation props only)")]
        [Tooltip("How quickly the object turns back to its held angle: roughly 1 / seconds.")]
        [Min(0f)] public float orientationResponse = 8f;
        [Tooltip("Fastest it turns to keep its angle, in rad/s.")]
        [Min(0f)] public float maxSpin = 6f;
        [Tooltip("How quickly spin catches up with the wanted spin, per second. Capped below the physics rate so it can't overshoot each step.")]
        [Min(0f)] public float spinCatchUp = 30f;
        [Tooltip("Cap on the angular acceleration used to reach the wanted spin, in rad/s².")]
        [Min(0f)] public float maxAngularAcceleration = 200f;

        [Header("Grab")]
        [Tooltip("How far the object is drawn toward you when grabbed, in metres.")]
        [Min(0f)] public float grabPull = 0.08f;
        [Tooltip("The held object's goal is kept at least this far outside the player's capsule, sideways, so it can't be dragged into the camera.")]
        [Min(0f)] public float keepOutMargin = 0.15f;

        [Header("Throw")]
        [Tooltip("Impulse given to a thrown object, in N·s. Launch speed is this × the prop's Throw Multiplier ÷ its mass, so weight decides distance.")]
        [Min(0f)] public float throwStrength = 8f;
        [Tooltip("Launch speed cap, in m/s, so very light things don't rocket off.")]
        [Min(0f)] public float maxThrowSpeed = 14f;
        [Tooltip("Degrees the throw is angled up from the aim, for a natural arc.")]
        [Range(0f, 45f)] public float throwLoft = 8f;

        [Header("Release")]
        [Tooltip("Speed cap when letting go, so whipping the view can't fling things. Throwing is the only launch.")]
        [Min(0f)] public float maxReleaseSpeed = 2f;
        [Tooltip("Spin cap when letting go, in rad/s.")]
        [Min(0f)] public float maxReleaseAngularSpeed = 4f;

        [Header("Player slowdown (by the object's real mass, kg)")]
        [Min(0f)] public float slowdownStartMass = 2f;
        [Min(0f)] public float slowdownFullMass = 20f;
        [Range(0f, 1f)] public float slowestSpeedMultiplier = 0.5f;

        public float SpeedMultiplierFor(float mass)
            => Mathf.Lerp(1f, slowestSpeedMultiplier, Mathf.InverseLerp(slowdownStartMass, slowdownFullMass, mass));

        public float ThrowSpeedFor(float mass, float multiplier)
            => Mathf.Min(throwStrength * multiplier / Mathf.Max(mass, 0.01f), maxThrowSpeed);
    }
}
