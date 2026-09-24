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
    /// Gravity stays on. The grabbed point carries the weight like a pivot, so the rest of the
    /// object swings down and hangs from where it was grabbed, and the swing is damped about that
    /// point until it settles. The body doesn't collide with the
    /// player while held. Letting go caps its speed, so whipping the view can't fling it;
    /// throwing is the only way to launch it, and its mass decides how fast. The state ends
    /// itself if the object is snagged and left behind, or stays out of sight too long.
    ///
    /// Only reachable in the cursor mode (see <see cref="PhysicsProp"/>), where Interact toggles:
    /// the press that grabbed went to the default state, so the next one lets go. Attack throws.
    /// <see cref="AdjustDepth"/>, <see cref="Rotate"/>, <see cref="Release"/> and
    /// <see cref="Throw"/> are the whole control surface.
    /// </summary>
    public sealed class GrabState : HeldState
    {
        const float BreakDistanceScale = 1.1f;
        const float BreakDistanceSlack = 0.2f;
        // Spin catch-up at or above the physics rate overshoots every step and buzzes.
        const float MaxSpinCatchUpPerStep = 0.9f;
        // Swing damping may take at most this fraction of the spin in one step, so it can't reverse it.
        const float MaxSwingDampingPerStep = 0.5f;
        const float MinInertia = 1e-6f;

        readonly PD3 _pullPd = new(0f, 0f);
        readonly PD3 _spinPd = new(0f, 0f);

        PlayerGrabSettings _settings;
        GrabData _data;
        Vector3 _localGrabPoint;   // body space, unscaled
        Vector3 _viewOffset;       // goal offset from the cursor point, view space
        Quaternion _localRotation; // relative to the view, for orientation holding
        bool _orient;
        float _depth;
        float _pointDepth;
        float _breakDistance;
        float _unseenTime;
        bool _holding;
        bool _throwing;

        float _savedMass;
        RigidbodyInterpolation _savedInterpolation;

        public GrabState(PlayerInteractor interactor) : base(interactor) { }

        public GrabData Data => _data;
        /// <summary>The body being carried, or null once it's let go.</summary>
        public Rigidbody HeldBody => _holding ? Body : null;
        /// <summary>Hold distance along the cursor ray that the object is pulled toward, in metres.</summary>
        public float Depth => _depth;
        /// <summary>How far along the cursor ray the grabbed point actually is, in metres, at the latest physics step.</summary>
        public float PointDepth => _pointDepth;
        public bool CanThrow => _data != null && _data.CanThrow;

        Vector3 GrabPoint => Body.position + Body.rotation * _localGrabPoint;

        /// <summary>
        /// Where the grabbed point is drawn this frame (the interpolated pose, not the physics
        /// one), so the cursor can sit on the object rather than on where it's being pulled.
        /// </summary>
        public bool TryGetDrawnGrabPoint(out Vector3 point)
        {
            if (!_holding || Body == null)
            {
                point = default;
                return false;
            }
            Transform t = Body.transform;
            point = t.position + t.rotation * _localGrabPoint;
            return true;
        }

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
            _unseenTime = 0f;

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
            _pointDepth = grabDepth;

            _savedMass = Body.mass;
            _savedInterpolation = Body.interpolation;

            Body.mass = _savedMass * _data.massMultiplier;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.interpolation = RigidbodyInterpolation.Interpolate; // no jitter between physics steps

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

        /// <summary>Throws toward where the object is on screen, as fast as its mass allows. Returns false if it can't be thrown.</summary>
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
            if (pressed && action == InteractionAction.Interact)
                Release();
            else if (pressed && action == InteractionAction.Attack)
                Throw();
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
            _pointDepth = Vector3.Dot(point - aim.origin, aim.direction);

            // Round a corner or behind something: let go, after a grace period so passing
            // behind something thin doesn't.
            _unseenTime = Interactor.HasLineOfSight(aim.origin, point, Body) ? 0f : _unseenTime + deltaTime;
            if (_unseenTime > _settings.lineOfSightGrace)
            {
                ReturnToPrevious();
                return;
            }

            // The PD gives the grabbed point's wanted acceleration, damped against the player's
            // motion rather than the goal's, so the cursor and view lead and the object follows,
            // while walking doesn't leave it behind. The point's effective mass turns that into
            // the force to apply there.
            Vector3 goal = KeepOutOfPlayer(aim.origin + aim.direction * _depth + view.rotation * _viewOffset, view);
            Vector3 relativeVelocity = Body.GetPointVelocity(point) - Interactor.Movement.Velocity;
            Matrix4x4 pointMass = EffectiveMassAt(Body, point);
            Vector3 pull = pointMass.MultiplyVector(_pullPd.Output(goal - point, -relativeVelocity));
            pull = Vector3.ClampMagnitude(pull, _settings.maxPullForce) * _data.forceMultiplier;

            if (!_orient)
            {
                // Carry the weight at the grabbed point, as a pivot would: the force that keeps
                // the point itself from falling, while gravity swings the rest down below it.
                // Outside the pull cap, so heavy things lag behind rather than drop.
                Vector3 support = Body.useGravity ? pointMass.MultiplyVector(-Physics.gravity) : Vector3.zero;
                Body.AddForceAtPosition(pull + support, point);
                DampSwing(point, deltaTime);
                return;
            }

            Body.AddForceAtPosition(pull, point);
            // Held-angle props carry their weight at the centre instead, so it can't twist them off their angle.
            if (Body.useGravity)
                Body.AddForce(-Physics.gravity, ForceMode.Acceleration);

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
        /// Maps a wanted acceleration of <paramref name="point"/> to the force to apply there.
        /// Off the centre of mass, part of any push turns the body instead of moving it, so the
        /// point is lighter than the whole body: a plank's end is a quarter of its mass, a box's
        /// corner less still. Sizing the pull by the whole mass overdrove such points past what
        /// one physics step can settle, and they buzzed and spun. Leaves out the spin's own
        /// centripetal pull, which the spring absorbs.
        /// </summary>
        static Matrix4x4 EffectiveMassAt(Rigidbody body, Vector3 point)
        {
            Vector3 r = point - body.worldCenterOfMass;
            float inverseMass = 1f / body.mass;

            // Column i: how the point accelerates under a unit force along axis i, from the push
            // itself plus the turn it causes.
            Matrix4x4 response = Matrix4x4.identity;
            for (int i = 0; i < 3; i++)
            {
                Vector3 force = Vector3.zero;
                force[i] = 1f;
                Vector3 angular = InverseInertiaTimes(body, Vector3.Cross(r, force));
                response.SetColumn(i, force * inverseMass + Vector3.Cross(angular, r));
            }
            return response.inverse;
        }

        /// <summary>
        /// Brakes the body's spin as if it turned about the grabbed point. A Rigidbody's angular
        /// damping acts about the centre of mass, but a hanging object swings about the point,
        /// and most of that motion is the centre of mass travelling round it, which angular
        /// damping never touches: a ball held at its surface kept swinging for seconds. This
        /// brakes the angular momentum about the point instead, so everything settles at the
        /// same rate whatever its shape.
        /// </summary>
        void DampSwing(Vector3 point, float deltaTime)
        {
            Vector3 spin = Body.angularVelocity;
            float speed = spin.magnitude;
            if (speed < 1e-4f)
                return;

            Vector3 r = Body.worldCenterOfMass - point;
            Vector3 momentum = InertiaTimes(Body, spin) + Body.mass * Vector3.Cross(r, Vector3.Cross(spin, r));
            Vector3 torque = -_settings.swingDamping * momentum;

            // A free body reacts to this more sharply than one hanging from a point, so cap what
            // one step can take off using the free response.
            float change = InverseInertiaTimes(Body, torque).magnitude * deltaTime;
            float limit = MaxSwingDampingPerStep * speed;
            if (change > limit)
                torque *= limit / change;
            Body.AddTorque(torque);
        }

        static Vector3 InertiaTimes(Rigidbody body, Vector3 vector)
        {
            Quaternion principal = body.rotation * body.inertiaTensorRotation;
            Vector3 local = Quaternion.Inverse(principal) * vector;
            return principal * Vector3.Scale(local, body.inertiaTensor);
        }

        static Vector3 InverseInertiaTimes(Rigidbody body, Vector3 vector)
        {
            Quaternion principal = body.rotation * body.inertiaTensorRotation;
            Vector3 local = Quaternion.Inverse(principal) * vector;
            Vector3 inertia = body.inertiaTensor;
            return principal * new Vector3(
                local.x / Mathf.Max(inertia.x, MinInertia),
                local.y / Mathf.Max(inertia.y, MinInertia),
                local.z / Mathf.Max(inertia.z, MinInertia));
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
                    body.interpolation = _savedInterpolation;

                    if (_throwing)
                    {
                        // Through the object as the cursor shows it, since the cursor sits on it.
                        Vector3 toObject = GrabPoint - Interactor.ViewPose.position;
                        Vector3 aim = toObject.sqrMagnitude > 1e-4f ? toObject.normalized : Interactor.AimDirection;
                        Vector3 direction = Vector3.RotateTowards(aim, Vector3.up, _settings.throwLoft * Mathf.Deg2Rad, 0f);
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
