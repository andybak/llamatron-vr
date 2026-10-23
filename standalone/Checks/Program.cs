using System;
using Llamatron.Core;

static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] {message}");
    }

    public static void Main()
    {
        PresentationChecks.Run();
        TitleRevealChecks.Run();
        SaveChecks.Run();
        ChainChecks.Run();
        OrbitChecks.Run();
        EdgeShooterChecks.Run();
        PursuitEmitterChecks.Run();
        RemainingActorChecks.Run();
        RemainingRenderChecks.Run();
        WaveCoverageChecks.Run();
        SoundChecks.Run();
        PaletteChecks.Run();
        DroidChecks.Run();
        AlternatingChecks.Run();
        DemoChecks.Run();
        var entity = new Entity();
        var bytes = new byte[Entity.ByteSize];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 5);
        entity.Read(bytes);
        var roundTrip = new byte[Entity.ByteSize];
        entity.Write(roundTrip);
        Require(bytes.AsSpan().SequenceEqual(roundTrip), "64-byte signed record round trip");

        entity.State = 0x0300; entity.RenderState = 3; entity[0x0e] = 2;
        RecoveredRules.SpawnDelay(entity);
        Require(entity.State == 0x0300 && entity[0x0e] == 0, "Spawn waits at zero");
        RecoveredRules.SpawnDelay(entity);
        Require(entity.State == 0x0303 && entity.RenderState == 4 && entity[0x10] == 1,
                "Spawn preserves the high byte while switching the low byte");

        entity.State = 2; entity[0x0e] = 13;
        Require(!RecoveredRules.ExpandingDeath(entity), "Death remains active at 14");
        Require(RecoveredRules.ExpandingDeath(entity) && !entity.Active, "Death releases at 15");
        entity.State = 9; entity[0x14] = 1;
        Require(!RecoveredRules.TimedDeath(entity), "Lifetime zero survives");
        Require(RecoveredRules.TimedDeath(entity), "Lifetime negative releases");

        entity.X = 8; entity.Y = 188; entity.VelocityX = -2; entity.VelocityY = 2;
        Require(RecoveredRules.MoveAndBounce(entity) && entity.X == 10 && entity.Y == 187 &&
                entity.VelocityX == 2 && entity.VelocityY == -2, "Asymmetric wall bounce");
        entity.X = 160; entity.Y = 100;
        RecoveredRules.AimChase(entity, 129, 115, 10, 10);
        Require(entity.VelocityX == -8 && entity.VelocityY == 3,
                "Chase uses arithmetic shifts, including negative odd values");
        Require(RecoveredRules.PlayerContact(entity, 175, 109, 0, 160, 100, 0, 2) == 3,
                "Both player contact bits");
        Require(RecoveredRules.PlayerContact(entity, 176, 100, 0, 160, 100, -1, 2) == 0,
                "Contact excludes exact width and inactive second player");

        var pool = new EntityPool();
        pool.Records[0].State = 3;
        pool.Records[1][0x24] = 123;
        Entity? child = pool.PrepareChild(entity);
        Require(ReferenceEquals(child, pool.Records[1]) && child!.State == -1 && child.X == 160 &&
                child.RenderState == 13 && child[0x24] == 123, "First-free allocation preserves residual words");
        for (int i = 0; i < EntityPool.GameplayRecords; i++) pool.Records[i].State = 3;
        Require(pool.PrepareChild(entity) == null && !pool.Records[70].Active,
                "Allocation stops at the 70th gameplay record");
        bool rejected = false;
        try { pool.StepLifecycle(0); } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Unported behavior fails explicitly");

        int samples = 0;
        RandomSequence random = RandomSequence.Shuffle(() => { samples++; return 0; });
        for (int i = 0; i < 1025; i++) Require(random.Next() == unchecked((byte)i), "Cyclic random table");
        Require(samples == 8000 && random.Index == 1, "4000 swaps and 512-entry wrap");

        WaveDefinition[] waves = OriginalWaves.Decode();
        Require(waves.Length == 100 && waves[0].Groups.Count == 2 &&
                waves[0].Groups[0].Kind == 0 && waves[0].Groups[0].Instances == 1 &&
                waves[0].Groups[1].Kind == 1 && waves[0].Groups[1].Instances == 31,
                "All original waves decode and zero-count groups spawn once");
        var parameters = new WaveParameters();
        int cues = 0;
        parameters.Apply(waves[0], 0, cue => cues++);
        Require(parameters.ChaseMaxX == 1 && parameters.ChaseMaxY == 1 &&
                parameters[0x0820] == 64 && parameters[0x086e] == 0x3030,
                "First wave configures recovered parameters");
        parameters.Apply(waves[1], 0, cue => cues++);
        Require(parameters[0x0820] == 64 && parameters[0x086e] == 0x3030,
                "Omitted wave settings carry forward");
        WaveDefinition special = WaveDefinition.Decode(new byte[]
        {
            0xff, 0xfe, 3, 0xfd, 0x81, 0xf3, 0x12, 0xaa,
            0xf9, 0x80, 0xfe, 0x0a, 0, 0xff, 0xff
        });
        parameters.Apply(special, 1, cue => { Require(cue == 0x16, "Level sound cue ID"); cues++; });
        Require(parameters.ChaseMaxX == -4 && parameters.ChaseMaxY == 6 &&
                parameters[0x21c0] == unchecked((short)0x8181) && parameters[0x21be] == 3 &&
                parameters[0x21c2] == 0x1212 && parameters[0x0828] == short.MinValue &&
                parameters[0x082c] == unchecked((short)0xfefe) && parameters[0x082e] == 10 && cues == 1,
                "Command signedness, unused fourth byte, three-byte command and sound event");

        parameters = new WaveParameters();
        parameters.Apply(waves[0], 0, cue => { });
        pool = new EntityPool();
        var spawn = new SpawnContext(pool, RandomSequence.Shuffle(() => 0), parameters);
        Entity herd = spawn.Create(0);
        Require(herd.X == 17 && herd.Y == 37 && herd.State == 0x0504 && herd.RenderState == 15 &&
                herd[0x0e] == 50 && herd[0x0c] == 35 && spawn.HerdCount == 1 && spawn.FreeRecords == 68,
                "Original herd initialization and random consumption");
        Entity chaser = spawn.Create(1);
        Require(chaser.X == 25 && chaser.Y == 45 && chaser.State == 0x0303 &&
                chaser.RenderState == 1 && chaser[0x14] == 1 && chaser[0x3e] == 3 && chaser[0x1e] == 0 &&
                chaser[0x2c] == 0x5392 && spawn.HostileCount == 1 && spawn.FreeRecords == 67,
                "Original chase spawn, fallback damage handler and sprite variant");
        var positionTable = new byte[512];
        positionTable[1] = 1; positionTable[2] = 30; positionTable[3] = 35;
        spawn = new SpawnContext(pool, new RandomSequence(positionTable), parameters);
        spawn.Position(chaser);
        Require(chaser.Y == 97, "First player permits vertically adjacent spawns above its Y");
        positionTable[2] = 127; positionTable[3] = 63;
        spawn = new SpawnContext(pool, new RandomSequence(positionTable), parameters);
        spawn.Position(chaser);
        Require(chaser.Y == 178, "Spawn Y clamps after rejection checks");
        chaser.X = 160; chaser.Y = 100; chaser[0x14] = 1;
        Require(!RecoveredRules.ChaseMotion(chaser, parameters, 129, 115) && chaser.X == 160 && chaser[0x14] == 0,
                "Chaser does not integrate old velocity between retargets");
        Require(RecoveredRules.ChaseMotion(chaser, parameters, 129, 115) && chaser.X == 158 &&
                chaser.Y == 100 && chaser[0x14] == 15, "Chase reload and quantized movement");
        herd.X = 16; herd.Y = 32; herd[0x14] = 0;
        random = RandomSequence.Shuffle(() => 0);
        HerdMotion.Step(herd, random, 0, -1, 160, 104);
        Require(herd.X == 15 && herd.Y == 31 && herd[0x14] == 10 && herd[0x0c] == 37 && random.Index == 2,
                "Herd wandering uses recovered direction and animation tables");
        HerdMotion.Step(herd, random, 1, -1, 160, 104);
        Require(herd.X == 15 && herd.Y == 31 && herd[0x14] == 9,
                "Herd moves only on even frames while the timer still ticks");
        HerdMotion.Step(herd, random, 2, 0, 160, 104);
        Require(herd.X == 16 && herd.Y == 32 && herd[0x0c] == 41 && random.Index == 2,
                "Love timer attracts herd without consuming random bytes");
        pool = new EntityPool();
        spawn = new SpawnContext(pool, random, parameters);
        var actors = new ActorContext(spawn);
        int deathMusic = 0, hitSounds = 0;
        actors.PaletteSequenceRequested += offset => deathMusic++;
        actors.SoundRequested += (sample, cl, ch) =>
        {
            Require(sample == 0x1d && cl == 3 && ch == 0x1e, "Original hit sound register values");
            hitSounds++;
        };
        chaser = pool.Records[0]; chaser.State = 0x0303; chaser.X = 160; chaser.Y = 104;
        chaser[0x14] = 7;
        ChaseActor.Step(actors, 0);
        Require(actors.ChaserVisits == 1 && chaser[0x14] == 7 && deathMusic == 0,
                "Frozen chasers count visits before returning");
        actors.FreezeTicks = 0; actors.PrimaryStatus = 0;
        ChaseActor.Step(actors, 0);
        Require(actors.PrimaryStatus == -2 && actors.DeathFlashTicks == 20 && deathMusic == 1 &&
                hitSounds == 1 && chaser[0x14] == 7, "Player contact gates chase movement and emits death events");
        spawn.PlayerFlags = 2; actors.PrimaryStatus = 0; actors.SecondaryStatus = 0;
        actors.PlayerHit(3);
        Require(actors.PrimaryStatus == 0 && actors.SecondaryStatus == -2,
                "Simultaneous contact prioritizes the second player");
        actors.Outcome = 2;
        int previousMusic = deathMusic;
        actors.PlayerHit(1);
        Require(deathMusic == previousMusic, "Outcome gate suppresses death side effects");
        actors.Outcome = 0; actors.TransitionLock = 1;
        actors.PlayerHit(1);
        Require(deathMusic == previousMusic + 1 && actors.PrimaryStatus == 0,
                "Death music request precedes transition-lock return");
        actors.PrimaryStatus = 100; actors.SecondaryStatus = -1;
        chaser.State = 15; chaser.X = 160; chaser.Y = 188;
        chaser.VelocityX = 0; chaser.VelocityY = 2; spawn.HostileCount = 1;
        short freeBefore = spawn.FreeRecords;
        ActorDispatcher.Step(actors, 0);
        Require(!chaser.Active && spawn.HostileCount == 0 && spawn.FreeRecords == freeBefore + 1,
                "State-15 wall hit releases the entity and updates both counters");
        chaser.State = 9; chaser[0x14] = 0;
        ActorDispatcher.Step(actors, 0);
        Require(!chaser.Active && spawn.FreeRecords == freeBefore + 2,
                "Lifecycle dispatch connects original free-record counters");
        var decimalScores = new DecimalScoreboard();
        int carries = 0;
        decimalScores.TenThousandCarry += () => carries++;
        decimalScores.Award(0, 9, 4, 0);
        Require(decimalScores.Value(0) == 10000 && carries == 1, "Decimal score carry at ten thousand");
        decimalScores.Award(1, 4, 5, 1);
        Require(decimalScores.Value(1) == 1000, "Inclusive score counter and double-score repeat");
        decimalScores = new DecimalScoreboard();
        for (int column = 0; column < 8; column++) decimalScores.Award(0, 8, column, 0);
        Require(decimalScores.Value(0) == 99999999, "Eight original decimal score digits");
        decimalScores.Award(0, 0, 7, 0);
        Require(decimalScores.Value(0) == 1, "Original full score carry retries the increment");

        spawn = new SpawnContext(new EntityPool(), RandomSequence.Shuffle(() => 0), parameters) { HerdCount = 1 };
        actors = new ActorContext(spawn) { RemainingRescues = 1 };
        var rescueSounds = new System.Collections.Generic.List<byte>();
        actors.SoundRequested += (sample, cl, ch) => rescueSounds.Add(sample);
        herd = spawn.Pool.Records[0]; herd.State = 5; herd.X = 160; herd.Y = 104; herd[0x30] = 35;
        ActorDispatcher.Step(actors, 0);
        Require(herd.State == 6 && herd[0x14] == 40 && herd[0x1e] == 0x6cc9 && spawn.HerdCount == 0 &&
                actors.HotBulletTimer == 80 && actors.Scores.Value(0) == 1500 && actors.PrimaryRescueLabelY == 94 &&
                rescueSounds.Count == 2 && rescueSounds[0] == 0x1a && rescueSounds[1] == 0x17,
                "Last herd rescue ignores invulnerability, adds bonus, and becomes a trail follower");
        Require(herd[0x32] == 160 && herd[0x34] == 104 && herd[0x36] == 0x45 && herd[0x38] == 20,
                "Rescue score popup keeps the original position and lifetime");
        spawn.PlayerFlags = 3; spawn.SecondPlayerX = 160;
        actors.RemainingRescues = 2; actors.CycleShift = 1; spawn.HerdCount = 1;
        herd = spawn.Pool.Records[1]; herd.State = 5; herd.X = 160; herd.Y = 104;
        ActorDispatcher.Step(actors, 1);
        Require(herd[0x1e] == 0x70c9 && herd[0x14] == 40 && actors.SecondaryTrailDelay == 40 &&
                actors.HotBulletTimer == 20 && actors.Scores.Value(0) == 1750,
                "Two-player simultaneous rescue selects secondary trail and 250-point award");

        var trail = new TrailHistory(10, 20);
        trail.Record(10, 20, 6);
        Require(trail.ByteCursor == 0 && trail.AtOffset(0).Direction == 0, "Direction-only changes do not append trail records");
        trail.Record(14, 20, 6);
        herd[0x14] = 0; herd[0x30] = 35;
        trail.Follow(herd);
        Require(herd.X == 14 && herd.Y == 20 && herd[0x0c] == 42, "Follower position and direction-based animation");
        for (short x = 20; x < 148; x++) trail.Record(x, 20, 0);
        Require(trail.ByteCursor == 8 && trail.AtOffset(-8).X == 146, "128-record trail cursor wraps in bytes");
        spawn = new SpawnContext(new EntityPool(), RandomSequence.Shuffle(() => 0), parameters);
        actors = new ActorContext(spawn);
        PlayerMotion.Step(actors, 8, 0, false);
        PlayerMotion.Step(actors, 129, 0, false);
        Require(spawn.PlayerX == 161 && spawn.PlayerY == 103 && actors.PrimaryMotion.FireX == 3 &&
                actors.PrimaryMotion.FireY == 0 && actors.PrimaryMotion.Facing == -9,
                "Firing lock preserves aim while movement and facing change");
        PlayerMotion.Step(actors, 0, 0, false);
        Require(actors.PrimaryMotion.AimMask == 0 && actors.PrimaryMotion.FireX == 3,
                "Idle input clears spread mask while retaining firing velocity");
        PlayerMotion.Step(actors, 8, 1, true);
        Require(spawn.PlayerX == 162 && actors.PrimaryMotion.FireX == 0 && actors.PrimaryMotion.FireY == -3,
                "Twin-stick movement and aim are independent");

        actors.PrimaryStatus = 0; actors.SecondaryStatus = 0; actors.SpawnComplete = true;
        actors.PrimaryMotion.AimMask = 8; actors.PrimaryMotion.FireX = 3; actors.PrimaryMotion.FireY = 0;
        actors.PrimaryMotion.ProjectileSprite = 29; actors.ScoreRepeat = 1;
        var bullets = new BulletSystem { Cooldown = 0 };
        bullets.Fire(actors);
        Require(bullets.Shots[0].Active && bullets.Shots[1].VelocityY == -3 && bullets.Shots[2].VelocityY == 3 &&
                !bullets.Shots[3].Active, "Original three-way spread occupies three shared slots");
        for (int i = 0; i < 8; i++) bullets.Fire(actors);
        Require(!bullets.Shots[3].Active, "Eight intervening firing ticks produce no new burst");
        bullets.Fire(actors);
        Require(bullets.Shots[5].Active && !bullets.Shots[6].Active, "Ninth eligible tick produces the next burst");
        bullets.Reset();
        Bullet shot = bullets.Shots[0]; shot.X = 310; shot.Y = 100; shot.VelocityX = 3;
        shot.VelocityY = 0; shot.Sprite = 29; shot.Lifetime = 1; actors.Ricochet = 1;
        bullets.Step(actors);
        Require(shot.X == 310 && shot.VelocityX == -3 && shot.Lifetime == 0,
                "Ricochet changes direction without moving through the wall");
        bullets.Step(actors);
        Require(!shot.Active, "Ricochet lifetime expires before movement on the next tick");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0));
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
        for (int i = 0; i < 43; i++) game.Step(0);
        Require(game.Actors.SpawnComplete && game.Actors.Spawning.HerdCount == 1 &&
                game.Actors.Spawning.HostileCount == 31 && game.Actors.Spawning.FreeRecords == 29 &&
                game.Actors.FreezeTicks == 50, "First wave executes eight obstacles and 32 scripted spawns in 43 ticks");
        game.Actors.PrimaryStatus = 0; game.Actors.SecondaryStatus = 0; game.Bullets.Cooldown = 0;
        chaser = game.Actors.Spawning.Pool.Records[9]; chaser.X = 150; chaser.Y = 104;
        game.Step(0);
        Require(chaser.State == 2 && chaser.RenderState == 5 && chaser[0x0e] == 1 &&
                game.Actors.Spawning.HostileCount == 30 && game.Actors.Scores.Value(0) == 40 &&
                !game.Bullets.Shots[0].Active,
                "Standalone frame connects firing, collision, damage, scoring and same-frame death expansion");
        var nextPoint = PowerupActor.LineStep(10, 10, 14, 11);
        Require(nextPoint.X == 11 && nextPoint.Y == 10 && PowerupActor.LineStep(10, 10, 10, 10) == (1, 1),
                "Original one-step line steering and reached-target sentinel");
        PowerupActor.Collect(game.Actors, 3, 1);
        PowerupActor.Collect(game.Actors, 0, 1);
        PowerupActor.Collect(game.Actors, 2, 1);
        Require(game.Actors.PermanentHotBullets == 1 && game.Actors.HotBulletTimer == 50 &&
                game.Actors.LoveTimer == 800 && game.Actors.Lives == 6, "Recovered bullet, herd and life power-ups");
        spawn = new SpawnContext(new EntityPool(), random, parameters) { HostileCount = 2 };
        actors = new ActorContext(spawn) { TransitionLock = 1 };
        for (int i = 0; i < 2; i++)
        {
            entity = spawn.Pool.Records[i]; entity.State = 3; entity.X = (short)(40 + i * 200); entity.Y = 40;
            entity[0x10] = 1; entity[0x3e] = 0; entity[0x3c] = 0x0309;
        }
        bullets = new BulletSystem(); shot = bullets.Shots[0]; shot.X = 160; shot.Y = 104;
        shot.VelocityX = 3; shot.Sprite = 29;
        bullets.Step(actors);
        Require(spawn.HostileCount == 0 && actors.TransitionLock == 0 && shot.Active && actors.Scores.Value(0) == 80,
                "Bomb collision sweeps distant actors, preserves the shot and clears its flag");
        CheckSpawnerFamily();
        CheckCampaign();
        CheckClock();
        CheckHunters();
        CheckAxisChase();
        CheckHunterVariants();
        CheckCenterSpawner();
        CheckFixedShooter();
        CheckContactEmitters();
        CheckFixedSplits();
        CheckBeams();
        CheckAxisDash();
        CheckMutableSpawner();
        CheckDeathAndRecoil();
        Console.WriteLine("[LLREBUILD] Core, spawner/campaign boundary checks and all 100 wave decodes passed.");
    }

    private static void CheckSpawnerFamily()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var random = new RandomSequence(table);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), random, parameters);
        Entity parent = spawn.Create(3);
        Require(parent.State == 0x0b0a && parent.X == 48 && parent.Y == 96 &&
                parent[0x16] == 300 && parent[0x18] == 96 && parent[0x1a] == 3 &&
                parent.VelocityX == -1 && random.Index == 6 && spawn.HostileCount == 1,
                "Spawner creation retains original delay, quota, cadence and random consumption");
        var rapidSpawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        Entity rapid = rapidSpawn.Create(33);
        Require(rapid[0x0c] == 0x122 && rapid[0x16] == 30 && rapid[0x18] == 48 && rapid[0x1a] == 20,
                "Rapid spawner retains its distinct graphics, startup wait, cadence and quota");
        var actors = new ActorContext(spawn) { FreezeTicks = 0 };
        parent.State = 11; parent[0x16] = -1; parent[0x14] = 10;
        ActorDispatcher.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        Require(parent[0x18] == 0x6060 && parent[0x1a] == 2 && parent.X == 47 &&
                child.State == 12 && child.X == 48 && child[0x16] == 63 &&
                spawn.FreeRecords == 67 && spawn.HostileCount == 2,
                "Spawner reloads its high-byte timer and creates its child before moving");
        actors.FreezeTicks = 1; actors.FrameCounter = 6;
        ActorDispatcher.Step(actors, 0);
        Require(parent.X == 47 && parent[0x18] == 0x6060 && parent[0x0c] == 63,
                "Freeze stops parent movement and spawning while animation continues");
        ActorDispatcher.Step(actors, 1);
        Entity shot = spawn.Pool.Records[2];
        Require(child[0x16] == 0x3f3f && child[0x14] == 64 && child.X == 48 &&
                shot.State == 13 && shot.VelocityX == 1 && shot.VelocityY == 0 &&
                spawn.FreeRecords == 66 && spawn.HostileCount == 3,
                "Child fires while frozen, then retargets without moving on its expired timer");
        shot.X = 100; shot.Y = 17; shot.VelocityX = 1; shot.VelocityY = -1;
        ActorDispatcher.Step(actors, 2);
        Require(!shot.Active && shot.X == 101 && shot.Y == 17 &&
                spawn.FreeRecords == 67 && spawn.HostileCount == 2,
                "Child shot commits X before releasing at the strict Y boundary");
        foreach (Entity record in spawn.Pool.Records) record.State = 1;
        parent.State = 11; parent[0x18] = 0; parent[0x16] = -1;
        spawn.FreeRecords = 0; actors.FreezeTicks = 0;
        ActorDispatcher.Step(actors, 0);
        Require(parent[0x1a] == 1 && spawn.FreeRecords == 0 && spawn.HostileCount == 2,
                "A full pool consumes the parent's quota without claiming a free record");
        Array.Fill(table, (byte)100);
        foreach (short freeze in new short[] { 0, 1 })
        {
            spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), new WaveParameters()) { HostileCount = 1 };
            actors = new ActorContext(spawn) { FreezeTicks = freeze };
            parent = spawn.Pool.Records[0]; parent.State = 11; parent.X = 50; parent.Y = 60;
            parent[0x3e] = 2; parent[0x3c] = 2;
            DamageRules.Apply(actors, parent, new Bullet { Sprite = 29 });
            Require(parent.Behavior == 28 && parent[0x14] == (freeze == 0 ? 7 : 3) &&
                    parent[0x36] == 70 && parent[0x38] == 20 && spawn.Random.Index == 1,
                    "Spawner damage uses freeze-dependent drop thresholds and preserves its score popup");
        }
    }

    private static void CheckCampaign()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var game = new GameSimulation(new RandomSequence(table));
        var actors = game.Actors; var spawn = actors.Spawning;
        for (int i = 0; i < 7; i++)
        {
            Entity record = spawn.Pool.Records[i];
            record.State = 3; record[0x3a] = i == 1 || i == 6 ? (short)0 : (short)1;
        }
        spawn.Pool.Records[3][0x3a] = -1;
        spawn.Pool.Records[4][0x3a] = -1;
        spawn.Pool.Records[5].State = -1;
        spawn.Pool.Records[6].State = unchecked((short)0x8005);
        spawn.Pool.Records[0][0x2a] = 1234;
        spawn.SpawnSerial = 77; spawn.InitialHerdCount = 9;
        actors.Scores.Award(0, 4, 7, 0);
        actors.HotBulletTimer = 123; actors.PermanentHotBullets = 1; actors.Ricochet = 1;
        actors.PrimaryMotion.FireX = -3; actors.FrameCounter = 22;
        game.Bullets.Cooldown = 2;
        var campaign = new GameCampaign(game);
        bool renderedBeforeRetry = false;
        campaign.FrameReady += _ => renderedBeforeRetry = actors.Lives == 5 && spawn.Pool.Records[0].State == 3;
        actors.Outcome = 1; campaign.TickDisplay();
        Require(renderedBeforeRetry && actors.Lives == 4 && actors.Outcome == 0 &&
                game.Wave.Groups.Count == 2 && game.Wave.Groups[0].Kind == 1 && game.Wave.Groups[0].Instances == 2 &&
                game.Wave.Groups[1].Kind == 0 && game.Wave.Groups[1].Instances == 2,
                "Retry renders first, spends one life, groups surviving kinds in pool order and uses the original low-byte sign test");
        Require(actors.Scores.Value(0) == 5 && actors.HotBulletTimer == 123 && actors.PermanentHotBullets == 1 &&
                actors.Ricochet == 0 && actors.PrimaryMotion.FireX == -3 && actors.FrameCounter == 22 &&
                spawn.Random.Index == 0 && spawn.SpawnSerial == 77 && spawn.InitialHerdCount == 9 &&
                spawn.Pool.Records[0][0x2a] == 1234 && game.Bullets.Cooldown == 2 &&
                spawn.FreeRecords == 69 && game.ObstacleRemaining == 7,
                "Retry resets only written fields and retains score, hot bullets, random state, aim and reused record words");

        game = new GameSimulation(new RandomSequence(table)); actors = game.Actors; spawn = actors.Spawning;
        for (int i = 0; i < 43; i++) game.Step(0);
        foreach (Entity record in spawn.Pool.Records) record.State = -1;
        spawn.HostileCount = 0; spawn.HerdCount = 0;
        actors.PrimaryStatus = 0; actors.SecondaryStatus = 0;
        campaign = new GameCampaign(game);
        int rendered = 0; campaign.FrameReady += _ => rendered++;
        campaign.TickDisplay(); rendered = 0;
        campaign.TickInterrupt(0);
        Require(campaign.Phase == CampaignPhase.Playing && actors.Outcome == 2,
                "An IRQ can clear the wave while outcome resolution waits for drawing");
        campaign.TickDisplay();
        for (int i = 0; i < 11; i++)
        {
            if (i < 7) campaign.TickInterrupt(0);
            campaign.TickDisplay();
        }
        Require(campaign.Phase == CampaignPhase.WipeExpand && campaign.TransitionTicks == 48 &&
                rendered == 12 && spawn.WaveIndex == 1 && spawn.SpriteVariant == 1,
                "A clear advances the index once and renders eleven additional frames independently of IRQ count");
        short beforeWipe = actors.FrameCounter, freezeBeforeWipe = actors.FreezeTicks;
        for (int i = 0; i < 48; i++)
        {
            if ((i & 1) == 0) campaign.TickInterrupt(0);
            campaign.TickDisplay();
        }
        Require(campaign.Phase == CampaignPhase.WipeContract && campaign.TransitionTicks == 47 &&
                actors.FrameCounter == unchecked((short)(beforeWipe + 24)) && actors.FreezeTicks == freezeBeforeWipe,
                "Wipe uses display steps while IRQ input counters advance without updating actors");
        for (int i = 0; i < 47; i++) campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Playing && campaign.WaveNumber == 2 &&
                rendered == 12 && actors.PrimaryStatus == 100 &&
                spawn.Parameters[0x21b8] == 0 && spawn.PlayerX == 160 && spawn.PlayerY == 104,
                "Second wipe has 47 steps without gameplay renders and next-wave setup retains prefix parameters");
        campaign.TickDisplay();
        for (int i = 0; i < 47; i++) campaign.TickInterrupt(0);
        Require(actors.SpawnComplete && spawn.HostileCount == 34 && spawn.HerdCount == 2,
                "The connected campaign spawns the actual second wave without reloading DOS or losing carried settings");
        spawn.WaveIndex = 100; game.AdvanceWave();
        Require(spawn.WaveIndex == 0 && actors.CycleShift == 1 && spawn.DifficultyIncrement == 1 &&
                spawn.Parameters.ChaseMaxX == 2, "Wave-table sentinel wraps the index and raises both original difficulty words");

        game = new GameSimulation(new RandomSequence(table)); actors = game.Actors;
        campaign = new GameCampaign(game); actors.Lives = 1; actors.Outcome = 1;
        campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Continue && campaign.ContinueCredits == 3 &&
                campaign.ContinueCount == 9 && campaign.ContinueTimer == 50,
                "Last life opens the original continue countdown and spends a continue credit");
        for (int i = 0; i < 49; i++) campaign.TickInterrupt(0);
        campaign.TickInterrupt(0, acceptContinue: true);
        campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Continue && campaign.ContinueCount == 8,
                "Continue timer rollover takes priority over the same-frame acceptance input");
        campaign.TickInterrupt(0, acceptContinue: true);
        Require(campaign.Phase == CampaignPhase.Continue && actors.Lives == 0,
                "Continue acceptance waits for the main display loop to resolve it");
        campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Playing && actors.Lives == 5 && campaign.ContinueCredits == 3,
                "Continue acceptance restores five lives and resumes the survivor stream");
        actors.Lives = 1; actors.Outcome = 1; campaign.TickDisplay();
        short frameCounter = actors.FrameCounter;
        for (int i = 0; i < 500; i++) campaign.TickInterrupt(0);
        campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.GameOver && campaign.ContinueCount == -1 &&
                actors.FrameCounter == unchecked((short)(frameCounter + 500)),
                "The continue countdown includes zero, ends on signed underflow and retains engine input-frame ticks");

        game = new GameSimulation(new RandomSequence(table)); campaign = new GameCampaign(game);
        game.Actors.Outcome = 4; campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.WarpAudio && game.Actors.Spawning.WaveIndex == 0,
                "Warp waits for the audio host before changing the wave or awarding score");
        campaign.NotifyWarpAudioCompleted();
        Require(game.Actors.Spawning.WaveIndex == 5 && game.Actors.Scores.Value(0) == 50000 &&
                campaign.Phase == CampaignPhase.WipeExpand, "Warp completion advances five waves and awards the original 50000-point bonus");
    }

    private static void CheckClock()
    {
        string RunClock(int chunks)
        {
            var clock = new GameClock(70);
            var trace = new System.Text.StringBuilder();
            double lastTime = 0;
            void Event(char kind)
            {
                Require(clock.Time >= lastTime, "Clock events are chronological");
                lastTime = clock.Time; trace.Append(kind);
            }
            for (int i = 0; i < chunks; i++) clock.Advance(1d / chunks, () => Event('I'), () => Event('D'));
            Require(clock.InterruptCount == 50 && clock.DisplayCount == 70 && trace.ToString().StartsWith("DIDIDD"),
                    "Recovered PIT rate produces 50 interrupts and 70 display steps in the first second");
            return trace.ToString();
        }
        Require(RunClock(1) == RunClock(60) && RunClock(60) == RunClock(90),
                "Host frame chunks do not change interrupt/display event ordering");
        var stopClock = new GameClock(70); bool stop = false;
        stopClock.Advance(10, () => { }, () => stop = stopClock.InterruptCount >= 120, () => stop);
        Require(stopClock.InterruptCount == 120 && stopClock.DisplayCount == 168,
                "Smoke capture stops on the first display after the 120th interrupt without running extra IRQs");

        var table = new byte[512]; Array.Fill(table, (byte)32);
        var game = new GameSimulation(new RandomSequence(table));
        var campaign = new GameCampaign(game); var frame = new SpriteFrame();
        Entity popup = game.Actors.Spawning.Pool.Records[0];
        popup.State = 1; popup.RenderState = 13; popup.X = 80; popup.Y = 80;
        popup[0x32] = 80; popup[0x34] = 80; popup[0x38] = 100;
        campaign.FrameReady += simulation => frame.Build(simulation);
        var gameClock = new GameClock(70);
        gameClock.Advance(1, () => campaign.TickInterrupt(0), () => campaign.TickDisplay());
        Require(game.Actors.FrameCounter == 50 && popup[0x38] == 30,
                "Actor/input ticks use PIT cadence while score popup lifetime follows drawing");
        short freeze = game.Actors.FreezeTicks;
        campaign.Paused = true;
        gameClock.Advance(1, () => campaign.TickInterrupt(0), () => campaign.TickDisplay());
        Require(game.Actors.FrameCounter == 100 && game.Actors.FreezeTicks == freeze && popup[0x32] == 0,
                "Pause retains IRQ input ticks and render-side expiry while stopping actor timers");

        campaign.Paused = false; game.Actors.Outcome = 1; campaign.TickDisplay();
        Require(game.ObstacleRemaining == 7, "Retry resets obstacle allocation");
        campaign.TickInterrupt(0);
        Require(game.ObstacleRemaining == 7, "IRQ before the first new-scene draw does not run gameplay");
        campaign.TickDisplay(); campaign.TickInterrupt(0);
        Require(game.ObstacleRemaining == 6, "Drawing the new scene opens the original gameplay gate");
    }

    private static void CheckDeathAndRecoil()
    {
        var table = new byte[512]; Array.Fill(table, (byte)33); table[0] = 0; table[1] = 7;
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        parameters.Apply(WaveDefinition.Decode(new byte[] { 0xfc, 4, 2, 0, 0xff, 0xff }), 0, _ => { });
        foreach (short outcome in new short[] { 5, 6, 8 })
        {
            var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
            Entity parent = spawn.Pool.Records[0]; parent.State = 3; parent.X = 100; parent.Y = 80;
            parent[0x3e] = 3; parent[0x18] = 0; parent[0x1e] = outcome; parent[0x3a] = 1; parent[0x20] = 123;
            spawn.Pool.Records[1][0x32] = 777; spawn.Pool.Records[1][0x3c] = 456;
            var actors = new ActorContext(spawn) { FreezeTicks = 100 };
            if (outcome == 8) actors.HotBulletTimer = 0;
            var bullet = new Bullet { Sprite = 30, VelocityX = -3, VelocityY = short.MinValue };
            DamageRules.Apply(actors, parent, bullet);
            Entity child = spawn.Pool.Records[1];
            if (outcome == 8)
                Require(parent.State == 0x1507 && parent[0x3a] == 1 && parent[0x20] == 123 && parent[0x18] == 0 &&
                        spawn.HostileCount == 1 && actors.KillCount == 0 && !child.Active,
                        "Hunter conversion bypasses common kill and retains kind/action with balanced hostile count");
            else
            {
                Require(parent.State == 2 && child[0x32] == 777 && child[0x3c] == 456 &&
                        spawn.FreeRecords == 69 && actors.KillCount == 1,
                        "Death offspring retain popup/sound words and do not consume the free counter");
                if (outcome == 5)
                {
                    Require(child.State == 0x1b1b && child.VelocityX == -1 && child.VelocityY == 0 &&
                            child[0x3a] == 1 && spawn.HostileCount == 0 && spawn.Random.Index == 2,
                            "Cardinal hazard rejects even samples, retains retry kind one and does not count as hostile");
                    actors.PrimaryStatus = 0; spawn.PlayerX = 99; spawn.PlayerY = 80;
                    ActorDispatcher.Step(actors, 1);
                    Require(child.X == 99 && actors.PrimaryStatus == -2,
                            "Cardinal hazard moves before harmful contact and ignores freeze");
                }
                else Require(child.State == 0x0f0f && child.VelocityX == 1 && child.VelocityY == -16384 &&
                             child[0x0c] == 193 && child[0x10] == 0 && spawn.HostileCount == 1,
                             "Reflected shot negates in sixteen bits before halving and counts as a hostile");
            }
        }

        var factoryTable = new byte[512]; Array.Fill(factoryTable, (byte)32);
        var factory = new SpawnContext(new EntityPool(), new RandomSequence(factoryTable), parameters);
        foreach (byte kind in new byte[] { 17, 18, 19, 20, 31 }) factory.Create(kind);
        Require(factory.RecoilBaseSprite == 280 && factory.HostileCount == 5 &&
                factory.Pool.Records[0][0x0c] == 206 && factory.Pool.Records[3][0x0c] == 218 &&
                factory.Pool.Records[4].State == 0x2828 && factory.Pool.Records[0][0x2e] == 2,
                "Recoil factories select shared sprite base and health, with distinct fixed-animation variant");
        Entity recoil = factory.Pool.Records[0]; recoil.X = 100; recoil.Y = 80;
        recoil[0x1c] = -1; recoil[0x1e] = 0; recoil[0x20] = -1; recoil[0x22] = 0;
        var context = new ActorContext(factory) { FreezeTicks = 100 };
        factory.PlayerX = 250;
        ActorDispatcher.Step(context, 0);
        Require(recoil[0x1c] == 3 && recoil[0x1e] == 1 && recoil[0x20] == 3 && recoil[0x22] == 1 &&
                recoil[0x0c] == 207 && recoil.X == 100 && recoil.Y == 80,
                "Recoil acceleration grows with unsigned carry and facing changes during movement freeze");
        recoil[0x16] = 7; recoil[0x1a] = -8; recoil[0x30] = 9;
        DamageRules.Apply(context, recoil, new Bullet { Sprite = 29, Y = 17, VelocityX = -3, VelocityY = 5 });
        Require(recoil[0x14] == 68 && recoil[0x18] == -12 && recoil[0x16] == 7 && recoil[0x1a] == -8 &&
                recoil[0x1c] == 512 && recoil[0x24] == 20 && recoil[0x30] == 9 && recoil[0x2e] == 1,
                "Recoil damage writes projectile Y/VX into low velocity words and retains high words/delay");

        var spawnChild = new SpawnContext(new EntityPool(), new RandomSequence(factoryTable), parameters)
            { HostileCount = 1, RecoilBaseSprite = 214 };
        Entity source = spawnChild.Pool.Records[0]; source.State = 31; source.X = 100; source.Y = 80;
        source[0x0c] = 206; source[0x3e] = 14;
        Entity offspring = spawnChild.Pool.Records[1]; offspring[0x26] = 123; offspring[0x2a] = 456; offspring[0x32] = 777;
        context = new ActorContext(spawnChild) { FreezeTicks = 100, FrameCounter = 8 };
        DamageRules.Apply(context, source, new Bullet { Sprite = 29 });
        Require(source.State == 2 && offspring.State == 0x2828 && offspring[0x0c] == 214 && offspring[0x28] == 0 &&
                offspring[0x2c] == 0 && offspring[0x26] == 123 && offspring[0x2a] == 456 && offspring[0x32] == 777 &&
                offspring.VelocityX == 214 && spawnChild.HostileCount == 1 && spawnChild.FreeRecords == 69,
                "Damage-created recoil retains fractions/popup but clears cached whole positions and uses shared sprite selector");
        ActorDispatcher.Step(context, 1);
        Require(offspring[0x0c] == 282 && offspring.X == 100 && offspring[0x28] == 0,
                "Recoil variant overrides common facing with fixed animation after movement handling");
        context.PrimaryStatus = 0; spawnChild.PlayerX = 100; spawnChild.PlayerY = 80;
        offspring[0x0c] = 999;
        ActorDispatcher.Step(context, 1);
        Require(context.PrimaryStatus == -2 && offspring[0x0c] == 282,
                "Fixed-animation recoil variant updates animation even after common contact returns");
        foreach (short outcome in new short[] { 5, 6 })
        {
            var hot = new SpawnContext(new EntityPool(), new RandomSequence(factoryTable), parameters) { HostileCount = 1 };
            source = hot.Pool.Records[0]; source.State = 3; source[0x3e] = 3; source[0x18] = 0; source[0x1e] = outcome;
            context = new ActorContext(hot) { HotBulletTimer = 0 };
            DamageRules.Apply(context, source, new Bullet { Sprite = 29 });
            Require(source.State == 2 && !hot.Pool.Records[1].Active && hot.Random.Index == 0 && hot.HostileCount == 0,
                    "Hot shot suppresses cardinal/reflected offspring before allocation or random consumption");
        }
        var full = new SpawnContext(new EntityPool(), new RandomSequence(factoryTable), parameters) { HostileCount = 1 };
        for (int i = 0; i < EntityPool.GameplayRecords; i++) full.Pool.Records[i].State = 1;
        source = full.Pool.Records[0]; source.State = 31; source[0x3e] = 14; source[0x0c] = 206;
        full.Pool.Records[70][0x3a] = 4321;
        DamageRules.Apply(new ActorContext(full), source, new Bullet { Sprite = 29 });
        Require(source.State == 2 && full.Pool.Records[70][0x3a] == 4321 && full.HostileCount == 0,
                "Full recoil-child allocation performs common kill without writing beyond the scan");
        source = full.Pool.Records[0]; source.State = 3; source[0x3e] = 15; source[0x24] = 0;
        context = new ActorContext(full);
        DamageRules.Apply(context, source, new Bullet { Sprite = 29 });
        Require(source.State == 3 && source[0x24] == -1 && context.PaletteSequenceOffset == 0x0713,
                "Damage fifteen kills at exact zero, not at signed underflow");
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 31);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
        var frame = new SpriteFrame();
        for (int i = 0; i < 350; i++) { game.Step(0); frame.Build(game); }
        Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                "Actual thirty-second-wave recoil script advances through 350 protected logic ticks");
    }

    private static void CheckMutableSpawner()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters)
            { RampAcceleration = 0x12340800 };
        Entity parent = spawn.Create(10), bounce = spawn.Create(11);
        Require(parent.State == 0x1307 && parent[0x1c] == 0x0800 && parent[0x1e] == 0x1234 &&
                parent[0x20] == 0x0800 && parent[0x22] == 0x1234 && parent[0x24] == 0x4040 && parent[0x2e] == 7 &&
                bounce.State == 0x1414 && bounce.VelocityX == -1 && bounce.VelocityY == -1 && bounce[0x18] == 4,
                "Mutable-spawner factory snapshots both ramp words and bouncing factory keeps zero-eligible random velocities");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
        parent = spawn.Pool.Records[0]; parent.State = 19; parent.X = 100; parent.Y = 80;
        parent[0x30] = -1; parent[0x24] = 1; parent[0x2e] = 1;
        spawn.Pool.Records[1].VelocityX = 777; spawn.Pool.Records[1].VelocityY = 888;
        spawn.Pool.Records[1][0x32] = 123;
        var actors = new ActorContext(spawn) { FreezeTicks = 100 };
        byte[] before = spawn.Scripts.CopyScript(21);
        ActorDispatcher.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        byte[] after = spawn.Scripts.CopyScript(21);
        Require(before[8] == 1 && before[9] == 15 && before[10] == 5 && before[11] == 3,
                "Wave twenty-two mutation vector matches the original runtime probe's preimage");
        for (int i = 0; i < before.Length; i++)
            Require(after[i] == (i >= 8 && i < 12 ? 0 : before[i]), "Child initializer changes only the four observed wave-script bytes");
        Require(child.State == 0x1010 && child[0x0c] == 145 && child[0x3c] == 12 && child[0x3a] == -1 &&
                child.VelocityX == 777 && child.VelocityY == 888 && child[0x32] == 123 && child[0x30] == 3 &&
                spawn.HostileCount == 2 && spawn.FreeRecords == 69 && parent[0x2e] == 0,
                "Child emission during freeze retains residual velocity/popup and increments hostiles without changing free counter");
        WaveScriptStore mutated = spawn.Scripts;
        WaveDefinition changed = mutated.Decode(21);
        Require(changed.Groups[2].Kind == 0 && changed.Groups[2].Instances == 1 &&
                changed.Groups[3].Kind == 0 && changed.Groups[3].Instances == 1,
                "Mutated raw script decodes the two original enemy groups as single herd spawns");
        ActorDispatcher.Step(actors, 0); ActorDispatcher.Step(actors, 0);
        Require(parent[0x2e] == -1 && parent[0x24] == unchecked((short)0xff01) && !spawn.Pool.Records[2].Active,
                "Exhausted quota is consumed before allocation and does not reload its expired timer");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
        for (int i = 0; i < EntityPool.GameplayRecords; i++) spawn.Pool.Records[i].State = 1;
        parent = spawn.Pool.Records[0]; parent.State = 19; parent[0x30] = -1; parent[0x24] = 1; parent[0x2e] = 1;
        actors = new ActorContext(spawn) { FreezeTicks = 100 };
        int sounds = 0; actors.SoundRequested += (al, cl, ch) => { if (al == 11 && cl == 1 && ch == 3) sounds++; };
        ActorDispatcher.Step(actors, 0);
        Require(spawn.Scripts.CopyScript(21).AsSpan().SequenceEqual(before) && spawn.Random.Index == 0 &&
                spawn.HostileCount == 1 && parent[0x2e] == 0 && sounds == 1 && !spawn.Pool.Records[70].Active,
                "Full child allocation consumes quota and sound without mutating wave data or extending the pool scan");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 21, scripts: mutated);
        Require(game.Wave.Groups[2].Kind == 0 && game.Wave.Groups[3].Kind == 0,
                "A later wave starts from the retained mutable scripts");
        game.SpawnRampAccumulator = 0x5678;
        Require(game.Actors.Spawning.RampAcceleration == 0x5678, "Shared ramp writes reach subsequent spawn factories");
        game.RetryWave();
        Require(game.Scripts.CopyScript(21).AsSpan().SequenceEqual(after), "Retry retains original-script mutations");
        var fresh = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 21);
        Require(fresh.Wave.Groups[2].Kind == 1 && fresh.Wave.Groups[3].Kind == 5,
                "Fresh data store is independent of other runs' mutations");
        foreach (int wave in new[] { 18, 21 })
        {
            game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: wave,
                                      scripts: wave == 18 ? new WaveScriptStore() : mutated);
            game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
            var sprites = new SpriteFrame();
            for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
            Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                    $"Actual wave {wave + 1} advances 350 protected ticks with mutable-spawner actors");
            if (wave == 18) Require(game.Scripts.CopyScript(21).AsSpan().SequenceEqual(after),
                                    "Natural nineteenth-wave child emission reproduces the observed script mutation");
        }
    }

    private static void CheckAxisDash()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var random = new RandomSequence(table);
        var spawn = new SpawnContext(new EntityPool(), random, parameters);
        Entity dash = spawn.Create(23);
        Require(dash.State == 0x2222 && dash[0x16] == 1 && dash[0x24] == 0x2000 && dash[0x26] == 1 &&
                dash[0x28] == 528 && dash[0x1e] == dash.X && dash[0x22] == dash.Y && random.Index == 8,
                "Axis-dash factory initializes cached positions and packed velocity without overwriting hit-pause word");
        var actors = new ActorContext(spawn) { FreezeTicks = 0 };
        short x = dash.X;
        ActorDispatcher.Step(actors, 0);
        Require(dash.X == x && dash[0x1a] == -1 && dash[0x0c] == 231,
                "Zero hit pause occupies an animation-only update before movement");
        ActorDispatcher.Step(actors, 0);
        Require(dash.X == x + 1 && dash[0x1c] == 0x2000 && dash[0x24] == 0x1df0 && dash[0x0c] == 234,
                "Axis dash integrates fractional position before signed speed decay");
        dash.X = 311; dash[0x1c] = 0; dash[0x1e] = 311; dash[0x14] = 0; dash[0x18] = 0;
        dash[0x24] = 0; dash[0x26] = 1; dash[0x0c] = 999;
        int before = random.Index;
        ActorDispatcher.Step(actors, 0);
        Require(dash.X == 311 && dash[0x1e] == 311 && dash[0x14] == 1 && dash[0x18] == 0 &&
                dash[0x0c] == 999 && random.Index == before + 3,
                "Upper dash boundary rejects position, preserves sprite and retargets the other axis");
        dash.X = 17; dash[0x1e] = 17; dash[0x1c] = 0; dash[0x14] = 0; dash[0x18] = 1;
        dash[0x24] = 0; dash[0x26] = 1; dash[0x28] = 0;
        ActorDispatcher.Step(actors, 0);
        Require(dash.X == 16 && dash[0x14] == 0 && dash[0x0c] == 232,
                "Reverse dash accepts exact lower boundary sixteen");
        dash.X = 100; dash[0x1e] = 100; dash[0x1c] = 0; dash[0x18] = 0;
        dash[0x24] = 0; dash[0x26] = 1; dash[0x28] = -16;
        ActorDispatcher.Step(actors, 0);
        Require(dash.X == 101 && dash[0x24] == 16 && dash[0x26] == 1,
                "Dash decay sign-extends its sixteen-bit operand");
        dash[0x24] = 8; dash[0x26] = 0; dash[0x28] = 16; dash[0x1c] = 0;
        spawn.PlayerFlags = 2; spawn.SecondPlayerY = 20; spawn.PlayerY = 180;
        ActorDispatcher.Step(actors, 0);
        Require(dash[0x1c] == 8 && dash[0x14] == 1 && dash[0x18] == 1,
                "Speed underflow commits movement then retargets using the per-record selected player");
        spawn.PlayerFlags = 0;
        DamageRules.Apply(actors, dash, new Bullet { Sprite = 29 });
        Require(dash[0x16] == 0 && dash[0x1a] == 16 && actors.PaletteSequenceOffset == 0x0707,
                "Dash damage sets pause before decrementing health");
        DamageRules.Apply(actors, dash, new Bullet { Sprite = 29 });
        Require(dash.State == 2 && spawn.HostileCount == 0, "Dash dies only after health becomes negative");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { FreeRecords = 0 };
        for (int i = 0; i < EntityPool.GameplayRecords; i++) spawn.Pool.Records[i].State = 1;
        spawn.Pool.Records[70][0x30] = 789; spawn.Pool.Records[70][0x32] = 456;
        Entity overflow = spawn.Create(4);
        Require(ReferenceEquals(overflow, spawn.Pool.Records[70]) && overflow.State == 0x0e07 &&
                overflow[0x32] == 0 && overflow[0x30] == 789 && spawn.FreeRecords == -1 && spawn.HostileCount == 1,
                "Full scripted spawn initializes record seventy with caller popup clear and retained residual fields");
        Entity obstacle = spawn.CreateObstacle();
        Require(ReferenceEquals(obstacle, overflow) && obstacle.State == 0x0100 && obstacle[0x3a] == -1 &&
                spawn.FreeRecords == -2 && !spawn.Pool.Records[71].Active,
                "Full obstacle spawn also reuses record seventy without extending the scan");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 13);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
        var sprites = new SpriteFrame();
        for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
        Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                "Actual fourteenth-wave script advances 350 protected logic ticks with dash and shooting actors");
    }

    private static void CheckBeams()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var random = new RandomSequence(table);
        var spawn = new SpawnContext(new EntityPool(), random, parameters) { DifficultyIncrement = 1 };
        Entity horizontal = spawn.Create(8), vertical = spawn.Create(9);
        Require(horizontal.State == 0x1111 && horizontal.RenderState == 2 && horizontal[0x1c] == 0x0800 &&
                vertical.State == 0x1212 && vertical.RenderState == 3 && vertical[0x1c] == 0x0400 &&
                vertical[0x2e] == 0x7070 && random.Index == 9 && spawn.HostileCount == 0,
                "Beam factories retain difficulty asymmetry, discarded vertical random byte and no hostile increment");
        var actors = new ActorContext(spawn) { FreezeTicks = 100, PrimaryStatus = 0 };
        horizontal.X = 100; horizontal.Y = 80; horizontal[0x24] = 0x0203;
        spawn.PlayerY = 82;
        ActorDispatcher.Step(actors, 0);
        Require(actors.PrimaryStatus == 0 && actors.DeathFlashTicks == 20 && horizontal[0x24] == 0x0203,
                "Beam contact passes raw distance bits to damage routing and returns before timer or movement");
        spawn.PlayerY = 84;
        ActorDispatcher.Step(actors, 0);
        Require(actors.PrimaryStatus == -2, "Raw distance four routes beam damage to primary player");
        spawn.PlayerY = 120;
        ActorDispatcher.Step(actors, 0); ActorDispatcher.Step(actors, 0);
        Require(horizontal[0x24] == 3 && horizontal.X == 100 && horizontal.Y == 80,
                "Active beam duration decrements during freeze without fixed-motion integration");
        horizontal[0x2e] = 0x0101;
        ActorDispatcher.Step(actors, 0);
        Require(horizontal[0x24] == 0x0303 && horizontal[0x2e] == 0x0101,
                "Zero-crossing delay reloads both packed timers and activates beam during freeze");
        horizontal[0x24] = 3; horizontal[0x2e] = 1;
        ActorDispatcher.Step(actors, 0);
        Require(horizontal[0x24] == 3 && horizontal[0x2e] == unchecked((short)0xff01),
                "Zero delay wraps to 255 rather than triggering activation on signed underflow");
        horizontal[0x14] = 123; horizontal[0x18] = 456;
        DamageRules.Apply(actors, horizontal, new Bullet { Sprite = 29, VelocityX = -3, VelocityY = 5 });
        Require(horizontal[0x14] == 123 && horizontal[0x18] == 456 && horizontal[0x16] == -12 &&
                horizontal[0x1a] == 20 && horizontal[0x30] == 5 && horizontal.Behavior == 17,
                "Beam impulse changes integer velocity words while preserving fractions and actor state");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0));
        horizontal = game.Actors.Spawning.Pool.Records[0]; vertical = game.Actors.Spawning.Pool.Records[1];
        horizontal.State = 17; horizontal.RenderState = 2; horizontal.X = 100; horizontal.Y = 80;
        horizontal[0x0c] = 135; horizontal[0x24] = 0x0100;
        vertical.State = 18; vertical.RenderState = 3; vertical.X = 200; vertical.Y = 120;
        vertical[0x0c] = 136; vertical[0x24] = 0x0100;
        var sprites = new SpriteFrame(); sprites.Build(game);
        int lines = 0, heads = 0;
        foreach (SpriteCommand command in sprites.Commands)
        {
            if (command.Offset == 0x7aa2) heads++;
            if (!command.IsLine) continue;
            lines++;
            Require(command.ColorIndex == 1 &&
                    ((command.X == 8 && command.EndX == 311 && command.Y == 80 && command.EndY == 80) ||
                     (command.X == 200 && command.EndX == 200 && command.Y == 10 && command.EndY == 195)),
                    "Beam pixel spans match original EGA byte/row bounds and set-reset palette index");
        }
        Require(lines == 2 && heads == 2, "Both beam renders include hard-coded head, beam span and actor body");

        game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 10);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
        for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
        Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                "Actual eleventh-wave script advances 350 protected logic ticks with split actors, beams and render commands");
    }

    private static void CheckFixedSplits()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var random = new RandomSequence(table);
        var spawn = new SpawnContext(new EntityPool(), random, parameters);
        Entity large = spawn.Create(5), medium = spawn.Create(6), small = spawn.Create(7);
        Require(large.State == 0x1010 && large[0x0c] == 123 && large[0x3e] == 4 && large[0x1c] == 0x0200 &&
                medium[0x0c] == 127 && medium[0x3e] == 5 && small[0x0c] == 131 && small[0x3e] == 0 &&
                large[0x30] == 0 && medium[0x30] == 0 && small[0x30] == 0 && random.Index == 15 &&
                spawn.HostileCount == 3 && spawn.FreeRecords == 66,
                "Split-family factories consume random delays then override them and initialize three distinct sizes");
        var actors = new ActorContext(spawn) { FreezeTicks = 100, FrameCounter = 12, PrimaryStatus = 0 };
        spawn.PlayerX = large.X; spawn.PlayerY = large.Y;
        ActorDispatcher.Step(actors, 0);
        Require(large[0x0c] == 126 && actors.PrimaryStatus == -2 && large[0x30] == 0,
                "State sixteen animates and handles contact before the fixed-motion freeze gate");

        foreach (bool four in new[] { false, true })
        {
            spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
            Entity parent = spawn.Pool.Records[0]; parent.State = 16; parent.X = 100; parent.Y = 80;
            parent[0x0c] = four ? (short)127 : (short)123; parent[0x3e] = four ? (short)5 : (short)4;
            spawn.Pool.Records[1].VelocityX = 777; spawn.Pool.Records[1].VelocityY = 888;
            spawn.Pool.Records[1][0x32] = 123;
            var bullet = new Bullet { Sprite = 29, Lifetime = 60, VelocityX = 3, VelocityY = -2 };
            actors = new ActorContext(spawn) { FreezeTicks = 0 };
            DamageRules.Apply(actors, parent, bullet);
            Entity first = spawn.Pool.Records[1], second = spawn.Pool.Records[2];
            int count = four ? 4 : 2;
            Require(parent.State == 2 && parent[0x14] == OriginalDamageData.DeathRotation[3] &&
                    bullet.Sprite == 0 && bullet.Lifetime == 0 && first.VelocityX == 777 && first.VelocityY == 888 &&
                    first[0x32] == 123 && first[0x16] == -4 && first[0x1a] == 6 && second[0x16] == 4 &&
                    second[0x1a] == (four ? 6 : -6) && first[0x3a] == (four ? 7 : 6) && first[0x30] == 3 &&
                    spawn.HostileCount == count && spawn.FreeRecords == 69 && actors.KillCount == 1 &&
                    !spawn.Pool.Records[count + 1].Active,
                    "Splitting preserves swapped initial velocity axes, projectile-register writes, popup/residual words and counters");
            if (four) Require(spawn.Pool.Records[3][0x16] == 4 && spawn.Pool.Records[3][0x1a] == -6 &&
                              spawn.Pool.Records[4][0x16] == -4 && spawn.Pool.Records[4][0x1a] == -6,
                              "Four-child split preserves its sign-flip order");
        }
        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
        for (int i = 0; i < EntityPool.GameplayRecords; i++) spawn.Pool.Records[i].State = 1;
        large = spawn.Pool.Records[0]; large.State = 16; large[0x0c] = 123; large[0x3e] = 4;
        var fullBullet = new Bullet { Sprite = 29, Lifetime = 60 };
        actors = new ActorContext(spawn);
        DamageRules.Apply(actors, large, fullBullet);
        Require(!spawn.Pool.Records[70].Active && spawn.Pool.Records[70][0x3a] == 6 &&
                spawn.HostileCount == 0 && fullBullet.Sprite == 29 && fullBullet.Lifetime == 60 && spawn.Random.Index == 0,
                "Full split allocation writes the kind at record seventy without initializing it or mutating the projectile");
        large.State = 16; large[0x3e] = 5; actors.HotBulletTimer = 0; spawn.HostileCount = 1;
        spawn.Pool.Records[70][0x3a] = 123;
        DamageRules.Apply(actors, large, fullBullet);
        Require(spawn.Pool.Records[70][0x3a] == 123 && fullBullet.Sprite == 29 && spawn.HostileCount == 0,
                "Hot projectile bypasses all split allocation and side effects");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 15);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
        var sprites = new SpriteFrame();
        for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
        Require(game.Actors.SpawnComplete, "Actual sixteenth-wave script and splitting-family actors advance through 350 logic ticks");
    }

    private static void CheckContactEmitters()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        parameters.Apply(OriginalWaves.Decode()[11], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        spawn.Pool.Records[0][0x3c] = 456; spawn.Pool.Records[0][0x3e] = 123;
        Entity falling = spawn.Create(28), emitter = spawn.Create(29);
        Require(falling.State == 0x2626 && falling.X == 160 && falling.Y == 30 && falling[0x1c] == 2 &&
                falling[0x3c] == 456 && falling[0x3e] == 123 && emitter.State == 0x2727 &&
                emitter[0x10] == 0 && emitter[0x16] == 0x1010 && spawn.HostileCount == 1,
                "Contact-actor factories retain untouched sound/damage fields and only the emitter counts as hostile");

        table[0] = 1; table[1] = 255; table[2] = 0; table[3] = 128;
        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
        emitter = spawn.Pool.Records[0]; emitter.State = 39; emitter.X = 100; emitter.Y = 80;
        emitter.VelocityX = 1; emitter.VelocityY = 2; emitter[0x16] = 16;
        var actors = new ActorContext(spawn) { FreezeTicks = 0 };
        ActorDispatcher.Step(actors, 0);
        Entity shot = spawn.Pool.Records[1];
        Require(emitter.X == 101 && emitter.Y == 82 && emitter[0x16] == 0x1010 &&
                shot.State == 15 && shot.X == 303 && shot.Y == 99 && shot[0x0c] == 274 &&
                shot.VelocityX == -1 && shot.VelocityY == 1 && spawn.FreeRecords == 68,
                "Emitter restores its moved position after side-origin shot using signed arithmetic half");
        emitter[0x16] = 16; actors.FreezeTicks = 100;
        ActorDispatcher.Step(actors, 0);
        Require(emitter.X == 102 && emitter.Y == 84 && spawn.Random.Index == 4 &&
                !spawn.Pool.Records[2].Active && spawn.FreeRecords == 68,
                "Emitter keeps movement, timer and random consumption during freeze but suppresses allocation");
        actors.FreezeTicks = 0; actors.PrimaryStatus = 0;
        spawn.PlayerX = emitter.X; spawn.PlayerY = emitter.Y;
        ActorDispatcher.Step(actors, 0);
        Require(emitter.State == 39 && emitter[0x14] == 1 && emitter[0x0c] == 273 && actors.PrimaryStatus == 0 &&
                spawn.HostileCount == 1 && spawn.FreeRecords == 68 && spawn.Random.Index == 4,
                "Contact deactivates emitter once without hurting player, releasing record or changing spawn kind");
        ActorDispatcher.Step(actors, 0);
        Require(spawn.HostileCount == 1 && emitter.X == 102 && emitter.Y == 84,
                "Deactivated emitter stays stationary and does not decrement hostiles twice");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        falling = spawn.Create(28);
        actors = new ActorContext(spawn) { FreezeTicks = 10, FrameCounter = 0 };
        ActorDispatcher.Step(actors, 0);
        Require(falling.X == 160 && falling[0x1c] == 2, "Falling actor freeze gate precedes pursuit and contact");
        actors.FreezeTicks = 0; spawn.PlayerX = 160; spawn.PlayerY = 104;
        ActorDispatcher.Step(actors, 0);
        Require(falling.X == 161 && falling.Y == 30 && falling[0x1c] == 0 && falling[0x18] == 0x2000,
                "Falling actor steps right at equal target X and starts fractional acceleration from resting phase");
        falling.Y = 183; falling[0x16] = 183; falling[0x14] = 0;
        falling[0x18] = 0; falling[0x1a] = 1; actors.FrameCounter = 1;
        ActorDispatcher.Step(actors, 0);
        Require(falling.Y == 183 && falling[0x16] == 184 && falling[0x1c] == 1,
                "Floor crossing retains overshoot in fractional state but delays visible Y update");
        spawn.PlayerY = 20;
        ActorDispatcher.Step(actors, 0);
        Require(falling.Y == 183 && falling[0x16] == 183 && falling[0x1c] == 1,
                "Returning actor decrements stored height by one without restarting fall above the player");

        foreach (int wave in new[] { 8, 11 })
        {
            var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: wave);
            game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
            var sprites = new SpriteFrame();
            for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
            Require(game.Actors.SpawnComplete, $"Actual wave {wave + 1} advances through 350 logic ticks");
        }
    }

    private static void CheckFixedShooter()
    {
        static void Pair(Entity entity, int offset, int value)
        {
            entity[offset] = unchecked((short)value); entity[offset + 2] = unchecked((short)(value >> 16));
        }
        static int ReadPair(Entity entity, int offset) => unchecked((ushort)entity[offset] | entity[offset + 2] << 16);
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        Entity entity = spawn.Pool.Records[0]; entity.State = 35; entity.X = 100; entity.Y = 80;
        Pair(entity, 0x26, (100 << 16) | 0xffff); Pair(entity, 0x2a, 80 << 16);
        Pair(entity, 0x14, 0x1000); Pair(entity, 0x1c, 0x400); Pair(entity, 0x20, 0x400);
        var actors = new ActorContext(spawn) { FreezeTicks = 0 };
        FixedMotion.Step(actors, 0);
        Require(entity.X == 101 && ReadPair(entity, 0x26) == ((101 << 16) | 0x0fff) && entity[0x30] == -1,
                "Fixed-point position carries across words and zero delay skips acceleration once");
        Pair(entity, 0x26, 100 << 16); entity.X = 100; entity[0x30] = 0;
        Pair(entity, 0x14, 0xffff); Pair(entity, 0x1c, 0x1000);
        FixedMotion.Step(actors, 0);
        Require(ReadPair(entity, 0x14) == 0x20000 && entity.X == 102,
                "Original clamp compares low words even when magnitude high word is smaller than limit");
        Pair(entity, 0x26, 9 << 16); entity.X = 9; entity[0x30] = 0;
        Pair(entity, 0x14, -0x10000); Pair(entity, 0x1c, 0x800);
        Pair(entity, 0x2a, 182 << 16); entity.Y = 182; Pair(entity, 0x18, 0x10000); Pair(entity, 0x20, 0x800);
        FixedMotion.Step(actors, 0);
        Require(entity.X == 9 && entity.Y == 182 && ReadPair(entity, 0x26) == 9 << 16 &&
                ReadPair(entity, 0x2a) == 182 << 16 && ReadPair(entity, 0x14) == 0 && ReadPair(entity, 0x18) == 0,
                "Exact fixed-motion wall limits reject candidate positions and zero velocity without snapping");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { WaveIndex = 6 };
        entity = spawn.Create(25);
        Require(entity.State == 0x2323 && entity[0x1c] == 0x0320 && entity[0x20] == 0x0620 &&
                entity[0x28] == entity.X && entity[0x2c] == entity.Y && entity[0x24] == 0x1818,
                "Fixed shooter initializes random acceleration pairs, cached positions and packed health");
        actors = new ActorContext(spawn) { FreezeTicks = 10 };
        entity.VelocityX = 0;
        ActorDispatcher.Step(actors, 0);
        Require(entity[0x30] == 0 && spawn.HostileCount == 1 && entity.VelocityX == 0,
                "Firing timer expires and reloads during freeze while movement and allocation stop");
        actors.FreezeTicks = 0; spawn.PlayerX = 250;
        ActorDispatcher.Step(actors, 0);
        Entity shot = spawn.Pool.Records[1];
        Require(shot.State == 15 && shot[0x0c] == 254 && shot.VelocityX == 1 &&
                spawn.HostileCount == 2 && spawn.FreeRecords == 67,
                "Early-wave fixed shooter emits the original horizontal projectile");
        spawn.WaveIndex = 50; entity.VelocityX = 0; spawn.Pool.Records[2][0x32] = 777;
        short free = spawn.FreeRecords;
        ActorDispatcher.Step(actors, 0);
        shot = spawn.Pool.Records[2];
        Require(shot.State == 0x2424 && shot[0x16] == 4 && shot[0x18] == 4 && shot[0x32] == 0 &&
                spawn.FreeRecords == free && spawn.HostileCount == 3,
                "Later-wave fixed shooter allocates directional child with cleared popup and unchanged free counter");
        shot.X = 100; shot.Y = 80; shot[0x16] = 7; shot[0x18] = 0;
        spawn.PlayerFlags = 2; spawn.SecondPlayerX = 20; spawn.SecondPlayerY = 20;
        ActorDispatcher.Step(actors, 2);
        Require(shot[0x16] == 0 && shot.X == 99 && shot.Y == 80 && shot[0x0c] == 310,
                "Directional child targets selected player and advances heading even when desired delta is zero");
        free = spawn.FreeRecords;
        for (int i = 0; i < 24; i++) DamageRules.Apply(actors, entity, new Bullet { Sprite = 29 });
        Require(entity.State == -1 && entity[0x24] == 24 && spawn.FreeRecords == free && actors.KillCount == 0 &&
                spawn.HostileCount == 2 && actors.PaletteSequenceOffset == 0x072d,
                "Fixed-shooter byte health expires at zero without common kill, score or free-counter effects");

        foreach (short outcome in new short[] { 10, 11 })
        {
            spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1 };
            entity = spawn.Pool.Records[0]; entity.State = 3; entity.X = 100; entity.Y = 80;
            entity[0x3e] = 3; entity[0x18] = 0; entity[0x1e] = outcome;
            spawn.Pool.Records[1][0x32] = 777;
            actors = new ActorContext(spawn);
            DamageRules.Apply(actors, entity, new Bullet { Sprite = 29 });
            shot = spawn.Pool.Records[1];
            Require(entity.State == 2 && shot.State == 0x2424 && shot[0x18] == 0x2004 &&
                    shot[0x16] == (outcome == 10 ? 1 : 0) && shot[0x32] == 0 &&
                    !spawn.Pool.Records[2].Active && spawn.HostileCount == 1 && spawn.FreeRecords == 69 && actors.KillCount == 1,
                    "Directional death outcome emits only one child because the allocator returns BP=-1, then performs common kill");
        }

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 6);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
        var sprites = new SpriteFrame();
        for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
        Require(game.Actors.SpawnComplete, "Actual seventh-wave script and fixed-motion actors advance through 350 logic ticks");
    }

    private static void CheckCenterSpawner()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var random = new RandomSequence(table);
        var spawn = new SpawnContext(new EntityPool(), random, parameters);
        Entity parent = spawn.Create(15);
        Require(parent.State == 0x1818 && parent.X == 160 && parent.Y == 30 && parent.VelocityY == 1 &&
                parent[0x18] == 100 && parent[0x1e] == 3 && random.Index == 0 && spawn.HostileCount == 1,
                "Center-spawner factory uses fixed coordinates without position random draws");
        var actors = new ActorContext(spawn) { FreezeTicks = 100, PrimaryStatus = 0 };
        parent.X = 50; parent.Y = 183; spawn.PlayerX = 160; spawn.PlayerY = 183;
        ActorDispatcher.Step(actors, 0);
        Require(parent.X == 160 && parent.Y == 183 && parent.VelocityY == -1 && actors.PrimaryStatus == 0 &&
                parent[0x0c] == 180 && random.Index == 1,
                "Parent pins X, cancels boundary overshoot, skips contact and ignores freeze");

        random = new RandomSequence(new byte[512]);
        spawn = new SpawnContext(new EntityPool(), random, parameters) { HostileCount = 1 };
        parent = spawn.Pool.Records[0]; parent.State = 24; parent.Y = 30; parent.VelocityY = 1; parent[0x14] = 1;
        spawn.Pool.Records[1][0x32] = 444;
        actors = new ActorContext(spawn) { FreezeTicks = 100 };
        ActorDispatcher.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        Require(parent[0x0c] == 180 && parent[0x14] == 0 && child.State == 0x1919 &&
                child.X == 160 && child.Y == 31 && child.VelocityX == -1 && child.VelocityY == -1 &&
                child[0x32] == 444 && spawn.FreeRecords == 69 && spawn.HostileCount == 2 && random.Index == 4,
                "Parent animation precedes phase toggle and child creation retains popup/free counter");
        child.X = 311; child.Y = 80; child.VelocityX = 2; child.VelocityY = 0;
        actors.FrameCounter = 4; ActorDispatcher.Step(actors, 1);
        Require(!child.Active && spawn.HostileCount == 1 && spawn.FreeRecords == 70 && child[0x0c] == 182,
                "First child animates then releases on a wall bounce");
        child.State = 26; child.X = 311; child.Y = 80; child.VelocityX = 2; child[0x3e] = 7; child[0x18] = 1;
        child[0x16] = 99;
        ActorDispatcher.Step(actors, 1);
        Require(child.Active && child.X == 309 && child.VelocityX == -2 && child[0x0c] == 184,
                "Death offspring animate and survive their wall bounce");
        DamageRules.Apply(actors, child, new Bullet { Sprite = 29, VelocityX = -3, VelocityY = 5 });
        Require(child[0x18] == 0 && child.X == 309 && child.Y == 80 && child.VelocityX == -2 &&
                child.VelocityY == 2 && child[0x16] == 99,
                "Damage seven replaces velocity using signed halves without positional knockback or flash timer");
        DamageRules.Apply(actors, child, new Bullet { Sprite = 29 });
        Require(child.State == 2, "Damage seven kills only after its health becomes negative");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(new byte[512]), parameters)
            { HostileCount = 1, FreeRecords = 17 };
        for (int i = 0; i < 69; i++) spawn.Pool.Records[i].State = 1;
        parent = spawn.Pool.Records[0]; parent.State = 24; parent.X = 160; parent.Y = 90;
        parent[0x3e] = 3; parent[0x18] = 0; parent[0x1e] = 3; parent[0x3a] = 15;
        spawn.Pool.Records[69][0x32] = 777;
        actors = new ActorContext(spawn);
        DamageRules.Apply(actors, parent, new Bullet { Sprite = 29 });
        Require(parent.State == -1 && parent[0x3a] == 15 && actors.KillCount == 0 &&
                spawn.HostileCount == 10 && spawn.FreeRecords == 17 &&
                spawn.Pool.Records[69][0x32] == 777 && spawn.Pool.Records[78].State == 0x1a1a &&
                spawn.Pool.Records[78][0x3e] == 7 && spawn.Pool.Records[78][0x18] == 3 &&
                !spawn.Pool.Records[79].Active,
                "Boss transformation creates ten consecutive offspring beyond scan boundary without common kill or free-counter effects");
    }

    private static void CheckHunterVariants()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters();
        parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        parameters.Apply(OriginalWaves.Decode()[4], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        Entity producer = spawn.Create(13), shooter = spawn.Create(24), alarm = spawn.Create(38);
        Require(producer.State == 0x1607 && producer[0x18] == 0 && producer[0x1a] == 0x0606 &&
                producer[0x22] == 0x0707 && producer[0x20] == 1 && producer[0x0e] == 40 &&
                shooter.State == 0x0e0e && shooter[0x0c] == 79 && shooter[0x20] == 2 &&
                alarm.State == 0x0e0e && alarm[0x0c] == 299 && alarm[0x20] == 3 && alarm[0x3c] == 0x0309 &&
                spawn.HostileCount == 3, "Hunter variants retain distinct cadence, action, rendering and health fields");

        var random = new RandomSequence(new byte[512]);
        spawn = new SpawnContext(new EntityPool(), random, parameters) { HostileCount = 1 };
        producer = spawn.Pool.Records[0]; producer.State = 22;
        producer.X = 100; producer.Y = 80; producer[0x22] = 7; producer[0x1a] = 0x0606;
        spawn.Pool.Records[1][0x32] = 777;
        var actors = new ActorContext(spawn) { FreezeTicks = 100 };
        int sounds = 0;
        actors.SoundRequested += (al, cl, ch) => { if (al == 7 && cl == 1 && ch == 4) sounds++; };
        ActorDispatcher.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        Require(child.State == 0x1717 && child.X == 100 && child.Y == 80 && child[0x3a] == -1 &&
                child[0x32] == 777 && child[0x16] == 100 && child[0x24] == 80 &&
                spawn.HostileCount == 2 && spawn.FreeRecords == 69 && sounds == 1 &&
                producer[0x22] == 0x0707 && producer[0x1a] == 0x0606 && random.Index == 1,
                "Producer allocation runs during freeze, retains popup, increments hostiles but leaves free count unchanged");
        ActorDispatcher.Step(actors, 0);
        Require(random.Index == 1 && producer[0x22] == 0x0607,
                "Producer high-byte countdown avoids consuming random bytes until underflow");
        for (int i = 0; i < EntityPool.GameplayRecords; i++) spawn.Pool.Records[i].State = 1;
        producer.State = 22; producer[0x22] = 0;
        ActorDispatcher.Step(actors, 0);
        Require(spawn.HostileCount == 2 && !spawn.Pool.Records[70].Active && sounds == 1 && random.Index == 2,
                "Full producer scan stops at 70 records and consumes its random attempt without a sound");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 4);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
        for (int i = 0; i < 45; i++) game.Step(0);
        int producers = 0;
        foreach (Entity entity in game.Actors.Spawning.Pool.Records) if (entity.Active && entity[0x3a] == 13) producers++;
        Require(game.Actors.SpawnComplete && producers == 6,
                "Actual fifth wave creates its six producer hunters and completes the script");
        var sprites = new SpriteFrame();
        for (int i = 0; i < 256; i++) { game.Step(0); sprites.Build(game); }
    }

    private static void CheckAxisChase()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var random = new RandomSequence(table);
        var parameters = new WaveParameters();
        parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        parameters.Apply(WaveDefinition.Decode(new byte[] { 0xfd, 1, 3, 2, 0, 0, 0xff, 0xff }), 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), random, parameters);
        Entity actor = spawn.Create(14);
        Require(actor.State == 0x1717 && actor[0x12] == 0x0aff && actor[0x0c] == 175 &&
                actor[0x16] == actor.X && actor[0x24] == actor.Y && spawn.HostileCount == 1,
                "Axis-chase spawn initializes its four history pairs and packed axis selector");
        actor.X = 100; actor.Y = 80;
        var context = new ActorContext(spawn) { FreezeTicks = 100, FrameCounter = 6 };
        spawn.PlayerFlags = 2; spawn.PlayerX = 200; spawn.PlayerY = 120;
        spawn.SecondPlayerX = 20; spawn.SecondPlayerY = 20;
        int before = random.Index;
        ActorDispatcher.Step(context, 0);
        Require(actor.X == 97 && actor.Y == 80 && actor[0x12] == 0x0a01 && actor[0x0c] == 174 &&
                actor[0x14] == 4 && actor[0x1a] == 100 && actor[0x1c] == 80 && random.Index == before,
                "First axis choice consumes no random byte, aims at the record-selected player and ignores freeze");
        ActorDispatcher.Step(context, 0);
        Require(actor.X == 94 && actor.Y == 80 && random.Index == ((before + 1) & 511),
                "Random values above the inclusive threshold retain velocity");
        actor[0x12] = unchecked((short)0xff01);
        spawn.PlayerY = 10;
        ActorDispatcher.Step(context, 0);
        Require(actor.X == 94 && actor.Y == 77 && actor[0x12] == unchecked((short)0xff00),
                "Unsigned high-byte threshold toggles to vertical pursuit");
        context.PrimaryStatus = 0; spawn.PlayerX = actor.X; spawn.PlayerY = actor.Y;
        short cursor = actor[0x14]; before = random.Index;
        ActorDispatcher.Step(context, 0);
        Require(context.PrimaryStatus == -2 && actor[0x14] == cursor && random.Index == before && actor.Y == 77,
                "Contact exits before animation, history, random choice or movement");
    }

    private static void CheckHunters()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters);
        spawn.Pool.Records[0][0x3c] = 1234;
        Entity basic = spawn.Create(2), timed = spawn.Create(4), converted = spawn.Create(12);
        Require(basic.State == 0x0807 && basic[0x3e] == 1 && basic[0x3c] == 1234 &&
                timed.State == 0x0e07 && timed[0x1a] == 0x0505 && timed[0x18] == 1 &&
                converted.State == 0x1507 && converted[0x3a] == 12 && converted[0x1a] == 0x0101 &&
                spawn.HostileCount == 2, "Hunter factories retain distinct cadence/health fields and basic hunters do not count as hostiles");

        (ActorContext Context, Entity Hunter, Entity Herd) Fixture(bool slow = false)
        {
            var world = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HerdCount = 1, HostileCount = 1 };
            var context = new ActorContext(world) { FreezeTicks = 0, FrameCounter = 8 };
            Entity hunter = world.Pool.Records[10], herd = world.Pool.Records[1];
            hunter.State = slow ? (short)8 : (short)14; hunter.X = 40; hunter.Y = 50;
            hunter[0x14] = -1; hunter[0x1c] = 111;
            herd.State = 5; herd.X = 100; herd.Y = 80; herd[0x3a] = 0;
            return (context, hunter, herd);
        }
        var f = Fixture();
        ActorDispatcher.Step(f.Context, 10);
        Require(f.Hunter[0x14] == 1 && f.Herd[0x18] == 1 && f.Hunter.X == 40,
                "Hunter claims an unclaimed herd record without moving on its selection tick");
        ActorDispatcher.Step(f.Context, 10);
        Require(f.Hunter.X == 43 && f.Hunter.Y == 51 && f.Hunter[0x0c] == 117,
                "Timed hunter uses scaled record targeting and arithmetic diagonal steering");
        foreach (short action in new short[] { 0, 1, 2, 3 })
        {
            f = Fixture(); f.Hunter[0x14] = 1; f.Hunter[0x20] = action;
            f.Hunter.X = 100; f.Hunter.Y = 80;
            ActorDispatcher.Step(f.Context, 10);
            Require(f.Context.Spawning.HerdCount == 0, "All four timed-hunter actions remove one herd member");
            if (action == 1)
                Require(f.Herd.State == 0x1507 && f.Herd[0x3a] == 0 && f.Hunter[0x14] == 1 &&
                        f.Context.Spawning.HostileCount == 2, "Herd conversion retains its retry kind and hunter's target until revalidation");
            else
                Require(f.Herd.State == 9 && f.Herd[0x0c] == 51 && f.Herd[0x14] == 20 && f.Herd[0x3a] == -1 &&
                        f.Hunter[0x14] == -1, "Herd death creates the original timed message and releases the target");
            if (action == 2)
                Require(f.Context.Spawning.HostileCount == 5 && f.Context.Spawning.Pool.Records[0][0x0c] == 235 &&
                        f.Context.Spawning.Pool.Records[2].VelocityY == 1, "Burst-on-herd action emits four directed child projectiles");
            if (action == 3) Require(f.Context.Announcement == 10, "Alarm action preserves the announcement for the next kill");
        }

        f = Fixture(true); ActorDispatcher.Step(f.Context, 10);
        f.Context.FrameCounter = 16; ActorDispatcher.Step(f.Context, 10);
        Require(f.Hunter[0x14] == -2 && f.Herd.State == 5 && f.Herd[0x18] == 1,
                "Basic hunter preserves the unscaled-byte target quirk and falls back after claiming an inaccessible herd index");
        f.Context.FrameCounter = 24; ActorDispatcher.Step(f.Context, 10);
        Require(f.Hunter.X == 43 && f.Hunter.Y == 51, "Basic hunter falls back to selected-player steering");
        f = Fixture(true); Entity raw = f.Context.Spawning.Pool.Records[0];
        raw.State = 0x0500; raw.X = 0x6400; raw.Y = 0x5000; raw.RenderState = 0;
        raw[0x3a] = 0x1234; raw[0x3c] = 0x5678;
        f.Hunter[0x14] = 1; f.Hunter.X = 100; f.Hunter.Y = 80;
        ActorDispatcher.Step(f.Context, 10);
        Require(raw.State == 0x0900 && raw[0x3a] == unchecked((short)0xff34) && raw[0x3c] == 0x56ff &&
                f.Herd.State == 5, "Unaligned target writes retain the source's byte-level effects instead of correcting its pointer");

        f = Fixture(); f.Context.FreezeTicks = 1;
        HunterProjectiles.EmitFour(f.Context, f.Hunter);
        Require(f.Context.Spawning.HostileCount == 4 && f.Context.Spawning.Pool.Records[0][0x0c] == 236,
                "Only the first of four emitted projectiles is suppressed by freeze");
        f = Fixture(); f.Hunter.State = 21; f.Hunter.X = 150; f.Hunter.Y = 100; f.Context.FreezeTicks = 1;
        int sounds = 0; f.Context.SoundRequested += (_, _, _) => sounds++;
        f.Context.SoundStatus = () => 12; ActorDispatcher.Step(f.Context, 10);
        f.Context.SoundStatus = () => 0; ActorDispatcher.Step(f.Context, 10);
        Require(sounds == 1, "Converted hunter's proximity sound checks driver status before its freeze gate");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), parameters) { HostileCount = 1, FreeRecords = 10 };
        var actors = new ActorContext(spawn) { FreezeTicks = 0 };
        for (int i = 0; i < 59; i++) spawn.Pool.Records[i].State = 1;
        Entity parent = spawn.Pool.Records[0]; parent.State = 14; parent[0x3e] = 3; parent[0x1e] = 1;
        spawn.Pool.Records[59][0x32] = 123;
        DamageRules.Apply(actors, parent, new Bullet { Sprite = 29 });
        Require(spawn.Pool.Records[59][0x0c] == 122 && spawn.Pool.Records[62][0x0c] == 119 &&
                spawn.Pool.Records[59][0x32] == 123 && spawn.Pool.Records[59][0x3e] == 16 &&
                spawn.HostileCount == 4 && spawn.FreeRecords == 6 && parent.State == 2,
                "Death burst preserves reverse pattern order, consecutive allocation past slot 59, popup words and counters");
        for (int i = 0; i < 60; i++) spawn.Pool.Records[i].State = 1;
        short hostiles = spawn.HostileCount;
        HunterProjectiles.Burst(actors, parent, OriginalHunterData.Diagonal, 1);
        Require(spawn.HostileCount == hostiles && actors.BurstFlag == 1,
                "Burst does not begin its search beyond slot 59 when the initial scan budget is exhausted");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 2);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000;
        for (int i = 0; i < 56; i++) game.Step(0);
        Require(game.Actors.SpawnComplete && game.Actors.Spawning.HostileCount == 42 && game.Actors.Spawning.HerdCount == 2,
                "Actual third-wave script creates its hunter plus 41 chasers and two herd members");
        var sprites = new SpriteFrame();
        for (int i = 0; i < 200; i++) { game.Step(0); sprites.Build(game); }
    }
}
