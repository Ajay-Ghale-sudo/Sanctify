using Sanctify.Characters;
using UnityEngine;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Camera dips and nods when the character lands, scaled by impact speed, using a
    /// damped spring so it settles naturally. Listens to <see cref="CharacterMotor.Landed"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LandingDipModifier : MonoBehaviour, ICameraModifier
    {
        [SerializeField] int order = 20;

        [Header("Impact → strength")]
        [Tooltip("Landing speed (m/s) below which there is no dip.")]
        [SerializeField, Min(0f)] float minImpactSpeed = 2f;
        [Tooltip("Landing speed (m/s) at which the dip is at full strength.")]
        [SerializeField, Min(0f)] float maxImpactSpeed = 10f;
        [Tooltip("Downward camera velocity injected at full strength, m/s.")]
        [SerializeField, Min(0f)] float maxKick = 1.2f;
        [Tooltip("Degrees of downward nod per metre of dip.")]
        [SerializeField] float pitchPerMetre = 60f;

        [Header("Spring")]
        [SerializeField, Min(1f)] float stiffness = 250f;
        [SerializeField, Min(0f)] float damping = 24f;

        CharacterMotor _motor;
        float _offset;
        float _velocity;

        public int Order => order;

        void Awake()
        {
            _motor = GetComponentInParent<CharacterMotor>();
        }

        void OnEnable()
        {
            if (_motor != null)
                _motor.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (_motor != null)
                _motor.Landed -= OnLanded;
        }

        void OnLanded(float impactSpeed)
        {
            float strength = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
            if (strength <= 0f)
                return;
            _velocity -= maxKick * strength;
        }

        public void Modify(ref CameraPose pose, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            // Semi-implicit Euler on a damped spring toward 0.
            float accel = -stiffness * _offset - damping * _velocity;
            _velocity += accel * deltaTime;
            _offset += _velocity * deltaTime;

            if (Mathf.Abs(_offset) < 1e-5f && Mathf.Abs(_velocity) < 1e-4f)
            {
                _offset = 0f;
                _velocity = 0f;
                return;
            }

            pose.PositionOffset.y += _offset;
            pose.EulerOffset.x += -_offset * pitchPerMetre; // dip (negative offset) → look down (positive x)
        }
    }
}
