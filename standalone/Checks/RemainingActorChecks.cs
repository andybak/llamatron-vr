using System;
using Llamatron.Core;

static class RemainingActorChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Remaining actors: {message}");
    }

    private static SpawnContext Spawn(byte first = 32, byte second = 32)
    {
        var p = new WaveParameters(); p.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        var bytes = new byte[512]; Array.Fill(bytes, (byte)32); bytes[0] = first; bytes[1] = second; bytes[4] = 7;
        return new SpawnContext(new EntityPool(), new RandomSequence(bytes), p);
    }

    public static void Run()
    {
        var spawn = Spawn(); Entity parent = spawn.Create(32);
        var actors = new ActorContext(spawn) { FreezeTicks = 0, PrimaryStatus = 30000, SecondaryStatus = -1, FrameCounter = 4 };
        spawn.Pool.Records[1][0x32] = 777;
        Require(parent.State == 0x2929 && parent[0x18] == 0x0202 && parent[0x1c] == 0x3119 &&
                parent[0x22] == 0x3030 && parent[0x3e] == 0 && parent[0x3c] == 0x0200,
                "kind thirty-two specializes shared phase logic with normal damage and its own child initializer");
        PursuitEmitterActor.Step(actors, 0); actors.FrameCounter = 8; PursuitEmitterActor.Step(actors, 0);
        Entity child = spawn.Pool.Records[1];
        Require(parent[0x0c] == 286 && parent[0x14] == 0 && child.State == 0x1c1c &&
                child[0x14] == 9 && child[0x18] == 100 && child[0x16] == 288 && child[0x0c] == 288 &&
                child[0x10] == 0 && child[0x32] == 777 && child[0x3a] == -1 &&
                spawn.HostileCount == 2 && spawn.FreeRecords == 68,
                "frame-clock animation and two-tick emission preserve harmful pickup popup/free counter");
        spawn = Spawn(); parent = spawn.Create(39);
        Require(spawn.Random.Index == 5 && parent.State == 0x2424 && parent[0x16] == 7 &&
                parent[0x0c] == 317 && parent[0x1a] == 310 && parent[0x18] == 0x0104 && parent[0x3c] == 0x0412,
                "kind thirty-nine uses one random heading after the four position samples");

        spawn = Spawn(255, 129); parent = spawn.Create(40);
        actors = new ActorContext(spawn) { FreezeTicks = 50, PrimaryStatus = 30000 };
        Require(spawn.Random.Index == 0 && parent.State == 0x2b2b && parent.RenderState == 17 &&
                parent.X == 160 && parent.Y == 100 && parent[0x16] == 50 && parent[0x3e] == 17,
                "kind forty uses fixed center without consuming position randomness");
        for (int i = 0; i < 5; i++) DetonationActor.Step(actors, 0);
        Require(parent[0x14] == 0x2828 && parent[0x16] == 50, "state forty-three is the source's explicit return");
        for (int i = 0; i < 50; i++) DamageRules.Apply(actors, parent, new Bullet());
        Require(parent.State == 0x2b2b && parent[0x16] == 0, "fifty hits reach zero without starting detonation");
        DamageRules.Apply(actors, parent, new Bullet());
        Require(parent.State == 0x2c2c && parent.RenderState == 18 && parent[0x14] == 150 &&
                parent[0x16] == 0x0404 && parent[0x3a] == 40 && actors.PaletteSequenceOffset == 0x0713 &&
                actors.KillCount == 0 && spawn.HostileCount == 1, "fifty-first hit starts detonation without common kill accounting");
        child = spawn.Pool.Records[1]; child[0x32] = 123; child.VelocityX = 456; child[0x3e] = 9;
        for (int i = 0; i < 4; i++) DetonationActor.Step(actors, 0);
        Require(!child.Active, "detonation clock waits through high-byte zero");
        DetonationActor.Step(actors, 0);
        Require(child.State == 0x2d2d && child.X == 159 && child.Y == 36 && child[0x0c] == 327 &&
                child[0x14] == 0x0303 && child[0x10] == 0 && child[0x32] == 0 && child.VelocityX == 456 &&
                child[0x3e] == 9 && spawn.Random.Index == 2 && spawn.HostileCount == 2 && spawn.FreeRecords == 68,
                "detonation proceeds during freeze, uses signed random coordinates and retains unspecified child words");
        for (int i = 0; i < 4; i++) DetonationActor.Step(actors, 1);
        Require(child[0x0c] == 328, "first explosion frame lasts four updates");
        for (int i = 0; i < 8; i++) DetonationActor.Step(actors, 1);
        Require(!child.Active && spawn.HostileCount == 1 && spawn.FreeRecords == 68,
                "explosion removes on sprite three-thirty without incrementing free count");
        parent[0x14] = 0; parent[0x16] = 0x0004; DetonationActor.Step(actors, 0);
        Require(!parent.Active && spawn.HostileCount == 0 && spawn.FreeRecords == 68 && actors.KillCount == 0 &&
                actors.PaletteSequenceOffset == 0x0721, "expired detonation quota sounds then releases parent without score/free changes");
        spawn = Spawn(); parent = spawn.Create(40); parent.State = 0x2c2c; parent[0x14] = 1; parent[0x16] = 0x0004;
        actors = new ActorContext(spawn);
        for (int i = 1; i < 70; i++) spawn.Pool.Records[i].State = 1;
        DetonationActor.Step(actors, 0);
        Require(parent[0x14] == 0 && spawn.Random.Index == 0 && spawn.HostileCount == 1 &&
                !spawn.Pool.Records[70].Active, "full pool still spends quota without random draws or out-of-pool writes");
        foreach (int wave in new[] { 60, 64, 65 })
        {
            var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: wave);
            // Permit normal contact/collection while suppressing player damage.
            // Permanent positive status lets homing pickups reach the exact
            // player coordinate, where the DOS line sentinel can loop in bounce.
            game.Actors.PrimaryStatus = 0; game.Actors.SecondaryStatus = 0; game.Actors.HitLockTicks = 30000;
            var sprites = new SpriteFrame();
            for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
            Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                    $"actual wave {wave + 1}: ticks={game.Actors.FrameCounter}, complete={game.Actors.SpawnComplete}, outcome={game.Actors.Outcome}, hostile={game.Actors.Spawning.HostileCount}");
        }
    }
}
