using System;

namespace Llamatron.Core
{
    // CS:220b-228a. DS:2d03/2d05 are selected per record by CS:4336-435b.
    // Contact, history and movement have no freeze-timer gate.
    public static class AxisChaseActor
    {
        private static short Word(int value) => unchecked((short)value);

        // CS:21af. Child callers enter after position selection and deliberately
        // retain popup and spawn-kind fields unless their own code changes them.
        public static void Initialize(SpawnContext context, Entity entity)
        {
            entity[0x3c] = 9; entity.RenderState = 13;
            entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0c] = 175; entity[0x0e] = 0; entity[0x10] = 1;
            entity[0x12] = 0x0aff; entity[0x14] = 0;
            for (int offset = 0x16; offset <= 0x22; offset += 4)
            {
                entity[offset] = entity.X; entity[offset + 2] = entity.Y;
            }
            entity.State = 0x1717; entity[0x3e] = 0;
            context.HostileCount = Word(context.HostileCount + 1);
        }

        public static void Step(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            if (!entity.Active || entity.Behavior != 23)
                throw new ArgumentException("Expected an active axis-chase actor.");
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            entity[0x0c] = Word(((context.FrameCounter >> 1) & 3) + 171);
            int cursor = (entity[0x14] + 4) & 12;
            entity[0x16 + cursor] = entity.X;
            entity[0x18 + cursor] = entity.Y;
            entity[0x14] = (short)cursor;

            int axis = unchecked((byte)entity[0x12]);
            bool choose;
            if (unchecked((sbyte)axis) < 0) { axis = 0; choose = true; }
            else choose = context.Spawning.Random.Next() <= ((unchecked((ushort)entity[0x12]) >> 8) & 255);
            if (choose)
            {
                axis ^= 1;
                entity[0x12] = Word((entity[0x12] & 0xff00) | axis);
                short speed = context.Spawning.Parameters[0x21be];
                var target = context.Target(index);
                if (axis != 0)
                {
                    entity.VelocityY = 0;
                    entity.VelocityX = Word(target.X - entity.X) < 0 ? Word(-speed) : speed;
                }
                else
                {
                    entity.VelocityX = 0;
                    entity.VelocityY = Word(target.Y - entity.Y) < 0 ? Word(-speed) : speed;
                }
            }
            RecoveredRules.MoveAndBounce(entity);
        }
    }
}
