using System;
using Sanctify.Cameras;
using Sanctify.Characters.Player;
using Sanctify.Interaction;
using UnityEngine;

namespace Sanctify.Magic
{
    /// <summary>
    /// The Mage Hand spell, on the player rig. Holding Magic casts it: the view fades to black,
    /// the hand appears a little ahead of where the player looked, and the player looks out of
    /// it. With the hand out, tapping Magic swaps between the two pawns, whatever either one
    /// holds: both keep ticking, and a hold is just a state nobody ends. The spell is a
    /// concentration spell, so every way it ends goes through
    /// <see cref="PlayerMagic.EndConcentration"/>. Today that's only the hand getting more than
    /// Tether Length from the player, measured round corners (<see cref="MageHandTether"/>):
    /// possessed, it fades back to the player; unpossessed, it just vanishes. Either way it
    /// drops whatever it held.
    ///
    /// It can't be cast from interact mode. Casts and swaps happen at full black, and never
    /// start while a fade is under way. Movement
    /// and look are locked for the fade, but actions aren't, since that lock would drop whatever
    /// the player holds.
    ///
    /// Driven by <see cref="PlayerController"/>: fades in Update, the tether in FixedUpdate after
    /// both pawns have moved. Has no Update of its own.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [DisallowMultipleComponent]
    public sealed class MageHandSpell : MonoBehaviour
    {
        // Gap left between a newly cast hand and whatever stopped it short, in metres.
        const float SpawnSkin = 0.02f;
        static readonly RaycastHit[] SpawnHits = new RaycastHit[16];

        [SerializeField] MageHand handPrefab;
        [SerializeField] MageHandSettings settings;
        [Tooltip("The player's visible body. Shown only while the hand is possessed, so the hand has someone to look back at.")]
        [SerializeField] Renderer playerBody;

        PlayerController _controller;
        PlayerInteractor _interactor;
        PlayerMagic _magic;
        PlayerControlLock _controls;
        PlayerCameraRig _cameraRig;
        FixedAspectRenderer _renderer; // optional: without it, fades are invisible
        readonly MageHandTether _tether = new();

        Action _atBlack;   // work to do at full black; null once done
        float _fade;       // 0 clear .. 1 black
        PlayerControlLock.ControlLock _fadeLock;

        /// <summary>Null when not summoned.</summary>
        public MageHand Hand { get; private set; }
        public bool HandPossessed { get; private set; }
        /// <summary>The interactor of the pawn being controlled.</summary>
        public PlayerInteractor PossessedInteractor => HandPossessed ? Hand.Interactor : _interactor;

        bool Fading => _atBlack != null || _fade > 0f;
        bool CanSummon => Hand == null && !_controller.InteractMode.IsActive;
        // The item of a pickup in progress flies to a pose relative to the camera, which a swap moves.
        bool CanSwap => Hand != null && !(PossessedInteractor.Current is PickupState);

        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _interactor = GetComponent<PlayerInteractor>();
            _magic = GetComponent<PlayerMagic>();
            _controls = GetComponent<PlayerControlLock>();
            _cameraRig = GetComponentInChildren<PlayerCameraRig>();
            _renderer = _cameraRig != null ? _cameraRig.GetComponent<FixedAspectRenderer>() : null;

            if (handPrefab == null || settings == null || _cameraRig == null)
            {
                Debug.LogError($"{nameof(MageHandSpell)} on {name} needs a hand prefab, settings and a camera rig, so it can't be cast.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            _magic.HoldStarted += OnHoldStarted;
            _magic.Tapped += OnTapped;
        }

        void OnDisable()
        {
            _magic.HoldStarted -= OnHoldStarted;
            _magic.Tapped -= OnTapped;

            // Never leave a hand floating, or the player locked in a fade.
            if (Hand != null)
            {
                Possess(false);
                DestroyHand();
            }
            _atBlack = null;
            _fade = 0f;
            _fadeLock?.Dispose();
            _fadeLock = null;
            if (_renderer != null)
                _renderer.Fade = 0f;
        }

        // Swallowed before it gets here if the player's hands are full.
        void OnHoldStarted()
        {
            if (!Fading && CanSummon)
                FadeThrough(SummonAndPossess);
        }

        void OnTapped()
        {
            if (!Fading && CanSwap)
                FadeThrough(Swap);
        }

        // Checked again at black, since a pickup can start or the hand go while the view fades.
        void Swap()
        {
            if (CanSwap)
                Possess(!HandPossessed);
        }

        void SummonAndPossess()
        {
            if (!CanSummon)
                return; // interact mode was entered during the fade
            Pose view = _interactor.ViewPose;
            Vector3 spawn = view.position + view.forward * SpawnDistance(view);
            Hand = Instantiate(handPrefab, spawn, view.rotation);
            Hand.Init(_controller, settings);
            _tether.Reset(view.position, spawn);
            _magic.BeginConcentration(Unsummon);
            Possess(true);
        }

        /// <summary>
        /// How far along the view the hand can appear: Spawn Distance, or short of anything solid
        /// in the way (walls, doors, props), so it can't be cast through them. Swept with the hand's
        /// own size, so it doesn't slip through gaps narrower than itself, like bars. Hard up
        /// against something, it appears at the eye.
        /// </summary>
        float SpawnDistance(Pose view)
        {
            float radius = handPrefab.GetComponent<SphereCollider>().radius;
            int count = Physics.SphereCastNonAlloc(view.position, radius, view.forward, SpawnHits,
                settings.spawnDistance, ~0, QueryTriggerInteraction.Ignore);
            float distance = settings.spawnDistance;
            for (int i = 0; i < count; i++)
            {
                // Starting inside the player's capsule, it hits that at once.
                if (!PlayerInteractor.IsPawn(SpawnHits[i].collider))
                    distance = Mathf.Min(distance, SpawnHits[i].distance - SpawnSkin);
            }
            return Mathf.Max(distance, 0f);
        }

        /// <summary>After both pawns have moved: ends the spell once the rope is too long.</summary>
        public void FixedTick(float deltaTime)
        {
            if (Hand == null)
                return;
            Vector3 player = _interactor.ViewPose.position;
            Vector3 hand = Hand.Interactor.ViewPose.position;
            float length = _tether.FixedTick(player, hand);
            if (settings.drawTether)
                _tether.DrawDebug(player, hand);
            if (length > settings.tetherLength)
                _magic.EndConcentration(); // ends up in Unsummon
        }

        void Possess(bool hand)
        {
            HandPossessed = hand;
            _interactor.IsPossessed = !hand;
            Hand.Interactor.IsPossessed = hand;
            _cameraRig.ViewOverride = hand ? Hand.Eye : null;
            if (playerBody != null)
                playerBody.enabled = hand;
        }

        /// <summary>How the spell ends. Only ever called through <see cref="PlayerMagic.EndConcentration"/>.</summary>
        void Unsummon()
        {
            if (Hand == null)
                return;
            if (HandPossessed)
                FadeThrough(() => { Possess(false); DestroyHand(); }); // takes over any fade in progress
            else
                DestroyHand();
        }

        void DestroyHand()
        {
            Hand.Interactor.Cancel(); // drops what it holds; its OnDisable would too, this says so
            Destroy(Hand.gameObject);
            Hand = null;
        }

        // ------------------------------------------------------------------
        // Fade
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts a fade, or takes over the one in progress: it carries on from where it is, and
        /// <paramref name="atBlack"/> runs at full black instead. Casts and swaps check
        /// <see cref="Fading"/> first; <see cref="Unsummon"/> doesn't, so it always wins.
        /// </summary>
        void FadeThrough(Action atBlack)
        {
            _atBlack = atBlack;
            // Never Actions: that lock exits interact mode and cancels holds.
            _fadeLock ??= _controls.Block(PlayerControls.Movement | PlayerControls.Look, "Mage Hand fade");
        }

        public void Tick(float deltaTime)
        {
            if (!Fading)
                return;

            float step = deltaTime / settings.fadeTime;
            if (_atBlack != null)
            {
                _fade = Mathf.MoveTowards(_fade, 1f, step);
                if (_fade >= 1f)
                {
                    Action work = _atBlack;
                    _atBlack = null;
                    work();
                }
            }
            else
            {
                _fade = Mathf.MoveTowards(_fade, 0f, step);
                if (_fade <= 0f)
                {
                    _fadeLock.Dispose();
                    _fadeLock = null;
                }
            }

            if (_renderer != null)
                _renderer.Fade = _fade;
        }
    }
}
