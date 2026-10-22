namespace Llamatron.Core
{
    // CS:1416, 13a6 and 12ed. These allocation paths retain popup fields and
    // do not decrement the free counter; their hostile-count effects differ.
    public static class DeathTransforms
    {
        private static short Word(int value) => unchecked((short)value);
        private static Entity? Find(SpawnContext spawn)
        {
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
                if (!spawn.Pool.Records[i].Active) return spawn.Pool.Records[i];
            return null;
        }

        public static void Cardinal(ActorContext context, Entity parent)
        {
            Entity? child = Find(context.Spawning);
            if (child == null) return;
            child.X = parent.X; child.Y = parent.Y; child.RenderState = 13;
            byte sample;
            do { sample = context.Spawning.Random.Next(); } while ((sample & 1) == 0);
            short speed = (sample & 2) == 0 ? (short)1 : (short)-1;
            child.VelocityX = (sample & 4) != 0 ? speed : (short)0;
            child.VelocityY = (sample & 4) == 0 ? speed : (short)0;
            child[0x0c] = 191; child[0x0e] = 0; child[0x10] = 0;
            child[0x3a] = 1; child.State = 0x1b1b;
            // Deliberately no hostile increment for this persistent hazard.
        }

        public static void Reflected(ActorContext context, Entity parent, Bullet bullet)
        {
            Entity? child = Find(context.Spawning);
            if (child == null) return;
            child.X = parent.X; child.Y = parent.Y;
            child.VelocityX = Word(Word(-bullet.VelocityX) >> 1);
            child.VelocityY = Word(Word(-bullet.VelocityY) >> 1);
            child[0x0c] = Word(bullet.Sprite + 163); child.RenderState = 13;
            child[0x0e] = 0; child[0x10] = 0; child.State = 0x0f0f; child[0x3a] = -1;
            context.Spawning.HostileCount = Word(context.Spawning.HostileCount + 1);
        }

        public static void Recoil(ActorContext context, Entity parent)
        {
            Entity? child = Find(context.Spawning);
            if (child == null) return;
            child.X = parent.X; child.Y = parent.Y;
            RecoilActor.Initialize(context.Spawning, child, true);
            child[0x3a] = -1;
        }

        public static void StepCardinal(ActorContext context, int index)
        {
            Entity entity = context.Spawning.Pool.Records[index];
            RecoveredRules.MoveAndBounce(entity);
            int contact = context.Contact(entity);
            if (contact != 0) context.HarmfulCollectible(contact);
        }
    }
}
