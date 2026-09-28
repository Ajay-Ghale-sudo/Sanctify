using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Swings a hinged body, such as a door or a <see cref="Lever"/>, by the point that was
    /// grabbed (Amnesia's rotate states). Moving the right stick out from the centre moves the
    /// hand, in view space: up
    /// pushes the point away, down pulls it back, sideways pushes it across the view. Only the
    /// part of that push along the point's swing counts, so it works from either side of a door
    /// and at any angle. How fast the stick moves decides how hard the shove: each bit of stick
    /// movement keeps pushing for a moment (Door Flick Time) and fades, so a flick adds up to a
    /// hard shove and a slow push to a gentle one. Holding the stick still, or letting it spring
    /// back, pushes nothing.
    ///
    /// The hand only pushes: it never holds the door back, so a shoved door swings on by itself
    /// until its hinge friction, a limit or something in the way stops it. Pushing the other way
    /// catches it. With the stick at rest, a <see cref="Door"/> near closed shuts itself. Walking
    /// plays no part, so the player can stand off the door, or walk, while holding it.
    ///
    /// The push goes in at the grabbed point and is capped at Door Strength, so a heavy prop
    /// wedged against a door holds it, and heavy doors get going slowly. The state ends itself if
    /// the player walks well away from the hinge, loses sight of the point, or the hinge breaks.
    /// Interact toggles, in either mode (see <see cref="Door"/>).
    /// </summary>
    public sealed class HingeState : HeldState
    {
        // Amnesia's keep-hold distance: generous, so nobody has to stay pressed against the door.
        const float BreakDistanceScale = 1.2f;
        const float BreakDistanceSlack = 0.5f;
        // Share of the gap to the hand's speed closed in one physics step: near the most that
        // can't overshoot, so a flick lands at once without buzzing.
        internal const float CatchUpPerStep = 0.5f;
        // Nearer the hinge line than this, a push has too little leverage to mean anything.
        const float MinLever = 0.05f;

        PlayerGrabSettings _settings;
        HingeJoint _hinge;
        Vector3 _localGrabPoint; // body space, unscaled
        float _pointMass;        // how heavy the point feels, pushed along its swing
        float _breakDistance;
        float _unseenTime;
        Vector2 _stick;
        Vector2 _lastStick;      // at the last physics step
        bool _stickSeen;         // since taking hold
        Vector2 _flick;          // stick movement still pushing, fading; stick space, up to 1
        bool _holding;
        RigidbodyInterpolation _savedInterpolation;

        public HingeState(PlayerInteractor interactor) : base(interactor) { }

        Vector3 GrabPoint => Body.position + Body.rotation * _localGrabPoint;
        Vector3 Pivot => _hinge.transform.TransformPoint(_hinge.anchor);
        Vector3 Axis => _hinge.transform.TransformDirection(_hinge.axis).normalized;

        /// <summary>The right stick is at rest: the hand is on the handle, but not working it.</summary>
        public bool StickAtRest => _stick == Vector2.zero; // the input is deadzoned, so rest is exact

        /// <summary>A body's moment of inertia about a hinge line, in kg·m².</summary>
        internal static float InertiaAbout(Rigidbody body, Vector3 pivot, Vector3 axis)
        {
            float centreOff = Vector3.ProjectOnPlane(body.worldCenterOfMass - pivot, axis).magnitude;
            return Vector3.Dot(axis, GrabState.InertiaTimes(body, axis)) + body.mass * centreOff * centreOff;
        }

        /// <summary>
        /// Degrees a body has turned about a hinge axis from a rest pose, signed by the axis.
        /// Worked out from the body's rotation, in the same terms as its angular velocity and
        /// torque, rather than from the hinge's own angle.
        /// </summary>
        internal static float SwingAngle(Rigidbody body, Quaternion rest, Vector3 axis)
        {
            (body.rotation * Quaternion.Inverse(rest)).ToAngleAxis(out float degrees, out Vector3 turn);
            if (degrees > 180f)
                degrees -= 360f;
            return Vector3.Dot(turn, axis) < 0f ? -degrees : degrees;
        }

        public override bool CanEnter(in InteractionContext context)
            => IsHoldable(context.Body) && context.Body.TryGetComponent(out HingeJoint _);

        public override void Enter()
        {
            base.Enter();
            _settings = Interactor.GrabSettings;
            _hinge = Body.GetComponent<HingeJoint>();
            _localGrabPoint = Quaternion.Inverse(Body.rotation) * (HitPoint - Body.position);

            // Pushed along its swing, the point moves as if it weighed the body's inertia about
            // the hinge over its distance from the hinge squared: light at the handle, heavy near
            // the hinge. The hinge doesn't move, so neither does this.
            Vector3 axis = Axis;
            Vector3 pivot = Pivot;
            float lever = Mathf.Max(Vector3.ProjectOnPlane(HitPoint - pivot, axis).magnitude, MinLever);
            _pointMass = InertiaAbout(Body, pivot, axis) / (lever * lever);

            _breakDistance = FromHinge(pivot) * BreakDistanceScale + BreakDistanceSlack;
            _unseenTime = 0f;
            _stick = _lastStick = _flick = Vector2.zero;
            _stickSeen = false;

            _savedInterpolation = Body.interpolation;
            Body.interpolation = RigidbodyInterpolation.Interpolate; // no judder against the moving camera
            Body.WakeUp();

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

        /// <summary>Lets go. The door swings on until its hinge friction stops it.</summary>
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

        // The right stick works the door instead of peeking or moving the cursor.
        public override bool OnPeek(Vector2 peek)
        {
            _stick = Vector2.ClampMagnitude(peek, 1f);
            if (!_stickSeen)
            {
                _lastStick = _stick; // already pushed when taking hold: not a flick
                _stickSeen = true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Physics
        // ------------------------------------------------------------------

        public override void FixedTick(float deltaTime)
        {
            if (!_holding)
                return;
            if (Body == null || Body.isKinematic || _hinge == null)
            {
                ReturnToPrevious(); // knocked off its hinges
                return;
            }

            Vector3 axis = Axis;
            Vector3 pivot = Pivot;
            if (FromHinge(pivot) > _breakDistance)
            {
                ReturnToPrevious(); // walked away
                return;
            }

            Vector3 point = GrabPoint;
            _unseenTime = Interactor.HasLineOfSight(Interactor.ViewPose.position, point, Body) ? 0f : _unseenTime + deltaTime;
            if (_unseenTime > _settings.lineOfSightGrace)
            {
                ReturnToPrevious();
                return;
            }

            // Only moving the stick further out counts, so it springing back to the centre doesn't
            // pull the door back. Each bit carries on pushing for a moment, fading: a flick piles up
            // into a hard shove before it fades, while a slow push fades as it goes and stays gentle.
            float outward = _stick.magnitude - _lastStick.magnitude;
            if (outward > 0f)
                _flick = Vector2.ClampMagnitude(_flick + _stick.normalized * outward, 1f);
            _lastStick = _stick;
            _flick *= Mathf.Exp(-deltaTime / _settings.doorFlickTime);

            Vector3 lever = Vector3.ProjectOnPlane(point - pivot, axis);
            if (lever.sqrMagnitude < MinLever * MinLever)
                return;
            Vector3 swing = Vector3.Cross(axis, lever).normalized;

            // Up on the stick pushes away and up, as in Amnesia; the hinge takes whatever part of
            // the push doesn't follow the swing.
            Vector3 push = Interactor.ViewPose.rotation * new Vector3(_flick.x, _flick.y, _flick.y);
            float wanted = Mathf.Clamp(Vector3.Dot(push, swing), -1f, 1f) * _settings.doorFlickSpeed;
            float gap = wanted - Vector3.Dot(Body.GetPointVelocity(point), swing);
            // The hand only pushes: a door already going faster its way is left to swing.
            if (gap * wanted <= 0f)
                return;
            // ponytail: the gain is sized to the free door, so against an obstacle a door under
            // about 23 kg pushes with less than Door Strength; add a lean-in ramp, as Drag has, if one must shove props
            float force = _pointMass * CatchUpPerStep / deltaTime * gap;
            Body.AddForceAtPosition(swing * Mathf.Clamp(force, -_settings.doorStrength, _settings.doorStrength), point);
        }

        /// <summary>
        /// How far the player stands from the hinge, measured flat: from a door's hinge line, or
        /// from a lever's pivot, since walking along a level hinge line is still walking away.
        /// </summary>
        float FromHinge(Vector3 pivot)
        {
            Transform player = Interactor.Motor.transform;
            return Vector3.ProjectOnPlane(player.position - pivot, player.up).magnitude;
        }

        public override void Exit()
        {
            if (_holding)
            {
                _holding = false;
                _stick = Vector2.zero;
                if (Body != null)
                {
                    Body.interpolation = _savedInterpolation;
                    Body.WakeUp();
                }
            }
            _hinge = null;
            base.Exit();
        }
    }
}
