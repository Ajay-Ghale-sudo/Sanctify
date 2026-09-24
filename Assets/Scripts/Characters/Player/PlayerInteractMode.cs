using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// The cursor interact mode of old first-person dungeon crawlers. Toggled with the right
    /// stick click. While on:
    ///
    ///   - the right stick moves a cursor over the 4:3 view, and the focus ray follows it
    ///   - pushing the cursor into the edge of the view turns the camera that way, only while
    ///     the stick is still pushed toward that edge, so a cursor left at the edge doesn't spin
    ///   - interact toggles a grab instead of needing to be held
    ///   - the bumpers move a held object nearer or farther instead of strafing
    ///   - the triggers raise the player onto their toes or crouch them, instead of pitching
    ///
    /// It sits above the interaction states rather than being one, so the cursor keeps working
    /// while an object is held. Turning it off drops whatever is held, stands the player back up
    /// and eases the drawn cursor back to the centre. The focus ray snaps back at once, so it
    /// doesn't pick things up on the way.
    ///
    /// Driven by <see cref="PlayerController"/>; has no Update of its own.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    [RequireComponent(typeof(PlayerStance))]
    [DisallowMultipleComponent]
    public sealed class PlayerInteractMode : MonoBehaviour
    {
        static readonly Vector2 Centre = new(0.5f, 0.5f);

        [Header("Cursor")]
        [Tooltip("Cursor speed at full stick, in view widths per second.")]
        [SerializeField, Min(0.05f)] float cursorSpeed = 0.9f;
        [Tooltip("Stick response curve. 1 = linear; higher gives finer control near centre.")]
        [SerializeField, Range(1f, 3f)] float stickCurve = 1.5f;
        [Tooltip("How far from the centre the cursor can go, in view units (0.5 = the very edge).")]
        [SerializeField, Range(0.3f, 0.5f)] float cursorLimit = 0.47f;
        [Tooltip("How quickly the drawn cursor eases back to the centre when the mode ends.")]
        [SerializeField, Min(0.1f)] float returnSharpness = 12f;

        [Header("Edge look")]
        [Tooltip("Distance from the centre where the turn band starts. Turn speed ramps from 0 there to full at the cursor limit.")]
        [SerializeField, Range(0.2f, 0.5f)] float edgeBandStart = 0.38f;

        [Header("Held object")]
        [Tooltip("How fast the bumpers move a held object nearer or farther, in metres per second.")]
        [SerializeField, Min(0.05f)] float depthSpeed = 1f;

        PlayerInputReader _input;
        PlayerInteractor _interactor;
        PlayerStance _stance;

        Vector2 _cursor = Centre;
        Vector2 _edgeLook;

        public bool IsActive { get; private set; }
        /// <summary>Where the focus ray goes, 0..1 with y up. The centre when the mode is off.</summary>
        public Vector2 Cursor => IsActive ? _cursor : Centre;
        /// <summary>Where to draw the cursor. Follows <see cref="Cursor"/>, and eases home after the mode ends.</summary>
        public Vector2 DisplayCursor { get; private set; } = Centre;

        void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _interactor = GetComponent<PlayerInteractor>();
            _stance = GetComponent<PlayerStance>();
        }

        void OnDisable()
        {
            if (IsActive)
                Exit();
        }

        /// <param name="canLook">Whether look controls are allowed this frame.</param>
        /// <param name="canAct">Whether action controls are allowed this frame. The mode ends when they aren't.</param>
        public void Tick(float deltaTime, bool canLook, bool canAct)
        {
            if (IsActive && !canAct)
                Exit();
            else if (canAct && _input.InteractModePressed)
            {
                if (IsActive) Exit();
                else Enter();
            }

            if (!IsActive)
            {
                DisplayCursor = Vector2.Lerp(DisplayCursor, Centre, 1f - Mathf.Exp(-returnSharpness * deltaTime));
                return;
            }

            Vector2 stick = canLook ? Vector2.ClampMagnitude(_input.Peek, 1f) : Vector2.zero;
            MoveCursor(stick, deltaTime);
            _edgeLook = EdgeLook(stick);

            _stance.Requested = _input.TiptoeHeld ? Stance.Tiptoe
                              : _input.CrouchHeld ? Stance.Crouching
                              : Stance.Standing;

            if (_interactor.Current is GrabState grab)
                grab.AdjustDepth(_input.Move.x * depthSpeed * deltaTime);

            _interactor.CursorViewport = _cursor;
            DisplayCursor = _cursor;
        }

        /// <summary>
        /// Replaces the normal look input while the mode is on: yaw from the left stick plus the
        /// edge band, pitch from the edge band alone (the triggers are the stance now).
        /// </summary>
        public Vector2 ShapeLook(Vector2 lookInput)
            => new(Mathf.Clamp(lookInput.x + _edgeLook.x, -1f, 1f), _edgeLook.y);

        public void Enter()
        {
            if (IsActive)
                return;
            IsActive = true;
            _cursor = Centre;
            DisplayCursor = Centre;
            _edgeLook = Vector2.zero;
            _interactor.CursorViewport = Centre;
            _interactor.CursorMode = true;
        }

        public void Exit()
        {
            if (!IsActive)
                return;
            IsActive = false;
            _edgeLook = Vector2.zero;
            _stance.Requested = Stance.Standing;
            _interactor.Cancel();
            _interactor.CursorViewport = Centre;
            _interactor.CursorMode = false;
        }

        // ------------------------------------------------------------------

        void MoveCursor(Vector2 stick, float deltaTime)
        {
            float magnitude = stick.magnitude;
            if (magnitude < 1e-4f)
                return;

            Vector2 shaped = stick / magnitude * Mathf.Pow(magnitude, stickCurve);
            _cursor += shaped * (cursorSpeed * deltaTime);
            _cursor.x = Mathf.Clamp(_cursor.x, 0.5f - cursorLimit, 0.5f + cursorLimit);
            _cursor.y = Mathf.Clamp(_cursor.y, 0.5f - cursorLimit, 0.5f + cursorLimit);
        }

        /// <summary>Look input from the cursor's depth into the edge band, gated by the stick still pushing that way.</summary>
        Vector2 EdgeLook(Vector2 stick)
        {
            return new Vector2(EdgeAxis(_cursor.x - 0.5f, stick.x), EdgeAxis(_cursor.y - 0.5f, stick.y));
        }

        float EdgeAxis(float offset, float push)
        {
            float sign = Mathf.Sign(offset);
            float depth = Mathf.InverseLerp(edgeBandStart, cursorLimit, Mathf.Abs(offset));
            float toward = Mathf.Max(0f, push * sign);
            return sign * depth * toward;
        }
    }
}
