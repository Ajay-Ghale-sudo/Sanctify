using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Player-wide tuning for handling objects: how hard and how loosely the hands pull, how
    /// throws scale with weight, what letting go leaves objects with, how much weight slows
    /// the player, and what's too heavy to lift and how dragging it goes. Per-prop tuning lives
    /// in <see cref="GrabData"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_PlayerGrab", menuName = "Sanctify/Player/Grab Settings")]
    public sealed class PlayerGrabSettings : ScriptableObject
    {
        [Header("Pull (acts on the point you grabbed, per kg of its effective mass)")]
        [Tooltip("Spring pulling the grabbed point toward the cursor. Higher = tighter following. Keep Damping near 2 × √Stiffness.")]
        [Min(0f)] public float stiffness = 225f;
        [Tooltip("Damping on the grabbed point's velocity relative to the player. Walking carries objects without lag; moving the cursor or turning leaves them trailing by roughly Damping / Stiffness seconds. At 2 × √Stiffness (critical damping) they stop on the cursor without overshooting; below it they sail past and swing back. Keep Damping × fixed timestep under about 0.6, or the point buzzes.")]
        [Min(0f)] public float damping = 30f;
        [Tooltip("Strongest pull, in newtons. Heavy objects hit this and trail further behind the cursor, and they're brought in only as fast as this can stop them, so they don't sail past it. Their weight is carried separately, so they lag rather than drop.")]
        [Min(0f)] public float maxPullForce = 80f;
        [Tooltip("How quickly a held object stops swinging and twisting, per second. Acts about the grabbed point, where the swing is, rather than about the centre of mass like a Rigidbody's angular damping. The swing dies to a tenth in about 4.6 / this seconds.")]
        [Min(0f)] public float swingDamping = 6f;

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
        [Tooltip("How far past the side of the player's body a loose object can be grabbed, in metres, measured flat so the floor is in reach too. Farther off, it isn't focused. Pickups reach as far as the focus ray; heavy objects only within Drag Reach.")]
        [Min(0f)] public float grabReach = 0.7f;
        [Tooltip("How far the object is drawn toward you when grabbed, in metres.")]
        [Min(0f)] public float grabPull = 0.08f;
        [Tooltip("Objects grabbed from farther than this, in metres from the eye along the cursor, are drawn in to it. The bumpers can still move them back out to the prop's Max Depth.")]
        [Min(0.1f)] public float holdDistance = 0.9f;
        [Tooltip("How fast they're drawn in, in m/s.")]
        [Min(0.1f)] public float drawInSpeed = 1.5f;
        [Tooltip("The held object's goal is kept at least this far outside the player's capsule, sideways, so it can't be dragged into the camera.")]
        [Min(0f)] public float keepOutMargin = 0.15f;
        [Tooltip("Seconds the grabbed point can be out of sight before the object is dropped, so passing behind something thin doesn't end the grab.")]
        [Min(0f)] public float lineOfSightGrace = 0.5f;

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

        [Header("Drag (too heavy to lift)")]
        [Tooltip("Heaviest object that can be lifted, in kg. Heavier ones are dragged instead: walking pushes and pulls them along the floor. A prop's Handling can override this.")]
        [Min(0f)] public float maxLiftMass = 20f;
        [Tooltip("How close the player has to be to drag something, in metres from the side of their body to the grabbed point, measured flat. They also have to face it, within three quarters of Drag Let Go Angle. Otherwise, heavy objects aren't focused.")]
        [Min(0f)] public float dragReach = 0.5f;
        [Tooltip("Hardest the player can push or pull, in newtons, applied at the grabbed point against the floor's friction. Decides what moves at all and how quickly it gets going. With the default 0.6 friction, about 110 kg is the most that budges.")]
        [Min(1f)] public float dragStrength = 650f;
        [Tooltip("Hardest the player can push or pull something jointed, such as a door by its handle, in newtons. Kept low so a prop heavier than Max Lift Mass wedged against a door holds it.")]
        [Min(1f)] public float jointedDragStrength = 200f;
        [Tooltip("Seconds to put full strength into a push or pull. Heavy things wait until the effort builds past their friction.")]
        [Min(0.01f)] public float dragLeanTime = 0.4f;
        [Tooltip("Top pace for an object just over Max Lift Mass, in m/s. The push eases off as it nears this, and the player never moves faster than the grabbed point does.")]
        [Min(0.05f)] public float dragSpeed = 0.9f;
        [Tooltip("Top pace for an object of Heavy Drag Mass or more, in m/s.")]
        [Min(0.05f)] public float heavyDragSpeed = 0.3f;
        [Min(0f)] public float heavyDragMass = 100f;
        [Tooltip("How far the hands try to raise the grabbed point above where it rested, in metres, as if pulling it up off the floor. Grabbed low, this lifts the near edge over a lip.")]
        [Min(0f)] public float dragLiftHeight = 0.05f;
        [Tooltip("Most upward force the hands put into that lift, in newtons, when grabbed at the base. It shrinks the higher up the object the grab is, to none at the top, so a crate pulled by its top tips over instead of sliding. It's also held under 60% of the object's weight, so dragging never carries it off the floor. Heavier things lift less, or just get lighter to drag.")]
        [Min(0f)] public float maxDragLift = 250f;
        [Tooltip("How far the player can turn from the grabbed point while dragging, in degrees, before letting go. A drag can only start within three quarters of this, so it doesn't end as soon as it starts.")]
        [Range(10f, 180f)] public float dragLetGoAngle = 60f;
        [Tooltip("Turning away from the grabbed point slows the further it's turned: from full speed facing it down to this share of full speed at the let-go angle. Turning back toward it is always full speed.")]
        [Range(0.05f, 1f)] public float dragSlowestTurn = 0.25f;

        public bool IsTooHeavyToLift(float mass) => mass > maxLiftMass;

        public float DragSpeedFor(float mass)
            => Mathf.Lerp(dragSpeed, heavyDragSpeed, Mathf.InverseLerp(maxLiftMass, heavyDragMass, mass));

        public float SpeedMultiplierFor(float mass)
            => Mathf.Lerp(1f, slowestSpeedMultiplier, Mathf.InverseLerp(slowdownStartMass, slowdownFullMass, mass));

        public float ThrowSpeedFor(float mass, float multiplier)
            => Mathf.Min(throwStrength * multiplier / Mathf.Max(mass, 0.01f), maxThrowSpeed);
    }
}
