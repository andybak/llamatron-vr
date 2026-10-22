using System;

namespace Llamatron.Core
{
    // CS:495f and 39e9, plus state 21's proximity-sound wrapper at CS:2457.
    public static class HunterActor
    {
        private static short Word(int value) => unchecked((short)value);
        private static short Abs(short value) => value < 0 ? Word(-value) : value;

        public static void Step(ActorContext context, int index)
        {
            Entity hunter = context.Spawning.Pool.Records[index];
            if (!hunter.Active || (hunter.Behavior != 8 && hunter.Behavior != 14 && hunter.Behavior != 21 && hunter.Behavior != 22))
                throw new ArgumentException("Expected an active hunter-family actor.");
            bool basic = hunter.Behavior == 8;
            if (hunter.Behavior == 22) SpawnAxisChild(context, hunter);
            if (hunter.Behavior == 21)
            {
                var player = context.Target(index);
                short distance = Word(Abs(Word(hunter.X - player.X)) + Abs(Word(hunter.Y - player.Y)));
                if (distance <= 30 && (context.SoundStatus?.Invoke() ?? 0) != 12) context.Sound(30, 2, 12);
            }
            if (context.FreezeTicks != 0) return;
            if (basic)
            {
                if ((context.FrameCounter & 7) != 0) return;
            }
            else
            {
                sbyte timer = unchecked((sbyte)((unchecked((ushort)hunter[0x1a]) >> 8) - 1));
                hunter[0x1a] = Word((hunter[0x1a] & 255) | unchecked((byte)timer) << 8);
                if (timer >= 0) return;
                hunter[0x1a] = Word((hunter[0x1a] & 255) * 257);
            }
            EntityPool pool = context.Spawning.Pool;
            short selected = hunter[0x14];
            if (selected < 0 && selected != -2) { SelectHerd(context, hunter); return; }
            if (selected == -2)
            {
                var player = context.Target(index);
                pool.TargetDummy.X = player.X; pool.TargetDummy.Y = player.Y;
            }

            short vx = hunter.VelocityX, vy = hunter.VelocityY;
            int phase;
            if (Abs(vy) > Abs(vx)) phase = (vy < 0 ? 2 : 4) + ((hunter.Y >> 2) & 1);
            else phase = (vx < 0 ? 0 : 6) + ((hunter.X >> 2) & 1);
            if (basic && hunter[0x16] != 0) { hunter[0x16] = Word(hunter[0x16] - 1); phase += 9; }
            hunter[0x0c] = Word(phase + (basic ? 43 : hunter[0x1c]));

            // CS:4a31 adds AX without the preceding BX scaling being used.
            // The timed handler at CS:3aab instead indexes full 64-byte records.
            int target = selected == -2 ? -Entity.ByteSize : basic ? selected : selected * Entity.ByteSize;
            if (selected != -2 && pool.ReadLayoutByte(target) != 5) { SelectHerd(context, hunter); return; }
            int contact = context.Contact(hunter);
            if (contact != 0) { context.PlayerHit(contact); return; }
            short x = Word(pool.ReadLayoutWord(target + 2) - hunter.X);
            short y = Word(pool.ReadLayoutWord(target + 4) - hunter.Y);
            short ax = Abs(x), ay = Abs(y);
            if (ax < 8 && ay < 8)
            {
                Reach(context, hunter, target, basic, selected == -2);
                return;
            }
            while (ax > 3 || ay > 3)
            {
                x = Word(x >> 1); y = Word(y >> 1);
                ax = Word(ax >> 1); ay = Word(ay >> 1);
            }
            hunter.VelocityX = x; hunter.VelocityY = y;
            hunter.X = Word(hunter.X + x); hunter.Y = Word(hunter.Y + y);
        }

        // CS:23a7 precedes the shared freeze and movement-timer gates. Unlike
        // PrepareChild, this allocation does not clear popup or decrement FreeRecords.
        private static void SpawnAxisChild(ActorContext context, Entity hunter)
        {
            sbyte timer = unchecked((sbyte)((unchecked((ushort)hunter[0x22]) >> 8) - 1));
            hunter[0x22] = Word((hunter[0x22] & 255) | unchecked((byte)timer) << 8);
            if (timer >= 0) return;
            hunter[0x22] = Word((hunter[0x22] & 255) * 257);
            if (context.Spawning.Random.Next() >= 4) return;
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = context.Spawning.Pool.Records[i];
                if (child.Active) continue;
                child.X = hunter.X; child.Y = hunter.Y;
                child.RenderState = 18; child[0x0e] = 0;
                AxisChaseActor.Initialize(context.Spawning, child);
                child[0x3a] = -1;
                context.Sound(7, 1, 4);
                return;
            }
        }

        private static void SelectHerd(ActorContext context, Entity hunter)
        {
            short remaining = context.Spawning.HerdCount;
            if (remaining != 0)
            {
                Entity[] records = context.Spawning.Pool.Records;
                for (int i = 0; i < records.Length; i++)
                {
                    Entity herd = records[i];
                    if (herd.Behavior != 5) continue;
                    if (herd[0x18] == 0) { herd[0x18] = 1; hunter[0x14] = (short)i; return; }
                    remaining = Word(remaining - 1);
                    if (remaining == 0) break;
                }
                if (remaining != 0) throw new InvalidOperationException("Herd counter exceeds the recovered hunter scan layout.");
            }
            hunter[0x14] = -2;
        }

        private static void Reach(ActorContext context, Entity hunter, int target, bool basic, bool dummy)
        {
            EntityPool pool = context.Spawning.Pool;
            if (!dummy)
            {
                int action = basic ? 0 : hunter[0x20];
                if (action == 1)
                {
                    context.Spawning.HerdCount = Word(context.Spawning.HerdCount - 1);
                    Convert(context.Spawning, pool.Records[target / Entity.ByteSize]);
                    context.Sound(26, 0, 3);
                    return;
                }
                if (action == 2) HunterProjectiles.EmitFour(context, hunter);
                else if (action == 3) context.Announcement = 10;
                else if (action != 0) throw new NotSupportedException($"Unported hunter herd action {action}.");
                pool.WriteLayoutWord(target, 9);
                context.Spawning.HerdCount = Word(context.Spawning.HerdCount - 1);
                pool.WriteLayoutWord(target + 0x3a, -1);
            }
            hunter[0x14] = -1;
            pool.WriteLayoutWord(target + 0x0c, 51);
            pool.WriteLayoutWord(target + 0x14, 20);
            if (basic) context.Sound(24, 2, 6);
            else context.Sound(0, 1, 4);
        }

        // CS:2407, called both by spawn kind 12 and when a hunter converts herd.
        // Its original spawn-kind word and unrelated record words survive.
        public static void Convert(SpawnContext context, Entity entity)
        {
            entity[0x3c] = 7; entity.RenderState = 12;
            entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0e] = 8; entity[0x10] = 1; entity[0x0c] = 153;
            entity.State = 0x1507; entity[0x14] = -2; entity[0x16] = 0;
            entity[0x18] = 0; entity[0x1a] = 0x0101; entity[0x1c] = 153;
            entity[0x1e] = 2; entity[0x3e] = 3;
            context.HostileCount = Word(context.HostileCount + 1);
        }
    }
}
