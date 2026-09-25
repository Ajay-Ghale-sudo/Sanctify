using System.Collections.Generic;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Pushes and pulls an object too heavy to lift (Amnesia's push state), by the point that was
    /// grabbed and against the floor's friction. It only starts with that point within arm's reach
    /// and roughly in front of the player (<see cref="CanTakeHold"/>); farther off, heavy objects
    /// aren't even focused.
    ///
    /// Walking forward pushes along the line from the player to that point and walking back
    /// pulls along it. There's no strafing. Turning away from the point slows the
    /// further it's turned, and turning too far lets go, so the player can keep facing an object
    /// that slews round but can't drag it from the side. The effort builds over a moment, and
    /// the player moves no faster than the grabbed point does, so they lean in until it gets
    /// going, stop when it jams, and can't walk away from it when it snags.
    ///
    /// Every force acts at the grabbed point, so where it's grabbed matters. Pulled by the top,
    /// a crate tips over toward the player; by the middle of a face, it slides; by the base, it
    /// slides with its near edge raised. That's because the hands also lift the grabbed point a
    /// little, as if pulling it up off the floor: fully when it's grabbed at the base, not at all
    /// at the top (lifting there would take weight off the floor, and the crate would slide
    /// instead of tipping), and never enough to carry the whole weight. The lift eases in as they
    /// take hold. If it tips below where it was grabbed, they let it go down rather than prop it up.
    ///
    /// The body stays solid to the player. The state ends itself if the object is left behind,
    /// or if it stays out of sight too long. Only reachable in the cursor mode (see
    /// <see cref="PhysicsProp"/>), where Interact toggles, as for grabbing.
    /// </summary>
    public sealed class DragState : HeldState
    {
        const float BreakDistanceScale = 1.2f;
        const float BreakDistanceSlack = 0.3f;
        // A drag can only start this far into the let-go angle, so it doesn't end as soon as it starts.
        const float TakeHoldAngleShare = 0.75f;
        // How near a pulled object may come to the player before they lead instead of following.
        const float PlayerClearance = 0.1f;
        // Easing off or turning round is quicker than leaning in, in seconds.
        const float EaseOffTime = 0.1f;
        // The push fades out over this much speed below the pace, so it settles there instead of surging past.
        const float PaceTaper = 0.15f;
        // Holds the grabbed point to the line to the player, per kg: its sideways slide is damped
        // at this rate. Kept low, since a point off the centre of mass responds up to five times as fast.
        const float DriftDamping = 8f;
        // The lift fades out as the grabbed point drops this far below where it rested, in metres.
        const float DropMargin = 0.1f;
        // Stiff enough that a crate's near edge, which takes about half its weight to raise, still
        // gets most of the lift height; limited per kg so light things don't outpace the physics step.
        const float LiftStiffness = 10000f; // N/m
        const float MaxLiftStiffnessPerKg = 300f;
        const float LiftDampingRatio = 0.3f;
        const float MaxLiftShareOfWeight = 0.6f;

        readonly List<Collider> _colliders = new();

        PlayerGrabSettings _settings;
        Vector3 _localGrabPoint; // body space, unscaled
        Vector3 _playerStart;    // where the player stood when grabbing
        float _restHeight;       // the grabbed point's height when grabbed
        float _pace;
        float _effort;           // -1 pulling .. +1 pushing, ramped from the walk input
        float _grip;             // 0..1, eases the lift in after taking hold
        float _liftStiffness;
        float _liftDamping;
        float _liftCap;
        Vector2 _move;
        float _breakDistance;
        float _unseenTime;
        bool _holding;
        RigidbodyInterpolation _savedInterpolation;

        public DragState(PlayerInteractor interactor) : base(interactor) { }

        Vector3 GrabPoint => Body.position + Body.rotation * _localGrabPoint;

        public override bool CanEnter(in InteractionContext context)
            => IsHoldable(context.Body) && CanTakeHold(Interactor, context.HitPoint);

        /// <summary>
        /// Whether a point can be taken hold of to drag: within Drag Reach of the side of the
        /// player's body, measured flat, and roughly in front of them. <see cref="PhysicsProp"/>
        /// only lets heavy objects be focused where this holds.
        /// </summary>
        public static bool CanTakeHold(PlayerInteractor interactor, Vector3 point)
        {
            PlayerGrabSettings settings = interactor.GrabSettings;
            return interactor.ReachTo(point) <= settings.dragReach
                && Mathf.Abs(TurnedFrom(point, interactor.Motor.transform)) <= settings.dragLetGoAngle * TakeHoldAngleShare;
        }

        public override void Enter()
        {
            base.Enter();
            _settings = Interactor.GrabSettings;
            _localGrabPoint = Quaternion.Inverse(Body.rotation) * (HitPoint - Body.position);

            Transform player = Interactor.Motor.transform;
            _playerStart = player.position;
            _restHeight = Vector3.Dot(GrabPoint, player.up);
            _breakDistance = Vector3.Distance(player.position, GrabPoint) * BreakDistanceScale + BreakDistanceSlack;

            float mass = Body.mass;
            _pace = _settings.DragSpeedFor(mass);
            _liftStiffness = Mathf.Min(LiftStiffness, MaxLiftStiffnessPerKg * mass);
            _liftDamping = 2f * LiftDampingRatio * Mathf.Sqrt(_liftStiffness * mass);
            _liftCap = Mathf.Min(_settings.maxDragLift, MaxLiftShareOfWeight * mass * Physics.gravity.magnitude)
                     * LiftShare(_restHeight, player.up);

            _grip = 0f;
            _effort = 0f;
            _move = Vector2.zero;
            _unseenTime = 0f;

            _savedInterpolation = Body.interpolation;
            Body.interpolation = RigidbodyInterpolation.Interpolate; // no judder against the moving camera
            Body.WakeUp();
            Interactor.Movement.SetAxisSpeedLimits(0f, 0f); // until it gets going

            _holding = true;
        }

        public override bool TryGetDrawnGrabPoint(out Vector3 point)
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

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        /// <summary>Lets go. The object slides to a stop.</summary>
        public void Release()
        {
            if (_holding)
                ReturnToPrevious();
        }

        public override bool OnAction(InteractionAction action, bool pressed)
        {
            if (pressed && action == InteractionAction.Interact)
                Release();
            return false; // hands are full: no attacking or casting
        }

        // Walking forward and back drives the effort. The player still walks, held to the
        // object's pace, and can't strafe.
        public override bool OnMove(Vector2 move)
        {
            _move = move;
            return true;
        }

        // Turning away from the grabbed point is slowed, the more the further it's turned.
        public override void Tick(float deltaTime)
        {
            PlayerLook look = Interactor.Look;
            if (look == null || !TryGetDrawnGrabPoint(out Vector3 point))
                return;
            float turned = TurnedFrom(point, Interactor.Motor.transform);
            float slowing = Mathf.Clamp01(Mathf.Abs(turned) / _settings.dragLetGoAngle);
            look.SetTurnAnchor(look.Yaw - turned, Mathf.Lerp(1f, _settings.dragSlowestTurn, slowing));
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

            Transform player = Interactor.Motor.transform;
            Vector3 point = GrabPoint;
            if (Vector3.Distance(player.position, point) > _breakDistance)
            {
                ReturnToPrevious(); // snagged and left behind, or fell away
                return;
            }

            _unseenTime = Interactor.HasLineOfSight(Interactor.ViewPose.position, point, Body) ? 0f : _unseenTime + deltaTime;
            if (_unseenTime > _settings.lineOfSightGrace)
            {
                ReturnToPrevious();
                return;
            }

            if (Mathf.Abs(TurnedFrom(point, player)) > _settings.dragLetGoAngle)
            {
                ReturnToPrevious(); // turned away from it
                return;
            }

            _grip = Mathf.MoveTowards(_grip, 1f, deltaTime / _settings.dragLeanTime);

            // Leaning in takes a moment; easing off or turning round is quick.
            float wanted = Mathf.Clamp(_move.y, -1f, 1f);
            bool leaningIn = wanted * _effort >= 0f && Mathf.Abs(wanted) > Mathf.Abs(_effort);
            _effort = Mathf.MoveTowards(_effort, wanted, deltaTime / (leaningIn ? _settings.dragLeanTime : EaseOffTime));

            Vector3 up = player.up;
            Vector3 toPoint = Vector3.ProjectOnPlane(point - player.position, up);
            Vector3 line = toPoint.sqrMagnitude > 1e-4f ? toPoint.normalized : Vector3.ProjectOnPlane(player.forward, up).normalized;
            Vector3 pointVelocity = Body.GetPointVelocity(point);
            Vector3 flatVelocity = Vector3.ProjectOnPlane(pointVelocity, up);

            // Push away along the line, or pull back along it, easing off near the pace.
            Vector3 direction = _effort >= 0f ? line : -line;
            float along = Vector3.Dot(flatVelocity, direction);
            float strength = Mathf.Abs(_effort) * _settings.dragStrength * Mathf.Clamp01((_pace - along) / PaceTaper);
            Vector3 force = direction * strength;

            // The hands keep the point on the line to the player; the rest of the object is free
            // to swing and tip about it.
            Vector3 sideways = flatVelocity - line * Vector3.Dot(flatVelocity, line);
            force += Vector3.ClampMagnitude(-sideways * (DriftDamping * Body.mass), _settings.dragStrength);

            force += up * Lift(point, pointVelocity, player.position, up);
            Body.AddForceAtPosition(force, point);

            // The player moves no faster than the grabbed point does along its way, and not at all
            // against the effort. Pulled right up against them, they lead instead, or each would
            // wait for the other. Takes effect next step.
            float pace = 0f;
            if (_move.y * _effort > 0f)
            {
                bool againstPlayer = _effort < 0f && Interactor.Motor.WouldTouch(Body, line, PlayerClearance);
                pace = againstPlayer ? _pace : Mathf.Clamp(along, 0f, _pace);
            }
            Interactor.Movement.SetAxisSpeedLimits(0f, pace);
        }

        /// <summary>
        /// How far the player faces away from the grabbed point, in degrees, measured flat:
        /// positive when the point is to their left, so turning right turns further away.
        /// </summary>
        static float TurnedFrom(Vector3 point, Transform player)
        {
            Vector3 toPoint = Vector3.ProjectOnPlane(point - player.position, player.up);
            return toPoint.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(toPoint, player.forward, player.up) : 0f;
        }

        /// <summary>
        /// How much of the lift the hands can give, from how far up the object it was grabbed:
        /// all of it at the base, none at the top.
        /// </summary>
        float LiftShare(float grabHeight, Vector3 up)
        {
            Body.GetComponentsInChildren(_colliders);
            var upMagnitudes = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            float bottom = float.MaxValue;
            float top = float.MinValue;
            foreach (Collider collider in _colliders)
            {
                if (collider.isTrigger || collider.attachedRigidbody != Body)
                    continue; // triggers, and parts of child bodies
                // Lowest and highest corners of the collider's bounding box, measured along up.
                Bounds bounds = collider.bounds;
                float centre = Vector3.Dot(bounds.center, up);
                float reach = Vector3.Dot(bounds.extents, upMagnitudes);
                bottom = Mathf.Min(bottom, centre - reach);
                top = Mathf.Max(top, centre + reach);
            }
            _colliders.Clear();
            return bottom < top ? 1f - Mathf.InverseLerp(bottom, top, grabHeight) : 1f;
        }

        /// <summary>
        /// Upward force at the grabbed point: a spring toward a little above where it rested,
        /// carried up and down with the player, and capped well under the object's weight. The cap
        /// eases in with the grip. Fades out if the point tips below its rest, so a crate pulled
        /// over by its top can fall.
        /// </summary>
        float Lift(Vector3 point, Vector3 pointVelocity, Vector3 playerPosition, Vector3 up)
        {
            float rest = _restHeight + Vector3.Dot(playerPosition - _playerStart, up);
            float height = Vector3.Dot(point, up);
            float engaged = Mathf.Clamp01(1f - (rest - height) / DropMargin);
            float spring = _liftStiffness * (rest + _settings.dragLiftHeight - height) - _liftDamping * Vector3.Dot(pointVelocity, up);
            return Mathf.Clamp(spring, 0f, _liftCap * _grip) * engaged;
        }

        public override void Exit()
        {
            if (_holding)
            {
                _holding = false;
                Interactor.Movement.ClearAxisSpeedLimits();
                if (Interactor.Look != null)
                    Interactor.Look.ClearTurnAnchor();

                if (Body != null)
                {
                    Body.interpolation = _savedInterpolation;
                    Body.WakeUp();
                }
            }
            base.Exit();
        }
    }
}
