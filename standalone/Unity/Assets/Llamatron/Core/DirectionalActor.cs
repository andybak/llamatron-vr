namespace Llamatron.Core
{
    // CS:3441/3490: eight-direction child with incremental heading changes.
    public static class DirectionalActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:3d43 then 3441. Allocation clears popup but does not decrement free count.
        internal static Entity? Spawn(ActorContext context, Entity parent, short heading, short cadence)
        {
            SpawnContext spawn = context.Spawning;
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child[0x32] = 0; child.RenderState = 18; child[0x0e] = 0;
                child[0x3a] = -1; child.X = parent.X; child.Y = parent.Y;
                Initialize(spawn, child, heading, cadence);
                return child;
            }
            return null;
        }

        public static void Initialize(SpawnContext spawn, Entity entity, short heading, short cadence,
                                      short baseSprite = 310, short sound = 0x0412)
        {
            entity[0x0c] = Word(baseSprite + heading); entity.RenderState = 13;
            entity.VelocityX = 0; entity.VelocityY = 0; entity[0x0e] = 0; entity[0x10] = 1;
            entity[0x14] = 0; entity[0x16] = heading; entity[0x18] = cadence; entity[0x1a] = baseSprite;
            entity[0x3c] = sound; entity[0x3e] = 0; entity.State = 0x2424;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
        }

        public static void Step(ActorContext context, int index)
        {
            if (context.FreezeTicks != 0) return;
            Entity entity = context.Spawning.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            sbyte timer = unchecked((sbyte)((unchecked((ushort)entity[0x18]) >> 8) - 1));
            entity[0x18] = Word((entity[0x18] & 255) | unchecked((byte)timer) << 8);
            int direction = entity[0x16];
            if (timer < 0)
            {
                entity[0x18] = Word((entity[0x18] & 255) * 257);
                var target = context.Target(index);
                int quadrant = (Word(entity.X - target.X) < 0 ? 1 : 0) | (Word(entity.Y - target.Y) < 0 ? 2 : 0);
                short delta = Word(OriginalDirectionalData.Quadrants[quadrant] - direction);
                short magnitude = delta < 0 ? Word(-delta) : delta;
                if ((delta >= 0 && magnitude >= 4) || (delta < 0 && magnitude > 4)) delta = Word(-delta);
                direction = (direction + (delta < 0 ? -1 : 1)) & 7;
                entity[0x16] = (short)direction;
            }
            entity[0x0c] = Word(direction + entity[0x1a]);
            entity.VelocityX = OriginalDirectionalData.X[direction];
            entity.VelocityY = OriginalDirectionalData.Y[direction];
            RecoveredRules.MoveAndBounce(entity);
        }
    }
}
