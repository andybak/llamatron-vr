using System;

namespace Llamatron.Core
{
    // CS:2f87 and 3014. These two actors have distinct contact/freeze semantics.
    public static class ContactEmitterActor
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            if (entity.Behavior == 39) Emitter(context, entity);
            else if (entity.Behavior == 38) Falling(context, entity, index);
            else throw new ArgumentException("Expected a contact-emitter family actor.");
        }

        private static void Emitter(ActorContext context, Entity entity)
        {
            if (entity[0x14] != 0) return;
            if (context.Contact(entity) != 0)
            {
                entity[0x14] = 1; entity[0x0c] = 273;
                context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
                return;
            }
            RecoveredRules.MoveAndBounce(entity);
            sbyte timer = unchecked((sbyte)((unchecked((ushort)entity[0x16]) >> 8) - 1));
            entity[0x16] = Word((entity[0x16] & 255) | unchecked((byte)timer) << 8);
            if (timer >= 0) return;
            entity[0x16] = Word((entity[0x16] & 255) * 257);
            short x = entity.X, y = entity.Y;
            bool side = (context.Spawning.Random.Next() & 1) != 0;
            sbyte sample = unchecked((sbyte)context.Spawning.Random.Next());
            entity.X = side ? (short)303 : Word(sample + 160);
            entity.Y = side ? Word((sample >> 1) + 100) : (short)20;
            try
            {
                if (context.FreezeTicks == 0) HunterProjectiles.Emit(context, entity, 274, -1, 1);
            }
            finally { entity.X = x; entity.Y = y; }
        }

        private static void Falling(ActorContext context, Entity entity, int index)
        {
            if (context.FreezeTicks != 0) return;
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            var target = context.Target(index);
            if ((context.FrameCounter & context.Spawning.Parameters[0x0830]) == 0 && entity[0x16] != 1)
                entity.X = Word(entity.X + (Word(target.X - entity.X) < 0 ? -1 : 1));
            if (entity[0x1c] != 0)
            {
                short distance = Word(target.X - entity.X);
                if (distance < 0) distance = Word(-distance);
                if (distance <= 16 && entity.Y <= target.Y)
                {
                    entity[0x18] = 0; entity[0x1a] = 0; entity[0x1c] = 0;
                }
                if (entity[0x1c] == 2) return;
                if (entity[0x1c] != 0)
                {
                    entity[0x16] = Word(entity[0x16] - 1);
                    if (entity[0x16] <= 30) entity[0x1c] = 2;
                    entity.Y = entity[0x16];
                    return;
                }
            }
            int position = unchecked((ushort)entity[0x14] | entity[0x16] << 16);
            int velocity = unchecked((ushort)entity[0x18] | entity[0x1a] << 16);
            position = unchecked(position + velocity); velocity = unchecked(velocity + 0x2000);
            entity[0x14] = Word(position); entity[0x16] = Word(position >> 16);
            entity[0x18] = Word(velocity); entity[0x1a] = Word(velocity >> 16);
            if (entity[0x16] < 184) entity.Y = entity[0x16];
            else { entity[0x1c] = 1; context.Sound(17, 3, 5); }
        }
    }
}
