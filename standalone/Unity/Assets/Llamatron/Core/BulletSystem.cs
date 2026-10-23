using System;
using System.Collections.Generic;

namespace Llamatron.Core
{
    public sealed class Bullet
    {
        public short X = -1, Y, VelocityX, VelocityY, Sprite, Lifetime;
        public short Word0A, Word0E;
        public bool Active => X >= 0;
    }

    public sealed partial class BulletSystem
    {
        private readonly Bullet[] shots = new Bullet[15];
        public IReadOnlyList<Bullet> Shots { get; }
        public sbyte Cooldown = 8; // 0631
        public short Alternate; // 0632

        public BulletSystem()
        {
            for (int i = 0; i < shots.Length; i++) shots[i] = new Bullet();
            Shots = Array.AsReadOnly(shots);
        }

        // CS:07b7 changes only X, retaining every other record word.
        public void Reset() { foreach (Bullet bullet in shots) bullet.X = -1; }

        // CS:0981-0c03. Both human players share 15 slots. Slot exhaustion in
        // the secondary path returns before the primary attempt, as originally.
        public void Fire(ActorContext context)
        {
            if (context.PrimaryStatus != 0 || context.SecondaryStatus != 0) return;
            Cooldown = unchecked((sbyte)(Cooldown - 1));
            if (Cooldown >= 0) return;
            Cooldown = 8;
            if (!context.SpawnComplete) return;
            if ((context.Spawning.PlayerFlags & 1) != 0)
            {
                Alternate = unchecked((short)(Alternate ^ 1));
                if (!FirePlayer(context, true, Alternate != 0)) return;
            }
            FirePlayer(context, false, Alternate == 0);
        }

        private bool FirePlayer(ActorContext context, bool secondary, bool spreadTurn)
        {
            int slot = FreeFrom(0);
            if (slot < 0) return false;
            PlayerMotionState motion = secondary ? context.SecondaryMotion : context.PrimaryMotion;
            short x = secondary ? context.Spawning.SecondPlayerX : context.Spawning.PlayerX;
            short y = secondary ? context.Spawning.SecondPlayerY : context.Spawning.PlayerY;
            Fill(shots[slot], x, y, motion.FireX, motion.FireY, motion.ProjectileSprite);
            if (spreadTurn && context.ScoreRepeat != 0)
            {
                int left = OriginalInputTables.SpreadLeft[motion.AimMask];
                if (left != 0)
                {
                    slot = FreeFrom(slot);
                    if (slot >= 0)
                    {
                        FillDirection(shots[slot], x, y, left);
                        int right = OriginalInputTables.SpreadRight[motion.AimMask];
                        if (right != 0 && (slot = FreeFrom(slot)) >= 0) FillDirection(shots[slot], x, y, right);
                    }
                }
            }
            context.Sound(0x0e, 0, 1);
            return true;
        }

        private int FreeFrom(int start)
        {
            for (int i = start; i < shots.Length; i++) if (!shots[i].Active) return i;
            return -1;
        }

        private static void Fill(Bullet bullet, short x, short y, short vx, short vy, short sprite)
        {
            bullet.Lifetime = 60; bullet.X = x; bullet.Y = y;
            bullet.VelocityX = vx; bullet.VelocityY = vy; bullet.Sprite = sprite;
        }

        private static void FillDirection(Bullet bullet, short x, short y, int mask) => Fill(bullet,
            x, y, OriginalInputTables.FireX[mask], OriginalInputTables.FireY[mask], OriginalInputTables.ProjectileSprite[mask]);

        // CS:0c28-0da9. Collision alternates by slot/frame parity. A hot bullet
        // survives impact, but still hits only the first colliding actor per tick.
        public void Step(ActorContext context)
        {
            for (int index = 0; index < shots.Length; index++)
            {
                Bullet bullet = shots[index];
                if (!bullet.Active) continue;
                if (context.Ricochet != 0)
                {
                    bullet.Lifetime = unchecked((short)(bullet.Lifetime - 1));
                    if (bullet.Lifetime < 0) { bullet.X = -1; continue; }
                }
                short x = unchecked((short)(bullet.X + bullet.VelocityX));
                if (x <= 8 || x >= 312) { Wall(context, bullet, true); continue; }
                bullet.X = x;
                short y = unchecked((short)(bullet.Y + bullet.VelocityY));
                if (y <= 8 || y >= 192) { Wall(context, bullet, false); continue; }
                bullet.Y = y;
                if ((context.FrameCounter & 1) != (index & 1)) continue;
                for (int actor = 0; actor < EntityPool.GameplayRecords; actor++)
                {
                    Entity entity = context.Spawning.Pool.Records[actor];
                    if (!entity.Active || entity[0x10] == 0) continue;
                    if (context.TransitionLock != 0)
                    {
                        // CS:0cfe-0d2d enters an unconditional remaining-pool
                        // sweep, then clears the flag after the final record.
                        for (int sweep = actor; sweep < EntityPool.GameplayRecords; sweep++)
                        {
                            Entity target = context.Spawning.Pool.Records[sweep];
                            if (target.Active && target[0x10] != 0) DamageRules.Apply(context, target, bullet);
                        }
                        context.TransitionLock = 0;
                        break;
                    }
                    if (AbsWord(unchecked((short)(entity.X - x))) > 12) continue;
                    int height = entity[0x0c] < 0 ? 8 : unchecked((short)OriginalDamageData.SpriteLastY[entity[0x0c]]);
                    if (AbsWord(unchecked((short)(entity.Y - y))) > height) continue;
                    if (context.HotBulletTimer < 0) bullet.X = -1;
                    if (entity.Behavior != 19) context.SpawnRampCountdown = context.SpawnRampReload;
                    DamageRules.Apply(context, entity, bullet);
                    break;
                }
            }
        }

        private static short AbsWord(short value) => value < 0 ? unchecked((short)-value) : value;

        private static void Wall(ActorContext context, Bullet bullet, bool xWall)
        {
            if (context.Ricochet != 0)
            {
                // Original scan extends through adjacent tables. For actual
                // player sprites 29..32, only the first 16 entries can match:
                // no later compared sprite byte is in 29..32. The recovery tool
                // verifies that constraint against the full adjacent byte range.
                for (int mask = 0; mask < 16; mask++)
                {
                    if (OriginalInputTables.ProjectileSprite[mask] == 0 ||
                        OriginalInputTables.ProjectileSprite[mask] != bullet.Sprite ||
                        OriginalInputTables.FireX[mask] != unchecked((sbyte)bullet.VelocityX) ||
                        OriginalInputTables.FireY[mask] != unchecked((sbyte)bullet.VelocityY)) continue;
                    int reflected = xWall ? OriginalInputTables.RicochetX[mask] : OriginalInputTables.RicochetY[mask];
                    bullet.VelocityX = OriginalInputTables.FireX[reflected];
                    bullet.VelocityY = OriginalInputTables.FireY[reflected];
                    bullet.Sprite = OriginalInputTables.ProjectileSprite[reflected];
                    return;
                }
            }
            bullet.X = -1;
        }
    }
}
