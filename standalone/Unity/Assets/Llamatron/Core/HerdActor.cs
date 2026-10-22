using System;

namespace Llamatron.Core
{
    public static class HerdActor
    {
        private static short Word(int value) => unchecked((short)value);
        private static short AbsWord(short value) => value < 0 ? Word(-value) : value;

        // CS:4c46-4e7e, state 5. Rescue proximity deliberately ignores the
        // player-status gates used by hostile contact, and tests flag 1, not 2.
        public static void Step(ActorContext context, int poolIndex)
        {
            Entity entity = context.Spawning.Pool.Records[poolIndex];
            SpawnContext spawn = context.Spawning;
            entity[0x10] = 0;
            bool Near(short x, short y) => AbsWord(Word(entity.X - x)) < 16 && AbsWord(Word(entity.Y - y)) < 10;
            int contact = Near(spawn.PlayerX, spawn.PlayerY) ? 1 : 0;
            if ((spawn.PlayerFlags & 1) != 0 && Near(spawn.SecondPlayerX, spawn.SecondPlayerY)) contact |= 2;
            if (contact == 0)
            {
                var target = context.Target(poolIndex);
                HerdMotion.Step(entity, spawn.Random, context.FrameCounter, context.LoveTimer, target.X, target.Y);
                return;
            }

            if (spawn.Random.Next() < 40) context.Announcement = 6;
            context.RemainingRescues = Word(context.RemainingRescues - 1);
            if (context.RemainingRescues == 0)
            {
                context.Announcement = 5;
                if ((spawn.PlayerFlags & 1) != 0)
                {
                    context.Announcement = 7;
                    if (spawn.Random.Next() > 40) context.Announcement = 8;
                }
                context.Sound(0x1a, 1, 6);
                context.AwardScore(0, 4);
                context.LastRescuePending = 1;
            }

            if ((spawn.PlayerFlags & 1) != 0 && (contact & 2) != 0)
            {
                if (context.LastRescuePending != 0) context.SecondaryRescueLabelY = Word(spawn.SecondPlayerY - 10);
                context.SecondaryTrailDelay = Word(context.SecondaryTrailDelay + 40);
                entity[0x14] = context.SecondaryTrailDelay;
                entity[0x1e] = 0x70c9;
                if ((spawn.PlayerFlags & 2) != 0) context.AwardScore(24, 6);
                else context.AwardScore(4, 5);
            }
            else
            {
                context.AwardScore(4, 5);
                context.HotBulletTimer = Word(((spawn.PlayerFlags & 2) != 0 ? 40 : 80) >> (context.CycleShift & 31));
                if (context.LastRescuePending != 0) context.PrimaryRescueLabelY = Word(spawn.PlayerY - 10);
                context.PrimaryTrailDelay = Word(context.PrimaryTrailDelay + 40);
                entity[0x14] = context.PrimaryTrailDelay;
                entity[0x1e] = 0x6cc9;
            }
            entity[0x3a] = -1;
            context.LastRescuePending = 0;
            entity[0x36] = 0x45; entity[0x38] = 20;
            entity[0x32] = entity.X; entity[0x34] = entity.Y;
            spawn.HerdCount = Word(spawn.HerdCount - 1);
            context.HotBulletTimer = Word(((spawn.PlayerFlags & 2) != 0 ? 40 : 80) >> (context.CycleShift & 31));
            entity.State = 6;
            context.Sound(0x17, 1, 3);
        }
    }
}
