using System.Collections.Generic;
using UnityEngine;

namespace Sanctify.Magic
{
    /// <summary>
    /// Rope between the player and the Mage Hand, measured round the corners the hand went past,
    /// so a hand round a corner is farther away than one in plain sight. Only static colliders
    /// (no attached Rigidbody) block it, so props, doors, gates and both pawns never add corners.
    ///
    /// Going round a corner adds a node where the line from the previous anchor was last clear:
    /// the hand's position one step earlier. Every step, the hand cuts back to the earliest node
    /// it can see and the player to the latest, so coming back shortens the rope, and once they
    /// see each other every node goes. The player walking away grows it from their end the same
    /// way. Owned by <see cref="MageHandSpell"/>, ticked every fixed step.
    /// </summary>
    public sealed class MageHandTether
    {
        // ponytail: a runaway rope counts as too long; raise this if real levels need more corners.
        const int MaxNodes = 32;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        readonly List<Vector3> _nodes = new(); // player end first
        Vector3 _lastPlayer, _lastHand;

        public int NodeCount => _nodes.Count;
        /// <summary>Player → nodes → hand, in metres, at the latest step. Infinite for a runaway rope.</summary>
        public float Length { get; private set; }

        public void Reset(Vector3 player, Vector3 hand)
        {
            _nodes.Clear();
            _lastPlayer = player;
            _lastHand = hand;
            Length = Vector3.Distance(player, hand);
        }

        /// <returns>The rope's length.</returns>
        public float FixedTick(Vector3 player, Vector3 hand)
        {
            if (Clear(player, hand))
                _nodes.Clear();
            else
            {
                // Hand end: cut back to the earliest node in sight. None in sight means it just
                // went round a corner, so where it was last step becomes a node. If even that
                // can't be seen (spawned behind bars), nothing is added: the stretch counts as straight.
                int seen = -1;
                for (int i = 0; i < _nodes.Count && seen < 0; i++)
                    if (Clear(_nodes[i], hand)) seen = i;
                if (seen >= 0) _nodes.RemoveRange(seen + 1, _nodes.Count - seen - 1);
                else if (Clear(_nodes.Count > 0 ? _nodes[^1] : player, _lastHand)) _nodes.Add(_lastHand);

                // Player end, the same from the other side.
                seen = -1;
                for (int i = _nodes.Count - 1; i >= 0 && seen < 0; i--)
                    if (Clear(player, _nodes[i])) seen = i;
                if (seen >= 0) _nodes.RemoveRange(0, seen);
                else if (Clear(_lastPlayer, _nodes.Count > 0 ? _nodes[0] : hand)) _nodes.Insert(0, _lastPlayer);
            }
            _lastPlayer = player;
            _lastHand = hand;
            Length = _nodes.Count > MaxNodes ? float.PositiveInfinity : PathLength(player, hand);
            return Length;
        }

        /// <summary>Draws the rope for one physics step. Needs Gizmos on in the Game view.</summary>
        public void DrawDebug(Vector3 player, Vector3 hand)
        {
            Vector3 from = player;
            foreach (Vector3 node in _nodes)
            {
                Debug.DrawLine(from, node, Color.magenta, Time.fixedDeltaTime);
                from = node;
            }
            Debug.DrawLine(from, hand, Color.magenta, Time.fixedDeltaTime);
        }

        float PathLength(Vector3 player, Vector3 hand)
        {
            float length = 0f;
            Vector3 from = player;
            foreach (Vector3 node in _nodes)
            {
                length += Vector3.Distance(from, node);
                from = node;
            }
            return length + Vector3.Distance(from, hand);
        }

        static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-3f)
                return true;
            int count = Physics.RaycastNonAlloc(a, delta / length, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].collider.attachedRigidbody == null)
                    return false; // static geometry
            return true;
        }
    }
}
