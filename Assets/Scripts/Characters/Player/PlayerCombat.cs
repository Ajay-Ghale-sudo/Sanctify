using System;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    /// <summary>
    /// Attack API. Only the timing skeleton exists for now: a swing starts, lasts
    /// <see cref="swingDuration"/>, then ends. Weapon data, hit detection and animation
    /// will hang off <see cref="SwingStarted"/> / <see cref="SwingEnded"/> later.
    ///
    /// Driven by <see cref="PlayerController"/>; has no Update of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] float swingDuration = 0.6f;

        float _swingRemaining;

        public bool IsSwinging { get; private set; }
        public bool CanAttack => !IsSwinging;
        /// <summary>0 at the start of a swing, 1 at the end.</summary>
        public float SwingProgress => IsSwinging ? 1f - Mathf.Clamp01(_swingRemaining / swingDuration) : 0f;

        public event Action SwingStarted;
        public event Action SwingEnded;

        /// <returns>True if a swing started.</returns>
        public bool TryAttack()
        {
            if (!CanAttack)
                return false;

            IsSwinging = true;
            _swingRemaining = swingDuration;
            Debug.Log("swinging", this);
            SwingStarted?.Invoke();
            return true;
        }

        /// <summary>Ends the current swing early, e.g. when control is taken away.</summary>
        public void CancelSwing()
        {
            if (IsSwinging)
                EndSwing();
        }

        public void Tick(float deltaTime)
        {
            if (!IsSwinging)
                return;

            _swingRemaining -= deltaTime;
            if (_swingRemaining <= 0f)
                EndSwing();
        }

        void EndSwing()
        {
            IsSwinging = false;
            _swingRemaining = 0f;
            Debug.Log("stopped swinging", this);
            SwingEnded?.Invoke();
        }
    }
}
