using System;

namespace Llamatron.Core
{
    // One entity invocation in the original ascending pool loop. Unported
    // behavior IDs fail explicitly rather than using a generic enemy fallback.
    public static class ActorDispatcher
    {
        public static void Step(ActorContext context, int poolIndex)
        {
            if ((uint)poolIndex >= EntityPool.GameplayRecords)
                throw new ArgumentOutOfRangeException(nameof(poolIndex));
            Entity entity = context.Spawning.Pool.Records[poolIndex];
            if (!entity.Active) return;
            switch (entity.Behavior)
            {
                case 0: case 2: case 4: case 7: case 9: case 10:
                    if (context.Spawning.Pool.StepLifecycle(poolIndex))
                        context.Spawning.FreeRecords = unchecked((short)(context.Spawning.FreeRecords + 1));
                    return;
                case 1: // CS:43f1: contact only, without the chase freeze gate.
                    Contact(context, entity);
                    return;
                case 3:
                    ChaseActor.Step(context, poolIndex);
                    return;
                case 5:
                    HerdActor.Step(context, poolIndex);
                    return;
                case 6: // CS:4bfb
                    (entity[0x1e] == 0x70c9 ? context.SecondaryTrail : context.PrimaryTrail).Follow(entity);
                    return;
                case 28:
                    PowerupActor.Step(context, poolIndex);
                    return;
                case 29: case 30:
                    ChainActor.Step(context, poolIndex);
                    return;
                case 32: case 33:
                    OrbitActor.Step(context, poolIndex);
                    return;
                case 42:
                    EdgeShooterActor.Step(context, poolIndex);
                    return;
                case 37: case 41:
                    PursuitEmitterActor.Step(context, poolIndex);
                    return;
                case 43: case 44: case 45:
                    DetonationActor.Step(context, poolIndex);
                    return;
                case 11: case 12: case 13:
                    SpawnerActor.Step(context, poolIndex);
                    return;
                case 8: case 14: case 21: case 22:
                    HunterActor.Step(context, poolIndex);
                    return;
                case 23:
                    AxisChaseActor.Step(context, poolIndex);
                    return;
                case 24: case 25: case 26:
                    CenterSpawnerActor.Step(context, poolIndex);
                    return;
                case 35:
                    FixedShooterActor.Step(context, poolIndex);
                    return;
                case 34:
                    AxisDashActor.Step(context, poolIndex);
                    return;
                case 36:
                    DirectionalActor.Step(context, poolIndex);
                    return;
                case 16:
                    FixedSplitActor.Step(context, poolIndex);
                    return;
                case 17: case 18:
                    BeamActor.Step(context, poolIndex);
                    return;
                case 19: case 20:
                    MutableSpawnerActor.Step(context, poolIndex);
                    return;
                case 27:
                    DeathTransforms.StepCardinal(context, poolIndex);
                    return;
                case 31: case 40:
                    RecoilActor.Step(context, poolIndex);
                    return;
                case 38: case 39:
                    ContactEmitterActor.Step(context, poolIndex);
                    return;
                case 15: // CS:3cea: contact, then movement, release on any wall bounce.
                    BouncingProjectile(context, entity);
                    return;
                default: throw new NotSupportedException($"Unported GAME86 state {entity.Behavior}.");
            }
        }

        internal static void BouncingProjectile(ActorContext context, Entity entity)
        {
            if (Contact(context, entity)) return;
            if (!RecoveredRules.MoveAndBounce(entity)) return;
            entity.State = -1;
            context.Spawning.FreeRecords = unchecked((short)(context.Spawning.FreeRecords + 1));
            context.Spawning.HostileCount = unchecked((short)(context.Spawning.HostileCount - 1));
        }

        private static bool Contact(ActorContext context, Entity entity)
        {
            int contact = context.Contact(entity);
            if (contact == 0) return false;
            context.PlayerHit(contact);
            return true;
        }
    }
}
