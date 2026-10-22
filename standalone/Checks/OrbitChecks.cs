using System;
using Llamatron.Core;

static class OrbitChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Orbit: {message}");
    }

    public static void Run()
    {
        var parameters = new WaveParameters(); parameters.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var random = RandomSequence.Shuffle(() => 0);
        var spawn = new SpawnContext(new EntityPool(), random, parameters) { OrbitPhase = 255 };
        Entity orbit = spawn.Create(21);
        Require(random.Index == 0 && orbit.X == 159 && orbit.Y == 173 && orbit.State == 0x2020 &&
                orbit[0x14] == 0x01ff && orbit[0x16] == 10 && orbit[0x18] == 0x3030 &&
                spawn.OrbitPhase == 319 && spawn.OrbitAcceleration == 0x20000 &&
                parameters.ChaseMaxX == 1 && parameters.ChaseMaxY == 1,
                "factory consumes no position randomness and uses signed S.CMP bytes with wrapped quarter turn");
        var actors = new ActorContext(spawn) { PrimaryStatus = 0, FreezeTicks = 1 };
        spawn.PlayerX = orbit.X; spawn.PlayerY = orbit.Y;
        OrbitActor.Step(actors, 0);
        Require(orbit[0x14] == 0x01ff && orbit[0x18] == 0x3030 && actors.PrimaryStatus == 0,
                "emitter freezes before timer, movement and any contact");
        actors.FreezeTicks = 0;
        OrbitActor.Step(actors, 0);
        Require(orbit[0x14] == 0x0100 && orbit.X == 160 && orbit.Y == 173 &&
                orbit[0x18] == 0x2f30 && spawn.OrbitAcceleration == 0x20040 &&
                parameters.ChaseMaxX == 2 && actors.PrimaryStatus == 0,
                "phase wraps at one byte and shared chase limits follow accumulator high word without contact");
        orbit[0x18] = 0x0030; spawn.Pool.Records[1][0x32] = 432;
        OrbitActor.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        Require(child.State == 0x0303 && child[0x3a] == -1 && child[0x32] == 432 &&
                spawn.HostileCount == 2 && spawn.FreeRecords == 68,
                "emitted chaser preserves popup and becomes ineligible for survivor retry");
        for (int i = 0; i < 61; i++) spawn.Pool.Records[i].State = 0x0101;
        orbit.State = 0x2020; orbit[0x18] = 0x0030;
        spawn.Pool.Records[61].State = 0x0303; spawn.Pool.Records[61][0x3a] = 17;
        OrbitActor.Step(actors, 0);
        Require(spawn.Pool.Records[61].State == 0x0303 && spawn.Pool.Records[61][0x3a] == -1 &&
                spawn.HostileCount == 2, "failed chaser scan still writes slot sixty-one kind");
        spawn.OrbitAcceleration = 0x0002ffc0; orbit[0x18] = 0x3030;
        OrbitActor.Step(actors, 0);
        Require(spawn.OrbitAcceleration == 0x30000 && parameters.ChaseMaxX == 3,
                "shared acceleration carries from low to high word");

        spawn.Pool.Reset(); actors.FreezeTicks = 50; spawn.OrbitPhase = 123;
        Entity shooter = spawn.Create(22);
        Require(shooter.State == 0x2121 && shooter.X == 160 && shooter.Y == 173 && shooter[0x16] == 20 &&
                shooter[0x18] == 0x0f0f && spawn.OrbitPhase == 187 && spawn.OrbitAcceleration == 0x30000 &&
                parameters.ChaseMaxX == 3, "shooter starts at phase zero without resetting shared speed globals");
        shooter[0x18] = 0x050f; shooter[0x1a] = 0;
        OrbitActor.Step(actors, 0);
        Require(shooter[0x0c] == 228 && shooter[0x1a] == -1 && shooter[0x18] == 0x040f &&
                shooter[0x14] == 0x0101 && !spawn.Pool.Records[1].Active,
                "shooter animates and advances during freeze while suppressing its count-five shot");
        actors.FreezeTicks = 0; shooter[0x18] = 0x050f;
        spawn.PlayerX = 0; spawn.PlayerY = 0;
        OrbitActor.Step(actors, 0);
        child = spawn.Pool.Records[1];
        Require(child.State == 15 && child[0x0c] == 230 && child.VelocityX == -2 && child.VelocityY == -1,
                "count-five shot uses selected player and signed two-by-one projectile velocity");
        shooter[0x18] = 0x000f; OrbitActor.Step(actors, 0);
        Require(shooter[0x18] == 0x0f0f, "shooter timer reloads only after signed byte underflow");
        DamageRules.Apply(actors, shooter, new Bullet { Sprite = 29 });
        Require(shooter[0x1a] == 16 && shooter[0x16] == 19,
                "existing damage eleven supplies hit flash and signed word health");
        foreach (int wave in new[] { 23, 36 })
        {
            var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: wave);
            game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
            var sprites = new SpriteFrame();
            for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
            Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                    $"actual wave {wave + 1} advances 350 protected logic ticks and render commands");
        }
    }
}
