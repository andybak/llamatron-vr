using System;

namespace Llamatron.Core
{
    public static class PowerupActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:32fe, BP=0, shared with phased pursuit emitters.
        public static (short X, short Y) LineStep(short x, short y, short targetX, short targetY)
            => LineMotion.Advance(x, y, targetX, targetY, 0);

        // CS:0eb8: replace the dying actor with a collectible message.
        internal static void Announce(ActorContext context, Entity entity)
        {
            if (context.Outcome == 2) return;
            int index = context.Announcement - 1;
            if ((uint)index >= 10) throw new NotSupportedException($"Unknown announcement {context.Announcement}.");
            context.Announcement = 0;
            entity[0x14] = (short)index; entity[0x18] = 200;
            entity[0x0c] = Word(OriginalDamageData.AnnouncementSprites[index]);
            entity.RenderState = 13; entity[0x3a] = -1;
            entity.VelocityX = 0; entity.VelocityY = 0;
            entity.State = 0x1c1c; entity[0x16] = entity[0x0c]; entity[0x10] = 0;
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount + 1);
            context.Sound(0x1a, 3, 4);
        }

        // CS:0f10-0f7d, state 28: homing collectible, animation, contact and expiry.
        public static void Step(ActorContext context, int poolIndex)
        {
            Entity entity = context.Spawning.Pool.Records[poolIndex];
            var target = context.Target(poolIndex);
            var next = LineStep(entity.X, entity.Y, target.X, target.Y);
            entity.VelocityX = Word(next.X - entity.X); entity.VelocityY = Word(next.Y - entity.Y);
            entity[0x18] = Word(entity[0x18] - 1);
            if (entity[0x18] < 0)
            {
                entity.State = -1;
                context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
            }
            else
            {
                entity[0x0c] = Word(((context.FrameCounter >> 3) & 1) + entity[0x16]);
                int contact = context.Contact(entity);
                if (contact != 0)
                {
                    entity.State = -1;
                    context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
                    Collect(context, entity[0x14], contact);
                    return;
                }
            }
            if (((context.FrameCounter >> 1) & 1) == 0) RecoveredRules.MoveAndBounce(entity);
            // The original does not increment DS:0927 on either removal path.
        }

        // CS:0f7e dispatch table and its ten local pickup routines.
        public static void Collect(ActorContext context, int index, int contact)
        {
            switch (index)
            {
                case 0: context.LoveTimer = 800; context.Sound(0x1c, 2, 8); return;
                case 1: context.Outcome = 4; return;
                case 2: context.Lives = Word(context.Lives + 1); context.Sound(0x19, 2, 8); return;
                case 3: context.HotBulletTimer = 50; context.PermanentHotBullets = 1; return;
                case 4:
                    if ((context.Spawning.PlayerFlags & 1) != 0) return;
                    if (context.Spawning.HostileCount != 0) context.ScoreRepeat = 1;
                    else context.Announcement = 5;
                    return;
                case 5: context.TransitionLock = 1; context.RequestPaletteSequence(0x06fd); context.Sound(0x10, 1, 10); return;
                case 6: context.HitLockTicks = 500; context.Sound(0x19, 2, 10); return;
                case 7: context.Ricochet = 1; return;
                case 8: context.AwardScore(1, 4); return;
                case 9: context.HarmfulCollectible(contact); return;
                default: throw new NotSupportedException($"Unknown collectible {index}.");
            }
        }
    }
}
