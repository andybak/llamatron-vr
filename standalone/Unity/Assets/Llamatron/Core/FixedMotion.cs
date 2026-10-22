namespace Llamatron.Core
{
    // CS:38a2-39e8. Record words store signed 16.16 pairs, low word first.
    public static class FixedMotion
    {
        private static int Read(Entity entity, int offset) => unchecked((ushort)entity[offset] | entity[offset + 2] << 16);
        private static void Write(Entity entity, int offset, int value)
        {
            entity[offset] = unchecked((short)value);
            entity[offset + 2] = unchecked((short)(value >> 16));
        }

        public static void Step(ActorContext context, int index)
        {
            if (context.FreezeTicks != 0) return;
            Entity entity = context.Spawning.Pool.Records[index];
            if (entity[0x30] >= 0) entity[0x30] = unchecked((short)(entity[0x30] - 1));
            else
            {
                var target = context.Target(index);
                int ax = Read(entity, 0x1c), ay = Read(entity, 0x20);
                if (unchecked((short)(entity.X - target.X)) >= 0) ax = unchecked(-ax);
                if (unchecked((short)(entity.Y - target.Y)) >= 0) ay = unchecked(-ay);
                Write(entity, 0x14, unchecked(Read(entity, 0x14) + ax));
                Write(entity, 0x18, unchecked(Read(entity, 0x18) + ay));
            }
            Clamp(entity, 0x14, 0x1c);
            Clamp(entity, 0x18, 0x20);
            Integrate(entity, 0x14, 0x26, 2, 8, 304);
            Integrate(entity, 0x18, 0x2a, 4, 16, 183);
        }

        private static void Clamp(Entity entity, int velocity, int acceleration)
        {
            int value = Read(entity, velocity);
            int magnitude = value < 0 ? unchecked(-value) : value;
            int limit = unchecked(Read(entity, acceleration) << 5);
            // CS:3924/3970 compare signed high words, then always compare low
            // words when high is not greater, including when it is smaller.
            if ((short)(magnitude >> 16) > (short)(limit >> 16) || (ushort)magnitude > (ushort)limit)
                Write(entity, velocity, value < 0 ? unchecked(-limit) : limit);
        }

        private static void Integrate(Entity entity, int velocity, int position, int coordinate, int minimum, int maximum)
        {
            int value = unchecked(Read(entity, position) + Read(entity, velocity));
            short whole = unchecked((short)(value >> 16));
            if (whole <= minimum || whole >= maximum) Write(entity, velocity, 0);
            else { entity[coordinate] = whole; Write(entity, position, value); }
        }
    }
}
