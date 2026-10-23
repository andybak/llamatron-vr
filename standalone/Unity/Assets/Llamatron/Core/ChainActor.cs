using System;

namespace Llamatron.Core
{
    // DS:4481: sixty-four eight-byte records; the unused fourth word is untouched.
    public sealed partial class ChainHistory
    {
        private readonly (short X, short Y, short Sprite)[] entries = new (short, short, short)[64];
        public short Cursor;
        public (short X, short Y, short Sprite) this[int index] => entries[index & 63];
        public void Record(int index, short x, short y, short sprite) => entries[index & 63] = (x, y, sprite);
        public void Fill(short x, short y, short sprite)
        {
            for (int i = 0; i < entries.Length; i++) Record(i, x, y, sprite);
        }
    }

    public static class ChainActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:2def. The original forward scan can exceed the seventy gameplay slots.
        internal static Entity Create(SpawnContext spawn, Entity initial)
        {
            int index = Array.IndexOf(spawn.Pool.Records, initial);
            short x = initial.X, y = initial.Y;
            for (int offset = 32; offset >= 4; offset -= 4)
            {
                Entity follower = Next(spawn, ref index);
                follower.X = x; follower.Y = y; follower.RenderState = 13;
                follower[0x0c] = 198; follower[0x0e] = 0; follower[0x10] = 1;
                follower[0x3a] = -1; follower[0x14] = Word(offset); follower[0x16] = -4;
                follower[0x18] = -1; follower[0x3e] = 9; follower.State = 0x1e1e;
                index++;
            }
            Entity head = Next(spawn, ref index);
            head.X = x; head.Y = y; head[0x3c] = 7; head.RenderState = 13;
            head[0x0c] = 204; head[0x08] = 0; head[0x0a] = 0x3030;
            head[0x0e] = 2; head[0x10] = 1;
            head[0x14] = 0; head[0x16] = 0; head[0x18] = 0; head[0x1a] = 0;
            head[0x1c] = 0x0800; head[0x1e] = 0; head[0x20] = 0x0800; head[0x22] = 0;
            head[0x24] = 0x0404; head[0x26] = 0; head[0x28] = x;
            head[0x2a] = 0; head[0x2c] = y; head[0x2e] = 0; head[0x30] = 0;
            head[0x3e] = 8; head[0x3a] = 16; head.State = 0x1d1d;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
            spawn.Chain.Fill(x, y, 204); // Constructor deliberately retains DS:0870.
            return head;
        }

        private static Entity Next(SpawnContext spawn, ref int index)
        {
            while (index < spawn.Pool.Records.Length && spawn.Pool.Records[index].Active) index++;
            if (index >= spawn.Pool.Records.Length)
                throw new NotSupportedException("Chain constructor exceeded the recovered entity layout.");
            return spawn.Pool.Records[index];
        }

        public static void Step(ActorContext context, int index)
        {
            SpawnContext spawn = context.Spawning;
            Entity entity = spawn.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            if (entity.Behavior == 30) // CS:36f6, contact tests the old position.
            {
                var entry = spawn.Chain[spawn.Chain.Cursor - entity[0x14]];
                entity.X = entry.X; entity.Y = entry.Y;
                int sprite = 198 + (entry.Sprite & 1);
                if (entity[0x18] >= 0) { sprite += 2; entity[0x18] = Word(entity[0x18] - 1); }
                entity[0x0c] = Word(sprite);
                return;
            }
            // CS:36a0: emission and history proceed even when movement is frozen.
            sbyte remaining = unchecked((sbyte)((entity[0x0a] >> 8) - 1));
            entity[0x0a] = Word((entity[0x0a] & 255) | (unchecked((byte)remaining) << 8));
            if (remaining < 0)
            {
                entity[0x0a] = Word((entity[0x0a] & 255) * 257);
                EmitChaser(spawn, entity);
            }
            spawn.Chain.Record(entity[0x08], entity.X, entity.Y, entity[0x0c]);
            entity[0x08] = Word((entity[0x08] + 1) & 63); spawn.Chain.Cursor = entity[0x08];
            entity[0x0c] = entity[0x14] >= 0 ? (short)204 : (short)202;
            FixedMotion.Step(context, index);
        }

        // CS:366f leaves SI at slot 61 after a failed scan. State 32 writes
        // that record's kind unconditionally, even though no child was created.
        internal static Entity EmitChaser(SpawnContext spawn, Entity parent)
        {
            for (int i = 0; i < 61; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child.X = parent.X; child.Y = parent.Y; child.RenderState = 18; child[0x0e] = 0;
                spawn.InitializeChaser(child); child[0x3a] = 1;
                return child;
            }
            return spawn.Pool.Records[61];
        }

        // CS:115d: scan order may encounter a head before any surviving follower.
        internal static void Damage(ActorContext context, Entity head, Bullet bullet)
        {
            context.RequestPaletteSequence(0x0707); context.Sound(23, 0, 4);
            sbyte health = unchecked((sbyte)((head[0x24] >> 8) - 1));
            head[0x24] = Word((head[0x24] & 255) | (unchecked((byte)health) << 8));
            if (health >= 0) { DamageRules.FixedImpulse(context, head, bullet); return; }
            head[0x24] = Word((head[0x24] & 255) * 257);
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity candidate = context.Spawning.Pool.Records[i];
                if (candidate.State == 0x1e1e) { context.Spawning.InitializeHerd(candidate, true); return; }
                if (candidate.State == 0x1d1d) break;
            }
            head.State = -1;
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
        }
    }
}
