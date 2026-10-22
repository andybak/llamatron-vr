using System;

namespace Llamatron.Core
{
    // CS:256e/25f1, with state 19's initializer at CS:10b0.
    public static class MutableSpawnerActor
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            if (entity.Behavior == 20)
            {
                if (context.FreezeTicks != 0) return;
                entity[0x0c] = Word(((context.FrameCounter >> 1) & 3) + 149);
                int hit = context.Contact(entity);
                if (hit != 0) { context.PlayerHit(hit); return; }
                RecoveredRules.MoveAndBounce(entity);
                return;
            }
            if (entity.Behavior != 19) throw new ArgumentException("Expected mutable-spawner family actor.");
            entity[0x0c] = Word(((context.FrameCounter >> 2) & 7) + 137);
            if (entity[0x30] < 0)
            {
                sbyte timer = unchecked((sbyte)((entity[0x24] >> 8) - 1));
                entity[0x24] = Word((entity[0x24] & 255) | unchecked((byte)timer) << 8);
                if (timer < 0)
                {
                    entity[0x2e] = Word(entity[0x2e] - 1);
                    if (entity[0x2e] >= 0)
                    {
                        entity[0x24] = Word((entity[0x24] & 255) * 257);
                        Emit(context.Spawning, entity);
                        context.Sound(11, 1, 3);
                    }
                }
            }
            int contact = context.Contact(entity);
            if (contact != 0) { context.HarmfulCollectible(contact); return; }
            FixedMotion.Step(context, index);
        }

        private static void Emit(SpawnContext spawn, Entity parent)
        {
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child.RenderState = 18; child[0x0e] = 0;
                // Normal dispatch leaves SI=256e. The initializer clears SI+8/a,
                // which are bytes in wave 22, rather than this child's velocities.
                spawn.Scripts.ClearChildInitializerWrites(0x256e);
                FixedSplitActor.Initialize(spawn, child, parent, 145, 0x0a00, 0, 0, 0, clearEntityVelocity: false);
                child[0x3a] = -1; child[0x3c] = 12;
                return;
            }
        }
    }
}
