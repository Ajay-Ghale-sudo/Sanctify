using NUnit.Framework;
using Sanctify.Magic;
using UnityEngine;

namespace Sanctify.Tests
{
    /// <summary>
    /// Flies a hand round the end of a wall and back, and checks the rope bends round the corner
    /// and straightens again. EditMode tests run in the open scene, so everything is built 1000 m
    /// from the arena.
    /// </summary>
    public sealed class MageHandTetherTests
    {
        const float Step = 0.02f; // metres per physics step, about the hand's speed
        const float Eye = 1.5f;
        static readonly Vector3 Middle = new(1000f, 0f, 1000f);

        [Test]
        public void RopeBendsRoundAWallAndStraightensComingBack()
        {
            // Static: 0.5 m thick, 3 m tall, 6 m long along z.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = Middle + new Vector3(0f, 1.5f, 0f);
                wall.transform.localScale = new Vector3(0.5f, 3f, 6f);
                Physics.SyncTransforms();

                Vector3 player = At(-2f, 0f);
                Vector3 hand = At(-1f, 0f);
                var tether = new MageHandTether();
                tether.Reset(player, hand);

                // North past the wall's end, east across it, south to 1 m east of its middle.
                hand = Fly(tether, player, hand, At(-1f, 3.5f));
                hand = Fly(tether, player, hand, At(1f, 3.5f));
                hand = Fly(tether, player, hand, At(1f, 0f));
                Assert.That(tether.NodeCount, Is.GreaterThan(0));
                Assert.That(tether.Length, Is.GreaterThan(5f), "the straight line is 3 m");

                // Back the same way.
                hand = Fly(tether, player, hand, At(1f, 3.5f));
                hand = Fly(tether, player, hand, At(-1f, 3.5f));
                Fly(tether, player, hand, At(-1f, 0f));
                Assert.That(tether.NodeCount, Is.EqualTo(0));
                Assert.That(tether.Length, Is.EqualTo(1f).Within(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        /// <summary>At eye height, x metres east and z metres north of the wall's middle.</summary>
        static Vector3 At(float x, float z) => Middle + new Vector3(x, Eye, z);

        /// <summary>Moves the hand to <paramref name="to"/> a step at a time, ticking the rope each step.</summary>
        static Vector3 Fly(MageHandTether tether, Vector3 player, Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / Step);
            for (int i = 1; i <= steps; i++)
                tether.FixedTick(player, Vector3.Lerp(from, to, (float)i / steps));
            return to;
        }
    }
}
