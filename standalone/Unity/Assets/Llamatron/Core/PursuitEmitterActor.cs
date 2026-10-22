using System;

namespace Llamatron.Core
{
    // CS:3155/3178/320a: alternating stationary emission and line-directed movement.
    public static class PursuitEmitterActor
    {
        private static short Word(int value) => unchecked((short)value);

        internal static void Initialize(SpawnContext spawn, Entity entity, byte kind)
        {
            bool pairedShots = kind == 30, pickupEmitter = kind == 32;
            entity[0x0c] = pairedShots ? (short)275 : (short)268;
            entity[0x16] = 0; entity[0x18] = pairedShots ? (short)0x0808 : (short)0x1010;
            entity[0x1a] = pairedShots ? (short)0x0707 : (short)0x0808;
            entity[0x1c] = pairedShots ? (short)0x32d7 : (short)0x33e3;
            entity[0x22] = pairedShots ? Word(0x8080) : (short)0x7070;
            if (pickupEmitter)
            {
                entity[0x0c] = 284; entity[0x18] = 0x0202; entity[0x1a] = 0x0202;
                entity[0x1c] = 0x3119; entity[0x22] = 0x3030;
            }
            entity.RenderState = 13; entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0e] = 0; entity[0x10] = 1; entity[0x14] = 1;
            entity[0x20] = entity[0x0c]; entity[0x24] = -1;
            entity[0x3c] = 0x0306; entity[0x3e] = 13; entity.State = 0x2525;
            spawn.HostileCount = Word(spawn.HostileCount + 1);
            if (pickupEmitter) { entity.State = 0x2929; entity[0x3e] = 0; entity[0x3c] = 0x0200; }
        }

        // Unlike signed-underflow clocks, these source branches trigger exactly at zero.
        private static bool Countdown(Entity entity, int offset)
        {
            int low = entity[offset] & 255;
            byte high = unchecked((byte)((unchecked((ushort)entity[offset]) >> 8) - 1));
            entity[offset] = Word(low | high << 8);
            if (high != 0) return false;
            entity[offset] = Word(low * 257);
            return true;
        }

        public static void Step(ActorContext context, int index)
        {
            if (context.FreezeTicks != 0) return;
            Entity entity = context.Spawning.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            int sprite = entity[0x20];
            if (entity.Behavior == 41) sprite = 284 + ((context.FrameCounter >> 2) & 3);
            else if (entity[0x14] != 0) sprite += 2;
            else if (entity[0x24] >= 0) { entity[0x24] = Word(entity[0x24] - 1); sprite++; }
            entity[0x0c] = Word(sprite);
            if (entity[0x14] != 0)
            {
                if (Countdown(entity, 0x1a)) Emit(context, entity);
                if (!Countdown(entity, 0x18)) return;
                var target = context.Target(index);
                var point = LineMotion.Advance(entity.X, entity.Y, target.X, target.Y, entity[0x16]);
                entity.VelocityX = Word(point.X - entity.X); entity.VelocityY = Word(point.Y - entity.Y);
                entity[0x14] = Word(entity[0x14] ^ 1); entity[0x24] = -1;
                return;
            }
            if (Countdown(entity, 0x22)) { entity[0x14] = Word(entity[0x14] ^ 1); return; }
            RecoveredRules.MoveAndBounce(entity);
        }

        private static void Emit(ActorContext context, Entity parent)
        {
            if (parent[0x1c] == 0x32d7)
            {
                // CS:32d7's second forward scan starts at the first shot. With
                // first-free allocation, this is the next free gameplay slot.
                HunterProjectiles.Emit(context, parent, 278, -2, 0);
                HunterProjectiles.Emit(context, parent, 279, 1, 0);
                return;
            }
            if (parent[0x1c] != 0x33e3 && parent[0x1c] != 0x3119)
                throw new NotSupportedException($"Unported pursuit-emitter child initializer {parent[0x1c]:x4}.");
            SpawnContext spawn = context.Spawning;
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity child = spawn.Pool.Records[i];
                if (child.Active) continue;
                child.RenderState = 18; child[0x0e] = 0; child.X = parent.X; child.Y = parent.Y;
                if (parent[0x1c] == 0x33e3)
                    DirectionalActor.Initialize(spawn, child, 0, 0x0120, 260, 0x031a);
                else // CS:3119, harmful homing pickup. Popup and free counter survive.
                {
                    child[0x14] = 9; child[0x18] = 100; child[0x0c] = 288;
                    child[0x0e] = 0; child[0x10] = 0; child.VelocityX = 0; child.VelocityY = 0;
                    child.RenderState = 13; child.State = 0x1c1c; child[0x16] = 288;
                    spawn.HostileCount = Word(spawn.HostileCount + 1);
                }
                child[0x3a] = -1;
                return; // No popup clearing or free-counter decrement in this path.
            }
        }

    }
}
