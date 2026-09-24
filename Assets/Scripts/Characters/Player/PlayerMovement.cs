using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Turns move input into a velocity and hands it to the <see cref="CharacterMotor"/>.
    ///
    /// Velocity model: the part of velocity along the wished direction is kept and
    /// accelerated toward the cap; everything else decelerates at a fixed rate. That gives
    /// gradual direction changes and a soft stop without Source-style friction eating speed
    /// on slopes. After the move, velocity is clipped against the surfaces the motor hit
    /// (walls and ceilings only; verified ground is skipped), never rebuilt from displacement.
    ///
    /// Driven by <see cref="PlayerController"/> from FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    [DisallowMultipleComponent]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] PlayerMovementSettings settings;

        CharacterMotor _motor;
        Vector3 _velocity;
        float _walkCapForDirection;
        float _runCapForDirection;
        float _speedMultiplier = 1f;

        public StrideCycle Stride { get; } = new();

        /// <summary>Scales every speed cap, e.g. while carrying something heavy. 1 = normal.</summary>
        public float SpeedMultiplier
        {
            get => _speedMultiplier;
            set => _speedMultiplier = Mathf.Max(0f, value);
        }

        public PlayerMovementSettings Settings => settings;
        public CharacterMotor Motor => _motor;
        public Vector3 Velocity => _velocity;
        public float HorizontalSpeed { get; private set; }
        public bool IsGrounded => _motor.IsGrounded;
        public bool IsRunning { get; private set; }
        /// <summary>0 at walking pace or below, 1 at full running pace, for the current direction.</summary>
        public float RunBlend { get; private set; }
        public bool IsStopped { get; private set; }
        public float CurrentStrideLength { get; private set; }

        void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            if (settings == null)
                Debug.LogError($"{nameof(PlayerMovement)} on {name} has no settings asset assigned.", this);
            CurrentStrideLength = settings != null ? settings.walkStrideLength : 1f;
        }

        /// <param name="moveInput">x = strafe (-1 left, +1 right), y = forward (-1 back, +1 forward).</param>
        public void Tick(float deltaTime, Vector2 moveInput, bool runHeld)
        {
            if (settings == null || deltaTime <= 0f)
                return;

            Vector3 up = transform.up;
            ComputeWish(moveInput, runHeld, out Vector3 wishDirection, out float wishSpeed);

            bool grounded = _motor.IsGrounded;
            Vector3 horizontal = Vector3.ProjectOnPlane(_velocity, up);
            float verticalSpeed = Vector3.Dot(_velocity, up);

            if (grounded)
            {
                horizontal = Decelerate(horizontal, wishDirection, wishSpeed, deltaTime);
                horizontal = Accelerate(horizontal, wishDirection, wishSpeed, settings.acceleration, deltaTime, softApproach: true);
                verticalSpeed = 0f;
            }
            else
            {
                float airWish = Mathf.Min(wishSpeed, settings.airSpeedCap);
                horizontal = Accelerate(horizontal, wishDirection, airWish, settings.airAcceleration, deltaTime, softApproach: false);
                verticalSpeed = Mathf.Max(verticalSpeed - settings.gravity * deltaTime, -settings.maxFallSpeed);
            }

            _velocity = horizontal + up * verticalSpeed;
            _motor.Move(_velocity, deltaTime);
            ClipVelocityToContacts(up);

            if (_motor.IsGrounded)
                _velocity = Vector3.ProjectOnPlane(_velocity, up);

            HorizontalSpeed = Vector3.ProjectOnPlane(_velocity, up).magnitude;
            IsRunning = runHeld && wishSpeed > 0f;
            RunBlend = _runCapForDirection > _walkCapForDirection
                ? Mathf.InverseLerp(_walkCapForDirection, _runCapForDirection, HorizontalSpeed)
                : 0f;
            CurrentStrideLength = Mathf.Lerp(settings.walkStrideLength, settings.runStrideLength, RunBlend);

            float travelled = _motor.IsGrounded ? Vector3.ProjectOnPlane(_motor.LastDisplacement, up).magnitude : 0f;
            if (travelled > 0f)
                Stride.Advance(travelled, CurrentStrideLength);
            else
                Stride.Settle(deltaTime);

            bool stopped = _motor.IsGrounded && wishSpeed <= 0f && HorizontalSpeed < settings.stoppedSpeedThreshold;
            if (stopped)
            {
                if (!IsStopped)
                    Stride.Reset();
                _velocity = up * Vector3.Dot(_velocity, up);
                HorizontalSpeed = 0f;
                RunBlend = 0f;
            }
            IsStopped = stopped;
        }

        public void ResetVelocity()
        {
            _velocity = Vector3.zero;
            HorizontalSpeed = 0f;
            RunBlend = 0f;
        }

        // ------------------------------------------------------------------

        void ClipVelocityToContacts(Vector3 up)
        {
            for (int i = 0; i < _motor.ContactCount; i++)
            {
                Vector3 normal = _motor.GetContactNormal(i);
                if (_motor.IsWalkable(normal))
                {
                    // Verified ground: only stop us falling into it. Speed along it survives.
                    float down = Vector3.Dot(_velocity, up);
                    if (down < 0f)
                        _velocity -= up * down;
                    continue;
                }

                float into = Vector3.Dot(_velocity, normal);
                if (into < 0f)
                    _velocity -= normal * into;
            }
        }

        void ComputeWish(Vector2 input, bool run, out Vector3 direction, out float speed)
        {
            input.x = Mathf.Clamp(input.x, -1f, 1f);
            input.y = Mathf.Clamp(input.y, -1f, 1f);
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            if (input.sqrMagnitude < 1e-6f)
            {
                direction = Vector3.zero;
                speed = 0f;
                return;
            }

            bool backward = input.y < 0f;
            float walkForwardCap = backward ? settings.walkBackward : settings.walkForward;
            float runForwardCap = backward ? settings.runBackward : settings.runForward;

            // Both axes draw from one speed budget: the result lies inside an ellipse, so
            // diagonals never exceed the larger axis cap and running + strafing can't stack.
            Vector2 walk = new Vector2(input.x * settings.walkStrafe, input.y * walkForwardCap) * _speedMultiplier;
            Vector2 sprint = new Vector2(input.x * settings.runStrafe, input.y * runForwardCap) * _speedMultiplier;
            Vector2 chosen = run ? sprint : walk;

            _walkCapForDirection = walk.magnitude / input.magnitude;
            _runCapForDirection = sprint.magnitude / input.magnitude;

            Vector3 world = transform.TransformDirection(new Vector3(chosen.x, 0f, chosen.y));
            speed = world.magnitude;
            direction = speed > 0f ? world / speed : Vector3.zero;
        }

        /// <summary>Keeps velocity along the wish direction (up to the cap) and bleeds off the rest.</summary>
        Vector3 Decelerate(Vector3 velocity, Vector3 wishDirection, float wishSpeed, float deltaTime)
        {
            Vector3 allowed = Vector3.zero;
            if (wishSpeed > 0f)
            {
                float along = Vector3.Dot(velocity, wishDirection);
                allowed = wishDirection * Mathf.Clamp(along, 0f, wishSpeed);
            }

            Vector3 rest = velocity - allowed;
            float restSpeed = rest.magnitude;
            if (restSpeed < 1e-6f)
                return allowed;

            float rate = settings.deceleration * (wishSpeed > 0f ? 1f : settings.noInputDecelerationScale);
            float newRestSpeed = Mathf.Max(restSpeed - rate * deltaTime, 0f);
            return allowed + rest * (newRestSpeed / restSpeed);
        }

        Vector3 Accelerate(Vector3 velocity, Vector3 wishDirection, float wishSpeed, float acceleration, float deltaTime, bool softApproach)
        {
            if (wishSpeed <= 0f)
                return velocity;

            float along = Vector3.Dot(velocity, wishDirection);
            float maxAdd = wishSpeed - along;
            if (maxAdd <= 0f)
                return velocity;

            if (softApproach && along > wishSpeed * settings.softApproachStart)
                acceleration *= settings.softApproachScale;

            float speedLimit = Mathf.Max(velocity.magnitude, wishSpeed);
            velocity += wishDirection * Mathf.Min(acceleration * deltaTime, maxAdd);

            float newSpeed = velocity.magnitude;
            if (newSpeed > speedLimit)
                velocity *= speedLimit / newSpeed;

            return velocity;
        }
    }
}
