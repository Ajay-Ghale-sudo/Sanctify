using Sanctify.Cameras;
using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Orchestrates the player. Sub-systems have no Update of their own, so this is the
    /// only place ordering is decided:
    ///
    ///   Update       read input → interact mode → look → actions → interaction tick   (frame rate)
    ///   FixedUpdate  stance → movement + motor → interaction forces                    (fixed step, deterministic)
    ///   LateUpdate   camera rig composes the interpolated pose → interaction follows it
    ///
    /// The cursor interact mode reshapes look and move input first (see <see cref="PlayerInteractMode"/>),
    /// then look, move and action input pass through the current interaction state, which
    /// decides whether the default action also runs (see <see cref="PlayerInteractor"/>).
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
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] PlayerCameraRig cameraRig;
        [SerializeField] bool lockCursor = true;

        PlayerInputReader _input;
        PlayerControlLock _controls;
        PlayerMovement _movement;
        PlayerLook _look;
        PlayerCombat _combat;
        PlayerMagic _magic;
        PlayerInteractor _interactor;
        PlayerStance _stance;
        PlayerInteractMode _interactMode;

        public PlayerInputReader InputReader => _input;
        public PlayerControlLock Controls => _controls;
        public PlayerMovement Movement => _movement;
        public PlayerLook Look => _look;
        public PlayerCombat Combat => _combat;
        public PlayerMagic Magic => _magic;
        public PlayerInteractor Interactor => _interactor;
        public PlayerStance Stance => _stance;
        public PlayerInteractMode InteractMode => _interactMode;
        public PlayerCameraRig CameraRig => cameraRig;

        void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _controls = GetComponent<PlayerControlLock>();
            _movement = GetComponent<PlayerMovement>();
            _look = GetComponent<PlayerLook>();
            _combat = GetComponent<PlayerCombat>();
            _magic = GetComponent<PlayerMagic>();
            _interactor = GetComponent<PlayerInteractor>();
            _stance = GetComponent<PlayerStance>();
            _interactMode = GetComponent<PlayerInteractMode>();

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

            _interactMode.Tick(dt, canLook, canAct);

            Vector2 lookInput = canLook ? _input.Look : Vector2.zero;
            Vector2 peekInput = canLook ? _input.Peek : Vector2.zero;
            if (_interactMode.IsActive)
            {
                lookInput = _interactMode.ShapeLook(lookInput);
                peekInput = Vector2.zero; // the right stick is the cursor now
            }
            if (!_interactor.RouteLook(lookInput))
                lookInput = Vector2.zero;
            _look.Tick(dt, lookInput, peekInput);

            if (canAct)
            {
                if (_input.AttackPressed && _interactor.RouteAction(InteractionAction.Attack, true))
                    _combat.TryAttack();

                if (_input.MagicPressed && _interactor.RouteAction(InteractionAction.Magic, true))
                    _magic.Press();
                if (_input.MagicReleased)
                {
                    // A swallowed release cancels instead, so magic can't be left charging.
                    if (_interactor.RouteAction(InteractionAction.Magic, false))
                        _magic.Release();
                    else
                        _magic.Cancel();
                }

                if (_input.InteractPressed)
                    _interactor.RouteAction(InteractionAction.Interact, true);
                if (_input.InteractReleased)
                    _interactor.RouteAction(InteractionAction.Interact, false);
            }
            else
            {
                if (_magic.IsPressed)
                    _magic.Cancel();
                _interactor.Cancel();
            }

            _interactor.Tick(dt);
            _combat.Tick(dt);
            _magic.Tick(dt);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // Before movement, so this step's move uses the resized capsule.
            _stance.FixedTick(dt);

            bool canMove = _input.isActiveAndEnabled && _controls.IsAllowed(PlayerControls.Movement);
            Vector2 moveInput = canMove ? _input.Move : Vector2.zero;
            if (_interactMode.IsActive)
                moveInput.x = 0f; // the bumpers are held-object depth now
            if (!_interactor.RouteMove(moveInput))
                moveInput = Vector2.zero;

            _movement.Tick(dt, moveInput, canMove && _input.RunHeld);
            // After movement, so held objects chase where the player is now.
            _interactor.FixedTick(dt);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (cameraRig != null)
                cameraRig.Tick(dt);
            // After the rig, so anything that follows the camera uses this frame's final pose.
            _interactor.LateTick(dt);
        }
    }
}
