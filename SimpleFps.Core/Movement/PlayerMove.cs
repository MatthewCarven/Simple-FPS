using System.Numerics;
using SimpleFps.Core.Bsp;
using SimpleFps.Core.Collision;

namespace SimpleFps.Core.Movement;

/// <summary>
/// One tick of player movement. The same code runs for local players, bots and later the server,
/// so it depends only on the state, the command and the world.
/// </summary>
public sealed class PlayerMove(ICollisionWorld world, MoveSettings settings)
{
    private const float Dt = 1f / GameInfo.TickRate;

    private const float GroundProbe = 0.25f;
    private const float WalkableNormalZ = 0.7f;
    private const float LiftOffSpeed = 180f;

    private const int MaxBumps = 4;
    private const int MaxClipPlanes = 5;
    private const float Overbounce = 1.01f;
    private const float StopEpsilon = 0.1f;

    private const float LadderReach = 1f;
    private const float LadderClimbSpeed = 200f;
    private const float LadderSideSpeed = 25f;
    private const float LadderLookPitch = 15f;

    private const int SwimLevel = 2;
    private const float WaterFriction = 1f;
    private const float SwimDrift = 60f;
    private const float SwimSpeedScale = 0.5f;
    private const float WaterJumpReach = 30f;
    private const float WaterJumpForwardSpeed = 50f;
    private const float WaterJumpUpSpeed = 350f;
    private const int WaterJumpTicks = 90;

    /// <summary>Sideways offsets are tried this far apart when looking for a way round a corner.</summary>
    private const float CornerProbeStep = 2f;

    /// <summary>The way ahead must be clear this far before a corner counts as passed.</summary>
    private const float CornerClearance = 8f;

    /// <summary>Pressing against something is noticed from this far away.</summary>
    private const float PressProbe = 2f;

    private const float UnstickWidthStep = 0.25f;

    private static readonly float[] UnstickDistances = [1f, 2f, 4f, 8f, 16f, 32f, 64f];

    public void Step(ref PlayerState s, in MoveCommand cmd)
    {
        Unstick(ref s);
        UpdateCrouch(ref s, cmd);
        CheckPosition(ref s);
        CheckLadder(ref s, cmd);

        if (s.WaterLevel == SwimLevel && s.WaterJumpTicks == 0)
        {
            TryWaterJump(ref s, cmd);
        }

        if (s.WaterJumpTicks > 0)
        {
            WaterJumpMove(ref s);
        }
        else if (s.OnLadder)
        {
            LadderMove(ref s, cmd);
        }
        else if (s.WaterLevel >= SwimLevel)
        {
            SwimMove(ref s, cmd);
        }
        else if (s.OnGround)
        {
            GroundMove(ref s, cmd);
        }
        else
        {
            AirMove(ref s, cmd);
        }

        bool wasOnGround = s.OnGround;
        CheckPosition(ref s);
        if (s.OnGround && !wasOnGround)
        {
            s.TouchTicks = cmd.Jump ? (int)settings.HopTouchTicks : 0;
        }
    }

    /// <summary>
    /// Pushes a player along a direction, stopping once they're moving that way at
    /// <paramref name="topSpeed"/>. This is what lets recoil be limited without limiting blasts.
    /// </summary>
    public static void AddKick(ref PlayerState s, Vector3 direction, float amount, float topSpeed)
    {
        float room = topSpeed - Vector3.Dot(s.Velocity, direction);
        if (room > 0)
        {
            s.Velocity += direction * MathF.Min(amount, room);
        }
    }

    private TraceResult Sweep(in PlayerState s, Vector3 from, Vector3 to) =>
        world.Trace(from, to, s.Mins, s.Maxs, Contents.PlayerSolid);

    private TraceResult SweepWidth(in PlayerState s, Vector3 from, Vector3 to, float halfWidth) =>
        world.Trace(from, to, new Vector3(-halfWidth, -halfWidth, s.Mins.Z), new Vector3(halfWidth, halfWidth, s.Maxs.Z), Contents.PlayerSolid);

    private void Unstick(ref PlayerState s)
    {
        if (!Sweep(s, s.Position, s.Position).StartSolid)
        {
            return;
        }

        // A squeeze is the gentlest way out, and it's the only one that fits a body-width pocket.
        float narrowest = PlayerHull.FullHalfWidth - settings.SqueezeAmount;
        for (float width = s.HalfWidth - UnstickWidthStep; width >= narrowest; width -= UnstickWidthStep)
        {
            if (!SweepWidth(s, s.Position, s.Position, width).StartSolid)
            {
                s.HalfWidth = width;
                return;
            }
        }

        // Otherwise take the nearest free spot in any direction, upward first. A few maps have
        // spawn points buried in their geometry, so this also runs on arrival.
        foreach (float distance in UnstickDistances)
        {
            for (int z = 1; z >= -1; z--)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        Vector3 spot = s.Position + (new Vector3(x, y, z) * distance);
                        if (!Sweep(s, spot, spot).StartSolid)
                        {
                            s.Position = spot;
                            return;
                        }
                    }
                }
            }
        }
    }

    private void UpdateCrouch(ref PlayerState s, in MoveCommand cmd)
    {
        if (cmd.Crouch)
        {
            s.Crouched |= s.OnGround || settings.AllowCrouchJump;
            return;
        }

        if (s.Crouched)
        {
            var standing = s;
            standing.Crouched = false;
            if (!Sweep(standing, s.Position, s.Position).StartSolid)
            {
                s.Crouched = false;
            }
        }
    }

    /// <summary>Works out what's underfoot and how deep in liquid the player is.</summary>
    private void CheckPosition(ref PlayerState s)
    {
        s.OnGround = false;
        if (s.Velocity.Z <= LiftOffSpeed)
        {
            TraceResult t = Sweep(s, s.Position, s.Position - (Vector3.UnitZ * GroundProbe));
            if (t.Hit && t.Normal.Z >= WalkableNormalZ)
            {
                s.OnGround = true;
                s.GroundNormal = t.Normal;
                s.GroundSurface = t.Surface;
                s.WaterJumpTicks = 0;
            }
        }

        s.WaterLevel = 0;
        s.WaterType = Contents.None;
        float feet = s.Position.Z + PlayerHull.Bottom;
        float eye = s.EyeHeight - PlayerHull.Bottom;

        Contents at = world.PointContents(new Vector3(s.Position.X, s.Position.Y, feet + 1f));
        if ((at & Contents.Liquid) == 0)
        {
            return;
        }

        s.WaterType = at & Contents.Liquid;
        s.WaterLevel = 1;
        if ((world.PointContents(new Vector3(s.Position.X, s.Position.Y, feet + (eye / 2f))) & Contents.Liquid) != 0)
        {
            s.WaterLevel = 2;
            if ((world.PointContents(new Vector3(s.Position.X, s.Position.Y, feet + eye)) & Contents.Liquid) != 0)
            {
                s.WaterLevel = 3;
            }
        }
    }

    private void CheckLadder(ref PlayerState s, in MoveCommand cmd)
    {
        TraceResult t = Sweep(s, s.Position, s.Position + (cmd.FlatForward * LadderReach));
        s.OnLadder = t.Hit && (t.Contents & Contents.Ladder) != 0;
    }

    private void TryWaterJump(ref PlayerState s, in MoveCommand cmd)
    {
        if (cmd.Forward <= 0 && !cmd.Jump)
        {
            return;
        }

        Vector3 ahead = cmd.FlatForward;
        Vector3 spot = s.Position + (ahead * WaterJumpReach) + (Vector3.UnitZ * 4f);
        if ((world.PointContents(spot) & Contents.Solid) == 0)
        {
            return;
        }

        spot.Z += 16f;
        if (world.PointContents(spot) != Contents.None)
        {
            return;
        }

        s.Velocity = (ahead * WaterJumpForwardSpeed) + (Vector3.UnitZ * WaterJumpUpSpeed);
        s.WaterJumpTicks = WaterJumpTicks;
    }

    private void WaterJumpMove(ref PlayerState s)
    {
        s.WaterJumpTicks--;
        s.Velocity.Z -= settings.Gravity * Dt;

        // The forward push has to survive hitting the pool's edge, or it's gone by the time
        // the player has risen high enough to use it.
        var forward = new Vector2(s.Velocity.X, s.Velocity.Y);
        SlideMove(ref s);
        s.Velocity.X = forward.X;
        s.Velocity.Y = forward.Y;
    }

    private void LadderMove(ref PlayerState s, in MoveCommand cmd)
    {
        ApplyFriction(ref s);

        Vector3 wish = ((cmd.FlatForward * cmd.Forward) + (cmd.FlatRight * cmd.Side)) * settings.RunSpeed;
        wish.X = Math.Clamp(wish.X, -LadderSideSpeed, LadderSideSpeed);
        wish.Y = Math.Clamp(wish.Y, -LadderSideSpeed, LadderSideSpeed);

        if (cmd.Jump || (cmd.Forward > 0 && cmd.Pitch <= -LadderLookPitch))
        {
            wish.Z = LadderClimbSpeed;
        }
        else if (cmd.Crouch || (cmd.Forward > 0 && cmd.Pitch >= LadderLookPitch))
        {
            wish.Z = -LadderClimbSpeed;
        }

        Accelerate(ref s, wish, settings.Accelerate);
        StepSlide(ref s);
    }

    private void SwimMove(ref PlayerState s, in MoveCommand cmd)
    {
        ApplyFriction(ref s);

        Vector3 wish = ((cmd.Look * cmd.Forward) + (cmd.FlatRight * cmd.Side)) * settings.RunSpeed;
        if (cmd.Jump)
        {
            wish.Z += settings.RunSpeed;
        }
        else if (cmd.Crouch)
        {
            wish.Z -= settings.RunSpeed;
        }
        else if (cmd.Forward == 0 && cmd.Side == 0)
        {
            wish.Z -= SwimDrift;
        }

        Accelerate(ref s, wish * SwimSpeedScale, settings.Accelerate);
        StepSlide(ref s);
    }

    private void GroundMove(ref PlayerState s, in MoveCommand cmd)
    {
        Vector3 wish = WishOnFlat(s, cmd);

        if (s.TouchTicks > 0 && cmd.Jump)
        {
            s.TouchTicks--;
            s.Velocity.Z = 0;
            HopSteer(ref s, wish);
            Move(ref s, wish);
            return;
        }

        s.TouchTicks = 0;

        if (cmd.Jump && !s.Crouched)
        {
            s.Velocity.Z = settings.JumpSpeed;
            s.OnGround = false;
            AirMove(ref s, cmd);
            return;
        }

        ApplyFriction(ref s);
        s.Velocity.Z = 0;
        Accelerate(ref s, wish, settings.Accelerate);
        s.Velocity.Z = 0;
        Move(ref s, wish);
    }

    private void AirMove(ref PlayerState s, in MoveCommand cmd)
    {
        Vector3 wish = WishOnFlat(s, cmd);
        SteerInAir(ref s, wish);
        s.Velocity.Z -= settings.Gravity * Dt;
        Move(ref s, wish);
    }

    /// <summary>The way the keys are pushing, on the flat, at full speed for the player's stance.</summary>
    private Vector3 WishOnFlat(in PlayerState s, in MoveCommand cmd)
    {
        Vector3 wish = (cmd.FlatForward * cmd.Forward) + (cmd.FlatRight * cmd.Side);
        float length = wish.Length();
        if (length < 1e-4f)
        {
            return Vector3.Zero;
        }

        float speed = s.Crouched ? settings.CrouchSpeed : settings.RunSpeed;
        return wish / MathF.Max(length, 1f) * speed;
    }

    private void ApplyFriction(ref PlayerState s)
    {
        float speed = s.Velocity.Length();
        if (speed < 1f)
        {
            s.Velocity.X = 0;
            s.Velocity.Y = 0;
            return;
        }

        float drop = 0f;
        bool grips = s.OnGround && (s.GroundSurface & SurfaceFlags.Slick) == 0;
        if (grips || s.OnLadder)
        {
            drop += MathF.Max(speed, settings.StopSpeed) * settings.Friction * Dt;
        }

        if (s.WaterLevel > 0 && !s.OnLadder)
        {
            drop += speed * WaterFriction * s.WaterLevel * Dt;
        }

        s.Velocity *= MathF.Max(speed - drop, 0f) / speed;
    }

    /// <summary>Accelerates toward a wished velocity without ever passing its speed in that direction.</summary>
    private static void Accelerate(ref PlayerState s, Vector3 wish, float rate)
    {
        float wishSpeed = wish.Length();
        if (wishSpeed < 1e-4f)
        {
            return;
        }

        Vector3 direction = wish / wishSpeed;
        float add = wishSpeed - Vector3.Dot(s.Velocity, direction);
        if (add > 0)
        {
            s.Velocity += direction * MathF.Min(rate * Dt * wishSpeed, add);
        }
    }

    /// <summary>
    /// The ice part. Your heading drifts only slowly toward where you're pushing, and speed
    /// never goes up, except for a tiny nudge when you're almost still.
    /// </summary>
    private void SteerInAir(ref PlayerState s, Vector3 wish)
    {
        if (wish == Vector3.Zero)
        {
            return;
        }

        Turn(ref s, wish, settings.AirSteer * Dt);

        float room = settings.AirNudgeSpeed - s.FlatSpeed;
        if (room > 0)
        {
            Vector3 direction = Vector3.Normalize(wish);
            s.Velocity += direction * MathF.Min(room, settings.Accelerate * Dt * settings.AirNudgeSpeed);
        }
    }

    /// <summary>
    /// The brief touch between hops. No friction applies. Below run speed you simply accelerate;
    /// above it you can only redirect what you've got, and the sharper the turn the more it costs.
    /// </summary>
    private void HopSteer(ref PlayerState s, Vector3 wish)
    {
        if (wish == Vector3.Zero)
        {
            return;
        }

        if (s.FlatSpeed < settings.RunSpeed)
        {
            Accelerate(ref s, wish, settings.Accelerate);
            float speed = s.FlatSpeed;
            if (speed > settings.RunSpeed)
            {
                s.Velocity *= settings.RunSpeed / speed;
            }

            return;
        }

        float turned = Turn(ref s, wish, settings.HopTurnPerTick);
        s.Velocity *= 1f - (settings.HopTurnCost * turned / 180f);
    }

    /// <summary>Swings flat velocity toward a direction and returns how many degrees it moved.</summary>
    private static float Turn(ref PlayerState s, Vector3 toward, float maxDegrees)
    {
        var current = new Vector2(s.Velocity.X, s.Velocity.Y);
        if (current.LengthSquared() < 1e-4f)
        {
            return 0f;
        }

        var target = new Vector2(toward.X, toward.Y);
        float cross = (current.X * target.Y) - (current.Y * target.X);
        float angle = float.RadiansToDegrees(MathF.Atan2(cross, Vector2.Dot(current, target)));
        float turn = Math.Clamp(angle, -maxDegrees, maxDegrees);

        float radians = float.DegreesToRadians(turn);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        s.Velocity.X = (current.X * cos) - (current.Y * sin);
        s.Velocity.Y = (current.X * sin) + (current.Y * cos);
        return MathF.Abs(turn);
    }

    /// <summary>Moves on the ground or in the air, with wall grace applied first.</summary>
    private void Move(ref PlayerState s, Vector3 wish)
    {
        if (TryCornerSlide(ref s))
        {
            return;
        }

        UpdateSqueeze(ref s, wish);
        StepSlide(ref s);
    }

    /// <summary>
    /// Wall grace, part one. If a move is blocked by something that ends just to one side, ease the
    /// player round it instead of stopping them, and leave their velocity alone so no speed is lost.
    /// Offsets run along the surface that was hit, so a plain wall never triggers this: moving
    /// along a flat wall doesn't change how far away it is.
    /// </summary>
    private bool TryCornerSlide(ref PlayerState s)
    {
        if (settings.CornerSlideReach <= 0)
        {
            return false;
        }

        var flat = new Vector3(s.Velocity.X, s.Velocity.Y, 0);
        float speed = flat.Length();
        if (speed < 1f)
        {
            return false;
        }

        Vector3 direction = flat / speed;
        Vector3 move = flat * Dt;
        TraceResult ahead = Sweep(s, s.Position, s.Position + move);
        if (!ahead.Hit || MathF.Abs(ahead.Normal.Z) >= WalkableNormalZ || CanStepOver(s, move))
        {
            return false;
        }

        Vector3 along = Vector3.Cross(ahead.Normal, Vector3.UnitZ);
        if (along.LengthSquared() < 1e-4f)
        {
            return false;
        }

        along = Vector3.Normalize(along);
        float clearance = MathF.Max(move.Length(), CornerClearance);

        for (float offset = CornerProbeStep; offset <= settings.CornerSlideReach; offset += CornerProbeStep)
        {
            foreach (float sign in (ReadOnlySpan<float>)[1f, -1f])
            {
                Vector3 shifted = s.Position + (along * sign * offset);
                if (Sweep(s, s.Position, shifted).Clear && Sweep(s, shifted, shifted + (direction * clearance)).Clear)
                {
                    s.Position += along * sign * MathF.Min(offset, settings.CornerSlideSpeed * Dt);
                    MoveVerticallyOnly(ref s);
                    return true;
                }
            }
        }

        return false;
    }

    private bool CanStepOver(in PlayerState s, Vector3 move)
    {
        TraceResult up = Sweep(s, s.Position, s.Position + (Vector3.UnitZ * settings.StepHeight));
        return Sweep(s, up.End, up.End + move).Clear;
    }

    /// <summary>Lets gravity carry on while horizontal movement is paused by a corner slide.</summary>
    private void MoveVerticallyOnly(ref PlayerState s)
    {
        if (s.OnGround)
        {
            return;
        }

        Vector2 flat = new(s.Velocity.X, s.Velocity.Y);
        s.Velocity.X = 0;
        s.Velocity.Y = 0;
        SlideMove(ref s);
        s.Velocity.X = flat.X;
        s.Velocity.Y = flat.Y;
    }

    /// <summary>
    /// Wall grace, part two. Pressing against something narrows the body if that gets the
    /// player any further. Once nothing's pressing, it springs back as far as there's room.
    /// </summary>
    private void UpdateSqueeze(ref PlayerState s, Vector3 wish)
    {
        float full = PlayerHull.FullHalfWidth;
        float narrowest = full - settings.SqueezeAmount;

        var flat = new Vector3(s.Velocity.X, s.Velocity.Y, 0);
        Vector3 pushing = wish != Vector3.Zero ? wish : flat;
        bool pressing = false;

        if (pushing.LengthSquared() > 1f)
        {
            Vector3 direction = Vector3.Normalize(pushing);
            Vector3 reach = direction * MathF.Max(flat.Length() * Dt, PressProbe);
            TraceResult now = Sweep(s, s.Position, s.Position + reach);
            pressing = now.Hit && MathF.Abs(now.Normal.Z) < WalkableNormalZ && !CanStepOver(s, reach);

            if (pressing && s.HalfWidth > narrowest)
            {
                TraceResult thin = SweepWidth(s, s.Position, s.Position + reach, narrowest);
                if (thin.Fraction > now.Fraction)
                {
                    s.HalfWidth = MathF.Max(narrowest, s.HalfWidth - (settings.SqueezeSpeed * Dt));
                }

                return;
            }
        }

        if (!pressing && s.HalfWidth < full)
        {
            float wider = MathF.Min(full, s.HalfWidth + (settings.SqueezeSpeed * Dt));
            if (!SweepWidth(s, s.Position, s.Position, wider + ICollisionWorld.SurfaceGap).StartSolid)
            {
                s.HalfWidth = wider;
            }
        }
    }

    /// <summary>Slides along the ground, or takes a step up if that gets further.</summary>
    private void StepSlide(ref PlayerState s)
    {
        Vector3 startPosition = s.Position;
        Vector3 startVelocity = s.Velocity;

        SlideMove(ref s);
        Vector3 lowPosition = s.Position;
        Vector3 lowVelocity = s.Velocity;

        TraceResult up = Sweep(s, startPosition, startPosition + (Vector3.UnitZ * settings.StepHeight));
        if (up.AllSolid || up.Fraction <= 0f)
        {
            return;
        }

        s.Position = up.End;
        s.Velocity = startVelocity;
        SlideMove(ref s);

        TraceResult down = Sweep(s, s.Position, s.Position - (Vector3.UnitZ * settings.StepHeight));
        if (!down.AllSolid)
        {
            s.Position = down.End;
        }

        var low = new Vector2(lowPosition.X - startPosition.X, lowPosition.Y - startPosition.Y);
        var high = new Vector2(s.Position.X - startPosition.X, s.Position.Y - startPosition.Y);

        if (low.LengthSquared() > high.LengthSquared() || down.Normal.Z < WalkableNormalZ)
        {
            s.Position = lowPosition;
            s.Velocity = lowVelocity;
            return;
        }

        s.Velocity.Z = lowVelocity.Z;
    }

    /// <summary>Moves along the velocity, sliding along anything hit on the way.</summary>
    private void SlideMove(ref PlayerState s)
    {
        Vector3 original = s.Velocity;
        Span<Vector3> planes = stackalloc Vector3[MaxClipPlanes];
        int planeCount = 0;
        float timeLeft = Dt;

        for (int bump = 0; bump < MaxBumps; bump++)
        {
            TraceResult t = Sweep(s, s.Position, s.Position + (s.Velocity * timeLeft));
            if (t.AllSolid)
            {
                s.Velocity.Z = 0;
                return;
            }

            if (t.Fraction > 0)
            {
                s.Position = t.End;
                planeCount = 0;
            }

            if (t.Fraction >= 1f)
            {
                return;
            }

            timeLeft -= timeLeft * t.Fraction;

            if (planeCount >= MaxClipPlanes)
            {
                s.Velocity = Vector3.Zero;
                return;
            }

            planes[planeCount++] = t.Normal;
            if (!SlideAlongPlanes(ref s.Velocity, planes[..planeCount]))
            {
                s.Velocity = Vector3.Zero;
                return;
            }

            // Reversing means being caught in a sloped corner. Stopping avoids jittering there.
            if (Vector3.Dot(s.Velocity, original) <= 0)
            {
                s.Velocity = Vector3.Zero;
                return;
            }
        }
    }

    /// <summary>Adjusts a velocity to run along every plane touched. False if that can't be done.</summary>
    private static bool SlideAlongPlanes(ref Vector3 velocity, ReadOnlySpan<Vector3> planes)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            Vector3 clipped = Clip(velocity, planes[i]);
            bool fits = true;
            for (int j = 0; j < planes.Length; j++)
            {
                if (j != i && Vector3.Dot(clipped, planes[j]) < 0)
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
            {
                velocity = clipped;
                return true;
            }
        }

        if (planes.Length != 2)
        {
            return false;
        }

        // Caught between two surfaces, so run along the crease where they meet.
        Vector3 crease = Vector3.Cross(planes[0], planes[1]);
        velocity = crease * Vector3.Dot(crease, velocity);
        return true;
    }

    private static Vector3 Clip(Vector3 velocity, Vector3 normal)
    {
        Vector3 result = velocity - (normal * (Vector3.Dot(velocity, normal) * Overbounce));
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(result[axis]) < StopEpsilon)
            {
                result[axis] = 0;
            }
        }

        return result;
    }
}
