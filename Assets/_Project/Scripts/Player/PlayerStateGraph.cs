using System.Collections.Generic;
using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;

namespace ShadowTan.Player
{
    // Единственное место, где описаны переходы героини (§9.5.1).
    public sealed class PlayerStateGraph
    {
        readonly Dictionary<PlayerStateId, IPlayerState> states = new Dictionary<PlayerStateId, IPlayerState>();
        readonly PlayerController player;
        IPlayerState current;
        bool started;

        public PlayerStateGraph(PlayerController player)
        {
            this.player = player;

            Add(new DisabledState());
            Add(new LocomoteState());
            Add(new RollState());
            Add(new AttackState());
            Add(new HangState());
            Add(new HangAttackState());
            Add(new HangStabState());
            Add(new FallState());
            Add(new DeadState());
        }

        public PlayerStateId CurrentId { get; private set; }
        public IPlayerState Current => current;

        public void Start()
        {
            started = true;
            Change(player.Motor.IsGrounded ? PlayerStateId.Locomote : PlayerStateId.Fall);
        }

        public void Tick(float deltaTime)
        {
            if (!started || current == null) return;

            var forced = ForcedState();
            if (forced.HasValue && forced.Value != CurrentId)
            {
                Change(forced.Value);
                return;
            }

            if (CurrentId == PlayerStateId.Hang || CurrentId == PlayerStateId.HangAttack || CurrentId == PlayerStateId.HangStab)
            {
                var attached = player.Grab.IsAttached;
                if (!attached || !player.Input.GrabHeld)
                {
                    player.Grab.Detach(attached ? GrabReleaseReason.Released : GrabReleaseReason.TargetUnavailable);
                    Change(PlayerStateId.Fall);
                    return;
                }

                player.Grab.Follow(deltaTime);
            }

            current.Tick(player, deltaTime);
            var next = Evaluate();
            if (next != CurrentId) Change(next);
        }

        PlayerStateId? ForcedState()
        {
            if (player.IsDead || CurrentId == PlayerStateId.Dead) return PlayerStateId.Dead;
            if (!player.ControlEnabled) return PlayerStateId.Disabled;
            return null;
        }

        PlayerStateId Evaluate()
        {
            var forced = ForcedState();
            if (forced.HasValue) return forced.Value;
            var config = player.Config;

            switch (CurrentId)
            {
                case PlayerStateId.Disabled:
                    return player.Motor.IsGrounded ? PlayerStateId.Locomote : PlayerStateId.Fall;

                case PlayerStateId.Locomote:
                    if (player.Input.GrabHeld && player.Grab.HasCandidate) return PlayerStateId.Hang;
                    if (player.CanRoll && player.Motor.IsGrounded && player.Input.HasBuffered(BufferedAction.Roll))
                        return PlayerStateId.Roll;
                    if (player.Motor.IsGrounded && player.Input.HasBuffered(BufferedAction.Attack))
                        return PlayerStateId.Attack;
                    if (!player.Motor.IsGrounded && player.Motor.TimeSinceGrounded > config.coyoteTime)
                        return PlayerStateId.Fall;
                    return PlayerStateId.Locomote;

                case PlayerStateId.Roll:
                    if (!current.IsFinished) return PlayerStateId.Roll;
                    return player.Motor.IsGrounded ? PlayerStateId.Locomote : PlayerStateId.Fall;

                case PlayerStateId.Attack:
                    if (!player.Motor.IsGrounded) return PlayerStateId.Fall;
                    if (current is AttackState attack && config.rollCancelsAttackRecovery && attack.CanCancelIntoRoll(player) &&
                        player.CanRoll && player.Input.HasBuffered(BufferedAction.Roll))
                    {
                        return PlayerStateId.Roll;
                    }

                    if (!current.IsFinished) return PlayerStateId.Attack;
                    return PlayerStateId.Locomote;

                case PlayerStateId.Hang:
                    if (player.Input.StabPressed) return PlayerStateId.HangStab;
                    if (player.Input.HasBuffered(BufferedAction.Attack) &&
                        player.Grab.CurrentTarget != null && player.Grab.CurrentTarget.AllowsAttack)
                    {
                        return PlayerStateId.HangAttack;
                    }

                    return PlayerStateId.Hang;

                case PlayerStateId.HangAttack:
                case PlayerStateId.HangStab:
                    if (!player.Grab.IsAttached)
                    {
                        player.Grab.Detach(GrabReleaseReason.TargetUnavailable);
                        return PlayerStateId.Fall;
                    }

                    return current.IsFinished ? PlayerStateId.Hang : CurrentId;

                case PlayerStateId.Fall:
                    return player.Motor.IsGrounded ? PlayerStateId.Locomote : PlayerStateId.Fall;

                default:
                    return CurrentId;
            }
        }

        void Change(PlayerStateId nextId)
        {
            if (!states.TryGetValue(nextId, out var next) || next == current) return;

            var was = current != null ? CurrentId.ToString() : "—";
            current?.Exit(player);
            current = next;
            CurrentId = nextId;
            current.Enter(player);
            player.Animator.SetState(nextId);

            if (started) EventBus.Publish(new PlayerStateChanged(was, nextId.ToString()));
        }

        void Add(IPlayerState state) => states.Add(state.Id, state);
    }
}
