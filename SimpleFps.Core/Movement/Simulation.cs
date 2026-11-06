using System.Numerics;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Movement;

/// <summary>
/// The game itself, advanced a fixed tick at a time and driven only by player commands. A human, a
/// bot and later a network client are all just sources of commands, which is what will let this
/// run as a server without a rewrite.
/// </summary>
public sealed class Simulation(ICollisionWorld world, MoveSettings settings)
{
    private readonly PlayerMove _move = new(world, settings);
    private readonly List<PlayerState> _players = [];

    public MoveSettings Settings { get; } = settings;

    public IReadOnlyList<PlayerState> Players => _players;

    public int AddPlayer(Vector3 spawn)
    {
        _players.Add(PlayerState.At(spawn + (Vector3.UnitZ * PlayerHull.SpawnLift)));
        return _players.Count - 1;
    }

    public void Teleport(int player, Vector3 position)
    {
        PlayerState state = _players[player];
        state.Position = position;
        state.Velocity = Vector3.Zero;
        state.TouchTicks = 0;
        state.WaterJumpTicks = 0;
        _players[player] = state;
    }

    /// <summary>Advances one tick, with one command for each player.</summary>
    public void Tick(ReadOnlySpan<MoveCommand> commands)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState state = _players[i];
            FireTestGun(ref state, commands[i]);
            _move.Step(ref state, commands[i]);
            _players[i] = state;
        }
    }

    /// <summary>A stand-in gun for feeling recoil, until real weapons arrive in milestone 5.</summary>
    private void FireTestGun(ref PlayerState s, in MoveCommand cmd)
    {
        if (s.AttackCooldown > 0)
        {
            s.AttackCooldown--;
        }

        if (cmd.Attack && s.AttackCooldown == 0)
        {
            PlayerMove.AddKick(ref s, -cmd.Look, Settings.TestRecoilKick, Settings.RecoilTopSpeed);
            s.AttackCooldown = (int)Settings.TestFireTicks;
        }
    }
}
