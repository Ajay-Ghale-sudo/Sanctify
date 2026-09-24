using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Cameras
{
    /// <summary>
    /// Applies <see cref="PlayerLook.Peek"/> (the right-stick glance) to the camera pose. The
    /// smoothing and limits live in <see cref="PlayerLook"/>; this only adds the offset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PeekModifier : MonoBehaviour, ICameraModifier
    {
        [SerializeField] int order = 5;

        PlayerLook _look;

        public int Order => order;

        void Awake()
        {
            _look = GetComponentInParent<PlayerLook>();
            if (_look == null)
                Debug.LogError($"{nameof(PeekModifier)} on {name} needs a {nameof(PlayerLook)} on a parent.", this);
        }

        public void Modify(ref CameraPose pose, float deltaTime)
        {
            if (_look == null)
                return;

            Vector2 peek = _look.Peek;
            pose.EulerOffset.x -= peek.y; // positive x rotation looks down
            pose.EulerOffset.y += peek.x;
        }
    }
}
