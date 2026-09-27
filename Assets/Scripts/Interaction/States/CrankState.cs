using Sanctify.Characters.Player;
using UnityEngine;

namespace Sanctify.Interaction
{
    /// <summary>
    /// Turns a <see cref="Crank"/> by circling the right stick: however far the stick goes round
    /// its rim, the crank follows, clockwise winding. The stick has to be pushed well out, so
    /// wobbling near the centre does nothing. Peek and the cursor are off meanwhile, and in
    /// interact mode the drawn cursor rides the point grabbed as it goes round. Walking is free.
    /// The state ends itself if the player walks well away. Interact toggles.
    /// </summary>
    public sealed class CrankState : HeldState
    {
        const float BreakDistanceScale = 1.2f;
        const float BreakDistanceSlack = 0.5f;
        // How far out the stick must be for its angle to count.
        const float RimThreshold = 0.6f;

        Crank _crank;
        Vector3 _localGrabPoint; // crank space, so it turns with it
        float _breakDistance;
        float _lastStickAngle;
        bool _onRim;
        bool _holding;

        public CrankState(PlayerInteractor interactor) : base(interactor) { }

        public override bool CanEnter(in InteractionContext context) => context.Prop is Crank;

        public override void Enter()
        {
            base.Enter();
            _crank = (Crank)Prop;
            _localGrabPoint = _crank.transform.InverseTransformPoint(HitPoint);
            _breakDistance = Mathf.Max(Interactor.ReachTo(_crank.transform.position), 0f) * BreakDistanceScale + BreakDistanceSlack;
            _onRim = false;
            _holding = true;
        }

        public override bool TryGetDrawnGrabPoint(out Vector3 point)
        {
            if (!_holding || _crank == null)
            {
                point = default;
                return false;
            }
            point = _crank.transform.TransformPoint(_localGrabPoint);
            return true;
        }

        public override bool OnAction(InteractionAction action, bool pressed)
        {
            if (pressed && action == InteractionAction.Interact && _holding)
                ReturnToPrevious();
            return false; // hands are full: no attacking or casting
        }

        public override bool OnPeek(Vector2 peek)
        {
            if (!_holding || peek.sqrMagnitude < RimThreshold * RimThreshold)
            {
                _onRim = false;
                return false;
            }
            float angle = Mathf.Atan2(peek.y, peek.x) * Mathf.Rad2Deg;
            // Up, right, down, left is clockwise, which lowers the stick's angle.
            if (_onRim)
                _crank.Turn(-Mathf.DeltaAngle(_lastStickAngle, angle));
            _lastStickAngle = angle;
            _onRim = true;
            return false;
        }

        public override void FixedTick(float deltaTime)
        {
            if (_holding && Interactor.ReachTo(_crank.transform.position) > _breakDistance)
                ReturnToPrevious(); // walked away
        }

        public override void Exit()
        {
            _holding = false;
            _crank = null;
            base.Exit();
        }
    }
}
