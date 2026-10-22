using System;

namespace Llamatron.Core
{
    // CS:203f-21ab: center-column parent, expiring shots, and death offspring.
    public static class CenterSpawnerActor
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            if (entity.Behavior == 24) { Parent(context, entity); return; }
            if (entity.Behavior != 25 && entity.Behavior != 26)
                throw new ArgumentException("Expected a center-spawner actor.");
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            if (entity.Behavior == 25)
            {
                entity[0x0c] = Word(((context.FrameCounter >> 2) & 1) + 181);
                ActorDispatcher.BouncingProjectile(context, entity);
            }
            else
            {
                entity[0x0c] = Word(((context.FrameCounter >> 2) & 3) + 183);
                RecoveredRules.MoveAndBounce(entity);
            }
        }

        private static void Parent(ActorContext context, Entity entity)
        {
            SpawnContext spawn = context.Spawning;
            entity.X = 160; entity[0x0c] = Word(entity[0x14] + 179);
            if (spawn.Random.Next() < 5) entity[0x14] ^= 1;
            short y = Word(entity.Y + entity.VelocityY);
            if (y < 16 || y >= 184)
            {
                entity.VelocityY = Word(-entity.VelocityY);
                y = Word(y + entity.VelocityY);
            }
            entity.Y = y;
            if (entity[0x14] != 0 || spawn.Random.Next() >= 20) return;
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child.X = entity.X; child.Y = entity.Y; child.RenderState = 13;
                child[0x0e] = 0; child.VelocityX = RandomX(spawn.Random);
                child[0x0c] = 181; child.VelocityY = Word((spawn.Random.Next() & 3) - 1);
                child[0x0e] = 1; child[0x10] = 1; child[0x3e] = 0;
                child[0x3c] = 7; child[0x3a] = -1; child.State = 0x1919;
                spawn.HostileCount = Word(spawn.HostileCount + 1);
                context.Sound(12, 1, 4);
                return;
            }
        }

        private static short RandomX(RandomSequence random)
        {
            short x;
            do { x = Word((random.Next() & 3) - 1); } while (x == 0);
            return x;
        }

        // CS:212d. Scan budget is checked only while skipping occupied records,
        // so consecutive allocations can extend beyond the first 70 records.
        // No common kill/scoring or free-counter update follows this outcome.
        public static void Transform(ActorContext context, Entity parent)
        {
            SpawnContext spawn = context.Spawning;
            int cursor = 0, budget = 69;
            for (int count = 0; count < 10; count++)
            {
                while (spawn.Pool.Records[cursor].Active)
                {
                    cursor++; budget--;
                    if (budget < 0) { RemoveParent(); return; }
                }
                budget--;
                Entity child = spawn.Pool.Records[cursor++];
                child.X = parent.X; child.Y = parent.Y; child.RenderState = 13;
                child.VelocityX = RandomX(spawn.Random); child[0x0c] = 183;
                child.VelocityY = Word(-(spawn.Random.Next() & 3)); child[0x3c] = 12;
                child[0x0e] = 1; child[0x10] = 1; child[0x3a] = -1;
                child[0x1e] = 4; child[0x18] = 3; child[0x3e] = 7; child.State = 0x1a1a;
                spawn.HostileCount = Word(spawn.HostileCount + 1);
            }
            RemoveParent();

            void RemoveParent()
            {
                parent.State = -1;
                spawn.HostileCount = Word(spawn.HostileCount - 1);
            }
        }
    }
}
