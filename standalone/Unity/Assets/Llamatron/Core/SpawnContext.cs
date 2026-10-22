using System;

namespace Llamatron.Core
{
    // Shared globals used by CS:1d8d and the spawn handlers. Each member retains
    // its original word semantics. Full wave startup/scheduling remains separate.
    public sealed class SpawnContext
    {
        public EntityPool Pool { get; }
        public RandomSequence Random { get; }
        public WaveParameters Parameters { get; }
        public WaveScriptStore Scripts { get; }
        public uint RampAcceleration = 0x800; // DS:0636 low word, 0638 high word.
        public short RecoilBaseSprite = OriginalRecoilData.InitialBaseSprite; // DS:0824.
        public short SelectedHerdSprite; // DS:0806, last common-factory selection.
        public ChainHistory Chain { get; } = new ChainHistory(); // DS:4481 and 0870.
        public short OrbitPhase = OriginalOrbitData.InitialPhase; // DS:081e.
        public uint OrbitAcceleration = OriginalOrbitData.InitialAcceleration; // DS:081a/081c.
        public short PlayerX = 160, PlayerY = 104;
        public short SecondPlayerX = 180, SecondPlayerY = 104;
        public byte PlayerFlags;
        public short WaveIndex;
        public short DifficultyIncrement; // DS:21ca, independent of shift count DS:233c.
        public short SpriteVariant; // DS:21a8
        public short SpawnSerial; // DS:0814
        public short HerdCount; // DS:091b
        public short HostileCount; // DS:062f
        public short InitialHerdCount; // DS:0804
        public short FreeRecords = 69; // CS:04bc initializes this to 69, despite 70 slots.

        public SpawnContext(EntityPool pool, RandomSequence random, WaveParameters parameters, WaveScriptStore? scripts = null)
        {
            Pool = pool; Random = random; Parameters = parameters; Scripts = scripts ?? new WaveScriptStore();
        }

        private static short Word(int value) => unchecked((short)value);

        // CS:1dd0, after position selection. Chain conversions temporarily use mode 1.
        internal void InitializeHerd(Entity entity, bool quick = false)
        {
            entity.RenderState = 15; entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0c] = SelectedHerdSprite;
            entity[0x0e] = quick || Parameters.Mode != 0 ? (short)2 : (short)50;
            entity[0x10] = 0; entity[0x18] = 0; entity[0x1a] = 0;
            entity[0x14] = 0; entity[0x16] = 0;
            entity[0x3e] = 0; entity[0x3c] = 9; entity[0x30] = SelectedHerdSprite;
            entity.State = 0x0504;
            HerdCount = Word(HerdCount + 1); InitialHerdCount = Word(InitialHerdCount + 1);
        }

        // CS:1edf, shared by the factory and the chain's first-61-slot emitter.
        internal void InitializeChaser(Entity entity)
        {
            entity.RenderState = 1; entity[0x0c] = -1;
            entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0e] = 0; entity[0x10] = 1;
            SpawnSerial = Word(SpawnSerial + 1); entity[0x14] = Word(SpawnSerial & 31);
            entity[0x18] = Word(Parameters[0x21b8] + DifficultyIncrement);
            entity[0x1e] = Parameters[0x21bc]; entity[0x20] = Parameters[0x0812];
            entity[0x3e] = Parameters[0x21b6];
            if (entity[0x3e] == 0) { entity[0x3e] = 3; entity[0x1e] = 0; }
            entity.State = 0x0303; entity[0x3c] = 0x0309;
            int pair = SpriteVariant * 2 + (entity[0x28] & 1);
            entity[0x2c] = Word(OriginalSpawnGraphics.EnemySpritePairs[pair]);
            HostileCount = Word(HostileCount + 1);
        }

        // CS:1c67. X retries the whole X selection; Y retries Y only. The first
        // player's Y exclusion is deliberately one-sided in the original.
        public void Position(Entity entity)
        {
            int attempts = 0;
            short x;
            do
            {
                if (++attempts > 256) throw new InvalidOperationException("Spawn X repeats a rejecting random-table cycle.");
                x = Word(Random.Next() + (Random.Next() & 31) + 16);
            } while (Math.Abs(PlayerX - x) < 32 ||
                     ((PlayerFlags & 1) != 0 && Math.Abs(SecondPlayerX - x) < 32));
            entity.X = x;

            attempts = 0;
            short y;
            do
            {
                if (++attempts > 256) throw new InvalidOperationException("Spawn Y repeats a rejecting random-table cycle.");
                y = Word((Random.Next() & 127) + (Random.Next() & 63) + 32);
            } while ((y > PlayerY && y - PlayerY < 32) ||
                     ((PlayerFlags & 1) != 0 && Math.Abs(SecondPlayerY - y) < 32));
            entity.Y = (short)Math.Max(0, Math.Min(178, (int)y));
        }

        // CS:1d8d-1dcb plus recovered spawn-table entries. A caller advances
        // the original group counter and cadence; this creates one entity only.
        public Entity Create(byte kind)
        {
            if (kind > 40)
                throw new NotSupportedException($"Unported spawn kind {kind}.");
            if ((uint)SpriteVariant >= 27) throw new InvalidOperationException("Sprite variant must be in 0..26.");
            int graphicsChoice = (Parameters.Mode == 0 ? WaveIndex : Random.Next()) & 3;
            SelectedHerdSprite = Word(OriginalSpawnGraphics.HerdBaseSprites[graphicsChoice]);
            Entity entity = PrepareSlot();
            entity[0x32] = 0; // CS:1db8 also clears this after a failed allocation.
            entity[0x3a] = kind; FreeRecords = Word(FreeRecords - 1);
            if (kind != 15 && kind != 21 && kind != 22 && kind != 28 && kind != 40) Position(entity); // Fixed-coordinate factories skip position random draws.
            if (kind == 16) return ChainActor.Create(this, entity);
            entity.VelocityX = 0; entity.VelocityY = 0;

            if (kind == 21 || kind == 22) { OrbitActor.Initialize(this, entity, kind == 22); return entity; }
            if (kind >= 34 && kind <= 37) { EdgeShooterActor.Initialize(this, entity, kind); return entity; }
            if (kind == 26) { DirectionalActor.Initialize(this, entity, 0, 0x0120, 260, 0x031a); return entity; }
            if (kind == 27 || kind == 30 || kind == 32) { PursuitEmitterActor.Initialize(this, entity, kind); return entity; }
            if (kind == 39) { DirectionalActor.Initialize(this, entity, Word(Random.Next() & 7), 0x0104); return entity; }
            if (kind == 40) { DetonationActor.Initialize(this, entity); return entity; }

            if (kind == 0) // CS:1dcd: herd member, delayed state 4 -> 5.
            {
                InitializeHerd(entity);
            }
            else if (kind == 1) // CS:1edc: immediate chase state 3, render state 1.
            {
                InitializeChaser(entity);
            }
            else if (kind == 2) // CS:1f61; deliberately does not increment hostile count.
            {
                entity.RenderState = 12; entity[0x0c] = 43; entity[0x0e] = 2;
                entity[0x14] = -1; entity[0x16] = 0; entity[0x10] = 1;
                entity.State = 0x0807; entity[0x3e] = 1;
            }
            else if (kind == 4) // CS:1f96; packed high-byte movement timer.
            {
                entity[0x3c] = 13; entity.RenderState = 12; entity[0x0c] = 111;
                entity[0x0e] = 2; entity[0x10] = 1; entity.State = 0x0e07;
                entity[0x14] = -2; entity[0x16] = 0; entity[0x18] = 1;
                entity[0x1a] = DifficultyIncrement == 0 ? (short)0x0505 : (short)0x0202;
                entity[0x1c] = entity[0x0c]; entity[0x1e] = 1; entity[0x3e] = 3;
                HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 12) HunterActor.Convert(this, entity); // CS:2404 -> 2407.
            else if ((kind >= 17 && kind <= 20) || kind == 31) // CS:2b01-2b2d.
            {
                RecoilBaseSprite = kind == 31 ? (short)280 : Word(206 + (kind - 17) * 4);
                RecoilActor.Initialize(this, entity);
                if (kind == 31) entity.State = 0x2828;
            }
            else if (kind == 10) // CS:24ef; snapshots shared ramp acceleration at creation.
            {
                entity[0x3c] = 0; entity.RenderState = 12; entity[0x0c] = 137; entity[0x0e] = 20; entity[0x10] = 1;
                entity[0x14] = 0; entity[0x16] = 0; entity[0x18] = 0; entity[0x1a] = 0;
                entity[0x1c] = entity[0x20] = Word((int)RampAcceleration);
                entity[0x1e] = entity[0x22] = Word((int)(RampAcceleration >> 16));
                entity[0x24] = 0x4040; entity[0x26] = 0; entity[0x28] = entity.X;
                entity[0x2a] = 0; entity[0x2c] = entity.Y; entity[0x2e] = 7; entity[0x30] = 20;
                entity[0x3e] = 0; entity.State = 0x1307; HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 11) // CS:25ae; persistent bouncing actor, frozen before contact.
            {
                entity[0x3c] = 5; entity.RenderState = 13;
                entity.VelocityX = Word((Random.Next() & 3) - 1); entity.VelocityY = Word((Random.Next() & 3) - 1);
                entity[0x0c] = 149; entity[0x0e] = 2; entity[0x10] = 1; entity[0x18] = 4;
                entity[0x3e] = 7; entity.State = 0x1414; HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 23) // CS:27e7: alternating-axis dash with signed decay.
            {
                entity.RenderState = 13; entity[0x0e] = 0; entity[0x10] = 1; entity[0x0c] = 231;
                entity[0x16] = 1; entity[0x18] = Word(Random.Next() & 1); entity[0x14] = 0;
                entity[0x1c] = 0; entity[0x1e] = entity.X; entity[0x20] = 0; entity[0x22] = entity.Y;
                entity[0x26] = Word((Random.Next() & 1) + 1); entity[0x24] = Word(Random.Next() << 8);
                entity[0x28] = Word((Random.Next() + 1) << 4);
                entity[0x3e] = 11; entity[0x3c] = 0x040b; entity.State = 0x2222;
                HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 8 || kind == 9) // CS:2612 / 3736: horizontal / vertical beam.
            {
                entity[0x3c] = 15; entity.RenderState = kind == 8 ? (short)2 : (short)3;
                entity[0x0e] = 2; entity[0x10] = 1; entity[0x0c] = kind == 8 ? (short)135 : (short)136;
                entity[0x14] = 0; entity[0x16] = 0; entity[0x18] = 0; entity[0x1a] = 0;
                entity[0x1c] = entity[0x20] = kind == 8 && DifficultyIncrement != 0 ? (short)0x0800 : (short)0x0400;
                entity[0x1e] = 0; entity[0x22] = 0; entity[0x24] = 32;
                entity[0x26] = 0; entity[0x28] = entity.X; entity[0x30] = 0;
                entity[0x2a] = 0; entity[0x2c] = entity.Y;
                if (kind == 9) Random.Next(); // CS:37a4 computes a byte pair but never stores it.
                entity[0x2e] = 0x7070; entity[0x3e] = 6;
                entity.State = kind == 8 ? (short)0x1111 : (short)0x1212;
                // Neither factory increments hostile count.
            }
            else if (kind == 5 || kind == 6 || kind == 7) // CS:24c1, 2493, 24aa -> 1232.
            {
                short sprite = kind == 5 ? (short)123 : kind == 6 ? (short)127 : (short)131;
                short acceleration = kind == 5 ? (short)0x0200 : kind == 6 ? (short)0x0400 : (short)0x0600;
                short damage = kind == 5 ? (short)4 : kind == 6 ? (short)5 : (short)0;
                FixedSplitActor.Initialize(this, entity, entity, sprite, acceleration, damage, 0, 0);
                entity[0x30] = 0; // Factory overrides delay after consuming the random byte.
            }
            else if (kind == 28) // CS:2f0d; does not increment hostile count.
            {
                entity.X = 160; entity.Y = 30; entity[0x0c] = 271; entity.RenderState = 13;
                entity[0x0e] = 0; entity[0x10] = 0; entity[0x16] = 30; entity[0x14] = 0;
                entity[0x18] = 0; entity[0x1a] = 0; entity[0x1c] = 2; entity.State = 0x2626;
            }
            else if (kind == 29) // CS:2f53; contact permanently stops the actor without releasing it.
            {
                entity[0x0c] = 272; entity.RenderState = 13; entity.VelocityX = 1; entity.VelocityY = 2;
                entity[0x0e] = 0; entity[0x10] = 0; entity[0x14] = 0; entity[0x16] = 0x1010;
                entity.State = 0x2727; HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 25) // CS:351e; acceleration and position are 16.16 pairs.
            {
                entity[0x3c] = 30; entity.RenderState = 13; entity[0x0c] = 252;
                entity[0x0e] = 0; entity[0x10] = 1;
                entity[0x14] = 0; entity[0x16] = 0; entity[0x18] = 0; entity[0x1a] = 0;
                entity[0x1c] = Word(Random.Next() | 0x0300); entity[0x1e] = 0;
                entity[0x20] = Word(Random.Next() | 0x0600); entity[0x22] = 0;
                entity[0x24] = 0x1818; entity[0x26] = 0; entity[0x28] = entity.X;
                entity[0x2a] = 0; entity[0x2c] = entity.Y; entity[0x2e] = 0; entity[0x30] = 0;
                entity.VelocityX = Parameters[0x086e]; entity[0x3e] = 12; entity.State = 0x2323;
                HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 14) AxisChaseActor.Initialize(this, entity); // CS:21ac -> 21af.
            else if (kind == 15) // CS:1ff5.
            {
                entity.X = 160; entity.Y = 30; entity[0x3c] = 9; entity.RenderState = 13;
                entity.VelocityY = 1; entity[0x0c] = 178; entity[0x0e] = 0;
                entity[0x10] = 1; entity[0x14] = 1; entity[0x18] = 100;
                entity.State = 0x1818; entity[0x1e] = 3; entity[0x3e] = 3;
                HostileCount = Word(HostileCount + 1);
            }
            else if (kind == 13 || kind == 24 || kind == 38)
            {
                // CS:2347 / 228b / 22e9: child-producing, shooting and alarm hunters.
                entity[0x3c] = kind == 38 ? (short)0x0309 : (short)9;
                entity.RenderState = kind == 13 ? (short)12 : (short)13;
                entity[0x0c] = kind == 13 ? (short)163 : kind == 24 ? (short)79 : (short)299;
                entity[0x0e] = kind == 13 ? (short)40 : (short)0;
                entity[0x10] = 1; entity.State = kind == 13 ? (short)0x1607 : (short)0x0e0e;
                entity[0x14] = -1; entity[0x16] = 0;
                entity[0x18] = kind == 13 ? Parameters[0x0816] : (short)4;
                entity[0x1a] = kind == 13 ? Parameters[0x21c2] : kind == 24 ? (short)0x0202 : (short)0x0404;
                entity[0x1c] = entity[0x0c]; entity[0x1e] = 0;
                entity[0x20] = kind == 13 ? (short)1 : kind == 24 ? (short)2 : (short)3;
                entity[0x22] = Parameters[0x21c0]; entity[0x3e] = 3;
                HostileCount = Word(HostileCount + 1);
            }
            else // CS:1e2c / 1e84: normal / rapid spawner, delayed state 10 -> 11.
            {
                bool rapid = kind == 33;
                entity.RenderState = 12;
                entity.VelocityX = Word((Random.Next() & 3) - 1);
                entity.VelocityY = Word((Random.Next() & 3) - 1);
                entity[0x0c] = rapid ? (short)0x122 : (short)0x3c; entity[0x1c] = entity[0x0c];
                entity[0x0e] = 50; entity[0x10] = 0;
                entity[0x14] = 100; entity[0x16] = rapid ? (short)30 : (short)300;
                entity[0x18] = rapid ? (short)48 : (short)96; entity[0x1a] = rapid ? (short)20 : (short)3;
                entity[0x3e] = 2; entity[0x3c] = 2;
                entity.State = 0x0b0a;
                HostileCount = Word(HostileCount + 1);
            }
            return entity;
        }

        // CS:4f93 decrements the free counter only after finding a slot. The
        // caller still owns the child's state and hostile counter, as in DOS.
        public Entity? PrepareChild(Entity parent)
        {
            Entity? child = Pool.PrepareChild(parent);
            if (child != null) FreeRecords = Word(FreeRecords - 1);
            return child;
        }

        private Entity PrepareSlot()
        {
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity entity = Pool.Records[i];
                if (entity.Active) continue;
                entity[0x32] = 0; entity.RenderState = 18; entity[0x0e] = 0;
                return entity;
            }
            // CS:3d58 returns SI one record beyond the 70-record scan. Scripted
            // factories do not test its failure flag and initialize this record.
            // The allocator itself leaves its fields untouched on failure.
            return Pool.Records[EntityPool.GameplayRecords];
        }

        // CS:1d40-1d8c: eight initial obstacles precede the scripted groups.
        public Entity CreateObstacle()
        {
            Entity entity = PrepareSlot();
            Position(entity);
            entity.RenderState = 12; entity.VelocityX = 0; entity.VelocityY = 0;
            entity[0x0c] = Word(OriginalSpawnGraphics.ObstacleSprites[WaveIndex & 3]);
            entity.State = 0x0100; entity[0x0e] = 50; entity[0x10] = 0;
            entity[0x3c] = 13; entity[0x3e] = 0; entity[0x32] = 0; entity[0x3a] = -1;
            FreeRecords = Word(FreeRecords - 1);
            return entity;
        }
    }
}
