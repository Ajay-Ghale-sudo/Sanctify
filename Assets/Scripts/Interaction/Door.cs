using System.Collections.Generic;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A hinged door, swung by its handle with the right stick through the <see cref="HingeState"/>,
    /// in either mode. Props stop it by just being in the way: the door and the props are all
    /// ordinary dynamic bodies. Let be within Close Zone of closed (nobody holding it, or the
    /// holder's stick at rest), it shuts itself like a door closer: slowing as it nears the latch,
    /// then a last push over it. One left open further does the same after Close Delay. Neither
    /// happens while something is against it. Once shut, it latches: its hinge limits narrow to
    /// a little play, so it stays shut when hit until someone works the handle. Shutting in the
    /// hand ends the hold. A latched door whose bolt is slid home is locked, and trying it only
    /// yanks it back and forth in that play.
    /// A prop marked <see cref="PhysicsProp.BreaksDoors"/> thrown at it knocks it off its hinges.
    ///
    /// The bolt is a <see cref="PhysicsProp"/> on a <see cref="ConfigurableJoint"/> to this door,
    /// grabbed and slid like any loose object. It's home past the middle of its travel toward the
    /// joint's +x axis; the joint's connected anchor marks the middle. Sitting on one face of the
    /// door, it can only be reached from that side.
    ///
    /// <see cref="State"/> is what monsters will read. A broken door reads Broken only until it
    /// swaps itself for a <see cref="PhysicsProp"/>; after that the Door is destroyed, so a stored
    /// reference is null. Hinge angles are measured from the pose at load, so author doors closed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class Door : Interactable
    {
        public enum Status { Open, Closed, Blocked, Locked, Broken }

        [Tooltip("The only part that can be taken hold of. One collider through the door serves both sides.")]
        [SerializeField] Collider handle;
        [Tooltip("A door let be within this of closed, in degrees, shuts itself straight away: nobody holding it, or the holder's stick at rest.")]
        [SerializeField, Min(0f)] float closeZone = 30f;
        [Tooltip("Within this of closed, in degrees, a door shutting itself gets its last push over the latch.")]
        [SerializeField, Min(0f)] float latchAngle = 10f;
        [Tooltip("Play left when latched, in degrees either side of closed. A door latches once it's shut to within this.")]
        [SerializeField, Min(0f)] float latchPlay = 2f;
        [Tooltip("Seconds a door left open further than Close Zone waits before swinging itself shut. Anything with a body touching it, like the player or a prop, restarts the wait, and stops it closing.")]
        [SerializeField, Min(0f)] float closeDelay = 10f;
        [Tooltip("How fast a door swings itself shut, in degrees per second. Within Close Zone it slows in step with how near closed it is.")]
        [SerializeField, Min(0f)] float closeSpeed = 45f;
        [Tooltip("How fast the last push over the latch swings it, in degrees per second. More than Close Speed has slowed to by then, so it clicks shut.")]
        [SerializeField, Min(0f)] float latchSpeed = 40f;
        [Tooltip("Most torque a door uses to shut itself, in N·m. Weak, so a push or anything in the way wins.")]
        [SerializeField, Min(0f)] float closeTorque = 15f;
        [Tooltip("Optional. A PhysicsProp jointed to this door that locks it while slid home, past the middle of its travel toward the joint's +x. Its X Drive damper is the friction that keeps it where it's left.")]
        [SerializeField] ConfigurableJoint bolt;
        [Tooltip("How hard a locked door is yanked at the handle when tried, in newtons: toward the player, then away, and so on. It rattles within the latch play.")]
        [SerializeField, Min(0f)] float rattleForce = 150f;
        [Tooltip("Seconds of yanking when a locked door is tried.")]
        [SerializeField, Min(0f)] float rattleTime = 0.6f;
        [Tooltip("Seconds per yank, each way.")]
        [SerializeField, Min(0.02f)] float yankTime = 0.1f;
        [Tooltip("Props at least this heavy touching the door count as blocking it, in kg. The door just shoves lighter ones.")]
        [SerializeField, Min(0f)] float blockingMass = 20f;
        [Tooltip("A prop marked Breaks Doors that hits the door at least this fast knocks it off its hinges, in m/s. Throws manage it; held props rarely move that fast.")]
        [SerializeField, Min(0f)] float breakSpeed = 2.5f;

        // Every body touching it, one entry per collider pair, so one touching with several
        // colliders stays until the last lets go. Walls and floors aren't kept.
        readonly List<Rigidbody> _touching = new();

        Rigidbody _body;
        HingeJoint _hinge;
        JointLimits _openLimits;
        Quaternion _rest; // closed
        float _openFor;
        bool _closing;
        bool _latched;
        bool _broken;
        Interactable _boltProp;
        float _boltFriction;
        bool _boltHeld;
        float _rattleLeft;
        Vector3 _rattlePoint; // door space
        Vector3 _rattlePull;  // world, flat, toward the player

        public bool IsBroken => _broken;
        public bool IsLocked => _latched && BoltHome;

        /// <summary>A prop of at least Blocking Mass is against the door, so it won't swing that way without shoving it.</summary>
        public bool IsBlocked
        {
            get
            {
                if (!IsTouched)
                    return false;
                foreach (Rigidbody body in _touching)
                {
                    if (body.mass >= blockingMass && body.GetComponentInParent<PhysicsProp>() != null)
                        return true;
                }
                return false;
            }
        }

        bool IsTouched
        {
            get
            {
                _touching.RemoveAll(body => body == null); // destroyed without a contact exit
                return _touching.Count > 0;
            }
        }

        public Status State => _broken ? Status.Broken
            : IsLocked ? Status.Locked
            : IsBlocked ? Status.Blocked
            : _latched ? Status.Closed
            : Status.Open;

        /// <summary>Degrees from closed, signed by the hinge axis.</summary>
        float Angle => HingeState.SwingAngle(_body, _rest, Axis);
        Vector3 Axis => _hinge.transform.TransformDirection(_hinge.axis).normalized;

        /// <summary>Held, and the holder is working the stick. Held with the stick at rest, a door is let be.</summary>
        bool IsPushed => IsInteractedWith && !(User.Current is HingeState { StickAtRest: true });

        bool BoltHome
        {
            get
            {
                if (bolt == null)
                    return false;
                Transform t = bolt.transform;
                Vector3 offset = t.TransformPoint(bolt.anchor) - transform.TransformPoint(bolt.connectedAnchor);
                return Vector3.Dot(offset, t.TransformDirection(bolt.axis)) > 0f;
            }
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _hinge = GetComponent<HingeJoint>();
            _rest = _body.rotation;
            _broken = _hinge == null;
            if (!_broken)
            {
                _openLimits = _hinge.useLimits ? _hinge.limits : new JointLimits { min = -180f, max = 180f };
                _hinge.useLimits = true;
            }
            if (bolt != null)
            {
                _boltProp = bolt.GetComponent<Interactable>();
                _boltFriction = bolt.xDrive.positionDamper;
            }
            SetLatched(!_broken); // authored closed
        }

        void FixedUpdate()
        {
            if (_broken)
            {
                // Once the hinge is really gone (Destroy is deferred), tip the slab so it doesn't
                // stand on its edge, and hand it over to an ordinary loose prop.
                if (_hinge == null)
                {
                    _body.AddTorque(transform.right * 0.5f, ForceMode.VelocityChange);
                    gameObject.AddComponent<PhysicsProp>();
                    enabled = false; // no second hand-over before Destroy lands
                    Destroy(this);
                }
                return;
            }

            // Latches only once shut to within the play, so narrowing the limits never yanks it
            // there. In the hand, only if it shut itself: taking hold of a shut door leaves it be.
            float angle = Angle;
            float off = Mathf.Abs(angle);
            if (!_latched && off <= latchPlay && (!IsInteractedWith || _closing))
            {
                SetLatched(true);
                if (IsInteractedWith)
                    User.Cancel(); // it clicks shut, and the hand comes off the handle
            }

            // Let be near closed, it shuts itself; left open, after a while. Once going, it keeps
            // on until it latches, unless it's pushed or something's against it.
            bool touched = IsTouched;
            _openFor = _latched || IsInteractedWith || touched ? 0f : _openFor + Time.fixedDeltaTime;
            _closing = !_latched && !IsPushed && !touched
                && (_closing || _openFor >= closeDelay || off < closeZone && off > latchPlay);
            if (_closing)
                DriveShut(angle);

            if (_rattleLeft > 0f)
            {
                // Yanked like a stuck handle: toward the player, then away, and so on.
                bool pulling = Mathf.FloorToInt((rattleTime - _rattleLeft) / yankTime) % 2 == 0;
                _body.AddForceAtPosition(_rattlePull * (pulling ? rattleForce : -rattleForce), transform.TransformPoint(_rattlePoint));
                _rattleLeft -= Time.fixedDeltaTime;
            }

            // Free in the hand; let go, friction holds it, so a swinging door can't fling it home.
            bool held = _boltProp != null && _boltProp.IsInteractedWith;
            if (bolt != null && held != _boltHeld)
            {
                _boltHeld = held;
                JointDrive drive = bolt.xDrive;
                drive.positionDamper = held ? 0f : _boltFriction;
                bolt.xDrive = drive;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            Rigidbody other = collision.rigidbody;
            if (other == null)
                return; // walls and floors aren't in the way
            PhysicsProp prop = other.GetComponentInParent<PhysicsProp>();
            // ponytail: a flag standing in for damage; goes when a Damageable calls Break
            if (prop != null && prop.BreaksDoors && collision.relativeVelocity.magnitude >= breakSpeed)
                Break();
            else
                _touching.Add(other);
        }

        void OnCollisionExit(Collision collision) => _touching.Remove(collision.rigidbody);

        // Only by the handle, and in either mode: a door isn't a loose object.
        protected override bool IsUsableBy(PlayerInteractor interactor, in RaycastHit hit)
            => !_broken && hit.collider == handle && DragState.CanTakeHold(interactor, hit.point);

        protected override void HandleInteract(PlayerInteractor interactor, Rigidbody body, Vector3 hitPoint)
        {
            if (IsLocked)
            {
                _rattleLeft = rattleTime;
                _rattlePoint = transform.InverseTransformPoint(hitPoint);
                _rattlePull = Vector3.ProjectOnPlane(interactor.ViewPose.position - hitPoint, Vector3.up).normalized;
                return;
            }
            SetLatched(false);
            Begin(interactor, InteractionStateId.Hinge, _body, hitPoint);
        }

        /// <summary>
        /// Knocks the door off its hinges. Once the hinge is gone, the door swaps itself for a
        /// plain <see cref="PhysicsProp"/>: the slab falls, and can be grabbed or dragged anywhere.
        /// </summary>
        // ponytail: no damage, pieces or debris yet; a Damageable calls this when there's something to deal damage
        [ContextMenu("Break")]
        public void Break()
        {
            if (_broken)
                return;
            _broken = true;
            _latched = false;
            Destroy(_hinge);
            _body.WakeUp();
        }

        [ContextMenu("Log State")]
        void LogState() => Debug.Log($"{name}: {State}", this);

        /// <summary>
        /// Swings it toward closed like a door closer: at Close Speed, slowing in step with how
        /// near closed it is once within Close Zone, then quickening to Latch Speed for the last
        /// push over the latch. Catches up with that speed as the hand does, braking too, with
        /// the torque capped at Close Torque.
        /// </summary>
        void DriveShut(float angle)
        {
            float off = Mathf.Abs(angle);
            float speed = off < latchAngle ? latchSpeed : closeSpeed * Mathf.Min(off / closeZone, 1f);
            Vector3 axis = Axis;
            float gap = (-Mathf.Sign(angle) * speed * Mathf.Deg2Rad) - Vector3.Dot(_body.angularVelocity, axis);
            float inertia = HingeState.InertiaAbout(_body, _hinge.transform.TransformPoint(_hinge.anchor), axis);
            float torque = inertia * HingeState.CatchUpPerStep / Time.fixedDeltaTime * gap;
            _body.AddTorque(axis * Mathf.Clamp(torque, -closeTorque, closeTorque));
        }

        void SetLatched(bool latched)
        {
            _latched = latched;
            if (_broken)
                return;
            JointLimits limits = _openLimits;
            if (latched)
            {
                limits.min = Mathf.Max(_openLimits.min, -latchPlay);
                limits.max = Mathf.Min(_openLimits.max, latchPlay);
            }
            _hinge.limits = limits; // a struct: must be reassigned
            _body.WakeUp();
        }
    }
}
