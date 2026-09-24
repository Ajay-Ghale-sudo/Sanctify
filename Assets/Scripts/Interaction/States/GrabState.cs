using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Carries a physics prop, dragged by the point that was grabbed. A spring pulls that point
    /// toward the cursor ray at the held depth, damped against the player's own motion: walking
    /// carries the object along, while dragging the cursor or turning makes it trail and swing.
    /// The pull is capped in newtons, so heavy objects lag further behind. Props with
    /// <see cref="GrabData.holdOrientation"/> also keep the angle they had when grabbed,
    /// relative to the view (Amnesia's behaviour).
    ///
    /// While held the body has no gravity and doesn't collide with the player. Letting go caps
    /// its speed, so whipping the view can't fling it; throwing is the only way to launch it,
    /// and its mass decides how fast. The state ends itself if the object is snagged and left behind.
    ///
    /// Controls: Interact toggles the grab in cursor mode and is held otherwise; Attack throws.
    /// <see cref="AdjustDepth"/>, <see cref="Rotate"/>, <see cref="Release"/> and
    /// <see cref="Throw"/> are the whole control surface.
    /// </summary>
    public sealed class GrabState : HeldState
    {
        const float BreakDistanceScale = 1.1f;
        const float BreakDistanceSlack = 0.2f;
        // Spin catch-up at or above the physics rate overshoots every step and buzzes.
        const float MaxSpinCatchUpPerStep = 0.9f;

        readonly PD3 _pullPd = new(0f, 0f);
        readonly PD3 _spinPd = new(0f, 0f);

        PlayerGrabSettings _settings;
        GrabData _data;
        Vector3 _localGrabPoint;   // body space, unscaled
        Vector3 _viewOffset;       // goal offset from the cursor point, view space
        Quaternion _localRotation; // relative to the view, for orientation holding
        bool _orient;
        float _depth;
        float _breakDistance;
        float _heldMass;
        bool _holding;
        bool _throwing;

        float _savedMass;
        bool _savedGravity;
        float _savedAngularDamping;
        RigidbodyInterpolation _savedInterpolation;

        public GrabState(PlayerInteractor interactor) : base(interactor) { }

        public GrabData Data => _data;
        /// <summary>Current hold distance along the cursor ray, in metres.</summary>
        public float Depth => _depth;
        public bool CanThrow => _data != null && _data.CanThrow;

        Vector3 GrabPoint => Body.position + Body.rotation * _localGrabPoint;

        public override bool CanEnter(in InteractionContext context)
        {
            Rigidbody body = context.Body;
            if (body == null || body.isKinematic)
                return false;

            // Grabbing what you stand on would drop you through it.
            Collider ground = Interactor.Motor.GroundCollider;
            return ground == null || ground.attachedRigidbody != body;
        }

        public override void Enter()
        {
            base.Enter();
            _settings = Interactor.GrabSettings;
            _data = Prop is PhysicsProp physicsProp ? physicsProp.Grab : GrabData.Default;
            _orient = _data.HoldsOrientation;
            _throwing = false;

            Pose view = Interactor.ViewPose;
            Ray aim = Interactor.GetFixedStepAimRay();

            if (_data.usePoseOffset)
            {
                _localGrabPoint = Body.centerOfMass;
                _viewOffset = _data.positionOffset;
                _localRotation = Quaternion.Euler(_data.rotationOffset);
            }
            else
            {
                _localGrabPoint = Quaternion.Inverse(Body.rotation) * (HitPoint - Body.position);
                _viewOffset = Vector3.zero;
                _localRotation = Quaternion.Inverse(view.rotation) * Body.rotation;
            }

            // The grabbed point starts on the cursor ray, drawn slightly toward the eye, so
            // grabbing never makes the object jump.
            float grabDepth = Mathf.Max(Vector3.Dot(HitPoint - aim.origin, aim.direction), 0f);
            _depth = _data.useFixedDepth ? _data.depth : Mathf.Max(grabDepth - _settings.grabPull, _data.minDepth);

            _savedMass = Body.mass;
            _savedGravity = Body.useGravity;
            _savedAngularDamping = Body.angularDamping;
            _savedInterpolation = Body.interpolation;

            Body.useGravity = false;
            Body.mass = _savedMass * _data.massMultiplier;
            Body.angularDamping = Mathf.Max(_savedAngularDamping, _settings.heldAngularDamping);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.interpolation = RigidbodyInterpolation.Interpolate; // no jitter between physics steps
            _heldMass = Body.mass;

            Interactor.CollisionFilter.Ignore(Body);
            Interactor.Movement.SpeedMultiplier = _settings.SpeedMultiplierFor(_savedMass);

            float startDistance = Vector3.Distance(aim.origin, GrabPoint);
            _breakDistance = Mathf.Max(startDistance, _data.maxDepth, _depth) * BreakDistanceScale + BreakDistanceSlack;

            _pullPd.P = _settings.stiffness;
            _pullPd.D = _settings.damping;
            _spinPd.P = Mathf.Min(_settings.spinCatchUp, MaxSpinCatchUpPerStep / Time.fixedDeltaTime);
            _spinPd.D = 0f;
            _pullPd.Reset();
            _spinPd.Reset();

            _holding = true;
        }

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        /// <summary>Moves the object away (positive) or closer (negative), in metres, within the prop's range.</summary>
        public void AdjustDepth(float metres)
        {
            if (_holding)
                _depth = Mathf.Clamp(_depth + metres, _data.minDepth, _data.maxDepth);
        }

        /// <summary>
        /// Turns the held angle relative to the view, in degrees: x around the view's up axis,
        /// y around its right axis. Only props that hold their orientation have one.
        /// </summary>
        public void Rotate(Vector2 degrees)
        {
            if (!_holding || !_orient)
                return;
            _localRotation = Quaternion.AngleAxis(degrees.x, Vector3.up)
                           * Quaternion.AngleAxis(degrees.y, Vector3.right)
                           * _localRotation;
        }

        /// <summary>Lets go. The object keeps its motion, capped.</summary>
        public void Release()
        {
            if (_holding)
                ReturnToPrevious();
        }

        /// <summary>Throws along the aim, as fast as its mass allows. Returns false if it can't be thrown.</summary>
        public bool Throw()
        {
            if (!_holding || !CanThrow)
                return false;
            _throwing = true;
            ReturnToPrevious();
            return true;
        }

        public override bool OnAction(InteractionAction action, bool pressed)
        {
            if (action == InteractionAction.Interact)
            {
                // Cursor mode toggles: the press that grabbed went to the default state, so the
                // next press lets go. Otherwise the grab lasts while the button is held.
                bool letGo = Interactor.CursorMode ? pressed : !pressed;
                if (letGo)
                    Release();
            }
            else if (action == InteractionAction.Attack && pressed)
            {
                Throw();
            }
            return false; // hands are full: no attacking or casting
        }

        // ------------------------------------------------------------------
        // Physics
        // ------------------------------------------------------------------

        public override void FixedTick(float deltaTime)
        {
            if (!_holding)
                return;
            if (Body == null || Body.isKinematic)
            {
                ReturnToPrevious();
                return;
            }

            Pose view = Interactor.ViewPose;
            Ray aim = Interactor.GetFixedStepAimRay();
            Vector3 point = GrabPoint;
            if (Vector3.Distance(aim.origin, point) > _breakDistance)
            {
                ReturnToPrevious(); // snagged, or left behind
                return;
            }

            // Damped against the player's motion rather than the goal's, so the cursor and view
            // lead and the object follows, while walking doesn't leave it behind.
            Vector3 goal = KeepOutOfPlayer(aim.origin + aim.direction * _depth + view.rotation * _viewOffset, view);
            Vector3 relativeVelocity = Body.GetPointVelocity(point) - Interactor.Movement.Velocity;
            Vector3 force = _pullPd.Output(goal - point, -relativeVelocity) * _heldMass;
            Body.AddForceAtPosition(Vector3.ClampMagnitude(force, _settings.maxPullForce) * _data.forceMultiplier, point);

            if (!_orient)
                return;

            // Angle error becomes a wanted spin (capped), and the spin is driven toward it.
            // Acceleration mode skips the inertia division, which matches Amnesia multiplying
            // torque by inertia. Both gains stay well below the physics rate so the correction
            // lands over several steps instead of overshooting each one.
            Quaternion goalRotation = view.rotation * _localRotation;
            (goalRotation * Quaternion.Inverse(Body.rotation)).ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f)
                degrees -= 360f;

            Vector3 wantedSpin = Mathf.Abs(degrees) > 0.01f
                ? Vector3.ClampMagnitude(axis * (degrees * Mathf.Deg2Rad * _settings.orientationResponse), _settings.maxSpin)
                : Vector3.zero;
            Vector3 spinChange = _spinPd.Output(wantedSpin - Body.angularVelocity, deltaTime);
            Body.AddTorque(Vector3.ClampMagnitude(spinChange, _settings.maxAngularAcceleration) * _data.torqueMultiplier, ForceMode.Acceleration);
        }

        /// <summary>
        /// Pushes the goal sideways out of the player's capsule. Collision with the player is off
        /// while holding, so without this the cursor could drag the object into the camera.
        /// </summary>
        Vector3 KeepOutOfPlayer(Vector3 goal, in Pose view)
        {
            Transform player = Interactor.Motor.transform;
            Vector3 up = player.up;
            Vector3 sideways = Vector3.ProjectOnPlane(goal - player.position, up);
            float minDistance = Interactor.Motor.Radius + _settings.keepOutMargin;
            float distance = sideways.magnitude;
            if (distance >= minDistance)
                return goal;

            Vector3 outward = distance > 1e-4f
                ? sideways / distance
                : Vector3.ProjectOnPlane(view.rotation * Vector3.forward, up).normalized;
            return goal + outward * (minDistance - distance);
        }

        public override void Exit()
        {
            if (_holding)
            {
                _holding = false;
                Interactor.Movement.SpeedMultiplier = 1f;

                Rigidbody body = Body;
                if (body != null)
                {
                    body.mass = _savedMass;
                    body.useGravity = _savedGravity;
                    body.angularDamping = _savedAngularDamping;
                    body.interpolation = _savedInterpolation;

                    if (_throwing)
                    {
                        Vector3 direction = Vector3.RotateTowards(Interactor.AimDirection, Vector3.up, _settings.throwLoft * Mathf.Deg2Rad, 0f);
                        body.linearVelocity = direction * _settings.ThrowSpeedFor(_savedMass, _data.throwMultiplier);
                        body.angularVelocity = Vector3.zero;
                    }
                    else
                    {
                        body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, _settings.maxReleaseSpeed);
                        body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, _settings.maxReleaseAngularSpeed);
                    }
                    body.WakeUp();
                }

                Interactor.CollisionFilter.RestoreWhenClear(body);
            }

            _throwing = false;
            base.Exit();
        }
    }
}
