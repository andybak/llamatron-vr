namespace Llamatron.Core
{
    // CS:2b2d, 2d6e and 2d5a. Growth/facing occur before the movement freeze gate.
    public static class RecoilActor
    {
        private static short Word(int value) => unchecked((short)value);

        internal static void Initialize(SpawnContext spawn, Entity entity, bool deathChild = false)
        {
            entity[0x3c] = 7; entity.RenderState = 13; entity[0x0c] = spawn.RecoilBaseSprite;
            entity.VelocityY = 0; entity[0x0e] = 0; entity[0x10] = 1;
            for (int offset = 0x14; offset <= 0x22; offset += 2) entity[offset] = 0;
            entity[0x24] = -1;
            if (!deathChild) { entity[0x26] = 0; entity[0x2a] = 0; }
            // CS:1370/137a clear the cached whole positions but retain fractions.
            entity[0x28] = deathChild ? (short)0 : entity.X;
            entity[0x2c] = deathChild ? (short)0 : entity.Y;
            entity[0x30] = 0; entity[0x2e] = spawn.Parameters[0x0818];
            entity[0x3e] = 10; entity.VelocityX = entity[0x0c];
            entity.State = deathChild ? (short)0x2828 : (short)0x1f1f;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
        }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            bool fixedAnimation = entity.Behavior == 40;
            int contact = context.Contact(entity);
            if (contact != 0) context.PlayerHit(contact);
            else
            {
                ushort increment = unchecked((ushort)context.Spawning.Parameters[0x0826]);
                Grow(entity, 0x1c, increment); Grow(entity, 0x20, increment);
                var target = context.Target(index);
                short sprite = Word(entity.VelocityX + (Word(entity.X - target.X) < 0 ? 1 : 0));
                if (entity[0x24] >= 0) { sprite = Word(sprite + 2); entity[0x24] = Word(entity[0x24] - 1); }
                entity[0x0c] = sprite;
                FixedMotion.Step(context, index);
            }
            // CS:2d5a calls the common handler and updates this even after contact.
            if (fixedAnimation) entity[0x0c] = Word(((context.FrameCounter >> 2) & 3) + 280);
        }

        private static void Grow(Entity entity, int offset, ushort increment)
        {
            uint value = unchecked((uint)((ushort)entity[offset] | entity[offset + 2] << 16));
            value = unchecked(value + increment);
            entity[offset] = Word((int)value); entity[offset + 2] = Word((int)(value >> 16));
        }
    }
}
