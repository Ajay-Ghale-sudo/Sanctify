using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Locomotion bob plus idle breathing, as a camera modifier.
    ///
    /// Locomotion is driven by the stride phase (distance, not time), interpolated between
    /// fixed steps. With footfalls at π/2 and 3π/2:
    ///   - vertical dip  = -sin²(φ)  → 0 between steps, lowest at each footfall
    ///   - lateral sway  =  sin(φ)   → alternates side per step
    ///   - roll follows the sway, a small nod follows the dip
    /// Phase 0 is a true neutral pose, so the stride's post-stop settle is invisible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeadBobModifier : MonoBehaviour, ICameraModifier
    {
        [SerializeField] HeadBobSettings settings;
        [SerializeField] int order = 10;

        PlayerMovement _movement;
        PlayerControlLock _controls;
        float _moveWeight;
        float _enabledWeight = 1f;
        float _breathTime;

        public int Order => order;
        public float MoveWeight => _moveWeight;

        void Awake()
        {
            _movement = GetComponentInParent<PlayerMovement>();
            _controls = GetComponentInParent<PlayerControlLock>();
            if (settings == null)
                Debug.LogError($"{nameof(HeadBobModifier)} on {name} has no settings asset assigned.", this);
            if (_movement == null)
                Debug.LogError($"{nameof(HeadBobModifier)} on {name} needs a {nameof(PlayerMovement)} on a parent.", this);
        }

        public void Modify(ref CameraPose pose, float deltaTime)
        {
            if (settings == null || _movement == null || deltaTime <= 0f)
                return;

            bool allowed = _controls == null || _controls.IsAllowed(PlayerControls.HeadBob);
            float blend = 1f - Mathf.Exp(-settings.blendSharpness * deltaTime);

            float moveTarget = _movement.IsGrounded ? Mathf.Clamp01(_movement.HorizontalSpeed / settings.speedForFullBob) : 0f;
            _moveWeight = Mathf.Lerp(_moveWeight, moveTarget, blend);
            _enabledWeight = Mathf.Lerp(_enabledWeight, allowed ? 1f : 0f, blend);

            // ---- Locomotion ----
            float phase = _movement.Stride.GetInterpolatedPhase(PlayerCameraRig.FixedAlpha);
            float run = _movement.RunBlend;

            float vertical = Mathf.Lerp(settings.walkVertical, settings.runVertical, run);
            float lateral = Mathf.Lerp(settings.walkLateral, settings.runLateral, run);
            float roll = Mathf.Lerp(settings.walkRollDegrees, settings.runRollDegrees, run);
            float pitch = Mathf.Lerp(settings.walkPitchDegrees, settings.runPitchDegrees, run);

            float sway = Mathf.Sin(phase);
            float dip = -sway * sway;

            Vector3 movePosition = new(lateral * sway, vertical * dip, 0f);
            float movePitch = -pitch * dip; // dip down → nod down (positive x rotation looks down)
            float moveRoll = roll * sway;

            // ---- Breathing ----
            _breathTime += deltaTime;
            float omega = settings.breathsPerMinute / 60f * Mathf.PI * 2f;
            float breath = Mathf.Sin(_breathTime * omega);
            float breathLag = Mathf.Sin(_breathTime * omega - 0.6f);

            Vector3 breathPosition = new(settings.breathLateral * Mathf.Sin(_breathTime * omega * 0.5f),
                                         settings.breathVertical * breath,
                                         0f);
            float breathPitch = -settings.breathPitchDegrees * breathLag;

            // ---- Combine ----
            float idle = 1f - _moveWeight;
            pose.PositionOffset += (movePosition * _moveWeight + breathPosition * idle) * _enabledWeight;
            pose.EulerOffset.x += (movePitch * _moveWeight + breathPitch * idle) * _enabledWeight;
            pose.EulerOffset.z += moveRoll * _moveWeight * _enabledWeight;
        }
    }
}
