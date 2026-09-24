using UnityEngine;

namespace Sanctify.Characters.Player
{
    public enum Stance
    {
        Standing,
        Crouching,
        Tiptoe,
    }

    /// <summary>
    /// Crouch and tiptoe. Resizes the motor capsule from the feet up toward the requested
    /// stance's height, and reports how far the eyes moved so the camera rig follows. The
    /// camera therefore never leaves the capsule: the eyes sit a fixed distance below its top.
    ///
    /// Growing is checked against headroom every step, so a crouch under a ledge stays down
    /// until the player is clear, then finishes rising on its own.
    ///
    /// Driven by <see cref="PlayerController"/> from FixedUpdate, before movement.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    [DisallowMultipleComponent]
    public sealed class PlayerStance : MonoBehaviour
    {
        [Header("Capsule heights (metres)")]
        [SerializeField, Min(0.7f)] float crouchHeight = 1.2f;
        [SerializeField, Min(0.7f)] float tiptoeHeight = 2.05f;

        [Header("Speed (metres per second)")]
        [SerializeField, Min(0.1f)] float riseSpeed = 2.5f;
        [SerializeField, Min(0.1f)] float lowerSpeed = 3.5f;

        CharacterMotor _motor;
        float _previousEyeOffset;

        /// <summary>The stance being moved toward. Set by whoever owns the controls.</summary>
        public Stance Requested { get; set; } = Stance.Standing;
        /// <summary>Eye height change from standing, in metres, at the latest fixed step.</summary>
        public float EyeOffset { get; private set; }
        /// <summary>True once the capsule has reached the requested height.</summary>
        public bool AtRequestedHeight { get; private set; } = true;

        void Awake() => _motor = GetComponent<CharacterMotor>();

        public void FixedTick(float deltaTime)
        {
            float standing = _motor.StandingHeight;
            float target = Requested switch
            {
                Stance.Crouching => crouchHeight,
                Stance.Tiptoe => tiptoeHeight,
                _ => standing,
            };

            float current = _motor.Height;
            float speed = target > current ? riseSpeed : lowerSpeed;
            float wanted = Mathf.MoveTowards(current, target, speed * deltaTime);
            float actual = _motor.TrySetHeight(wanted);

            _previousEyeOffset = EyeOffset;
            EyeOffset = actual - standing;
            AtRequestedHeight = Mathf.Approximately(actual, target);
        }

        /// <summary>Eye offset between the last two fixed steps, for the camera rig.</summary>
        public float GetInterpolatedEyeOffset(float alpha) => Mathf.LerpUnclamped(_previousEyeOffset, EyeOffset, Mathf.Clamp01(alpha));
    }
}
