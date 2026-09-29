using System.Collections.Generic;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Stops carried bodies colliding with the pawn carrying them, both in the physics engine and
    /// in any movement queries. After release a body stays ignored until it no longer overlaps
    /// the pawn, so letting go of something inside you doesn't shove you out of the way.
    ///
    /// Owned by <see cref="Sanctify.Characters.Player.PlayerInteractor"/>, ticked every fixed step.
    /// </summary>
    public sealed class PlayerCollisionFilter
    {
        sealed class Entry
        {
            public Rigidbody Body;
            public readonly List<Collider> Colliders = new();
            public bool Releasing;
        }

        readonly IInteractorBody _pawn;
        readonly List<Entry> _entries = new();
        readonly List<Collider> _scratch = new();

        public PlayerCollisionFilter(IInteractorBody pawn) => _pawn = pawn;

        /// <summary>Stops every collider on the body colliding with the player until released.</summary>
        public void Ignore(Rigidbody body)
        {
            Entry entry = Find(body);
            if (entry != null)
            {
                entry.Releasing = false; // grabbed again before it was clear
                return;
            }

            entry = new Entry { Body = body };
            body.GetComponentsInChildren(_scratch);
            foreach (Collider collider in _scratch)
            {
                if (collider.attachedRigidbody != body)
                    continue; // belongs to a child body
                entry.Colliders.Add(collider);
                _pawn.SetIgnored(collider, true);
            }
            _scratch.Clear();
            _entries.Add(entry);
        }

        /// <summary>Restores collision once the body no longer overlaps the player. Safe to call with a destroyed body.</summary>
        public void RestoreWhenClear(Rigidbody body)
        {
            Entry entry = Find(body);
            if (entry != null)
                entry.Releasing = true;
        }

        public void FixedTick()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (!entry.Releasing || (entry.Body != null && OverlapsPawn(entry)))
                    continue;

                foreach (Collider collider in entry.Colliders)
                    _pawn.SetIgnored(collider, false);
                _entries.RemoveAt(i);
            }
        }

        bool OverlapsPawn(Entry entry)
        {
            Collider shape = _pawn.Shape;
            Transform pawn = shape.transform;

            foreach (Collider collider in entry.Colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;
                Transform t = collider.transform;
                if (Physics.ComputePenetration(shape, pawn.position, pawn.rotation,
                        collider, t.position, t.rotation, out _, out _))
                    return true;
            }
            return false;
        }

        // By reference, so an entry whose body was destroyed can still be found.
        Entry Find(Rigidbody body)
        {
            foreach (Entry entry in _entries)
            {
                if (ReferenceEquals(entry.Body, body))
                    return entry;
            }
            return null;
        }
    }
}
