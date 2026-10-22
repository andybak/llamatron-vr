using System;
using Llamatron.Core;

static class EdgeShooterChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Edge shooter: {message}");
    }

    private static SpawnContext Spawn(bool configured = true)
    {
        var p = new WaveParameters(); p.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        if (configured) p.Apply(WaveDefinition.Decode(new byte[] { 0xf9, 4, 2, 3, 0, 0xff, 0xff }), 0, _ => { });
        var bytes = new byte[512]; Array.Fill(bytes, (byte)32);
        return new SpawnContext(new EntityPool(), new RandomSequence(bytes), p);
    }

    public static void Run()
    {
        foreach (byte kind in new byte[] { 34, 35, 36, 37 })
        {
            var spawn = Spawn(); spawn.Pool.Records[0][0x30] = 321;
            Entity e = spawn.Create(kind);
            bool horizontal = kind == 34 || kind == 35;
            Require(e.State == 0x2a2a && e.RenderState == 13 && e[0x3e] == 15 &&
                    e[0x24] == 3 && e[0x2e] == 0x0202 && e[0x30] == 321 &&
                    e[0x28] == e.X && e[0x2c] == e.Y && spawn.Random.Index == 5 &&
                    spawn.HostileCount == 1 && spawn.FreeRecords == 68,
                    $"kind {kind} retains delay and uses four position draws plus one acceleration draw");
            Require((horizontal ? e[0x1c] == 0x0420 && e[0x20] == 0 : e[0x1c] == 0 && e[0x20] == 0x0420) &&
                    e[0x1e] == 0 && e[0x22] == 0 &&
                    (kind == 34 ? e.Y == 190 && e.VelocityY == -2 && e[0x0c] == 294 :
                     kind == 35 ? e.Y == 17 && e.VelocityY == 2 && e[0x0c] == 296 :
                     kind == 36 ? e.X == 8 && e.VelocityX == 2 && e[0x0c] == 297 :
                                  e.X == 311 && e.VelocityX == -2 && e[0x0c] == 295),
                    $"kind {kind} has original edge, firing direction, sprite and acceleration axis");
            var actors = new ActorContext(spawn) { PrimaryStatus = 30000, SecondaryStatus = 30000, FreezeTicks = 50 };
            e[0x2e] = 0x0002; short x = e.X, y = e.Y;
            EdgeShooterActor.Step(actors, 0);
            Require(e[0x2e] == 0x0202 && e.X == x && e.Y == y && e[0x30] == 321 &&
                    !spawn.Pool.Records[1].Active, "freeze preserves movement/delay but reloads expired shot timer");
            actors.FreezeTicks = 0; e[0x2e] = 0x0002;
            EdgeShooterActor.Step(actors, 0);
            Entity shot = spawn.Pool.Records[1];
            Require(shot.State == 15 && shot[0x0c] == 298 && shot.X == x && shot.Y == y &&
                    shot.VelocityX == e.VelocityX && shot.VelocityY == e.VelocityY &&
                    shot[0x3a] == -1 && spawn.FreeRecords == 67 && spawn.HostileCount == 2 && e[0x30] == 320,
                    "shot precedes fixed motion and uses separate projectile direction words");
            actors.PrimaryStatus = 0; spawn.PlayerX = e.X; spawn.PlayerY = e.Y;
            e[0x2e] = 0x0102; EdgeShooterActor.Step(actors, 0);
            Require(e[0x2e] == 0x0102 && actors.PrimaryStatus == -2,
                    "contact returns before timer and motion");
        }
        var defaults = Spawn(false); Entity zero = defaults.Create(35);
        Require(zero[0x1c] == 0 && zero[0x20] == 32 && zero[0x24] == 0 && zero[0x2e] == 0,
                "zero low-X acceleration routes random OR into Y and preserves load-image defaults");
        var directional = Spawn(); Entity fish = directional.Create(26);
        Require(directional.Random.Index == 4 && fish.State == 0x2424 && fish[0x0c] == 260 &&
                fish[0x1a] == 260 && fish[0x18] == 0x0120 && fish[0x3c] == 0x031a &&
                fish[0x16] == 0 && directional.HostileCount == 1 && directional.FreeRecords == 68,
                "kind twenty-six shares directional movement with its own sprite base, cadence and sound");
        foreach (int wave in new[] { 45, 66, 67, 68 })
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
