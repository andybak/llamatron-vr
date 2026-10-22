namespace Llamatron.Core
{
    // CS:1232, 1108, 11e2 and 388a. Fixed-motion actors split into the next size.
    public static class FixedSplitActor
    {
        private static short Word(int value) => unchecked((short)value);

        internal static void Initialize(SpawnContext spawn, Entity child, Entity parent,
            short sprite, short acceleration, short damage, short x, short y, Bullet? source = null, bool clearEntityVelocity = true)
        {
            child[0x3c] = 15; child.RenderState = 13;
            // CS:123c writes through SI, not BX. The split callers leave SI
            // pointing at the projectile; ordinary factories use SI=BX=DI.
            if (source != null) { source.Sprite = 0; source.Lifetime = 0; }
            else if (clearEntityVelocity) { child.VelocityX = 0; child.VelocityY = 0; }
            child[0x0e] = 2; child[0x10] = 1; child[0x0c] = sprite;
            child[0x14] = 0; child[0x16] = x; child[0x18] = 0; child[0x1a] = y;
            child.X = parent.X; child.Y = parent.Y;
            child[0x1c] = acceleration; child[0x1e] = 0;
            child[0x20] = acceleration; child[0x22] = 0; child[0x24] = sprite;
            child[0x26] = 0; child[0x28] = child.X;
            child[0x30] = Word((spawn.Random.Next() & 7) + 3);
            child[0x2a] = 0; child[0x2c] = child.Y; child[0x3e] = damage;
            child.State = 0x1010; spawn.HostileCount = Word(spawn.HostileCount + 1);
        }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            entity[0x0c] = Word(((context.FrameCounter >> 2) & 3) + entity[0x24]);
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            FixedMotion.Step(context, index);
        }

        internal static void Split(ActorContext context, Entity parent, Bullet bullet, bool four)
        {
            short x = Word(bullet.VelocityY << 1), y = Word(bullet.VelocityX << 1);
            Emit(context, parent, bullet, x, y, four);
            if (four)
            {
                Emit(context, parent, bullet, Word(-x), y, true);
                Emit(context, parent, bullet, Word(-x), Word(-y), true);
                Emit(context, parent, bullet, x, Word(-y), true);
            }
            else Emit(context, parent, bullet, Word(-x), Word(-y), false);
        }

        private static void Emit(ActorContext context, Entity parent, Bullet bullet, short x, short y, bool four)
        {
            SpawnContext spawn = context.Spawning;
            int index = 0;
            while (index < EntityPool.GameplayRecords && spawn.Pool.Records[index].Active) index++;
            Entity child = spawn.Pool.Records[index];
            if (index < EntityPool.GameplayRecords)
                Initialize(spawn, child, parent, four ? (short)131 : (short)127,
                    four ? (short)0x0600 : (short)0x0400, four ? (short)0 : (short)5, x, y, bullet);
            // CS:1221 returns BX at record 70 on failure. Both callers still
            // write its kind word, without activating it or changing counters.
            child[0x3a] = four ? (short)7 : (short)6;
        }
    }
}
