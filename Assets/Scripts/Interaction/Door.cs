using System.Collections.Generic;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A hinged door, pushed and pulled by its handle through the <see cref="DragState"/>, in
    /// either mode. Props stop it by just being in the way: the door and the props are all
    /// ordinary dynamic bodies. A door left near closed latches: its hinge limits narrow to a
    /// little play, so it stays shut when hit until someone works the handle. A latched door
    /// whose bolt is slid home is locked, and trying it only yanks it back and forth in that play.
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
        [Tooltip("A door nobody holds latches shut below this angle, in degrees.")]
        [SerializeField, Min(0f)] float latchAngle = 10f;
        [Tooltip("Play left when latched, in degrees either side of closed.")]
        [SerializeField, Min(0f)] float latchPlay = 2f;
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

        // One entry per touching collider pair, so a prop touching with several colliders stays until the last lets go.
        readonly List<Rigidbody> _touching = new();

        Rigidbody _body;
        HingeJoint _hinge;
        JointLimits _openLimits;
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
                _touching.RemoveAll(body => body == null); // destroyed without a contact exit
                foreach (Rigidbody body in _touching)
                {
                    if (body.mass >= blockingMass)
                        return true;
                }
                return false;
            }
        }

        public Status State => _broken ? Status.Broken
            : IsLocked ? Status.Locked
            : IsBlocked ? Status.Blocked
            : _latched ? Status.Closed
            : Status.Open;

        bool NearClosed => !_broken && Mathf.Abs(_hinge.angle) < latchAngle;

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
            SetLatched(NearClosed);
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

            if (!_latched && !IsInteractedWith && NearClosed)
                SetLatched(true);

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
            PhysicsProp prop = other != null ? other.GetComponentInParent<PhysicsProp>() : null;
            if (prop == null)
                return;
            // ponytail: a flag standing in for damage; goes when a Damageable calls Break
            if (prop.BreaksDoors && collision.relativeVelocity.magnitude >= breakSpeed)
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
            Begin(interactor, InteractionStateId.Drag, _body, hitPoint);
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
