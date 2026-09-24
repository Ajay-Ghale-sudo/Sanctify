using System.Collections.Generic;
using Sanctify.Cameras;
using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Amnesia-style interaction state machine. Holds one instance of every
    /// <see cref="InteractionState"/>, routes input to the current one and switches between
    /// them. A prop starts an interaction through <see cref="BeginInteraction"/>, which stores
    /// its <see cref="Context"/> for the new state to read on entry.
    ///
    /// The Route methods return true when the default action (look, walk, attack, magic)
    /// should also run. Driven by <see cref="PlayerController"/>; has no Update of its own.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("Where the focus ray starts and which way it points. Normally the camera.")]
        [SerializeField] Transform rayOrigin;
        [Tooltip("Longest focus ray. Each prop can shorten it with its own focus distance.")]
        [SerializeField, Min(0.1f)] float range = 2f;
        [SerializeField] LayerMask mask = ~0;
        [SerializeField] QueryTriggerInteraction triggers = QueryTriggerInteraction.Collide;

        [Header("Pickup")]
        [Tooltip("Where picked-up items travel to. Leave empty to use Hold Offset from the ray origin.")]
        [SerializeField] Transform holdPoint;
        [Tooltip("Hold position relative to the ray origin, in metres (x right, y up, z forward). Used when Hold Point is empty.")]
        [SerializeField] Vector3 holdOffset = new(0.15f, -0.2f, 0.45f);

        [Header("Grab")]
        [SerializeField] PlayerGrabSettings grabSettings;

        [Header("Debug")]
        [SerializeField, Min(0f)] float normalRayLength = 0.5f;
        [SerializeField, Min(0f)] float debugDrawSeconds = 1.5f;

        static readonly Vector2 ViewCentre = new(0.5f, 0.5f);

        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly Dictionary<InteractionStateId, InteractionState> _states = new();
        DefaultState _default;
        PlayerCameraRig _cameraRig;
        Camera _camera;

        /// <summary>
        /// Where in the view the focus ray goes through, 0..1 with y up. The centre unless the
        /// cursor interact mode moves it.
        /// </summary>
        public Vector2 CursorViewport { get; set; } = ViewCentre;

        /// <summary>True while the cursor interact mode is on. Grabbing toggles instead of holding then.</summary>
        public bool CursorMode { get; set; }

        public Transform RayOrigin => rayOrigin;
        public float Range => range;
        public CharacterMotor Motor { get; private set; }
        public PlayerMovement Movement { get; private set; }
        public PlayerGrabSettings GrabSettings { get; private set; }
        public PlayerCollisionFilter CollisionFilter { get; private set; }

        /// <summary>
        /// Unmodified view pose at the latest fixed step, for physics that follows the view.
        /// Use <see cref="AimDirection"/> for where the crosshair points.
        /// </summary>
        public Pose ViewPose
        {
            get
            {
                if (_cameraRig != null)
                    return _cameraRig.FixedStepPose;
                Transform origin = rayOrigin != null ? rayOrigin : transform;
                return new Pose(origin.position, origin.rotation);
            }
        }

        /// <summary>The ray under the cursor, from the rendered camera, so it hits what the player sees.</summary>
        public Ray FocusRay
        {
            get
            {
                if (_camera != null)
                    return _camera.ViewportPointToRay(CursorViewport);
                Transform origin = rayOrigin != null ? rayOrigin : transform;
                return new Ray(origin.position, origin.forward);
            }
        }

        /// <summary>Where the cursor points, including bob and peek.</summary>
        public Vector3 AimDirection => FocusRay.direction;

        /// <summary>
        /// The cursor ray from <see cref="ViewPose"/>: same direction through the view as
        /// <see cref="FocusRay"/>, but without bob, so physics can follow it steadily.
        /// </summary>
        public Ray GetFixedStepAimRay()
        {
            Pose view = ViewPose;
            Vector3 direction = Vector3.forward;
            if (_camera != null)
            {
                // Direction relative to the camera depends only on the FOV, aspect and cursor.
                Transform cam = _camera.transform;
                direction = Quaternion.Inverse(cam.rotation) * _camera.ViewportPointToRay(CursorViewport).direction;
            }
            return new Ray(view.position, view.rotation * direction);
        }
        /// <summary>Written by the prop that started the current interaction.</summary>
        public InteractionContext Context { get; private set; }
        public InteractionState Current { get; private set; }
        /// <summary>The prop under the crosshair. Null while an interaction is in progress.</summary>
        public Interactable FocusProp => _default?.FocusProp;

        /// <summary>World pose that picked-up items travel to.</summary>
        public Pose HoldPose
        {
            get
            {
                if (holdPoint != null)
                    return new Pose(holdPoint.position, holdPoint.rotation);
                Transform origin = rayOrigin != null ? rayOrigin : transform;
                return new Pose(origin.position + origin.rotation * holdOffset, origin.rotation);
            }
        }

        void Awake()
        {
            if (rayOrigin == null)
                Debug.LogWarning($"{nameof(PlayerInteractor)} on {name} has no ray origin assigned, so nothing can be focused.", this);

            Motor = GetComponent<CharacterMotor>();
            Movement = GetComponent<PlayerMovement>();
            _cameraRig = GetComponentInChildren<PlayerCameraRig>();
            _camera = rayOrigin != null ? rayOrigin.GetComponent<Camera>() : null;
            CollisionFilter = new PlayerCollisionFilter(Motor);

            GrabSettings = grabSettings;
            if (GrabSettings == null)
            {
                Debug.LogWarning($"{nameof(PlayerInteractor)} on {name} has no grab settings assigned; using defaults.", this);
                GrabSettings = ScriptableObject.CreateInstance<PlayerGrabSettings>();
            }

            _default = new DefaultState(this);
            _states.Add(InteractionStateId.Default, _default);
            _states.Add(InteractionStateId.Pickup, new PickupState(this));
            _states.Add(InteractionStateId.Grab, new GrabState(this));

            Current = _default;
            Current.Enter();
        }

        // Whatever is mid-interaction gets restored rather than left frozen.
        void OnDisable() => Cancel();

        // ------------------------------------------------------------------
        // Driven by PlayerController
        // ------------------------------------------------------------------

        public bool RouteLook(Vector2 look) => Current.OnLook(look);
        public bool RouteMove(Vector2 move) => Current.OnMove(move);

        public bool RouteAction(InteractionAction action, bool pressed)
        {
            if (action == InteractionAction.Interact && pressed)
                DrawPressDebug();
            return Current.OnAction(action, pressed);
        }

        public void Tick(float deltaTime) => Current.Tick(deltaTime);

        public void FixedTick(float deltaTime)
        {
            Current.FixedTick(deltaTime);
            // After the state, so something let go of this step is checked straight away.
            CollisionFilter.FixedTick();
        }

        public void LateTick(float deltaTime) => Current.LateTick(deltaTime);

        /// <summary>Ends whatever interaction is in progress and returns to the default state.</summary>
        public void Cancel()
        {
            if (Current != null && Current != _default)
                ChangeState(_default);
        }

        // ------------------------------------------------------------------
        // Called by props and states
        // ------------------------------------------------------------------

        /// <summary>
        /// Stores the prop's context, then switches to the state that handles it. Does nothing if
        /// that state refuses the context (e.g. grabbing the object you're standing on).
        /// </summary>
        public void BeginInteraction(InteractionStateId id, in InteractionContext context)
        {
            if (!_states.TryGetValue(id, out InteractionState next))
            {
                Debug.LogError($"No interaction state is registered for {id}.", this);
                return;
            }
            if (!next.CanEnter(context))
                return;

            Context = context;
            ChangeState(next);
        }

        public void ChangeState(InteractionStateId id)
        {
            if (!_states.TryGetValue(id, out InteractionState next))
            {
                Debug.LogError($"No interaction state is registered for {id}.", this);
                return;
            }
            ChangeState(next);
        }

        /// <param name="next">Null returns to the default state.</param>
        internal void ChangeState(InteractionState next)
        {
            next ??= _default;
            if (next == Current)
                return;

            InteractionState previous = Current;
            previous.Exit();
            next.Previous = previous;
            // Set before Enter, so a state that bails out during Enter can change state again.
            Current = next;
            next.Enter();
        }

        internal void NotifyPropDestroyed(Interactable prop) => Current?.OnPropDestroyed(prop);

        /// <summary>Nearest hit along the focus ray, skipping the player and triggers that aren't props.</summary>
        public bool CastFocusRay(out RaycastHit best)
        {
            best = default;
            if (rayOrigin == null)
                return false;

            int count = Physics.RaycastNonAlloc(FocusRay, _hits, range, mask, triggers);
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                ref RaycastHit hit = ref _hits[i];
                if (hit.distance >= bestDistance)
                    continue;
                Collider collider = hit.collider;
                if (collider.transform.IsChildOf(transform))
                    continue;
                // Triggers only count when they belong to a prop, so volumes like audio zones don't block focus.
                if (collider.isTrigger && collider.GetComponentInParent<Interactable>() == null)
                    continue;

                bestDistance = hit.distance;
                best = hit;
                found = true;
            }

            return found;
        }

        void DrawPressDebug()
        {
            if (debugDrawSeconds <= 0f || rayOrigin == null)
                return;

            Ray ray = FocusRay;
            if (CastFocusRay(out RaycastHit hit))
            {
                Debug.DrawLine(ray.origin, hit.point, Color.green, debugDrawSeconds);
                Debug.DrawRay(hit.point, hit.normal * normalRayLength, Color.cyan, debugDrawSeconds);
            }
            else
            {
                Debug.DrawRay(ray.origin, ray.direction * range, Color.red, debugDrawSeconds);
            }
        }
    }
}
