using UnityEngine;

namespace Sanctify.Magic
{
    /// <summary>
    /// Tuning for the Mage Hand spell. The hand's strength is a separate
    /// <see cref="Sanctify.Interaction.PlayerGrabSettings"/> asset on its interactor
    /// (<c>SO_MageHandGrab</c>).
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MageHand", menuName = "Sanctify/Magic/Mage Hand Settings")]
    public sealed class MageHandSettings : ScriptableObject
    {
        [Header("Casting")]
        [Tooltip("How far ahead of the eye the hand appears, in metres, along the view. Anything solid in the way stops it short.")]
        [Min(0f)] public float spawnDistance = 1.5f;
        [Tooltip("Seconds to fade to black, and the same again back, when casting or swapping.")]
        [Min(0.01f)] public float fadeTime = 0.2f;

        [Header("Flight")]
        [Tooltip("Top speed, in m/s, along the hand's own facing, pitch included. Under the player's 1.8 m/s walk.")]
        [Min(0f)] public float maxSpeed = 1.2f;
        [Tooltip("Top speed while Run is held, in m/s. Double Max Speed, as the player's run is double their walk, and still under it.")]
        [Min(0f)] public float fastSpeed = 2.4f;
        [Tooltip("How quickly the hand closes on the speed it's steered toward, per second. At 3 it gets about 95% of the way in a second, and drifts to a stop when let go.")]
        [Min(0f)] public float response = 3f;
        [Tooltip("Hardest it speeds up or brakes, in m/s², so starts stay soft.")]
        [Min(0f)] public float maxAcceleration = 4f;
        [Tooltip("Top turn rate about its own up, in degrees per second. Under the player's 100.")]
        [Min(0f)] public float yawRate = 90f;
        [Tooltip("Top turn rate about its own right, in degrees per second. There's no limit, so it can loop and fly upside down.")]
        [Min(0f)] public float pitchRate = 70f;
        [Tooltip("How quickly turning builds up and dies away, in degrees per second squared. At 360 it reaches full rate in a quarter second.")]
        [Min(1f)] public float turnAcceleration = 360f;

        [Header("Lean (the visible hand only; the view doesn't lean)")]
        [Tooltip("Degrees of lean per m/s: going forward tips it nose down, strafing rolls it toward the strafe.")]
        [Min(0f)] public float leanPerSpeed = 15f;
        [Range(0f, 90f)] public float maxLean = 25f;
        [Tooltip("How quickly it settles into the lean, per second.")]
        [Min(0.1f)] public float leanSharpness = 8f;

        [Header("Tether")]
        [Tooltip("Longest the rope from the player's eye to the hand can get, in metres, measured round the corners the hand went past. Past it, the spell ends. 9 m is D&D's 30 ft.")]
        [Min(0.5f)] public float tetherLength = 9f;
        [Tooltip("Draws the rope in magenta, for tuning. Needs Gizmos on in the Game view.")]
        public bool drawTether;
    }
}
