using System;
using Llamatron.Core;

static class PursuitEmitterChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Pursuit emitter: {message}");
    }

    private static (SpawnContext Spawn, ActorContext Actors, Entity Parent) Create(byte kind)
    {
        var p = new WaveParameters(); p.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var bytes = new byte[512]; Array.Fill(bytes, (byte)32);
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(bytes), p);
        Entity parent = spawn.Create(kind);
        return (spawn, new ActorContext(spawn) { FreezeTicks = 0, PrimaryStatus = 30000, SecondaryStatus = -1 }, parent);
    }

    public static void Run()
    {
        Require(LineMotion.Advance(10, 10, 20, 13, 0) == ((short)11, (short)10) &&
                LineMotion.Advance(10, 10, 20, 13, 1) == ((short)12, (short)11) &&
                LineMotion.Advance(20, 13, 10, 10, 0) == ((short)19, (short)13) &&
                LineMotion.Advance(10, 10, 13, 20, 0) == ((short)10, (short)11) &&
                LineMotion.Advance(10, 10, 10, 0, 0) == ((short)10, (short)9) &&
                LineMotion.Advance(10, 10, 11, 11, 0) == ((short)11, (short)11),
                "integer raster steps retain slope, direction, axis swap, diagonal tie and BP-plus-one count");
        Require(LineMotion.Advance(10, 10, 10, 10, 0) == ((short)1, (short)1) &&
                LineMotion.Advance(10, 10, 12, 10, 2) == ((short)1, (short)1),
                "coincident or exhausted line returns the original sentinel point");
        var f = Create(27); Entity e = f.Parent;
        Require(e.State == 0x2525 && e[0x0c] == 268 && e[0x20] == 268 && e[0x14] == 1 &&
                e[0x18] == 0x1010 && e[0x1a] == 0x0808 && e[0x22] == 0x7070 && e[0x3e] == 13 &&
                f.Spawn.Random.Index == 4 && f.Spawn.FreeRecords == 68 && f.Spawn.HostileCount == 1,
                "kind twenty-seven factory initializes original phase clocks and common allocation counters");
        f.Spawn.Pool.Records[1][0x32] = 543;
        for (int i = 0; i < 16; i++) PursuitEmitterActor.Step(f.Actors, 0);
        Require(e[0x14] == 0 && e.VelocityX == 1 && e.VelocityY == 0 && e.X == 48 && e.Y == 96 &&
                f.Spawn.HostileCount == 3 && f.Spawn.FreeRecords == 68 &&
                f.Spawn.Pool.Records[1][0x32] == 543 && f.Spawn.Pool.Records[1][0x3a] == -1 &&
                f.Spawn.Pool.Records[1][0x1a] == 260 && f.Spawn.Pool.Records[2].State == 0x2424,
                "sixteen stationary updates emit two directional children then choose movement without moving");
        var bullet = new Bullet { Sprite = 29, VelocityX = -3, VelocityY = 5 };
        DamageRules.Apply(f.Actors, e, bullet);
        Require(e.State == 0x2525 && e.VelocityX == -2 && e.VelocityY == 2 && e[0x24] == 10 &&
                f.Actors.KillCount == 0, "moving phase receives signed half-shot impulse without death");
        short x = e.X, y = e.Y;
        PursuitEmitterActor.Step(f.Actors, 0);
        Require(e.X == x - 2 && e.Y == y + 2 && e[0x0c] == 269 && e[0x24] == 9,
                "moving phase uses retained impulse and decrements its hit-flash counter");
        e[0x22] = 0x0170; x = e.X; PursuitEmitterActor.Step(f.Actors, 0);
        Require(e[0x14] == 1 && e.X == x && e[0x22] == 0x7070,
                "movement clock zero switches to stationary phase without a final move");
        f.Actors.FreezeTicks = 1; e[0x1a] = 0x0108; e[0x18] = 0x0110;
        DamageRules.Apply(f.Actors, e, bullet); PursuitEmitterActor.Step(f.Actors, 0);
        Require(e.State == 0x2525 && e[0x1a] == 0x0108 && e[0x18] == 0x0110,
                "freeze protects stationary actor from death and stops its update clocks");
        f.Actors.FreezeTicks = 0; DamageRules.Apply(f.Actors, e, bullet);
        Require(e.State == 2 && e[0x3a] == -1 && f.Actors.KillCount == 1 && f.Spawn.HostileCount == 2,
                "unfrozen stationary phase enters common score/death handling");

        f = Create(27); e = f.Parent; e[0x18] = 0x0010; e[0x1a] = 0x0008;
        PursuitEmitterActor.Step(f.Actors, 0);
        Require(e[0x18] == unchecked((short)0xff10) && e[0x1a] == unchecked((short)0xff08) &&
                e[0x14] == 1 && f.Spawn.HostileCount == 1,
                "zero high bytes wrap to 255 without triggering their exact-zero branch");
        e[0x18] = 0x0110; f.Spawn.PlayerFlags = 2; f.Spawn.SecondPlayerX = 0; f.Spawn.SecondPlayerY = e.Y;
        PursuitEmitterActor.Step(f.Actors, 0);
        Require(e.VelocityX == -1 && e.VelocityY == 0, "per-record target selection supplies the second player's line endpoint");

        f = Create(30); e = f.Parent;
        for (int i = 0; i < 7; i++) PursuitEmitterActor.Step(f.Actors, 0);
        Require(e[0x0c] == 277 && e[0x22] == unchecked((short)0x8080) &&
                f.Spawn.Pool.Records[1][0x0c] == 278 && f.Spawn.Pool.Records[1].VelocityX == -2 &&
                f.Spawn.Pool.Records[2][0x0c] == 279 && f.Spawn.Pool.Records[2].VelocityX == 1 &&
                f.Spawn.FreeRecords == 66 && f.Spawn.HostileCount == 3,
                "kind thirty emits its asymmetric horizontal pair with normal projectile counters");
        f = Create(30); e = f.Parent;
        for (int i = 1; i < 69; i++) f.Spawn.Pool.Records[i].State = 1;
        f.Spawn.Pool.Records[70][0x3a] = 42; e[0x1a] = 0x0107;
        PursuitEmitterActor.Step(f.Actors, 0);
        Require(f.Spawn.Pool.Records[69].State == 15 && f.Spawn.Pool.Records[69][0x0c] == 278 &&
                !f.Spawn.Pool.Records[70].Active && f.Spawn.Pool.Records[70][0x3a] == 42 &&
                f.Spawn.FreeRecords == 67 && f.Spawn.HostileCount == 2,
                "paired emission can allocate just its first shot at slot sixty-nine without writing beyond gameplay pool");
        foreach (int wave in new[] { 47, 55 })
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
