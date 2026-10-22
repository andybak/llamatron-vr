namespace Llamatron.Core
{
    public static class OrbitActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:2974 / 26a0 enter the shared CS:29af initializer without random position.
        internal static void Initialize(SpawnContext spawn, Entity entity, bool shooter)
        {
            int phase = shooter ? 0 : spawn.OrbitPhase & 255;
            if (!shooter)
            {
                spawn.OrbitAcceleration = 0x00020000;
                spawn.Parameters.SetChaseLimits(1);
            }
            Position(entity, phase);
            entity.RenderState = 13; entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0c] = shooter ? (short)224 : (short)222;
            entity[0x0e] = 0; entity[0x10] = 1;
            entity[0x14] = Word(phase | 0x0100); entity[0x16] = shooter ? (short)20 : (short)10;
            entity[0x18] = shooter ? (short)0x0f0f : (short)0x3030; entity[0x1a] = -1;
            entity[0x3c] = 8; entity[0x3e] = 11;
            spawn.OrbitPhase = Word(spawn.OrbitPhase + spawn.Parameters[0x0820]);
            spawn.HostileCount = Word(spawn.HostileCount + 1);
            entity.State = shooter ? (short)0x2121 : (short)0x2020;
        }

        // CS:29af / 2ab5 use signed bytes, arithmetic half and byte-wrapped quarter turn.
        private static void Position(Entity entity, int phase)
        {
            entity.X = Word(160 + (OriginalOrbitData.Motion[phase & 255] >> 1));
            entity.Y = Word(110 + (OriginalOrbitData.Motion[(phase + 64) & 255] >> 1));
        }

        public static void Step(ActorContext context, int index)
        {
            SpawnContext spawn = context.Spawning;
            Entity entity = spawn.Pool.Records[index];
            int timer = (unchecked((ushort)entity[0x18]) >> 8);
            if (entity.Behavior == 32) // CS:2a75, whole routine is frozen.
            {
                if (context.FreezeTicks != 0) return;
                if (unchecked((sbyte)(timer - 1)) < 0)
                {
                    timer = entity[0x18] & 255;
                    ChainActor.EmitChaser(spawn, entity)[0x3a] = -1;
                }
                else timer = unchecked((byte)(timer - 1));
                spawn.OrbitAcceleration = unchecked(spawn.OrbitAcceleration + 0x40);
                spawn.Parameters.SetChaseLimits(Word((int)(spawn.OrbitAcceleration >> 16)));
                entity[0x0c] = entity[0x1a] >= 0 ? (short)223 : (short)222;
                if (entity[0x1a] >= 0) entity[0x1a] = Word(entity[0x1a] - 1);
            }
            else // CS:2a21, animation and phase continue during freeze; only shots are gated.
            {
                entity[0x0c] = Word(224 + ((timer >> 2) & 3));
                if (entity[0x1a] >= 0) { entity[0x0c] = 228; entity[0x1a] = Word(entity[0x1a] - 1); }
                if (timer == 5 && context.FreezeTicks == 0)
                {
                    var target = context.Target(index);
                    bool left = Word(entity.X - target.X) < 0;
                    short vx = left ? (short)2 : (short)-2;
                    short vy = Word(entity.Y - target.Y) < 0 ? (short)1 : (short)-1;
                    HunterProjectiles.Emit(context, entity, left ? (short)229 : (short)230, vx, vy);
                }
                timer = unchecked((byte)(timer - 1));
                if (unchecked((sbyte)timer) < 0) timer = entity[0x18] & 255;
            }
            entity[0x18] = Word((entity[0x18] & 255) | timer << 8);
            int phase = unchecked((byte)(entity[0x14] + (entity[0x14] >> 8)));
            entity[0x14] = Word((entity[0x14] & 0xff00) | phase);
            Position(entity, phase);
        }
    }
}
