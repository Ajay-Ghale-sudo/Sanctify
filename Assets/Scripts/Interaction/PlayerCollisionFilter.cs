using System.Collections.Generic;
using Sanctify.Characters;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Stops carried bodies colliding with the player, both in the physics engine and in the
    /// motor's movement queries. After release a body stays ignored until it no longer overlaps
    /// the player's capsule, so letting go of something inside you doesn't shove you out of the way.
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

        readonly CharacterMotor _motor;
        readonly List<Entry> _entries = new();
        readonly List<Collider> _scratch = new();

        public PlayerCollisionFilter(CharacterMotor motor) => _motor = motor;

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
                SetIgnored(collider, true);
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
                if (!entry.Releasing || (entry.Body != null && OverlapsPlayer(entry)))
                    continue;

                foreach (Collider collider in entry.Colliders)
                    SetIgnored(collider, false);
                _entries.RemoveAt(i);
            }
        }

        bool OverlapsPlayer(Entry entry)
        {
            CapsuleCollider capsule = _motor.Capsule;
            Transform player = _motor.transform;

            foreach (Collider collider in entry.Colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;
                Transform t = collider.transform;
                if (Physics.ComputePenetration(capsule, player.position, player.rotation,
                        collider, t.position, t.rotation, out _, out _))
                    return true;
            }
            return false;
        }

        void SetIgnored(Collider collider, bool ignored)
        {
            _motor.SetIgnored(collider, ignored);
            if (collider != null)
                Physics.IgnoreCollision(_motor.Capsule, collider, ignored);
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
