using System.Collections.Generic;
using Sanctify.Characters;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Composes the camera pose every frame instead of relying on a transform chain:
    ///
    ///   position = interpolated body position + eye height + Σ modifier offsets (look-local)
    ///   rotation = yaw × pitch × Σ modifier euler offsets
    ///   fov      = base fov + Σ modifier fov offsets
    ///
    /// The body moves on the fixed timestep; this reads <see cref="CharacterMotor"/>'s
    /// previous/current positions and interpolates, so the camera is smooth at any frame rate.
    /// Modifiers are found on this GameObject at startup and run in <see cref="ICameraModifier.Order"/>.
    ///
    /// Driven by <see cref="PlayerController"/> from LateUpdate.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public enum FovAxis { Vertical, Horizontal }

        [SerializeField, Min(0f)] float eyeHeight = 1.65f;

        [Header("Field of view")]
        [SerializeField, Range(1f, 179f)] float fieldOfView = 80f;
        [Tooltip("Which axis the FOV value applies to. Unity's own FOV is vertical.")]
        [SerializeField] FovAxis fovAxis = FovAxis.Vertical;

        Camera _camera;
        CharacterMotor _motor;
        PlayerLook _look;
        PlayerStance _stance;
        FixedAspectRenderer _aspect;
        readonly List<ICameraModifier> _modifiers = new();

        public Camera Camera => _camera;
        public float EyeHeight => eyeHeight;
        /// <summary>Pose before modifiers, for systems that want a stable reference (e.g. aiming).</summary>
        public Vector3 BasePosition { get; private set; }
        public Quaternion BaseRotation { get; private set; }

        /// <summary>
        /// View pose at the latest fixed step: body position + eye height with the look rotation,
        /// no modifiers and no interpolation. Physics that follows the view (held objects) aims at
        /// this, so head bob and dips don't shake it, and it lines up with the interpolated camera.
        /// </summary>
        public Pose FixedStepPose
        {
            get
            {
                if (_motor == null || _look == null)
                    return new Pose(transform.position, transform.rotation);
                float eye = eyeHeight + (_stance != null ? _stance.EyeOffset : 0f);
                return new Pose(_motor.Position + _motor.transform.up * eye, LookRotation);
            }
        }

        Quaternion LookRotation => Quaternion.Euler(-_look.Pitch, _look.Yaw, 0f);

        /// <summary>Fraction of the way from the last fixed step to the next, for interpolating fixed-step state.</summary>
        public static float FixedAlpha
        {
            get
            {
                float step = Time.fixedDeltaTime;
                return step > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / step) : 1f;
            }
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _motor = GetComponentInParent<CharacterMotor>();
            _look = GetComponentInParent<PlayerLook>();
            _stance = GetComponentInParent<PlayerStance>();
            _aspect = GetComponent<FixedAspectRenderer>();

            if (_motor == null)
                Debug.LogError($"{nameof(PlayerCameraRig)} on {name} needs a {nameof(CharacterMotor)} on a parent.", this);
            if (_look == null)
                Debug.LogError($"{nameof(PlayerCameraRig)} on {name} needs a {nameof(PlayerLook)} on a parent.", this);

            GetComponents(_modifiers);
            _modifiers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public void Tick(float deltaTime)
        {
            if (_motor == null || _look == null)
                return;

            Vector3 up = _motor.transform.up;
            float alpha = FixedAlpha;
            Vector3 body = _motor.GetInterpolatedPosition(alpha);
            float eye = eyeHeight + (_stance != null ? _stance.GetInterpolatedEyeOffset(alpha) : 0f);

            BaseRotation = LookRotation;
            BasePosition = body + up * eye;

            CameraPose pose = default;
            for (int i = 0; i < _modifiers.Count; i++)
                _modifiers[i].Modify(ref pose, deltaTime);

            Vector3 position = BasePosition + BaseRotation * pose.PositionOffset;
            Quaternion rotation = BaseRotation * Quaternion.Euler(pose.EulerOffset);
            transform.SetPositionAndRotation(position, rotation);

            float aspect = _aspect != null ? _aspect.TargetAspect : _camera.aspect;
            float baseFov = fovAxis == FovAxis.Horizontal ? Camera.HorizontalToVerticalFieldOfView(fieldOfView, aspect) : fieldOfView;
            _camera.fieldOfView = Mathf.Clamp(baseFov + pose.FovOffset, 1f, 179f);
        }

        /// <summary>Register a modifier created at runtime (e.g. a cutscene blend).</summary>
        public void AddModifier(ICameraModifier modifier)
        {
            if (modifier == null || _modifiers.Contains(modifier))
                return;
            _modifiers.Add(modifier);
            _modifiers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public void RemoveModifier(ICameraModifier modifier) => _modifiers.Remove(modifier);
    }
}
