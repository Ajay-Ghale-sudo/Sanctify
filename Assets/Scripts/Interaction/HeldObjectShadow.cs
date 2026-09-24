using System.Collections.Generic;
using Sanctify.Characters.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Sanctify.Interaction
{
    /// <summary>
    /// A soft shadow on whatever is directly below a held object. In first person it's hard to
    /// judge how far away or how high a held object is, and moving it nearer or farther moves it
    /// along the one direction the eye judges worst. The shadow turns that into a spot on the
    /// surface below, which is easy to read: it shows where the object would land, and darkens
    /// as the object nears the surface.
    ///
    /// A URP decal, so it needs the Decal renderer feature. The projector is made at startup.
    /// Driven by <see cref="PlayerController"/> from LateUpdate, after the camera, so it follows
    /// the object where it's drawn. Has no Update of its own.
    /// </summary>
    [RequireComponent(typeof(PlayerInteractor))]
    [DisallowMultipleComponent]
    public sealed class HeldObjectShadow : MonoBehaviour
    {
        // Upward-facing surfaces take the shadow; walls and the sides of props fade out of it.
        // Needs a decal shader with angle fade on, like the one the rig builder uses.
        const float FadeStartAngle = 50f;
        const float FadeEndAngle = 70f;

        static readonly Quaternion ProjectDown = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        [Tooltip("Decal material with a soft round mask in its alpha. The rig builder makes one.")]
        [SerializeField] Material material;
        [Tooltip("Opacity when the object touches the surface below.")]
        [SerializeField, Range(0f, 1f)] float opacity = 0.7f;
        [Tooltip("Height of the object above the surface, in metres, at which the shadow has faded out.")]
        [SerializeField, Min(0.1f)] float fadeHeight = 3f;
        [Tooltip("Shadow size relative to the object's footprint seen from above.")]
        [SerializeField, Min(0.1f)] float sizeScale = 1.1f;
        [Tooltip("Smallest shadow width, in metres, so something thin hanging end-down still casts one.")]
        [SerializeField, Min(0.01f)] float minSize = 0.12f;
        [Tooltip("How far above and below the surface the shadow reaches, in metres. Enough to wrap over small steps; more starts darkening the object itself as it nears the surface.")]
        [SerializeField, Min(0.01f)] float surfaceBand = 0.1f;

        readonly List<Collider> _colliders = new();
        PlayerInteractor _interactor;
        DecalProjector _projector;
        Rigidbody _body;

        void Awake()
        {
            _interactor = GetComponent<PlayerInteractor>();
            if (material == null)
            {
                Debug.LogWarning($"{nameof(HeldObjectShadow)} on {name} has no material, so held objects cast no shadow.", this);
                return;
            }

            var projectorObject = new GameObject("Held Object Shadow");
            projectorObject.transform.SetParent(transform, false);
            _projector = projectorObject.AddComponent<DecalProjector>();
            _projector.material = material;
            _projector.startAngleFade = FadeStartAngle;
            _projector.endAngleFade = FadeEndAngle;
            _projector.enabled = false;
        }

        public void LateTick()
        {
            if (_projector == null)
                return;
            Rigidbody body = _interactor.Current is GrabState grab ? grab.HeldBody : null;
            _projector.enabled = body != null && Place(body);
        }

        /// <summary>Puts the shadow under the body. False if there's nothing close enough below it.</summary>
        bool Place(Rigidbody body)
        {
            if (body != _body)
                CacheColliders(body);
            if (!TryGetDrawnBounds(body, out Bounds bounds))
                return false;

            var down = new Ray(bounds.center, Vector3.down);
            if (!_interactor.CastSolid(down, bounds.extents.y + fadeHeight, body, out RaycastHit hit))
                return false;

            float height = Mathf.Max(hit.distance - bounds.extents.y, 0f);
            float fade = opacity * (1f - height / fadeHeight);
            if (fade <= 0.01f)
                return false;

            // The projector points down (its x is world x, its y world z) and reaches from a
            // little above the surface to a little below it.
            _projector.size = new Vector3(
                Mathf.Max(bounds.size.x * sizeScale, minSize),
                Mathf.Max(bounds.size.z * sizeScale, minSize),
                surfaceBand * 2f);
            _projector.pivot = new Vector3(0f, 0f, surfaceBand);
            _projector.fadeFactor = fade;
            _projector.transform.SetPositionAndRotation(hit.point + Vector3.up * surfaceBand, ProjectDown);
            return true;
        }

        void CacheColliders(Rigidbody body)
        {
            _body = body;
            body.GetComponentsInChildren(_colliders);
            for (int i = _colliders.Count - 1; i >= 0; i--)
            {
                Collider collider = _colliders[i];
                if (collider.isTrigger || collider.attachedRigidbody != body)
                    _colliders.RemoveAt(i); // triggers, and parts of child bodies
            }
        }

        bool TryGetDrawnBounds(Rigidbody body, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Collider collider in _colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;
                if (any)
                    bounds.Encapsulate(collider.bounds);
                else
                    bounds = collider.bounds;
                any = true;
            }
            // Collider bounds sit at the physics pose; move them to where the body is drawn this frame.
            if (any)
                bounds.center += body.transform.position - body.position;
            return any;
        }
    }
}
