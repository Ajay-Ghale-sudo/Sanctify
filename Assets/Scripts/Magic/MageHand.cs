using Sanctify.Characters.Player;
using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Magic
{
    /// <summary>
    /// The Mage Hand: a second pawn the player can possess. It carries its own
    /// <see cref="PlayerInteractor"/> with a weaker grab settings asset, so it reuses every
    /// interaction state, and it's that interactor's <see cref="IInteractorBody"/>: a
    /// gravity-free sphere with the camera at its centre, whose reach is measured from its surface.
    ///
    /// It flies like a drone: every physics step it's steered toward a wanted velocity and turn
    /// rate in its own frame, so it eases in, drifts and settles. It turns about its own axes
    /// with no pitch limit, so it can loop and fly upside down, but never rolls: the view stays
    /// level (or level upside down). The visible hand leans into its
    /// motion; the view doesn't. It isn't tilted to move, as a real quadcopter is, since that
    /// makes a first-person hand hard to aim.
    ///
    /// It has its own <see cref="PlayerInteractMode"/>, which works as the player's does, so loose
    /// objects are grabbed and dragged from its cursor.
    ///
    /// Spawned by <see cref="MageHandSpell"/>. Driven by <see cref="PlayerController"/>, which
    /// routes input to it as it does to the player while it's possessed, and ticks it whether it's
    /// possessed or not, so whatever it holds stays held; unpossessed, it gets no input and
    /// hovers. Has no Update of its own.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(PlayerInteractor))]
    [RequireComponent(typeof(PlayerInteractMode))]
    [DisallowMultipleComponent]
    public sealed class MageHand : MonoBehaviour, IInteractorBody
    {
        // Sine of the steepest pitch roll is levelled at (about 82°): steeper, level means nothing.
        const float MaxLevelledSlope = 0.99f;

        [Tooltip("Where the camera sits while the hand is possessed. At the centre, so the sphere keeps walls out of the near clip.")]
        [SerializeField] Transform eye;
        [Tooltip("The placeholder mesh seen at the bottom of the view. Leans into the hand's motion.")]
        [SerializeField] Transform visual;

        Rigidbody _body;
        SphereCollider _sphere;
        PlayerLookSettings _lookSettings; // the player's, so pitch works the same way for both
        MageHandSettings _settings;
        Vector2 _turn;                    // x = yaw, y = pitch, -1..1, from the latest frame
        Vector3 _rate;                    // local turn rate, rad/s
        float _speedMultiplier = 1f;

        public PlayerInteractor Interactor { get; private set; }
        public PlayerInteractMode InteractMode { get; private set; }
        /// <summary>The camera anchor.</summary>
        public Transform Eye => eye;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _sphere = GetComponent<SphereCollider>();
            Interactor = GetComponent<PlayerInteractor>();
            InteractMode = GetComponent<PlayerInteractMode>();
        }

        /// <summary>Hooks a freshly spawned hand up to the player who cast it.</summary>
        public void Init(PlayerController owner, MageHandSettings settings)
        {
            _settings = settings;
            _lookSettings = owner.Look.Settings;
            Interactor.SetRayOrigin(owner.CameraRig.transform);
            InteractMode.SetInput(owner.InputReader);
            // ponytail: the motor keeps each dead hand's collider in its ignore set; clear it in
            // DestroyHand if casting ever happens thousands of times a session
            owner.Interactor.Body.SetIgnored(_sphere, true); // neither pawn collides with the other
        }

        /// <param name="turn">
        /// x = yaw, y = pitch, -1..1, already routed through the interactor, as look input is for
        /// the player. Zero while unpossessed. Applied in <see cref="FixedTick"/>.
        /// </param>
        /// <param name="rawPitch">Take pitch as given, without the player's inversion: for interact mode's edge turn.</param>
        public void Tick(float deltaTime, Vector2 turn, bool rawPitch)
        {
            _turn = turn;
            if (!rawPitch && _lookSettings != null && _lookSettings.invertPitch)
                _turn.y = -_turn.y;
            Interactor.Tick(deltaTime);
        }

        /// <param name="move">x = strafe, y = forward, -1..1. Zero while unpossessed.</param>
        /// <param name="fast">Run is held: fly at Fast Speed.</param>
        public void FixedTick(float deltaTime, Vector2 move, bool fast)
        {
            Turn(deltaTime);
            Thrust(move, fast ? _settings.fastSpeed : _settings.maxSpeed);
            Interactor.FixedTick(deltaTime);
        }

        public void LateTick(float deltaTime)
        {
            Interactor.LateTick(deltaTime);
            Lean(deltaTime);
        }

        // ------------------------------------------------------------------
        // Flight
        // ------------------------------------------------------------------

        /// <summary>
        /// Yaw about its own up and pitch about its own right, eased toward the stick so turning
        /// has momentum, with roll locked level. The rate is written into the spin every step, so
        /// a collision only knocks it for one step. Positive look y pitches up, as for the player.
        /// </summary>
        void Turn(float deltaTime)
        {
            Vector3 wantedRate = new Vector3(-_turn.y * _settings.pitchRate, _turn.x * _settings.yawRate, 0f) * Mathf.Deg2Rad;
            _rate = Vector3.MoveTowards(_rate, wantedRate, _settings.turnAcceleration * Mathf.Deg2Rad * deltaTime);
            _body.angularVelocity = _body.rotation * new Vector3(_rate.x, _rate.y, LevellingRoll(deltaTime));
        }

        /// <summary>
        /// The roll rate that levels the hand within one step. Yawing about its own up while
        /// pitched rolls it, and so do knocks, which tilted the view. Level is world up, or world
        /// down while it's upside down, so a loop carries on inverted rather than flipping over.
        /// Facing almost straight up or down, roll and yaw are the same turn, so it's left alone.
        /// </summary>
        float LevellingRoll(float deltaTime)
        {
            Vector3 forward = _body.rotation * Vector3.forward;
            if (Mathf.Abs(forward.y) > MaxLevelledSlope)
                return 0f;
            Vector3 up = _body.rotation * Vector3.up;
            Vector3 level = Vector3.ProjectOnPlane(up.y >= 0f ? Vector3.up : Vector3.down, forward);
            return Vector3.SignedAngle(up, level, forward) * Mathf.Deg2Rad / deltaTime;
        }

        /// <summary>Steers toward a wanted velocity along its own facing, with capped acceleration, so it drifts to a stop.</summary>
        void Thrust(Vector2 move, float topSpeed)
        {
            Vector2 m = Vector2.ClampMagnitude(move, 1f);
            Vector3 wanted = _body.rotation * new Vector3(m.x, 0f, m.y) * (topSpeed * _speedMultiplier);
            Vector3 change = (wanted - _body.linearVelocity) * _settings.response;
            _body.AddForce(Vector3.ClampMagnitude(change, _settings.maxAcceleration), ForceMode.Acceleration);
        }

        /// <summary>Visual only: going forward tips the nose down, strafing rolls toward the strafe.</summary>
        void Lean(float deltaTime)
        {
            if (visual == null)
                return;
            Vector3 local = Quaternion.Inverse(_body.rotation) * _body.linearVelocity;
            float maxLean = _settings.maxLean;
            Quaternion lean = Quaternion.Euler(
                Mathf.Clamp(local.z * _settings.leanPerSpeed, -maxLean, maxLean), 0f,
                Mathf.Clamp(-local.x * _settings.leanPerSpeed, -maxLean, maxLean));
            visual.localRotation = Quaternion.Slerp(visual.localRotation, lean, 1f - Mathf.Exp(-_settings.leanSharpness * deltaTime));
        }

        // ------------------------------------------------------------------
        // IInteractorBody
        // ------------------------------------------------------------------

        Pose IInteractorBody.ViewPose => new(_body.position, _body.rotation);
        Vector3 IInteractorBody.Velocity => _body.linearVelocity;
        float IInteractorBody.TurnSpeed => _body.angularVelocity.magnitude * Mathf.Rad2Deg;
        float IInteractorBody.SpeedMultiplier { set => _speedMultiplier = Mathf.Max(value, 0f); }
        Collider IInteractorBody.Ground => null;
        Collider IInteractorBody.Shape => _sphere;

        float IInteractorBody.ReachTo(Vector3 point) => Vector3.Distance(_body.position, point) - _sphere.radius;

        // Straight out from the centre: a sphere has no sides to push past.
        Vector3 IInteractorBody.KeepOut(Vector3 goal, float margin)
        {
            Vector3 centre = _body.position;
            Vector3 offset = goal - centre;
            float minDistance = _sphere.radius + margin;
            float distance = offset.magnitude;
            if (distance >= minDistance)
                return goal;

            Vector3 outward = distance > 1e-4f ? offset / distance : _body.rotation * Vector3.forward;
            return centre + outward * minDistance;
        }

        // The hand moves by physics alone, so there are no movement queries to exclude it from.
        void IInteractorBody.SetIgnored(Collider collider, bool ignored)
        {
            if (collider != null)
                Physics.IgnoreCollision(_sphere, collider, ignored);
        }
    }
}
