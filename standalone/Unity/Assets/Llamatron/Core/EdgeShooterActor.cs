namespace Llamatron.Core
{
    public static class EdgeShooterActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:2bb6 / 2c04 / 2c52 / 2c9f, after each factory's random position call.
        internal static void Initialize(SpawnContext spawn, Entity entity, byte kind)
        {
            bool horizontal = kind == 35 || kind == 34;
            if (horizontal)
            {
                entity.Y = kind == 35 ? (short)17 : (short)190;
                entity.VelocityX = 0; entity.VelocityY = kind == 35 ? (short)2 : (short)-2;
            }
            else
            {
                entity.X = kind == 36 ? (short)8 : (short)311;
                entity.VelocityX = kind == 36 ? (short)2 : (short)-2; entity.VelocityY = 0;
            }
            entity[0x3c] = 0x0307; entity.RenderState = 13;
            entity[0x0c] = kind == 34 ? (short)294 : kind == 35 ? (short)296 : kind == 36 ? (short)297 : (short)295;
            entity[0x14] = 0; entity[0x16] = 0; entity[0x18] = 0; entity[0x1a] = 0;
            entity[0x1c] = horizontal ? spawn.Parameters[0x0828] : (short)0;
            entity[0x1e] = horizontal ? spawn.Parameters[0x082a] : (short)0;
            entity[0x20] = horizontal ? (short)0 : spawn.Parameters[0x0828];
            entity[0x22] = horizontal ? (short)0 : spawn.Parameters[0x082a];
            int random = spawn.Random.Next();
            // CS:2ced tests only the low X word, even if the high X word is nonzero.
            int randomizedAxis = entity[0x1c] != 0 ? 0x1c : 0x20;
            entity[randomizedAxis] = Word(unchecked((ushort)entity[randomizedAxis]) | random);
            entity[0x0e] = 0; entity[0x10] = 1; entity[0x24] = spawn.Parameters[0x082e];
            entity[0x26] = 0; entity[0x28] = entity.X; entity[0x2a] = 0; entity[0x2c] = entity.Y;
            entity[0x2e] = spawn.Parameters[0x082c]; entity[0x3e] = 15; entity.State = 0x2a2a;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
            // Delay word +30 and other unspecified fields are intentionally retained.
        }

        // CS:2d35. Contact precedes the timer; freeze gates only the shot helper and movement.
        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            sbyte remaining = unchecked((sbyte)((unchecked((ushort)entity[0x2e]) >> 8) - 1));
            entity[0x2e] = Word((entity[0x2e] & 255) | unchecked((byte)remaining) << 8);
            if (remaining < 0)
            {
                entity[0x2e] = Word((entity[0x2e] & 255) * 257);
                if (context.FreezeTicks == 0)
                    HunterProjectiles.Emit(context, entity, 298, entity.VelocityX, entity.VelocityY);
            }
            FixedMotion.Step(context, index);
        }
    }
}
