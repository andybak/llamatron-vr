namespace Llamatron.Core
{
    // CS:2865-2973. Alternates axes after a wall rejection or speed underflow.
    public static class AxisDashActor
    {
        private static short Word(int value) => unchecked((short)value);
        private static int Pair(Entity entity, int offset) => unchecked((ushort)entity[offset] | entity[offset + 2] << 16);
        private static void Write(Entity entity, int offset, int value)
        { entity[offset] = Word(value); entity[offset + 2] = Word(value >> 16); }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            if (context.FreezeTicks != 0) return;
            if (entity[0x1a] >= 0)
            {
                entity[0x0c] = Word(((context.FrameCounter >> 1) & 3) + 231);
                entity[0x1a] = Word(entity[0x1a] - 1);
                return;
            }
            bool vertical = entity[0x14] != 0, reverse = entity[0x18] != 0;
            int sprite = (vertical ? 231 : 232) + (reverse ? 0 : 2);
            int offset = vertical ? 0x20 : 0x1c;
            int speed = Pair(entity, 0x24);
            int position = unchecked(Pair(entity, offset) + (reverse ? -speed : speed));
            short whole = Word(position >> 16);
            if ((!reverse && whole >= (vertical ? 190 : 312)) || (reverse && whole < 16))
            { Retarget(context, entity, index); return; }
            entity[vertical ? 4 : 2] = whole; Write(entity, offset, position);
            entity[0x0c] = (short)sprite;
            speed = unchecked(speed - entity[0x28]);
            Write(entity, 0x24, speed);
            if (speed < 0) Retarget(context, entity, index);
        }

        private static void Retarget(ActorContext context, Entity entity, int index)
        {
            RandomSequence random = context.Spawning.Random;
            entity[0x26] = Word((random.Next() & 1) + 1); entity[0x24] = Word(random.Next() << 8);
            entity[0x28] = Word((random.Next() + 1) << 4);
            entity[0x14] = Word(entity[0x14] ^ 1);
            var target = context.Target(index);
            short difference = entity[0x14] != 0 ? Word(entity.Y - target.Y) : Word(entity.X - target.X);
            entity[0x18] = difference < 0 ? (short)0 : (short)1;
        }
    }
}
