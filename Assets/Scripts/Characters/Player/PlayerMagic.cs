using System;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Magic button API. Distinguishes a tap from a hold: releasing before
    /// <see cref="holdThreshold"/> is a tap; crossing it raises <see cref="HoldStarted"/>
    /// and the eventual release raises <see cref="HoldReleased"/> with the held time.
    /// Spells subscribe to these (<see cref="Sanctify.Magic.MageHandSpell"/>).
    ///
    /// Also holds the one concentration slot: only one concentration spell can be up, and
    /// every way one ends goes through <see cref="EndConcentration"/>.
    ///
    /// Driven by <see cref="PlayerController"/>; has no Update of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMagic : MonoBehaviour
    {
        [Tooltip("Seconds the button must be held before it counts as a hold rather than a tap.")]
        [SerializeField, Min(0.01f)] float holdThreshold = 0.35f;

        public bool IsPressed { get; private set; }
        /// <summary>Held past the threshold and not yet released.</summary>
        public bool IsHolding { get; private set; }
        /// <summary>Seconds since the button went down.</summary>
        public float HoldTime { get; private set; }
        /// <summary>0..1 progress toward the hold threshold; stays 1 once holding.</summary>
        public float HoldProgress => IsPressed ? Mathf.Clamp01(HoldTime / holdThreshold) : 0f;

        public event Action Tapped;
        public event Action HoldStarted;
        /// <summary>Argument is total seconds held.</summary>
        public event Action<float> HoldReleased;
        public event Action Cancelled;

        Action _endConcentration;

        /// <summary>True while a concentration spell is up.</summary>
        public bool IsConcentrating => _endConcentration != null;

        /// <summary>
        /// Starts a concentration spell. Only one can be up, so the current one ends first.
        /// <paramref name="end"/> is how to end the new one.
        /// </summary>
        public void BeginConcentration(Action end)
        {
            EndConcentration();
            _endConcentration = end;
        }

        /// <summary>Ends the concentration spell that's up, if any. Every ending comes through here.</summary>
        public void EndConcentration()
        {
            Action end = _endConcentration;
            _endConcentration = null; // cleared first, so the spell's own ending can't loop back in
            end?.Invoke();
        }

        public void Press()
        {
            if (IsPressed)
                return;
            IsPressed = true;
            IsHolding = false;
            HoldTime = 0f;
        }

        public void Release()
        {
            if (!IsPressed)
                return;

            IsPressed = false;

            if (IsHolding)
            {
                IsHolding = false;
                Debug.Log($"magic: hold released after {HoldTime:0.00}s", this);
                HoldReleased?.Invoke(HoldTime);
            }
            else
            {
                Debug.Log("magic: tap", this);
                Tapped?.Invoke();
            }
        }

        /// <summary>Abandons the current press without firing tap or release, e.g. when control is taken away.</summary>
        public void Cancel()
        {
            if (!IsPressed)
                return;
            IsPressed = false;
            IsHolding = false;
            Debug.Log("magic: cancelled", this);
            Cancelled?.Invoke();
        }

        public void Tick(float deltaTime)
        {
            if (!IsPressed)
                return;

            HoldTime += deltaTime;
            if (!IsHolding && HoldTime >= holdThreshold)
            {
                IsHolding = true;
                Debug.Log("magic: hold started", this);
                HoldStarted?.Invoke();
            }
        }
    }
}
