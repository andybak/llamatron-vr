namespace Llamatron.Core
{
    // CS:176c-181a and 480e-495e: first-herd pursuit and quadrant firing.
    public sealed partial class DroidControl
    {
        public short AimCountdown = OriginalDroidData.InitialAimCountdown; // DS:0682.
        public short TargetRecord = OriginalDroidData.InitialTargetRecord; // DS:0919.
        public short RandomTargetX = 160, RandomTargetY = 100; // DS:06a8/06aa.
        private readonly byte[] quadrants = new byte[4]; // DS:409c.
        private static short Word(int value) => unchecked((short)value);

        internal void Step(ActorContext context)
        {
            Follow(context);
            AimCountdown = Word(AimCountdown - 1);
            if (AimCountdown >= 0) return;
            AimCountdown = 20;
            for (int i = 0; i < quadrants.Length; i++) quadrants[i] = 0;
            Count(context, 0, 70);
            int quadrant = Largest();
            if (quadrant < 0)
            {
                SpawnContext spawn = context.Spawning;
                quadrant = Word(spawn.SecondPlayerX - spawn.PlayerX) < 0 ? 1 : 0;
                if (Word(spawn.SecondPlayerY - spawn.PlayerY) < 0) quadrant += 2;
                else
                {
                    // CS:17ef jumps back into 17af, increments this bucket and
                    // scans records 71..74 before selecting again. Record 70 is skipped.
                    quadrants[quadrant]++;
                    Count(context, 71, 4);
                    quadrant = Largest();
                }
            }
            PlayerMotionState state = context.SecondaryMotion;
            state.FireX = OriginalDroidData.FireX[quadrant]; state.FireY = OriginalDroidData.FireY[quadrant];
            state.ProjectileSprite = OriginalDroidData.Sprites[quadrant]; state.AimMask = OriginalDroidData.AimMasks[quadrant];
        }

        private int Largest()
        {
            int selected = -1, maximum = 0;
            for (int i = 0; i < 4; i++)
                if (quadrants[i] > maximum) { selected = i; maximum = quadrants[i]; }
            return selected;
        }

        private void Count(ActorContext context, int first, int count)
        {
            SpawnContext spawn = context.Spawning;
            for (int i = first; i < first + count; i++)
            {
                Entity entity = spawn.Pool.Records[i];
                if (!entity.Active || entity[0x10] == 0) continue;
                int quadrant = Word(spawn.SecondPlayerX - entity.X) < 0 ? 1 : 0;
                if (Word(spawn.SecondPlayerY - entity.Y) < 0) quadrant += 2;
                quadrants[quadrant] = unchecked((byte)(quadrants[quadrant] + 1));
            }
        }

        private void Follow(ActorContext context)
        {
            if (context.FreezeTicks != 0) { TargetRecord = -1; return; }
            if ((context.FrameCounter & 1) != 0) return;
            SpawnContext spawn = context.Spawning;
            if (TargetRecord < 0)
            {
                Entity dummy = spawn.Pool.TargetDummy;
                dummy.X = context.PrimaryMotion.CompanionX; dummy.Y = context.PrimaryMotion.CompanionY;
                if (context.ScriptedMode != 0)
                {
                    if ((context.FrameCounter & 63) == 0)
                    {
                        RandomTargetX = Word(160 + unchecked((sbyte)spawn.Random.Next()));
                        RandomTargetY = Word(100 + (unchecked((sbyte)spawn.Random.Next()) >> 1));
                    }
                    dummy.X = RandomTargetX; dummy.Y = RandomTargetY;
                }
                if (FindHerd(spawn)) return;
            }
            else if (spawn.Pool.Records[TargetRecord].Behavior != 5)
            {
                TargetRecord = -1;
                if (FindHerd(spawn)) return;
            }
            Entity target = TargetRecord == -2 ? spawn.Pool.TargetDummy : spawn.Pool.Records[TargetRecord];
            short x = Word(target.X - spawn.SecondPlayerX), y = Word(target.Y - spawn.SecondPlayerY);
            short absX = x < 0 ? Word(-x) : x, absY = y < 0 ? Word(-y) : y;
            while (absX > 2 || absY > 1)
            {
                x = Word(x >> 1); y = Word(y >> 1); absX = Word(absX >> 1); absY = Word(absY >> 1);
            }
            PlayerMotionState state = context.SecondaryMotion;
            state.MoveX = x; state.MoveY = y;
            state.Facing = x < 0 ? (short)5 : (short)1;
            state.TrailDirection = x < 0 ? (short)0 : (short)6;
            spawn.SecondPlayerX = Word(spawn.SecondPlayerX + x); spawn.SecondPlayerY = Word(spawn.SecondPlayerY + y);
        }

        private bool FindHerd(SpawnContext spawn)
        {
            for (int i = 0; i < 69; i++)
                if (spawn.Pool.Records[i].Behavior == 5) { TargetRecord = (short)i; return true; }
            TargetRecord = -2;
            return false;
        }
    }
}
