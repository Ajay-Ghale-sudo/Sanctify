using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// The cursor interact mode of old first-person dungeon crawlers. Toggled with the right
    /// stick click. While on:
    ///
    ///   - the right stick moves a cursor over the 4:3 view, and the focus ray follows it
    ///   - pushing the cursor into the edge of the view turns the camera that way. Once started,
    ///     the turn keeps going while the stick pushes that way, even as a held object lags back
    ///     from the edge, easing off the further back it falls. Letting the stick off stops it,
    ///     so a cursor left at the edge doesn't spin
    ///   - loose objects can be grabbed, and interact toggles the grab
    ///   - while something is held, the drawn cursor sits on the grabbed point, showing where the
    ///     object is. The stick still moves the point it's pulled toward, unseen, but only so far
    ///     ahead of the object (the same for depth), so heavy objects are waited for rather than
    ///     slung. Only the object reaching the edge turns the view. Letting go leaves the cursor
    ///     where the object was
    ///   - while something is held, the bumpers move it nearer or farther instead of strafing
    ///   - the triggers raise the player onto their toes or crouch them, instead of pitching
    ///
    /// It sits above the interaction states rather than being one, so the cursor keeps working
    /// while an object is held. Turning it off drops whatever is held, stands the player back up
    /// and eases the drawn cursor back to the centre. The focus ray snaps back at once, so it
    /// doesn't pick things up on the way.
    ///
    /// Driven by <see cref="PlayerController"/>: input in Update, the drawn cursor in
    /// LateUpdate after the camera. Has no Update of its own.
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
        [Tooltip("Distance from the centre, in view units, that the cursor must reach while the stick pushes that way to start turning. Measured where the cursor is drawn, so a held object has to reach the edge itself.")]
        [SerializeField, Range(0.2f, 0.5f)] float edgeBandStart = 0.38f;
        [Tooltip("Once turning, how far back toward the centre the cursor can fall and the turn carry on. Turn speed eases from full at the cursor limit to nothing here, so a lagging held object slows the turn smoothly instead of stopping it.")]
        [SerializeField, Range(0.05f, 0.45f)] float edgeHoldStart = 0.2f;

        [Header("Held object")]
        [Tooltip("How fast the bumpers move a held object nearer or farther, in metres per second.")]
        [SerializeField, Min(0.05f)] float depthSpeed = 1f;
        [Tooltip("How far the point a held object is pulled toward can get ahead of the object, in view units. A heavy one is waited for instead of being slung at a far target. Light objects trail by about 0.12 at full stick or full turn, so below that they slow down too.")]
        [SerializeField, Range(0.02f, 0.4f)] float maxLead = 0.15f;
        [Tooltip("How far the bumpers can push the held depth past where the object really is, in metres. Light objects trail by about 0.12 at full depth speed.")]
        [SerializeField, Min(0.02f)] float maxDepthLead = 0.25f;

        PlayerInputReader _input;
        PlayerInteractor _interactor;
        PlayerStance _stance;

        Vector2 _cursor = Centre;
        Vector2 _edgeLook;
        int _edgeTurnX; // -1, 0, +1: the edge being turned toward on each axis
        int _edgeTurnY;
        bool _onHeldObject; // the drawn cursor is following a held object

        public bool IsActive { get; private set; }
        /// <summary>True while the bumpers set a held object's depth, so they don't strafe.</summary>
        public bool BumpersMoveHeldObject => IsActive && _interactor.Current is GrabState;
        /// <summary>Where the focus ray goes, 0..1 with y up. The centre when the mode is off.</summary>
        public Vector2 Cursor => IsActive ? _cursor : Centre;
        /// <summary>
        /// Where to draw the cursor. Follows <see cref="Cursor"/>, except that it sits on a held
        /// object's grabbed point, and eases home after the mode ends.
        /// </summary>
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
                return;

            Vector2 stick = canLook ? Vector2.ClampMagnitude(_input.Peek, 1f) : Vector2.zero;
            MoveCursor(stick, deltaTime);
            // A held object leads: the pull target stays within reach of it, and it has to reach
            // the edge itself to turn the view.
            if (_onHeldObject)
                _cursor = ClampToLimit(DisplayCursor + Vector2.ClampMagnitude(_cursor - DisplayCursor, maxLead));
            _edgeLook = EdgeLook(_onHeldObject ? DisplayCursor : _cursor, stick);

            _stance.Requested = _input.TiptoeHeld ? Stance.Tiptoe
                              : _input.CrouchHeld ? Stance.Crouching
                              : Stance.Standing;

            if (_interactor.Current is GrabState grab)
                grab.AdjustDepth(LeashDepthChange(grab, _input.Move.x * depthSpeed * deltaTime));

            _interactor.CursorViewport = _cursor;
        }

        /// <summary>Places the drawn cursor. After the camera rig, so a held object is tracked where it's drawn this frame.</summary>
        public void LateTick(float deltaTime)
        {
            if (!IsActive)
            {
                _onHeldObject = false;
                DisplayCursor = Vector2.Lerp(DisplayCursor, Centre, 1f - Mathf.Exp(-returnSharpness * deltaTime));
                return;
            }

            if (_interactor.Current is GrabState grab && grab.TryGetDrawnGrabPoint(out Vector3 grabPoint))
            {
                _onHeldObject = true;
                // Behind the camera: stay where it was last seen.
                if (_interactor.TryGetViewportPoint(grabPoint, out Vector2 onScreen))
                    DisplayCursor = ClampToLimit(onScreen);
                return;
            }

            if (_onHeldObject)
            {
                // Just let go: carry on from where the object was, not from where it was being pulled.
                _onHeldObject = false;
                _cursor = DisplayCursor;
                _interactor.CursorViewport = _cursor;
            }
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
            _onHeldObject = false;
            _edgeLook = Vector2.zero;
            _edgeTurnX = _edgeTurnY = 0;
            _interactor.CursorViewport = Centre;
            _interactor.CursorMode = true;
        }

        public void Exit()
        {
            if (!IsActive)
                return;
            IsActive = false;
            _edgeLook = Vector2.zero;
            _edgeTurnX = _edgeTurnY = 0;
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
            _cursor = ClampToLimit(_cursor + shaped * (cursorSpeed * deltaTime));
        }

        Vector2 ClampToLimit(Vector2 viewport) => new(
            Mathf.Clamp(viewport.x, 0.5f - cursorLimit, 0.5f + cursorLimit),
            Mathf.Clamp(viewport.y, 0.5f - cursorLimit, 0.5f + cursorLimit));

        /// <summary>
        /// Limits a bumper depth change so the held depth can't get more than
        /// <see cref="maxDepthLead"/> past where the object really is. Doesn't pull back a lead
        /// that arose some other way, such as a prop's fixed hold depth.
        /// </summary>
        float LeashDepthChange(GrabState grab, float change)
        {
            if (change > 0f)
                return Mathf.Min(change, Mathf.Max(0f, grab.PointDepth + maxDepthLead - grab.Depth));
            if (change < 0f)
                return Mathf.Max(change, Mathf.Min(0f, grab.PointDepth - maxDepthLead - grab.Depth));
            return 0f;
        }

        /// <summary>Look input from the edge turn, for the cursor at <paramref name="at"/>.</summary>
        Vector2 EdgeLook(Vector2 at, Vector2 stick)
        {
            return new Vector2(EdgeAxis(ref _edgeTurnX, at.x - 0.5f, stick.x), EdgeAxis(ref _edgeTurnY, at.y - 0.5f, stick.y));
        }

        /// <summary>
        /// One axis of the edge turn. Starts when the cursor reaches the edge band with the stick
        /// pushing toward that edge, and lasts while the stick keeps pushing that way. Speed
        /// follows how far out the cursor is, from nothing at <see cref="edgeHoldStart"/> to full
        /// at the limit, so a held object lagging back from the edge slows the turn gradually.
        /// </summary>
        /// <param name="turning">The edge being turned toward: -1, 0 or +1. Kept between frames.</param>
        float EdgeAxis(ref int turning, float offset, float push)
        {
            int side = offset < 0f ? -1 : 1;
            if (turning == 0 && Mathf.Abs(offset) >= edgeBandStart && push * side > 0f)
                turning = side;

            float toward = push * turning;
            float reach = Mathf.InverseLerp(edgeHoldStart, cursorLimit, offset * turning);
            if (turning == 0 || toward <= 0f || reach <= 0f)
            {
                turning = 0;
                return 0f;
            }
            return turning * reach * toward;
        }
    }
}
