using System;

namespace Llamatron.Core
{
    // CS:4ed7, 4feb, 504f: the moving spawner and its two child generations.
    // Contact runs before the parent's freeze gate; child handlers have no gate.
    public static class SpawnerActor
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            switch (entity.Behavior)
            {
                case 11: Parent(context, entity); return;
                case 12: Child(context, entity, index); return;
                case 13: Shot(context, entity); return;
                default: throw new ArgumentException("Expected a spawner-family actor.");
            }
        }

        private static void Parent(ActorContext context, Entity entity)
        {
            SpawnContext spawn = context.Spawning;
            entity[0x0c] = Word(((context.FrameCounter >> 1) & 3) + entity[0x1c]);
            if (context.FreezeTicks != 0) return;
            if (entity[0x16] >= 0) entity[0x16] = Word(entity[0x16] - 1);
            else if (ByteTimer(entity, 0x18) && entity[0x1a] != 0)
            {
                // A full pool consumes this attempt too: the quota decrements
                // before allocation in CS:4f0d.
                entity[0x1a] = Word(entity[0x1a] - 1);
                Entity? child = spawn.PrepareChild(entity);
                if (child != null)
                {
                    child[0x0c] = 0x40; child[0x14] = 0; child[0x16] = 0x3f;
                    child.State = 12; spawn.HostileCount = Word(spawn.HostileCount + 1);
                    context.Sound(8, 1, 3);
                }
            }
            entity[0x14] = Word(entity[0x14] - 1);
            if (entity[0x14] < 0)
            {
                entity.VelocityX = NonzeroRandom(spawn.Random);
                entity.VelocityY = NonzeroRandom(spawn.Random);
                entity[0x14] = Word((spawn.Random.Next() & 31) + 31);
            }
            short x = Word(entity.X + entity.VelocityX), y = Word(entity.Y + entity.VelocityY);
            if (x > 8 && x < 312) entity.X = x;
            if (y > 16 && y < 192) entity.Y = y;
        }

        // Decrement the signed high byte, reloading it from the low byte only
        // when it becomes negative. A word decrement would change both cadence
        // and unrelated bits in these records.
        private static bool ByteTimer(Entity entity, int offset)
        {
            ushort word = unchecked((ushort)entity[offset]);
            sbyte high = unchecked((sbyte)((word >> 8) - 1));
            bool expired = high < 0;
            byte next = expired ? unchecked((byte)word) : unchecked((byte)high);
            entity[offset] = unchecked((short)((next << 8) | (word & 255)));
            return expired;
        }

        private static short NonzeroRandom(RandomSequence random)
        {
            for (int attempt = 0; attempt < 512; attempt++)
            {
                short value = Word((random.Next() & 3) - 1);
                if (value != 0) return value;
            }
            throw new InvalidOperationException("Spawner velocity repeats a rejecting random-table cycle.");
        }

        private static void Child(ActorContext context, Entity entity, int index)
        {
            SpawnContext spawn = context.Spawning;
            if (ByteTimer(entity, 0x16))
            {
                Entity? shot = spawn.PrepareChild(entity);
                if (shot != null)
                {
                    shot[0x0c] = 0x41;
                    Aim(context, entity, shot, index);
                    shot.State = 13;
                    spawn.HostileCount = Word(spawn.HostileCount + 1);
                }
            }
            entity[0x14] = Word(entity[0x14] - 1);
            if (entity[0x14] >= 0) RecoveredRules.MoveAndBounce(entity);
            else
            {
                entity[0x14] = Word((spawn.Random.Next() & 127) + 32);
                Aim(context, entity, entity, index);
            }
        }

        // CS:509b uses the selected player's position and signed arithmetic
        // halving, with independent limits 2 and 1; it does not toggle animation.
        private static void Aim(ActorContext context, Entity parent, Entity output, int index)
        {
            var target = context.Target(index);
            short x = Word(target.X - parent.X), y = Word(target.Y - parent.Y);
            short absX = x < 0 ? Word(-x) : x, absY = y < 0 ? Word(-y) : y;
            while (absX > 2 || absY > 1)
            {
                x = Word(x >> 1); y = Word(y >> 1);
                absX = Word(absX >> 1); absY = Word(absY >> 1);
            }
            output.VelocityX = x; output.VelocityY = y;
        }

        private static void Shot(ActorContext context, Entity entity)
        {
            entity[0x0c] = Word(((context.FrameCounter >> 2) & 3) + 0x41);
            short x = Word(entity.X + entity.VelocityX);
            if (x > 8 && x < 312)
            {
                entity.X = x;
                short y = Word(entity.Y + entity.VelocityY);
                if (y > 16 && y < 192) { entity.Y = y; return; }
            }
            entity.State = -1;
            context.Spawning.FreeRecords = Word(context.Spawning.FreeRecords + 1);
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
        }
    }
}
