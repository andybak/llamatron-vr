using System.Collections.Generic;

namespace Llamatron.Core
{
    public readonly struct ProjectilePattern
    {
        public readonly short Sprite, X, Y;
        public ProjectilePattern(short sprite, short x, short y) { Sprite = sprite; X = x; Y = y; }
    }

    public static class HunterProjectiles
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:3b26. Only the first shot goes through CS:3c84's freeze gate.
        public static void EmitFour(ActorContext context, Entity parent)
        {
            if (context.FreezeTicks == 0) Emit(context, parent, 235, -3, 0);
            short y = context.Spawning.PlayerY < parent.Y ? (short)-1 : (short)1;
            Emit(context, parent, 236, -2, y);
            Emit(context, parent, 237, 2, y);
            Emit(context, parent, 238, 3, 0);
        }

        internal static void Emit(ActorContext context, Entity parent, short sprite, short x, short y)
        {
            Entity? child = context.Spawning.PrepareChild(parent);
            if (child == null) return;
            child[0x0c] = sprite; child.VelocityX = x; child.VelocityY = y;
            child[0x3c] = 0x0307; child.State = 15;
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount + 1);
        }

        // CS:3c12 uses a shared scan budget initialized to 59. Consecutive
        // allocations do not retest that budget until encountering an occupied
        // slot, so the final consecutive shots can extend beyond slot 59.
        public static void Burst(ActorContext context, Entity parent, IReadOnlyList<ProjectilePattern> pattern, short flag)
        {
            context.BurstFlag = flag;
            int cursor = 0, budget = 59;
            Entity[] records = context.Spawning.Pool.Records;
            for (int shot = pattern.Count - 1; shot >= 0; shot--)
            {
                while (records[cursor].Active)
                {
                    cursor++; budget--;
                    if (budget < 0) return;
                }
                Entity child = records[cursor++]; budget--;
                ProjectilePattern p = pattern[shot];
                int shift = context.Spawning.Parameters[0x21ba] & 31;
                child.X = parent.X; child.Y = parent.Y; child.RenderState = 13;
                child[0x0c] = p.Sprite;
                child.VelocityX = Word(p.X << shift); child.VelocityY = Word(p.Y << shift);
                child[0x0e] = 0; child[0x10] = 1; child[0x3a] = -1;
                child[0x3c] = 0x0406; child[0x3e] = 16; child.State = 15;
                context.Spawning.HostileCount = Word(context.Spawning.HostileCount + 1);
                context.Spawning.FreeRecords = Word(context.Spawning.FreeRecords - 1);
            }
        }
    }
}
