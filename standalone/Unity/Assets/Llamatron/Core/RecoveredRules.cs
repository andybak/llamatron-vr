using System;

namespace Llamatron.Core
{
    // Direct source ports. These routines are building blocks, not a complete
    // wave simulation. Unsupported states must never silently become no-ops.
    public static class RecoveredRules
    {
        private static short Word(int value) => unchecked((short)value);
        private static short AbsWord(short value) => value < 0 ? Word(-value) : value;

        // CS:43dc. The high byte contains the state to enter after the spawn delay.
        public static void SpawnDelay(Entity entity)
        {
            entity[0x0e] = Word(entity[0x0e] - 2);
            if (entity[0x0e] >= 0) return;
            int state = unchecked((ushort)entity.State);
            entity.State = Word((state & 0xff00) | (state >> 8));
            entity.RenderState = Word(entity.RenderState + 1);
            entity[0x10] = 1;
        }

        // CS:43c9; global DS:0927 counts available records.
        public static bool ExpandingDeath(Entity entity)
        {
            entity[0x0e] = Word(entity[0x0e] + 1);
            if (entity[0x0e] < 15) return false;
            entity.State = -1;
            return true;
        }

        // CS:4ec8. A zero timer survives until the next decrement becomes negative.
        public static bool TimedDeath(Entity entity)
        {
            entity[0x14] = Word(entity[0x14] - 1);
            if (entity[0x14] >= 0) return false;
            entity.State = -1;
            return true;
        }

        // CS:4b49. Result bits identify contact with the first/second player.
        public static int PlayerContact(Entity entity, short x1, short y1, short status1,
                                        short x2, short y2, short status2, byte playerFlags)
        {
            bool Near(short x, short y) =>
                AbsWord(Word(entity.X - x)) < 16 && AbsWord(Word(entity.Y - y)) < 10;
            int contact = status1 == 0 && Near(x1, y1) ? 1 : 0;
            if (status2 >= 0 && (playerFlags & 2) != 0 && Near(x2, y2)) contact |= 2;
            return contact;
        }

        // CS:4ae9-4b42, shared chase steering; caller handles timer and collisions.
        // Arithmetic shifts deliberately retain the original diagonal quantization.
        public static void AimChase(Entity entity, short targetX, short targetY,
                                    short maxX, short maxY)
        {
            if (maxX < 1 || maxY < 1)
                throw new ArgumentOutOfRangeException(nameof(maxX), "Chase limits must be positive.");
            short x = Word(targetX - entity.X), y = Word(targetY - entity.Y);
            short absX = AbsWord(x), absY = AbsWord(y);
            while (absX > maxX || absY > maxY)
            {
                x = Word(x >> 1); y = Word(y >> 1);
                absX = Word(absX >> 1); absY = Word(absY >> 1);
            }
            entity.VelocityX = x; entity.VelocityY = y;
            entity[0x28] = Word(entity[0x28] ^ 1);
        }

        // CS:4add-4b46, reached only after the freeze/contact gates in state 3.
        // The original returns on non-expiring ticks: velocity is not integrated
        // every frame. The caller selects the player using the pool-loop parity.
        public static bool ChaseMotion(Entity entity, WaveParameters parameters,
                                       short targetX, short targetY)
        {
            entity[0x14] = Word(entity[0x14] - 1);
            if (entity[0x14] >= 0) return false;
            entity[0x14] = parameters[0x21b2];
            AimChase(entity, targetX, targetY, parameters.ChaseMaxX, parameters.ChaseMaxY);
            MoveAndBounce(entity);
            return true;
        }

        // CS:4e7f. Bottom collisions force Y=187, unlike the symmetric X/top bounce.
        public static bool MoveAndBounce(Entity entity)
        {
            short x = Word(entity.X + entity.VelocityX);
            bool bounced = x < 8 || x > 312;
            if (bounced)
            {
                entity.VelocityX = Word(-entity.VelocityX);
                x = Word(entity.X + entity.VelocityX);
                if (x < 8 || x > 312)
                    throw new InvalidOperationException("Original X bounce would loop for this state.");
            }
            entity.X = x;
            short y = Word(entity.Y + entity.VelocityY);
            int attempts = 0;
            while (y < 16 || y > 188)
            {
                bounced = true;
                entity.VelocityY = Word(-entity.VelocityY);
                y = y < 100 ? Word(entity.Y + entity.VelocityY) : (short)187;
                if (++attempts > 2)
                    throw new InvalidOperationException("Original top bounce would loop for this state.");
            }
            entity.Y = y;
            return bounced;
        }
    }
}
