using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sanctify.Characters.Player
{
    [Flags]
    public enum PlayerControls
    {
        None     = 0,
        Movement = 1 << 0,
        Look     = 1 << 1,
        Actions  = 1 << 2,   // attack, magic, interact
        HeadBob  = 1 << 3,
        All      = Movement | Look | Actions | HeadBob,
    }

    /// <summary>
    /// The single place that decides which player controls are currently allowed.
    /// Anything that needs to take control away (menus, cutscenes, dialogue, death)
    /// takes a lock and disposes it when done. Locks stack: controls stay blocked
    /// until every lock that blocks them has been released, so two systems can't
    /// accidentally re-enable input for each other.
    /// </summary>
    public sealed class PlayerControlLock : MonoBehaviour
    {
        [Tooltip("Extra controls to block while testing in the editor. Behaves like a permanent lock.")]
        [SerializeField] PlayerControls debugBlock = PlayerControls.None;

        readonly List<ControlLock> _locks = new();
        PlayerControls _blocked;

        /// <summary>Controls currently blocked by at least one lock.</summary>
        public PlayerControls Blocked => _blocked | debugBlock;

        public event Action<PlayerControls> BlockedChanged;

        public bool IsAllowed(PlayerControls controls) => (Blocked & controls) == 0;

        /// <summary>
        /// Blocks the given controls until the returned handle is disposed.
        /// <code>using var _ = controlLock.Block(PlayerControls.All, "Pause menu");</code>
        /// </summary>
        public ControlLock Block(PlayerControls controls, string reason = null)
        {
            var handle = new ControlLock(this, controls, reason);
            _locks.Add(handle);
            Recompute();
            return handle;
        }

        public void Release(ControlLock handle)
        {
            if (handle == null || !_locks.Remove(handle))
                return;
            Recompute();
        }

        /// <summary>Drops every lock. Use for hard resets such as loading a new scene.</summary>
        public void ReleaseAll()
        {
            if (_locks.Count == 0)
                return;
            _locks.Clear();
            Recompute();
        }

        void Recompute()
        {
            var previous = Blocked;
            _blocked = PlayerControls.None;
            for (int i = 0; i < _locks.Count; i++)
                _blocked |= _locks[i].Controls;

            if (Blocked != previous)
                BlockedChanged?.Invoke(Blocked);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying)
                Recompute();
        }
#endif

        public sealed class ControlLock : IDisposable
        {
            public PlayerControls Controls { get; }
            public string Reason { get; }

            PlayerControlLock _owner;

            internal ControlLock(PlayerControlLock owner, PlayerControls controls, string reason)
            {
                _owner = owner;
                Controls = controls;
                Reason = reason;
            }

            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                if (owner != null)
                    owner.Release(this);
            }
        }
    }
}
