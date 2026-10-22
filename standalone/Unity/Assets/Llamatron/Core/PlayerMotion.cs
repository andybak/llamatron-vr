using System;

namespace Llamatron.Core
{
    public sealed class PlayerMotionState
    {
        public short FireX, FireY, ProjectileSprite, AimMask, Facing;
        public short MoveX, MoveY, TrailDirection;
        public short CompanionX, CompanionY;

        public PlayerMotionState(bool secondary)
        {
            FireX = secondary ? OriginalInputTables.SecondaryFireX : OriginalInputTables.PrimaryFireX;
            FireY = secondary ? OriginalInputTables.SecondaryFireY : OriginalInputTables.PrimaryFireY;
            ProjectileSprite = secondary ? OriginalInputTables.SecondaryProjectileSprite : OriginalInputTables.PrimaryProjectileSprite;
            AimMask = secondary ? OriginalInputTables.SecondaryAimMask : OriginalInputTables.PrimaryAimMask;
            Facing = secondary ? OriginalInputTables.SecondaryFacing : OriginalInputTables.PrimaryFacing;
        }
    }

    public static class PlayerMotion
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:16c1-1a00. Input bits: up=1, down=2, left=4, right=8, lock=128.
        // Human single/twin-stick/two-player and the original droid path.
        public static void Step(ActorContext context, byte primary, byte secondary, bool twinStick)
        {
            if (context.ScriptedMode != 0)
                throw new NotSupportedException("Original scripted demo movement is not ported yet.");

            SpawnContext spawn = context.Spawning;
            PlayerMotionState first = context.PrimaryMotion, second = context.SecondaryMotion;
            int moveMask = primary & 15;
            if ((spawn.PlayerFlags & 3) == 3)
            {
                int secondMask = secondary & 15;
                if ((secondary & 128) == 0) Aim(second, secondMask);
                MoveData(second, secondMask);
                MoveSecondary(spawn, second);
            }
            else if ((spawn.PlayerFlags & 1) != 0) context.Droid.Step(context);
            if (twinStick)
            {
                first.AimMask = (short)(secondary & 15);
                Aim(first, secondary & 15);
            }
            else if ((primary & 128) == 0)
            {
                first.AimMask = (short)moveMask;
                Aim(first, moveMask);
            }
            MoveData(first, moveMask);

            // Primary trail recording precedes primary movement; secondary
            // recording follows secondary movement, exactly as in the original.
            TrailPoint companion = context.PrimaryTrail.AtOffset(context.PrimaryTrailDelay);
            first.CompanionX = companion.X; first.CompanionY = companion.Y;
            context.PrimaryTrail.Record(spawn.PlayerX, spawn.PlayerY, first.TrailDirection);
            if ((spawn.PlayerFlags & 1) != 0)
                context.SecondaryTrail.Record(spawn.SecondPlayerX, spawn.SecondPlayerY, second.TrailDirection);
            short x = Word(spawn.PlayerX + first.MoveX), y = Word(spawn.PlayerY + first.MoveY);
            if (x >= 15 && x < 304) spawn.PlayerX = x;
            if (y >= 18 && y < 188) spawn.PlayerY = y;
        }

        private static void Aim(PlayerMotionState state, int mask)
        {
            if (OriginalInputTables.ProjectileSprite[mask] == 0) return;
            state.AimMask = (short)mask;
            state.FireX = OriginalInputTables.FireX[mask];
            state.FireY = OriginalInputTables.FireY[mask];
            state.ProjectileSprite = OriginalInputTables.ProjectileSprite[mask];
        }

        private static void MoveData(PlayerMotionState state, int mask)
        {
            state.MoveX = OriginalInputTables.MoveX[mask]; state.MoveY = OriginalInputTables.MoveY[mask];
            if (OriginalInputTables.Facing[mask] != 0) state.Facing = OriginalInputTables.Facing[mask];
            state.TrailDirection = OriginalInputTables.TrailDirection[mask];
        }

        private static void MoveSecondary(SpawnContext spawn, PlayerMotionState state)
        {
            short x = Word(spawn.SecondPlayerX + state.MoveX), y = Word(spawn.SecondPlayerY + state.MoveY);
            if (x >= 15 && x < 304) spawn.SecondPlayerX = x;
            if (y >= 18 && y < 188) spawn.SecondPlayerY = y;
        }
    }
}
