using System;

namespace Llamatron.Core
{
    public sealed class EntityPool
    {
        public const int InitializedRecords = 100;
        public const int GameplayRecords = 70;
        public Entity[] Records { get; } = new Entity[InitializedRecords];
        // DS:4881, immediately before the records: a shared player-target dummy.
        public Entity TargetDummy { get; } = new Entity();

        // State 8's original target lookup uses an unscaled byte offset. Keep
        // that observable quirk, including unaligned words, within this logical
        // record layout rather than silently substituting a record index.
        private (Entity Entity, int Local) LayoutByte(int offset)
        {
            if (offset < -Entity.ByteSize || offset >= InitializedRecords * Entity.ByteSize)
                throw new InvalidOperationException("Hunter target is outside the recovered record layout.");
            return offset < 0 ? (TargetDummy, offset + Entity.ByteSize) : (Records[offset / Entity.ByteSize], offset % Entity.ByteSize);
        }

        internal byte ReadLayoutByte(int offset)
        {
            var location = LayoutByte(offset);
            return unchecked((byte)(location.Entity[location.Local & ~1] >> ((location.Local & 1) * 8)));
        }

        internal short ReadLayoutWord(int offset) => unchecked((short)(ReadLayoutByte(offset) | ReadLayoutByte(offset + 1) << 8));

        private void WriteLayoutByte(int offset, byte value)
        {
            var location = LayoutByte(offset);
            int shift = (location.Local & 1) * 8;
            short old = location.Entity[location.Local & ~1];
            location.Entity[location.Local & ~1] = unchecked((short)((old & ~(255 << shift)) | value << shift));
        }

        internal void WriteLayoutWord(int offset, short value)
        {
            WriteLayoutByte(offset, unchecked((byte)value));
            WriteLayoutByte(offset + 1, unchecked((byte)(value >> 8)));
        }

        public EntityPool()
        {
            for (int i = 0; i < Records.Length; i++) Records[i] = new Entity();
            Reset();
        }

        // CS:4307 initializes 100 records even though gameplay scans only 70.
        // The reset leaves all other words intact, including position/velocity.
        public void Reset()
        {
            foreach (Entity entity in Records)
            {
                entity.State = -1;
                entity.RenderState = 18;
                entity[0x0e] = 0; entity[0x10] = 0; entity[0x32] = 0;
            }
        }

        // CS:4f93. Allocation is first-free in ascending memory order. The caller
        // chooses state and sprite afterwards; claiming state here would differ.
        public Entity? PrepareChild(Entity parent)
        {
            for (int i = 0; i < GameplayRecords; i++)
            {
                Entity child = Records[i];
                if (child.Active) continue;
                child.X = parent.X; child.Y = parent.Y;
                child.VelocityX = 0; child.VelocityY = 0;
                child[0x0e] = 0; child[0x10] = 1;
                child[0x3c] = 9; child[0x3e] = 0;
                child.RenderState = 13; child[0x32] = 0; child[0x3a] = -1;
                return child;
            }
            return null;
        }

        // Complete lifecycle handlers only. Remaining gameplay handlers are an
        // explicit reconstruction obligation, never a generic enemy fallback.
        public bool StepLifecycle(int index)
        {
            if ((uint)index >= GameplayRecords) throw new ArgumentOutOfRangeException(nameof(index));
            Entity entity = Records[index];
            if (!entity.Active) return false;
            switch (entity.Behavior)
            {
                case 0: case 4: case 7: case 10:
                    RecoveredRules.SpawnDelay(entity); return false;
                case 2: return RecoveredRules.ExpandingDeath(entity);
                case 9: return RecoveredRules.TimedDeath(entity);
                default: throw new NotSupportedException($"Unported GAME86 state {entity.Behavior}.");
            }
        }
    }
}
