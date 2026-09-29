using Sanctify.Cameras;
using Sanctify.Interaction;
using Sanctify.Magic;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Orchestrates the player, and the Mage Hand while it's out. Sub-systems have no Update of
    /// their own, so this is the only place ordering is decided:
    ///
    ///   Update       read input → right stick → interact mode → look → actions → interaction tick → hand → magic, fades   (frame rate)
    ///   FixedUpdate  stance → movement + motor → interaction forces → hand flight and forces                             (fixed step, deterministic)
    ///   LateUpdate   camera rig composes the interpolated pose → interaction, hand, drawn cursor and held-object shadow follow it
    ///
    /// The current interaction state gets the right stick first, and can keep it from peeking or
    /// moving the cursor. The cursor interact mode then reshapes look and move input (see
    /// <see cref="PlayerInteractMode"/>), and look, move and action input pass through the
    /// current interaction state, which decides whether the default action also runs (see
    /// <see cref="PlayerInteractor"/>).
    ///
    /// Input goes only to the pawn being controlled, routed the same way for either (each has its
    /// own interactor and interact mode), but both pawns tick every frame and step, so what each
    /// one holds stays held. The other pawn's view and interact mode are frozen rather than
    /// exited, and its movement and interaction run on zero input: gravity still applies, and a
    /// drag sees no effort.
    ///
    /// Also the interactor's <see cref="IInteractorBody"/>: a walking capsule, whose reach is
    /// measured flat from its side.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader))]
    [RequireComponent(typeof(PlayerControlLock))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerLook))]
    [RequireComponent(typeof(PlayerCombat))]
    [RequireComponent(typeof(PlayerMagic))]
    [RequireComponent(typeof(PlayerInteractor))]
    [RequireComponent(typeof(PlayerStance))]
    [RequireComponent(typeof(PlayerInteractMode))]
    [DisallowMultipleComponent]
    public sealed class PlayerController : MonoBehaviour, IInteractorBody
    {
        [SerializeField] PlayerCameraRig cameraRig;
        [SerializeField] bool lockCursor = true;

        CharacterMotor _motor;
        PlayerInputReader _input;
        PlayerControlLock _controls;
        PlayerMovement _movement;
        PlayerLook _look;
        PlayerCombat _combat;
        PlayerMagic _magic;
        PlayerInteractor _interactor;
        PlayerStance _stance;
        PlayerInteractMode _interactMode;
        HeldObjectShadow _heldShadow; // optional: without it, held objects cast no shadow
        MageHandSpell _mageHand;      // optional: without it, no Mage Hand

        public PlayerInputReader InputReader => _input;
        public PlayerControlLock Controls => _controls;
        public PlayerMovement Movement => _movement;
        public PlayerLook Look => _look;
        public PlayerCombat Combat => _combat;
        public PlayerMagic Magic => _magic;
        public PlayerInteractor Interactor => _interactor;
        public PlayerStance Stance => _stance;
        public PlayerInteractMode InteractMode => _interactMode;
        public HeldObjectShadow HeldShadow => _heldShadow;
        public PlayerCameraRig CameraRig => cameraRig;

        MageHand Hand => _mageHand != null ? _mageHand.Hand : null;
        bool PlayerPossessed => _mageHand == null || !_mageHand.HandPossessed;

        void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _input = GetComponent<PlayerInputReader>();
            _controls = GetComponent<PlayerControlLock>();
            _movement = GetComponent<PlayerMovement>();
            _look = GetComponent<PlayerLook>();
            _combat = GetComponent<PlayerCombat>();
            _magic = GetComponent<PlayerMagic>();
            _interactor = GetComponent<PlayerInteractor>();
            _stance = GetComponent<PlayerStance>();
            _interactMode = GetComponent<PlayerInteractMode>();
            _heldShadow = GetComponent<HeldObjectShadow>();
            _mageHand = GetComponent<MageHandSpell>();

            if (cameraRig == null)
                cameraRig = GetComponentInChildren<PlayerCameraRig>();
        }

        void Start()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool inputReady = _input.isActiveAndEnabled;
            bool canLook = inputReady && _controls.IsAllowed(PlayerControls.Look);
            bool canAct = inputReady && _controls.IsAllowed(PlayerControls.Actions);
            MageHand hand = Hand;
            bool playerPossessed = PlayerPossessed;
            PlayerInteractor interactor = playerPossessed ? _interactor : hand.Interactor;
            PlayerInteractMode interactMode = playerPossessed ? _interactMode : hand.InteractMode;

            // The right stick goes to the interaction state first, which may take it (to swing a door, say).
            Vector2 peekInput = canLook ? _input.Peek : Vector2.zero;
            if (!interactor.RoutePeek(peekInput))
                peekInput = Vector2.zero;

            interactMode.Tick(dt, peekInput, canAct);

            Vector2 lookInput = canLook ? _input.Look : Vector2.zero;
            if (interactMode.IsActive)
            {
                lookInput = interactMode.ShapeLook(lookInput);
                peekInput = Vector2.zero; // the right stick is the cursor now
            }
            if (!interactor.RouteLook(lookInput))
                lookInput = Vector2.zero;
            // In interact mode pitch comes only from the edge turn, which is already smooth. The
            // hand turns in its own tick, below, and has no peek.
            if (playerPossessed)
                _look.Tick(dt, lookInput, peekInput, rawPitch: interactMode.IsActive);

            if (canAct)
            {
                if (_input.AttackPressed && interactor.RouteAction(InteractionAction.Attack, true) && playerPossessed)
                    _combat.TryAttack(); // the hand only throws

                // With the hand out, Magic only swaps pawns, whatever either holds, so it skips
                // the interactor. Otherwise it's a cast, which full hands swallow.
                bool handOut = hand != null;
                if (_input.MagicPressed && (handOut || _interactor.RouteAction(InteractionAction.Magic, true)))
                    _magic.Press();
                if (_input.MagicReleased)
                {
                    // A swallowed release cancels instead, so magic can't be left charging.
                    if (handOut || _interactor.RouteAction(InteractionAction.Magic, false))
                        _magic.Release();
                    else
                        _magic.Cancel();
                }

                if (_input.InteractPressed)
                    interactor.RouteAction(InteractionAction.Interact, true);
                if (_input.InteractReleased)
                    interactor.RouteAction(InteractionAction.Interact, false);
            }
            else
            {
                if (_magic.IsPressed)
                    _magic.Cancel();
                interactor.Cancel();
            }

            _interactor.Tick(dt);
            if (hand != null)
                hand.Tick(dt, playerPossessed ? Vector2.zero : lookInput, rawPitch: interactMode.IsActive);
            _combat.Tick(dt);
            _magic.Tick(dt);
            if (_mageHand != null)
                _mageHand.Tick(dt);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // Before movement, so this step's move uses the resized capsule.
            _stance.FixedTick(dt);

            bool playerPossessed = PlayerPossessed;
            bool canMove = _input.isActiveAndEnabled && _controls.IsAllowed(PlayerControls.Movement);
            Vector2 moveInput = canMove ? _input.Move : Vector2.zero;
            MageHand hand = Hand;

            // Zero, not skipped, for the pawn not being controlled: a drag keeps the last move it was given.
            Vector2 playerMove = RouteMove(_interactor, _interactMode, playerPossessed ? moveInput : Vector2.zero);
            _movement.Tick(dt, playerMove, playerPossessed && canMove && _input.RunHeld);
            // After movement, so held objects chase where the player is now.
            _interactor.FixedTick(dt);

            if (hand != null)
                hand.FixedTick(dt, RouteMove(hand.Interactor, hand.InteractMode, playerPossessed ? Vector2.zero : moveInput),
                    !playerPossessed && canMove && _input.RunHeld);
            // After both pawns have moved, so the tether measures where they are now.
            if (_mageHand != null)
                _mageHand.FixedTick(dt);
        }

        /// <summary>Move input through a pawn's interaction state. While it holds something in interact mode, the bumpers set its depth instead of strafing.</summary>
        static Vector2 RouteMove(PlayerInteractor interactor, PlayerInteractMode interactMode, Vector2 move)
        {
            if (interactMode.BumpersMoveHeldObject)
                move.x = 0f;
            return interactor.RouteMove(move) ? move : Vector2.zero;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (cameraRig != null)
                cameraRig.Tick(dt);
            // After the rig, so anything that follows the camera uses this frame's final pose.
            _interactor.LateTick(dt);
            MageHand hand = Hand;
            if (hand != null)
                hand.LateTick(dt);
            // The drawn cursor tracks things through the camera, so only the pawn looked through has one.
            (PlayerPossessed ? _interactMode : hand.InteractMode).LateTick(dt);
            if (_heldShadow != null)
                _heldShadow.LateTick();
        }

        // ------------------------------------------------------------------
        // IInteractorBody
        // ------------------------------------------------------------------

        Pose IInteractorBody.ViewPose => cameraRig.FixedStepPose;
        Vector3 IInteractorBody.Velocity => _movement.Velocity;
        float IInteractorBody.TurnSpeed => _look.AngularVelocity.magnitude;
        float IInteractorBody.SpeedMultiplier { set => _movement.SpeedMultiplier = value; }
        Collider IInteractorBody.Ground => _motor.GroundCollider;
        Collider IInteractorBody.Shape => _motor.Capsule;

        // Measured flat, so arms bending down reach the floor as easily as a table. Negative inside the capsule.
        float IInteractorBody.ReachTo(Vector3 point)
            => Vector3.ProjectOnPlane(point - transform.position, transform.up).magnitude - _motor.Radius;

        // Sideways out of the capsule: collision with the player is off while holding.
        Vector3 IInteractorBody.KeepOut(Vector3 goal, float margin)
        {
            Vector3 up = transform.up;
            Vector3 sideways = Vector3.ProjectOnPlane(goal - transform.position, up);
            float minDistance = _motor.Radius + margin;
            float distance = sideways.magnitude;
            if (distance >= minDistance)
                return goal;

            Vector3 outward = distance > 1e-4f ? sideways / distance : transform.forward;
            return goal + outward * (minDistance - distance);
        }

        // Movement queries ignore Physics.IgnoreCollision, so the motor keeps its own list.
        void IInteractorBody.SetIgnored(Collider collider, bool ignored)
        {
            _motor.SetIgnored(collider, ignored);
            if (collider != null)
                Physics.IgnoreCollision(_motor.Capsule, collider, ignored);
        }
    }
}
