using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Ramped first-person look state. Yaw is applied to the body (so movement is relative to
    /// facing); pitch is stored here and read by the camera rig. No camera transforms live on
    /// the player: the rig composes the final pose.
    ///
    /// Each axis has a ramp progress that eases up while the input is held and eases down when
    /// it is released or reversed. Angular speed = S-curve(progress) × top speed × stick
    /// magnitude. A reversal decelerates to rest first, then accelerates the other way, which
    /// gives the heavy turning of King's Field.
    ///
    /// Driven by <see cref="PlayerController"/> from Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] PlayerLookSettings settings;

        struct AxisRamp
        {
            public float Progress;   // 0..1
            public int Direction;    // -1, 0, +1
            public float Magnitude;  // 0..1, shaped stick deflection
        }

        AxisRamp _yaw;
        AxisRamp _pitch;

        bool _hasTurnAnchor;
        float _turnAnchorYaw;
        float _awayTurnScale = 1f;

        public PlayerLookSettings Settings => settings;
        /// <summary>Body yaw in degrees.</summary>
        public float Yaw { get; private set; }
        /// <summary>Pitch in degrees, positive looking up.</summary>
        public float Pitch { get; private set; }
        /// <summary>Current angular speeds in degrees per second (x = yaw, y = pitch).</summary>
        public Vector2 AngularVelocity { get; private set; }
        /// <summary>
        /// Temporary view offset from the right stick, in degrees (x = right, y = up). It never
        /// changes <see cref="Yaw"/> or <see cref="Pitch"/>; the camera adds it on top and it
        /// returns to zero when the stick is released.
        /// </summary>
        public Vector2 Peek { get; private set; }

        void Awake()
        {
            if (settings == null)
                Debug.LogError($"{nameof(PlayerLook)} on {name} has no settings asset assigned.", this);
            Yaw = transform.eulerAngles.y;
            ApplyYaw();
        }

        /// <param name="lookInput">x = yaw (-1 left, +1 right), y = pitch (-1 down, +1 up). Pass zero to coast to a stop.</param>
        /// <param name="peekInput">Right stick, x = right, y = up. Pass zero to return to centre.</param>
        /// <param name="rawPitch">
        /// Take pitch input as given, without the digital snapping or inversion meant for the pitch
        /// buttons. For input that's already a smooth direction, like the interact-mode edge turn.
        /// </param>
        public void Tick(float deltaTime, Vector2 lookInput, Vector2 peekInput, bool rawPitch)
        {
            if (settings == null || deltaTime <= 0f)
                return;

            UpdatePeek(deltaTime, peekInput);

            float yawInput = lookInput.x;
            float pitchInput = lookInput.y;
            if (!rawPitch)
            {
                if (settings.digitalPitch)
                    pitchInput = Mathf.Abs(pitchInput) >= settings.digitalThreshold ? Mathf.Sign(pitchInput) : 0f;
                if (settings.invertPitch)
                    pitchInput = -pitchInput;
            }

            float yawTop = settings.yawSpeed;
            float pitchTop = settings.yawSpeed * settings.pitchToYawRatio;

            float yawVelocity = UpdateAxis(ref _yaw, yawInput, yawTop, deltaTime);
            float pitchVelocity = UpdateAxis(ref _pitch, pitchInput, pitchTop, deltaTime);

            // Turning away from the anchor is slowed; turning back toward it isn't.
            if (_hasTurnAnchor && yawVelocity * Mathf.DeltaAngle(_turnAnchorYaw, Yaw) > 0f)
                yawVelocity *= _awayTurnScale;

            Yaw = Mathf.Repeat(Yaw + yawVelocity * deltaTime, 360f);
            ApplyYaw();

            float unclamped = Pitch + pitchVelocity * deltaTime;
            Pitch = Mathf.Clamp(unclamped, settings.pitchMin, settings.pitchMax);
            if (Pitch != unclamped)
            {
                // Hit a limit: don't wind up against it.
                _pitch.Progress = 0f;
                _pitch.Direction = 0;
                pitchVelocity = 0f;
            }

            AngularVelocity = new Vector2(yawVelocity, pitchVelocity);
        }

        /// <summary>
        /// Slows turning away from a direction, e.g. from something being dragged: while the view
        /// turns away from <paramref name="anchorYaw"/>, yaw speed is scaled by
        /// <paramref name="awayScale"/>. Turning back toward it is unaffected. Lasts until
        /// <see cref="ClearTurnAnchor"/>; set it again each frame to follow something that moves.
        /// </summary>
        /// <param name="awayScale">Share of the normal yaw speed when turning away, 0..1.</param>
        public void SetTurnAnchor(float anchorYaw, float awayScale)
        {
            _hasTurnAnchor = true;
            _turnAnchorYaw = anchorYaw;
            _awayTurnScale = Mathf.Clamp01(awayScale);
        }

        public void ClearTurnAnchor() => _hasTurnAnchor = false;

        /// <summary>Instantly sets the facing. For spawning, cutscene hand-back, etc.</summary>
        public void SetLook(float yawDegrees, float pitchDegrees)
        {
            Yaw = Mathf.Repeat(yawDegrees, 360f);
            Pitch = settings != null ? Mathf.Clamp(pitchDegrees, settings.pitchMin, settings.pitchMax) : pitchDegrees;
            ApplyYaw();
            ResetVelocity();
        }

        public void ResetVelocity()
        {
            _yaw = default;
            _pitch = default;
            AngularVelocity = Vector2.zero;
            Peek = Vector2.zero;
        }
        // ------------------------------------------------------------------

        void ApplyYaw() => transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

        void UpdatePeek(float deltaTime, Vector2 input)
        {
            // Clamp to the unit circle so the limit is the same in every direction, diagonals included.
            input = Vector2.ClampMagnitude(input, 1f);
            if (settings.invertPeekY)
                input.y = -input.y;

            Vector2 target = input * settings.peekMaxDegrees;
            Vector2 current = Peek;

            // Moving back toward centre (stick easing off or released) uses the fast return rate.
            bool returning = target.sqrMagnitude < current.sqrMagnitude;
            float sharpness = returning ? settings.peekReturnSharpness : settings.peekOutSharpness;
            current = Vector2.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));

            if (target == Vector2.zero && current.sqrMagnitude < 1e-4f)
                current = Vector2.zero;

            Peek = current;
        }

        float UpdateAxis(ref AxisRamp axis, float input, float topSpeed, float deltaTime)
        {
            int wanted = Mathf.Abs(input) > 1e-3f ? (input > 0f ? 1 : -1) : 0;
            float magnitude = Shape(Mathf.Abs(input));

            bool holding = wanted != 0 && (axis.Direction == 0 || wanted == axis.Direction);
            if (holding)
            {
                axis.Direction = wanted;
                // Follow the stick quickly but not instantly so small wobbles don't jitter.
                axis.Magnitude = Mathf.MoveTowards(axis.Magnitude, magnitude, 8f * deltaTime);
                axis.Progress = Mathf.MoveTowards(axis.Progress, 1f, deltaTime / settings.accelerationTime);
            }
            else
            {
                // Released, or asked to reverse: ease down to rest first.
                axis.Progress = Mathf.MoveTowards(axis.Progress, 0f, deltaTime / settings.decelerationTime);
                if (axis.Progress <= 0f)
                {
                    axis.Direction = wanted;
                    axis.Magnitude = wanted != 0 ? magnitude : 0f;
                }
            }

            float eased = Mathf.SmoothStep(0f, 1f, axis.Progress);
            return axis.Direction * eased * topSpeed * axis.Magnitude;
        }

        float Shape(float magnitude) => Mathf.Pow(Mathf.Clamp01(magnitude), settings.stickCurve);
    }
}
