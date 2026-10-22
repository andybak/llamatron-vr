namespace Llamatron.Core
{
    // CS:35b4: sprite choice/contact precede the packed-byte firing clock.
    public static class FixedShooterActor
    {
        private static short Word(int value) => unchecked((short)value);

        public static void Step(ActorContext context, int index)
        {
            SpawnContext spawn = context.Spawning;
            Entity entity = spawn.Pool.Records[index];
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            var target = context.Target(index);
            bool right = Word(entity.X - target.X) < 0;
            entity[0x0c] = right ? (short)253 : (short)252;
            sbyte timer = unchecked((sbyte)((unchecked((ushort)entity.VelocityX) >> 8) - 1));
            entity.VelocityX = Word((entity.VelocityX & 255) | unchecked((byte)timer) << 8);
            if (timer < 0)
            {
                entity.VelocityX = Word((entity.VelocityX & 255) * 257);
                if (context.FreezeTicks != 0) return;
                if (spawn.WaveIndex < 50) HunterProjectiles.Emit(context, entity, right ? (short)254 : (short)255, right ? (short)1 : (short)-1, 0);
                else DirectionalActor.Spawn(context, entity, right ? (short)4 : (short)0, 4);
            }
            FixedMotion.Step(context, index);
        }
    }
}
