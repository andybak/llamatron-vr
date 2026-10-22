namespace Llamatron.Core
{
    // CS:382d/37fe -> 3860. Damage receives the absolute coordinate delta in
    // AX, including its bit-1 player selection quirk; do not replace with a mask.
    public static class BeamActor
    {
        private static short Word(int value) => unchecked((short)value);
        private static short Distance(short a, short b)
        {
            short value = Word(a - b); return value < 0 ? Word(-value) : value;
        }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            bool horizontal = entity.Behavior == 17;
            byte active = unchecked((byte)(entity[0x24] >> 8));
            if (active != 0)
            {
                short distance = horizontal ? Distance(entity.Y, context.Spawning.PlayerY) : Distance(entity.X, context.Spawning.PlayerX);
                if (distance < 10) { context.HarmfulCollectible(distance); return; }
                if ((context.Spawning.PlayerFlags & 2) != 0)
                {
                    distance = horizontal ? Distance(entity.Y, context.Spawning.SecondPlayerY) : Distance(entity.X, context.Spawning.SecondPlayerX);
                    if (distance < 10) { context.HarmfulCollectible(distance); return; }
                }
                active--;
                entity[0x24] = Word((entity[0x24] & 255) | active << 8);
            }
            else
            {
                byte delay = unchecked((byte)((entity[0x2e] >> 8) - 1));
                entity[0x2e] = Word((entity[0x2e] & 255) | delay << 8);
                if (delay == 0)
                {
                    entity[0x2e] = Word((entity[0x2e] & 255) * 257);
                    entity[0x24] = Word((entity[0x24] & 255) * 257);
                    context.Sound(8, 0, 7);
                }
            }
            FixedMotion.Step(context, index);
        }
    }
}
