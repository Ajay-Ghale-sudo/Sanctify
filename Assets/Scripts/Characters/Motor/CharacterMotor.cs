using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sanctify.Characters
{
    /// <summary>
    /// Kinematic capsule motor. Owns collision only: callers decide the velocity, this
    /// decides where the capsule ends up and reports what it touched so the caller can
    /// clip its velocity (the motor never rewrites velocity from displacement).
    ///
    /// Intended to be driven from FixedUpdate. <see cref="PreviousPosition"/> and
    /// <see cref="Position"/> bracket the last step so the camera can interpolate.
    ///
    /// Per <see cref="Move"/>:
    ///   1. Depenetrate: push out of anything we're overlapping.
    ///   2. Horizontal collide-and-slide (Quake plane clipping, so converging walls slide along
    ///      the crease and corners stop cleanly). While grounded the move is run twice, with
    ///      and without a step-up, and the version that travels further wins.
    ///   3. Vertical collide-and-slide (gravity).
    ///   4. Ground probe, snapping down while grounded so stairs and slopes keep contact.
    ///
    /// Contact normals are verified at the contact point: a capsule cast against a ledge lip
    /// reports the rounded sphere normal, so a short ray at the hit point recovers the true
    /// surface normal. Ground must also touch the lower sphere ("at the feet").
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class CharacterMotor : MonoBehaviour
    {
        const int MaxPlanes = 6;
        const int MaxContacts = 16;
        const float MinMoveDistance = 1e-5f;
        const float Overbounce = 1.001f;
        const float ContactProbe = 0.02f;

        [Header("Collision")]
        [Tooltip("Layers the capsule collides with. The motor ignores its own colliders automatically.")]
        [SerializeField] LayerMask collisionMask = ~0;
        [Tooltip("Gap kept between the capsule and every surface so casts never start inside geometry.")]
        [SerializeField, Min(0.005f)] float skinWidth = 0.02f;
        [SerializeField, Range(1, 8)] int maxSlideIterations = 5;
        [SerializeField, Range(0, 8)] int maxDepenetrationIterations = 4;

        [Header("Ground")]
        [Tooltip("Slopes steeper than this are walls: the character slides off and can't walk up them.")]
        [SerializeField, Range(1f, 89f)] float maxSlopeAngle = 46f;
        [Tooltip("Tallest ledge the character walks up without stopping.")]
        [SerializeField, Min(0f)] float stepHeight = 0.3f;
        [Tooltip("How far below the feet to look for ground while airborne. Keep small.")]
        [SerializeField, Min(0f)] float groundProbeDistance = 0.04f;
        [Tooltip("How far down to snap while grounded, so walking down stairs and slopes keeps contact.")]
        [SerializeField, Min(0f)] float groundSnapDistance = 0.3f;

        CapsuleCollider _capsule;
        Rigidbody _rigidbody;
        float _minGroundDot;
        float _standingHeight;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlaps = new Collider[16];
        readonly SlideResult _noStep = new();
        readonly SlideResult _step = new();
        readonly SlideResult _vertical = new();
        readonly Vector3[] _contacts = new Vector3[MaxContacts];
        int _contactCount;
        readonly HashSet<Collider> _ignored = new();

        // ---- Results of the last Move ----

        public bool IsGrounded { get; private set; }
        public bool IsOnSteepSlope { get; private set; }
        /// <summary>Verified normal of whatever is under the feet. <c>up</c> when nothing is.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public Collider GroundCollider { get; private set; }
        public bool HitWall { get; private set; }
        public Vector3 LastWallNormal { get; private set; }
        public bool HitCeiling { get; private set; }
        public bool SteppedUp { get; private set; }
        /// <summary>Position at the start of the last step. Interpolate from here to <see cref="Position"/>.</summary>
        public Vector3 PreviousPosition { get; private set; }
        /// <summary>Position at the end of the last step.</summary>
        public Vector3 Position { get; private set; }
        public Vector3 LastDisplacement { get; private set; }
        /// <summary>Displacement / dt of the last step. Informational; do not feed back as velocity.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// Surfaces touched during the last move, for velocity clipping. Walkable normals are
        /// verified ground (skip them); everything else is a wall or ceiling normal, already
        /// flattened to horizontal when it was hit while grounded.
        /// </summary>
        public int ContactCount => _contactCount;
        public Vector3 GetContactNormal(int index) => _contacts[index];

        /// <summary>Raised when the capsule goes from airborne to grounded. Argument is downward speed at impact.</summary>
        public event Action<float> Landed;

        public float SkinWidth => skinWidth;
        public float MaxSlopeAngle => maxSlopeAngle;
        public float StepHeight => stepHeight;
        public CapsuleCollider Capsule => _capsule;
        /// <summary>Capsule height as authored, before any crouch or tiptoe.</summary>
        public float StandingHeight => _standingHeight;
        /// <summary>Current capsule height. The capsule always stands on the feet, at the transform position.</summary>
        public float Height => _capsule.height;
        public float Radius => _capsule.radius;

        void Awake()
        {
            _capsule = GetComponent<CapsuleCollider>();
            _rigidbody = GetComponent<Rigidbody>();
            ConfigureRigidbody();
            _capsule.direction = 1;
            _minGroundDot = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);
            _standingHeight = _capsule.height;
            PreviousPosition = Position = transform.position;
        }

        void OnValidate()
        {
            _minGroundDot = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);
            if (groundSnapDistance < stepHeight)
                groundSnapDistance = stepHeight;
        }

        void Reset()
        {
            _capsule = GetComponent<CapsuleCollider>();
            _rigidbody = GetComponent<Rigidbody>();
            _capsule.direction = 1;
            _capsule.height = 1.8f;
            _capsule.radius = 0.35f;
            _capsule.center = new Vector3(0f, 0.9f, 0f);
            ConfigureRigidbody();
        }

        void ConfigureRigidbody()
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.None;
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void Move(Vector3 velocity, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            Vector3 up = transform.up;
            Vector3 start = transform.position;
            Vector3 position = start;
            bool wasGrounded = IsGrounded;

            HitWall = false;
            HitCeiling = false;
            SteppedUp = false;
            LastWallNormal = Vector3.zero;
            _contactCount = 0;

            Depenetrate(ref position);

            Vector3 delta = velocity * deltaTime;
            Vector3 vertical = up * Vector3.Dot(delta, up);
            Vector3 horizontal = delta - vertical;
            bool movingUp = Vector3.Dot(vertical, up) > MinMoveDistance;

            if (wasGrounded && horizontal.sqrMagnitude > 0f)
                horizontal = FollowGroundPlane(horizontal, GroundNormal, up);

            if (horizontal.sqrMagnitude > 0f)
            {
                if (wasGrounded)
                {
                    position = MoveHorizontalWithStep(position, horizontal, up);
                }
                else
                {
                    SlideMove(_noStep, position, horizontal, up, grounded: false);
                    position = _noStep.Position;
                    Collect(_noStep);
                }
            }

            if (vertical.sqrMagnitude > 0f)
            {
                SlideMove(_vertical, position, vertical, up, grounded: false);
                position = _vertical.Position;
                Collect(_vertical);
            }

            ProbeGround(ref position, up, wasGrounded, movingUp);

            if (!wasGrounded && IsGrounded)
                Landed?.Invoke(Mathf.Max(0f, -Vector3.Dot(velocity, up)));

            PreviousPosition = start;
            Position = position;
            transform.position = position;
            LastDisplacement = position - start;
            Velocity = LastDisplacement / deltaTime;
        }

        /// <summary>Teleports without collision resolution and clears motion state.</summary>
        public void SetPosition(Vector3 position)
        {
            transform.position = position;
            PreviousPosition = Position = position;
            IsGrounded = false;
            IsOnSteepSlope = false;
            GroundNormal = transform.up;
            GroundCollider = null;
            Velocity = Vector3.zero;
            LastDisplacement = Vector3.zero;
            _contactCount = 0;
        }

        public Vector3 GetInterpolatedPosition(float alpha) => Vector3.LerpUnclamped(PreviousPosition, Position, Mathf.Clamp01(alpha));

        public bool IsWalkable(Vector3 normal) => Vector3.Dot(normal, transform.up) >= _minGroundDot;

        /// <summary>
        /// Resizes the capsule from the feet up. Shrinking always succeeds; growing stops short of
        /// whatever is overhead, so call it every step while a taller stance is wanted and it
        /// finishes rising once there is room. Returns the height actually set.
        /// </summary>
        public float TrySetHeight(float height)
        {
            height = Mathf.Max(height, _capsule.radius * 2f);
            float current = _capsule.height;
            float grow = height - current;

            if (grow > 0f && Cast(transform.position, transform.up, grow + skinWidth, out RaycastHit hit))
                height = current + Mathf.Max(hit.distance - skinWidth, 0f);

            if (Mathf.Approximately(height, current))
                return current;

            _capsule.height = height;
            _capsule.center = new Vector3(_capsule.center.x, height * 0.5f, _capsule.center.z);
            return height;
        }

        /// <summary>
        /// Excludes a collider from every movement query, e.g. something the player is carrying.
        /// Queries don't respect <see cref="Physics.IgnoreCollision"/>, so the physics side is separate.
        /// </summary>
        public void SetIgnored(Collider collider, bool ignored)
        {
            if (ignored)
                _ignored.Add(collider);
            else
                _ignored.Remove(collider);
        }

        // ------------------------------------------------------------------
        // Horizontal move with step-up
        // ------------------------------------------------------------------

        Vector3 MoveHorizontalWithStep(Vector3 position, Vector3 horizontal, Vector3 up)
        {
            SlideMove(_noStep, position, horizontal, up, grounded: true);

            // Only bother stepping when something actually blocked us.
            if (stepHeight <= 0f || !_noStep.HitWall)
                return Accept(_noStep);

            float rise = stepHeight;
            if (Cast(position, up, stepHeight + skinWidth, out RaycastHit headHit))
                rise = Mathf.Max(headHit.distance - skinWidth, 0f);
            if (rise < 0.01f)
                return Accept(_noStep);

            SlideMove(_step, position + up * rise, horizontal, up, grounded: true);

            if (!Cast(_step.Position, -up, rise + skinWidth, out RaycastHit landingHit))
                return Accept(_noStep);

            Vector3 landed = _step.Position - up * Mathf.Max(landingHit.distance - skinWidth, 0f);
            Vector3 landingNormal = ResolveContactNormal(landingHit, up);
            if (!IsWalkable(landingNormal) || !IsAtFeet(landingHit.point, landed, up))
                return Accept(_noStep);

            // Must actually climb. Going down is the ground snap's job.
            if (Vector3.Dot(landed - position, up) < 0.005f)
                return Accept(_noStep);

            if (_noStep.Travel >= _step.Travel)
                return Accept(_noStep);

            SteppedUp = true;
            GroundNormal = landingNormal;
            GroundCollider = landingHit.collider;
            _step.Position = landed;
            return Accept(_step);
        }

        Vector3 Accept(SlideResult result)
        {
            Collect(result);
            return result.Position;
        }

        void Collect(SlideResult result)
        {
            for (int i = 0; i < result.ContactCount && _contactCount < MaxContacts; i++)
                _contacts[_contactCount++] = result.Contacts[i];
            if (result.HitWall)
            {
                HitWall = true;
                LastWallNormal = result.WallNormal;
            }
            if (result.HitCeiling)
                HitCeiling = true;
        }

        // ------------------------------------------------------------------
        // Collide and slide
        // ------------------------------------------------------------------

        void SlideMove(SlideResult result, Vector3 start, Vector3 delta, Vector3 up, bool grounded)
        {
            result.Reset(start);
            if (grounded)
                result.AddPlane(GroundNormal); // never clip into the floor we're standing on

            Vector3 original = delta;
            Vector3 remaining = delta;
            Vector3 position = start;

            for (int iteration = 0; iteration < maxSlideIterations; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance < MinMoveDistance)
                    break;

                Vector3 direction = remaining / distance;
                if (!Cast(position, direction, distance + skinWidth, out RaycastHit hit))
                {
                    position += remaining;
                    break;
                }

                float travel = Mathf.Max(hit.distance - skinWidth, 0f);
                position += direction * travel;
                remaining = direction * (distance - travel);

                // The raw normal is what we physically slide against. The verified normal
                // decides whether this counts as ground or as a wall.
                Vector3 plane = hit.normal;
                Vector3 verified = ResolveContactNormal(hit, up);
                bool isGround = IsWalkable(verified) && IsAtFeet(hit.point, position, up);

                if (isGround)
                {
                    result.AddContact(verified);
                }
                else if (Vector3.Dot(plane, up) < -0.1f)
                {
                    result.HitCeiling = true;
                    result.AddContact(plane);
                }
                else
                {
                    result.HitWall = true;
                    result.WallNormal = plane;

                    // While grounded a steep surface is a wall: flatten so we slide along it
                    // instead of being nudged up it a little every step.
                    if (grounded)
                    {
                        Vector3 flat = Vector3.ProjectOnPlane(verified, up);
                        if (flat.sqrMagnitude > 1e-6f)
                            plane = flat.normalized;
                    }
                    result.AddContact(plane);
                }

                if (result.PlaneCount >= MaxPlanes)
                    break;

                result.AddPlane(plane);
                remaining = ClipToPlanes(result, remaining);

                if (Vector3.Dot(remaining, original) <= 0f)
                    break; // clipping turned us around: wedged
            }

            result.Position = position;
            result.Travel = Vector3.ProjectOnPlane(position - start, up).magnitude;
        }

        /// <summary>
        /// Quake's PM_SlideMove plane resolution: try each plane alone, then creases between
        /// pairs, otherwise stop.
        /// </summary>
        static Vector3 ClipToPlanes(SlideResult result, Vector3 velocity)
        {
            int count = result.PlaneCount;
            Vector3[] planes = result.Planes;

            for (int i = 0; i < count; i++)
            {
                Vector3 clipped = ClipVelocity(velocity, planes[i]);
                if (SatisfiesPlanes(planes, count, clipped, i, -1))
                    return clipped;
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 crease = Vector3.Cross(planes[i], planes[j]);
                    if (crease.sqrMagnitude < 1e-6f)
                        continue;
                    crease.Normalize();
                    Vector3 along = crease * Vector3.Dot(velocity, crease);
                    if (SatisfiesPlanes(planes, count, along, i, j))
                        return along;
                }
            }

            return Vector3.zero;
        }

        static bool SatisfiesPlanes(Vector3[] planes, int count, Vector3 velocity, int skipA, int skipB)
        {
            for (int k = 0; k < count; k++)
            {
                if (k == skipA || k == skipB)
                    continue;
                if (Vector3.Dot(velocity, planes[k]) < 0f)
                    return false;
            }
            return true;
        }

        static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal)
        {
            float into = Vector3.Dot(velocity, normal);
            return into >= 0f ? velocity : velocity - normal * (into * Overbounce);
        }

        static Vector3 FollowGroundPlane(Vector3 horizontal, Vector3 groundNormal, Vector3 up)
        {
            if (Vector3.Dot(groundNormal, up) > 0.9999f)
                return horizontal;
            Vector3 onPlane = Vector3.ProjectOnPlane(horizontal, groundNormal);
            if (onPlane.sqrMagnitude < 1e-8f)
                return horizontal;
            return onPlane.normalized * horizontal.magnitude;
        }

        // ------------------------------------------------------------------
        // Ground
        // ------------------------------------------------------------------

        void ProbeGround(ref Vector3 position, Vector3 up, bool wasGrounded, bool movingUp)
        {
            IsGrounded = false;
            IsOnSteepSlope = false;
            GroundCollider = null;

            bool snap = wasGrounded && !movingUp;
            float probe = snap ? groundSnapDistance : groundProbeDistance;

            if (!Cast(position, -up, probe + skinWidth, out RaycastHit hit))
            {
                GroundNormal = up;
                return;
            }

            float drop = Mathf.Max(hit.distance - skinWidth, 0f);
            Vector3 landed = position - up * drop;
            Vector3 normal = ResolveContactNormal(hit, up);
            GroundNormal = normal;

            if (!IsWalkable(normal) || !IsAtFeet(hit.point, landed, up))
            {
                IsOnSteepSlope = IsWalkable(normal) == false;
                return;
            }

            IsGrounded = true;
            GroundCollider = hit.collider;
            if (snap && drop > 0f)
                position = landed;
        }

        /// <summary>
        /// Capsule casts return the sphere's normal at the contact point, which on a ledge lip
        /// points sideways even though the surface on top is flat. Probe with a tiny ray just
        /// inside the surface at the contact point to recover the real face normal.
        /// </summary>
        Vector3 ResolveContactNormal(in RaycastHit hit, Vector3 up)
        {
            if (IsWalkable(hit.normal))
                return hit.normal;

            Vector3 probe = hit.point - hit.normal * (ContactProbe * 0.1f);
            if (Physics.Raycast(probe + up * ContactProbe, -up, out RaycastHit ray, ContactProbe * 2f, collisionMask, QueryTriggerInteraction.Ignore)
                && !IsIgnored(ray.collider)
                && ray.normal.sqrMagnitude > 0.5f)
            {
                return ray.normal;
            }

            return hit.normal;
        }

        /// <summary>Contact must be on the lower sphere to count as ground, not the side of the capsule.</summary>
        bool IsAtFeet(Vector3 contactPoint, Vector3 capsulePosition, Vector3 up)
        {
            GetCapsulePoints(capsulePosition, out Vector3 bottomSphere, out _, out _);
            return Vector3.Dot(contactPoint - bottomSphere, up) <= 0f;
        }

        // ------------------------------------------------------------------
        // Depenetration
        // ------------------------------------------------------------------

        void Depenetrate(ref Vector3 position)
        {
            Quaternion rotation = transform.rotation;

            for (int iteration = 0; iteration < maxDepenetrationIterations; iteration++)
            {
                GetCapsulePoints(position, out Vector3 p1, out Vector3 p2, out float radius);
                int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, _overlaps, collisionMask, QueryTriggerInteraction.Ignore);
                bool moved = false;

                for (int i = 0; i < count; i++)
                {
                    Collider other = _overlaps[i];
                    if (IsIgnored(other))
                        continue;

                    Transform otherTransform = other.transform;
                    if (!Physics.ComputePenetration(_capsule, position, rotation,
                            other, otherTransform.position, otherTransform.rotation,
                            out Vector3 direction, out float distance) || distance <= 0f)
                        continue;

                    position += direction * (distance + skinWidth * 0.5f);
                    moved = true;
                }

                if (!moved)
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        bool Cast(Vector3 position, Vector3 direction, float distance, out RaycastHit best)
        {
            GetCapsulePoints(position, out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, direction, _hits, distance, collisionMask, QueryTriggerInteraction.Ignore);

            best = default;
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                ref RaycastHit hit = ref _hits[i];
                if (IsIgnored(hit.collider))
                    continue;
                if (hit.distance <= 0f)
                    continue; // started overlapping; depenetration handles it
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            return found;
        }

        bool IsIgnored(Collider collider) => collider == _capsule || _ignored.Contains(collider) || collider.transform.IsChildOf(transform);

        void GetCapsulePoints(Vector3 position, out Vector3 bottom, out Vector3 top, out float radius)
        {
            Transform t = transform;
            Vector3 scale = t.lossyScale;
            radius = _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(_capsule.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = position + t.rotation * Vector3.Scale(_capsule.center, scale);
            Vector3 halfSegment = t.up * (height * 0.5f - radius);
            bottom = center - halfSegment;
            top = center + halfSegment;
        }

        sealed class SlideResult
        {
            public Vector3 Position;
            public float Travel;
            public bool HitWall;
            public bool HitCeiling;
            public Vector3 WallNormal;
            public readonly Vector3[] Planes = new Vector3[MaxPlanes];
            public int PlaneCount;
            public readonly Vector3[] Contacts = new Vector3[MaxPlanes + 1];
            public int ContactCount;

            public void Reset(Vector3 position)
            {
                Position = position;
                Travel = 0f;
                HitWall = false;
                HitCeiling = false;
                WallNormal = Vector3.zero;
                PlaneCount = 0;
                ContactCount = 0;
            }

            public void AddPlane(Vector3 normal)
            {
                if (PlaneCount < Planes.Length)
                    Planes[PlaneCount++] = normal;
            }

            public void AddContact(Vector3 normal)
            {
                if (ContactCount < Contacts.Length)
                    Contacts[ContactCount++] = normal;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_capsule == null)
                _capsule = GetComponent<CapsuleCollider>();
            if (_capsule == null)
                return;

            GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2, out float radius);
            Gizmos.color = IsGrounded ? Color.green : (IsOnSteepSlope ? Color.yellow : Color.red);
            Gizmos.DrawWireSphere(p1, radius);
            Gizmos.DrawWireSphere(p2, radius);
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, GroundNormal * 0.5f);
        }
#endif
    }
}
