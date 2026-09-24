using System.Collections.Generic;
using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Short, non-physics pickup: the item eases from where it lies to the hold point, is taken
    /// on the grab frame, then hides while the rest of the beat plays out. The player stands
    /// still and the view settles for the duration, as in King's Field.
    ///
    /// The item is taken at <see cref="PickupItem.TakeAt"/>, not at the end, so an interruption
    /// after that point keeps it. An interruption before it, or a refusal, puts the item back
    /// exactly where and how it was.
    /// </summary>
    public sealed class PickupState : HeldState
    {
        readonly List<Collider> _colliders = new();
        readonly List<Collider> _disabledColliders = new();

        PickupItem _item;
        Transform _itemTransform;
        Pose _start;
        float _progress;
        bool _taken;

        bool _savedKinematic;
        RigidbodyInterpolation _savedInterpolation;
        CollisionDetectionMode _savedCollisionMode;

        public PickupState(PlayerInteractor interactor) : base(interactor) { }

        public override void Enter()
        {
            base.Enter();

            _item = Prop as PickupItem;
            if (_item == null)
            {
                Debug.LogError($"{nameof(PickupState)} needs a {nameof(PickupItem)}, got '{Prop}'.", Prop);
                ReturnToPrevious();
                return;
            }

            _itemTransform = _item.transform;
            _start = new Pose(_itemTransform.position, _itemTransform.rotation);
            _progress = 0f;
            _taken = false;

            // The item is moved by hand from here on: no physics, and no collisions with the player or world.
            if (Body != null)
            {
                _savedKinematic = Body.isKinematic;
                _savedInterpolation = Body.interpolation;
                _savedCollisionMode = Body.collisionDetectionMode;
                Body.collisionDetectionMode = CollisionDetectionMode.Discrete; // kinematic bodies can't use continuous
                Body.isKinematic = true;
                Body.interpolation = RigidbodyInterpolation.None; // would fight the direct transform writes
            }

            _item.GetComponentsInChildren(_colliders);
            _disabledColliders.Clear();
            foreach (Collider collider in _colliders)
            {
                if (!collider.enabled)
                    continue;
                collider.enabled = false;
                _disabledColliders.Add(collider);
            }
        }

        public override void Tick(float deltaTime)
        {
            _progress += deltaTime / _item.Duration;

            if (!_taken && _progress >= _item.TakeAt)
            {
                if (!_item.Take())
                {
                    ReturnToPrevious(); // refused: Exit puts the item back
                    return;
                }

                _taken = true;
                // Hide rather than destroy, so the beat finishes instead of the prop's
                // destruction ending the state early. Exit destroys it.
                _item.gameObject.SetActive(false);
            }

            if (_progress >= 1f)
                ReturnToPrevious();
        }

        public override void LateTick(float deltaTime)
        {
            if (_taken)
                return;

            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_progress / _item.TakeAt));
            Pose hold = Interactor.HoldPose;
            _itemTransform.SetPositionAndRotation(
                Vector3.Lerp(_start.position, hold.position, k),
                Quaternion.Slerp(_start.rotation, hold.rotation, k));
        }

        // Hands are busy and the player stands still: swallow everything.
        public override bool OnAction(InteractionAction action, bool pressed) => false;
        public override bool OnLook(Vector2 look) => false;
        public override bool OnMove(Vector2 move) => false;

        public override void Exit()
        {
            if (_item != null && !PropDestroyed)
            {
                if (_taken)
                    Object.Destroy(_item.gameObject);
                else
                    Restore();
            }

            _item = null;
            _itemTransform = null;
            _colliders.Clear();
            _disabledColliders.Clear();
            base.Exit();
        }

        void Restore()
        {
            _itemTransform.SetPositionAndRotation(_start.position, _start.rotation);

            foreach (Collider collider in _disabledColliders)
            {
                if (collider != null)
                    collider.enabled = true;
            }

            if (Body != null)
            {
                Body.isKinematic = _savedKinematic;
                Body.collisionDetectionMode = _savedCollisionMode;
                Body.interpolation = _savedInterpolation;
            }
        }
    }
}
