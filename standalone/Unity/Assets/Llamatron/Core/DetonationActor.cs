namespace Llamatron.Core
{
    // CS:26c5-27e6. The dormant state is intentionally a RET in the original.
    public static class DetonationActor
    {
        private static short Word(int value) => unchecked((short)value);

        internal static void Initialize(SpawnContext spawn, Entity entity)
        {
            entity.RenderState = 17; entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0c] = 326; entity[0x0e] = 0; entity[0x3c] = 0x041e;
            entity[0x3e] = 17; entity[0x10] = 1; entity[0x14] = 0x2828; entity[0x16] = 50;
            entity.X = 160; entity.Y = 100; entity.State = 0x2b2b;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
        }

        internal static void Damage(ActorContext context, Entity entity)
        {
            context.RequestPaletteSequence(0x0713); context.Sound(30, 4, 4);
            entity[0x16] = Word(entity[0x16] - 1);
            if (entity[0x16] >= 0) return;
            entity.RenderState = 18; entity[0x14] = 150; entity[0x16] = 0x0404; entity.State = 0x2c2c;
        }

        private static bool Clock(Entity entity, int offset)
        {
            sbyte high = unchecked((sbyte)((unchecked((ushort)entity[offset]) >> 8) - 1));
            entity[offset] = Word((entity[offset] & 255) | unchecked((byte)high) << 8);
            if (high >= 0) return false;
            entity[offset] = Word((entity[offset] & 255) * 257);
            return true;
        }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            if (entity.Behavior == 43) return;
            if (entity.Behavior == 45)
            {
                if (!Clock(entity, 0x14)) return;
                short sprite = Word(entity[0x0c] + 1);
                if (sprite != 330) { entity[0x0c] = sprite; return; }
                Release(context, entity); return;
            }
            if (!Clock(entity, 0x16)) return;
            context.RequestPaletteSequence(0x0721); context.Sound(17, 3, 10);
            entity[0x14] = Word(entity[0x14] - 1);
            if (entity[0x14] < 0) { Release(context, entity); return; }
            SpawnContext spawn = context.Spawning;
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child[0x32] = 0; child.RenderState = 18; child[0x0e] = 0;
                child[0x3a] = -1; child[0x10] = 0;
                child.X = Word(160 + unchecked((sbyte)spawn.Random.Next()));
                child.Y = Word(100 + (unchecked((sbyte)spawn.Random.Next()) >> 1));
                child.RenderState = 13; child[0x0c] = 327; child[0x14] = 0x0303; child.State = 0x2d2d;
                spawn.HostileCount = Word(spawn.HostileCount + 1);
                return;
            }
        }

        private static void Release(ActorContext context, Entity entity)
        {
            entity.State = -1;
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
        }
    }
}
