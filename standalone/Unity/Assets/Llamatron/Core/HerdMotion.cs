namespace Llamatron.Core
{
    public static class HerdMotion
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:4df3-4e7e: movement/animation after the herd rescue test. This does
        // not replace the full state-5 handler; rescue and scoring run first.
        public static bool Step(Entity entity, RandomSequence random, short frameCounter,
                                short loveTimer, short targetX, short targetY)
        {
            if (loveTimer < 0)
            {
                entity[0x14] = Word(entity[0x14] - 1);
                if (entity[0x14] < 0)
                {
                    entity[0x14] = Word((random.Next() & 15) + 10);
                    int direction = random.Next() & 7;
                    entity.VelocityX = OriginalSpawnGraphics.HerdDirectionX[direction];
                    entity.VelocityY = OriginalSpawnGraphics.HerdDirectionY[direction];
                    entity[0x16] = OriginalSpawnGraphics.HerdAnimation[direction];
                }
            }
            else
            {
                entity.VelocityX = entity.X < targetX ? (short)1 : (short)-1;
                entity[0x16] = entity.X < targetX ? (short)6 : (short)0;
                entity.VelocityY = entity.Y < targetY ? (short)1 : (short)-1;
            }

            bool bounced = false;
            if ((frameCounter & 1) == 0) bounced = RecoveredRules.MoveAndBounce(entity);
            short position = entity.VelocityX == 0 ? entity.Y : entity.X;
            entity[0x0c] = Word(((position >> 2) & 1) + entity[0x16] + entity[0x30]);
            return bounced;
        }
    }
}
