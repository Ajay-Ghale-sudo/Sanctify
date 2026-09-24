using UnityEngine;
using UnityEngine.InputSystem;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Thin wrapper over the "Player" action map in Sanctify.inputactions. Polled once per
    /// frame by <see cref="PlayerController"/>; nothing else should touch the actions directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        InputActionMap _playerMap;
        InputAction _moveForward;
        InputAction _strafe;
        InputAction _lookYaw;
        InputAction _lookPitch;
        InputAction _run;
        InputAction _attack;
        InputAction _magic;
        InputAction _interact;
        InputAction _peek;
        InputAction _interactMode;
        InputAction _tiptoe;
        InputAction _crouch;

        public InputActionAsset Actions => actions;

        /// <summary>x = strafe, y = forward.</summary>
        public Vector2 Move => new(_strafe.ReadValue<float>(), _moveForward.ReadValue<float>());
        /// <summary>x = yaw, y = pitch.</summary>
        public Vector2 Look => new(_lookYaw.ReadValue<float>(), _lookPitch.ReadValue<float>());
        /// <summary>Right stick. x = right, y = up. Already radially deadzoned by the gamepad layout.</summary>
        public Vector2 Peek => _peek.ReadValue<Vector2>();
        public bool RunHeld => _run.IsPressed();
        public bool AttackPressed => _attack.WasPressedThisFrame();
        public bool MagicPressed => _magic.WasPressedThisFrame();
        public bool MagicReleased => _magic.WasReleasedThisFrame();
        public bool MagicHeld => _magic.IsPressed();
        public bool InteractPressed => _interact.WasPressedThisFrame();
        public bool InteractReleased => _interact.WasReleasedThisFrame();
        /// <summary>Right stick click. Toggles the cursor interact mode.</summary>
        public bool InteractModePressed => _interactMode.WasPressedThisFrame();
        /// <summary>Right trigger. Shares the control with pitch; only read in interact mode.</summary>
        public bool TiptoeHeld => _tiptoe.IsPressed();
        /// <summary>Left trigger. Shares the control with pitch; only read in interact mode.</summary>
        public bool CrouchHeld => _crouch.IsPressed();

        void Awake()
        {
            if (actions == null)
            {
                Debug.LogError($"{nameof(PlayerInputReader)} on {name} has no InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            _playerMap = actions.FindActionMap("Player", throwIfNotFound: true);
            _moveForward = _playerMap.FindAction("MoveForward", throwIfNotFound: true);
            _strafe = _playerMap.FindAction("Strafe", throwIfNotFound: true);
            _lookYaw = _playerMap.FindAction("LookYaw", throwIfNotFound: true);
            _lookPitch = _playerMap.FindAction("LookPitch", throwIfNotFound: true);
            _run = _playerMap.FindAction("Run", throwIfNotFound: true);
            _attack = _playerMap.FindAction("Attack", throwIfNotFound: true);
            _magic = _playerMap.FindAction("Magic", throwIfNotFound: true);
            _interact = _playerMap.FindAction("Interact", throwIfNotFound: true);
            _peek = _playerMap.FindAction("Peek", throwIfNotFound: true);
            _interactMode = _playerMap.FindAction("InteractMode", throwIfNotFound: true);
            _tiptoe = _playerMap.FindAction("Tiptoe", throwIfNotFound: true);
            _crouch = _playerMap.FindAction("Crouch", throwIfNotFound: true);
        }

        void OnEnable()
        {
            _playerMap?.Enable();
        }

        void OnDisable()
        {
            _playerMap?.Disable();
        }

        /// <summary>Switch the gameplay map off entirely, e.g. while a UI map is active.</summary>
        public void SetPlayerMapEnabled(bool enabledState)
        {
            if (_playerMap == null)
                return;
            if (enabledState) _playerMap.Enable();
            else _playerMap.Disable();
        }
    }
}
