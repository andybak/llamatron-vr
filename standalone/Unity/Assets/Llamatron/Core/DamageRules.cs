using System;

namespace Llamatron.Core
{
    public static class DamageRules
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Apply(ActorContext context, Entity entity, Bullet bullet)
        {
            switch (entity[0x3e])
            {
                case 0: case 16: Kill(context, entity, bullet); return;
                case 1: Knockback(context, entity, bullet); return;
                case 8: ChainActor.Damage(context, entity, bullet); return;
                case 9: entity[0x18] = 40; return; // CS:11b0.
                case 17: DetonationActor.Damage(context, entity); return;
                case 13: // CS:31d8, stationary phase is vulnerable only outside freeze.
                    if (context.FreezeTicks == 0 && entity[0x14] != 0) Kill(context, entity, bullet);
                    else
                    {
                        entity[0x24] = 10;
                        entity.VelocityX = Word(bullet.VelocityX >> 1);
                        entity.VelocityY = Word(bullet.VelocityY >> 1);
                        context.Sound(7, 3, 4);
                    }
                    return;
                case 10: // CS:2da7 uses projectile Y and VX in LOW velocity words.
                    entity[0x2e] = Word(entity[0x2e] - 1);
                    if (entity[0x2e] < 0) { Kill(context, entity, bullet); return; }
                    context.RequestPaletteSequence(0x0707);
                    entity[0x14] = Word(bullet.Y << 2); entity[0x18] = Word(bullet.VelocityX << 2);
                    entity[0x1c] = 0x0200; entity[0x1e] = 0; entity[0x20] = 0x0200; entity[0x22] = 0;
                    entity[0x24] = 20; context.Sound(7, 1, 3); return;
                case 14: DeathTransforms.Recoil(context, entity); Kill(context, entity, bullet); return;
                case 15:
                    context.RequestPaletteSequence(0x0713); entity[0x24] = Word(entity[0x24] - 1);
                    if (entity[0x24] == 0) Kill(context, entity, bullet);
                    else context.Sound(32, 3, 4);
                    return;
                case 4: case 5: // CS:11d2 / 10f8: hot shots suppress splitting.
                    if (context.HotBulletTimer < 0) FixedSplitActor.Split(context, entity, bullet, entity[0x3e] == 5);
                    Kill(context, entity, bullet);
                    return;
                case 6: // CS:11b6: fixed-point impulse keeps fractional velocity words.
                    FixedImpulse(context, entity, bullet);
                    return;
                case 11: // CS:2ae1: hit pause precedes health and normal death.
                    entity[0x1a] = 16; context.RequestPaletteSequence(0x0707);
                    entity[0x16] = Word(entity[0x16] - 1);
                    if (entity[0x16] < 0) Kill(context, entity, bullet);
                    else context.Sound(4, 1, 3);
                    return;
                case 12: // CS:363c: byte health, no score or free-counter update.
                    byte health = unchecked((byte)((unchecked((ushort)entity[0x24]) >> 8) - 1));
                    entity[0x24] = Word((entity[0x24] & 255) | health << 8);
                    if (health == 0)
                    {
                        entity.State = -1;
                        context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
                        context.RequestPaletteSequence(0x072d); context.Sound(30, 1, 6);
                    }
                    else { context.RequestPaletteSequence(0x0729); context.Sound(30, 3, 4); }
                    return;
                case 7: // CS:1022: health and replacement velocity, without positional knockback.
                    context.Sound(13, 0, 3);
                    entity[0x18] = Word(entity[0x18] - 1);
                    if (entity[0x18] < 0) Kill(context, entity, bullet);
                    else { entity.VelocityX = Word(bullet.VelocityX >> 1); entity.VelocityY = Word(bullet.VelocityY >> 1); }
                    return;
                case 2: // CS:0dbf: spawner popup and conditional pickup award.
                    entity[0x36] = 0x46; entity[0x38] = 20;
                    entity[0x32] = entity.X; entity[0x34] = entity.Y;
                    int random = context.Spawning.Random.Next();
                    int threshold = context.FreezeTicks == 0 ? 60 : 160;
                    if (random < threshold) context.Announcement = context.HotBulletTimer < 0 ? (short)4 : (short)9;
                    else if (random < 140) context.Announcement = context.Ricochet == 0 ? (short)8 : (short)9;
                    Kill(context, entity, bullet);
                    return;
                case 3: // CS:105f; the supported CS:1047 outcome-table entries are below.
                    context.RequestPaletteSequence(0x0707);
                    entity[0x18] = Word(entity[0x18] - 1);
                    if (entity[0x18] >= 0) { Knockback(context, entity, bullet); return; }
                    switch (entity[0x1e])
                    {
                        case 0: break;
                        case 1:
                            if (context.HotBulletTimer < 0) HunterProjectiles.Burst(context, entity, OriginalHunterData.Diagonal, 0);
                            break;
                        case 2:
                            if (context.HotBulletTimer < 0) HunterProjectiles.Burst(context, entity, OriginalHunterData.Horizontal, 0);
                            break;
                        case 3: CenterSpawnerActor.Transform(context, entity); return;
                        case 4: HunterProjectiles.Burst(context, entity, OriginalHunterData.Horizontal, 1); break;
                        case 5:
                            if (context.HotBulletTimer < 0) DeathTransforms.Cardinal(context, entity);
                            break;
                        case 6:
                            if (context.HotBulletTimer < 0) DeathTransforms.Reflected(context, entity, bullet);
                            break;
                        case 7: HunterProjectiles.EmitFour(context, entity); break;
                        case 8:
                            HunterActor.Convert(context.Spawning, entity);
                            context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
                            return;
                        case 9: HunterProjectiles.Burst(context, entity, OriginalHunterData.EightWay, 0); break;
                        case 10: case 11:
                            // CS:3d71/3dac contain loops, but 3d43 returns BP=-1
                            // on success and 3441 preserves it. Thus the next
                            // call to 3d4e terminates after the first child.
                            DirectionalActor.Spawn(context, entity, entity[0x1e] == 10 ? (short)1 : (short)0, 0x2004);
                            break;
                        default: throw new NotSupportedException($"Unported chase damage outcome {entity[0x1e]}.");
                    }
                    Kill(context, entity, bullet);
                    return;
                default: throw new NotSupportedException($"Unported damage handler {entity[0x3e]}.");
            }
        }

        // CS:1074-10af: signed half-shot impulse and five-tick hit display.
        internal static void FixedImpulse(ActorContext context, Entity entity, Bullet bullet)
        {
            entity[0x16] = Word(bullet.VelocityX << 2);
            entity[0x1a] = Word(bullet.VelocityY << 2); entity[0x30] = 5;
            context.Sound(13, 1, 3);
        }

        private static void Knockback(ActorContext context, Entity entity, Bullet bullet)
        {
            short x = Word(entity.X + (bullet.VelocityX >> 1));
            short y = Word(entity.Y + (bullet.VelocityY >> 1));
            if (x >= 8 && x < 312) entity.X = x;
            if (y >= 24 && y < 184) entity.Y = y;
            entity[0x16] = 5;
            context.Sound(0x0d, 1, 3);
        }

        // CS:0dfe-0eb4: shared death, scoring and collectible-message conversion.
        private static void Kill(ActorContext context, Entity entity, Bullet bullet)
        {
            entity[0x14] = OriginalDamageData.DeathRotation[(bullet.Sprite - 29) & 3];
            entity[0x3a] = -1;
            int state = entity.Behavior;
            if (state != 1) context.Spawning.HostileCount = Word(context.Spawning.HostileCount - 1);
            int column = OriginalDamageData.ScoreColumn[state];
            if (column != 0) context.AwardScore(Word(OriginalDamageData.ScoreAx[state]), column);
            entity.State = 2; entity.RenderState = 5; entity[0x10] = 0;
            context.RequestPaletteSequence(0x06c9);
            context.KillCount = Word(context.KillCount + 1);
            if (context.KillCount >= 200)
            {
                short saved = context.Announcement;
                context.KillCount = 0; context.Announcement = 1;
                context.SpecialDropCountdown = Word(context.SpecialDropCountdown - 1);
                if (context.SpecialDropCountdown == 0)
                {
                    if (context.Spawning.WaveIndex < 90) context.Announcement = 2;
                    context.SpecialDropCountdown = 2;
                }
                PowerupActor.Announce(context, entity);
                context.Announcement = saved;
            }
            else if (context.Announcement != 0) PowerupActor.Announce(context, entity);
            else context.Sound(unchecked((byte)entity[0x3c]), unchecked((byte)(entity[0x3c] >> 8)), 2);
        }
    }
}
