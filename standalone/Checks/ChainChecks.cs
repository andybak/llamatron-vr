using System;
using Llamatron.Core;

static class ChainChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Chain: {message}");
    }

    private static WaveParameters Parameters()
    {
        var result = new WaveParameters(); result.Apply(OriginalWaves.Decode()[0], 0, _ => { });
        return result;
    }

    public static void Run()
    {
        var table = new byte[512]; Array.Fill(table, (byte)32);
        var spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), Parameters());
        spawn.Chain.Cursor = 47;
        spawn.Pool.Records[0].VelocityX = 123;
        spawn.Pool.Records[1][0x32] = 321;
        Entity head = spawn.Create(16);
        Require(ReferenceEquals(head, spawn.Pool.Records[8]) && head.State == 0x1d1d &&
                head[0x3a] == 16 && spawn.HostileCount == 1 && spawn.FreeRecords == 68,
                "eight followers and one head consume only the common free counter");
        Require(spawn.Pool.Records[0].VelocityX == 123 && spawn.Pool.Records[1][0x32] == 321,
                "followers retain velocity and popup residue");
        for (int i = 0; i < 8; i++)
            Require(spawn.Pool.Records[i].State == 0x1e1e && spawn.Pool.Records[i][0x14] == 32 - i * 4 &&
                    spawn.Pool.Records[i][0x3a] == -1, "follower offsets and retry exclusion");
        Require(spawn.Chain.Cursor == 47 && spawn.Chain[0] == (head.X, head.Y, (short)204) &&
                spawn.Chain[63] == spawn.Chain[0], "constructor fills history without resetting global cursor");
        var actors = new ActorContext(spawn) { PrimaryStatus = 30000, SecondaryStatus = 30000, FreezeTicks = 100 };
        short x = head.X, y = head.Y;
        head[0x0c] = 205; head[0x14] = -1;
        ChainActor.Step(actors, 8);
        Require(spawn.Chain.Cursor == 1 && spawn.Chain[0] == (x, y, (short)205) &&
                head[0x0c] == 202 && head.X == x && head.Y == y,
                "frozen leader records old sprite before signed low-word facing selection");
        Entity follower = spawn.Pool.Records[0]; follower[0x14] = 1;
        DamageRules.Apply(actors, follower, new Bullet());
        ChainActor.Step(actors, 0);
        Require(follower.X == x && follower.Y == y && follower[0x0c] == 201 && follower[0x18] == 39,
                "follower damage starts at forty and decrements after frozen history tracking");
        // Collision is evaluated before reading a follower's new history position.
        actors.PrimaryStatus = 0; spawn.PlayerX = follower.X; spawn.PlayerY = follower.Y;
        spawn.Chain.Record(0, 8, 24, 204); follower[0x18] = 17;
        ChainActor.Step(actors, 0);
        Require(follower.X == x && follower[0x18] == 17 && actors.PrimaryStatus == -2,
                "old-position contact returns before history or flash advancement");
        actors.PrimaryStatus = 30000; actors.Outcome = 0;
        head[0x14] = 111; head[0x18] = 222;
        var bullet = new Bullet { VelocityX = -3, VelocityY = 5 };
        for (int i = 0; i < 4; i++) DamageRules.Apply(actors, head, bullet);
        Require(head[0x24] == 4 && head[0x14] == 111 && head[0x18] == 222 &&
                head[0x16] == -12 && head[0x1a] == 20 && head[0x30] == 5,
                "first four hits preserve fractional velocity and apply fixed impulse");
        spawn.SelectedHerdSprite = 77;
        DamageRules.Apply(actors, head, bullet);
        Require(follower.State == 0x0504 && follower[0x0e] == 2 && follower[0x0c] == 77 &&
                follower[0x3a] == -1 && spawn.HerdCount == 1 && spawn.InitialHerdCount == 1 &&
                spawn.HostileCount == 1 && spawn.FreeRecords == 68 && actors.KillCount == 0 &&
                spawn.Parameters.Mode == 0 && head[0x24] == 0x0404,
                "fifth hit converts first follower using shared sprite without allocation or kill accounting");
        for (int i = 0; i < 8; i++) spawn.Pool.Records[i].State = -1;
        head[0x24] = 4;
        // A preceding head stops the scan even if a follower remains later.
        spawn.Pool.Records[0].State = 0x1d1d; spawn.Pool.Records[9].State = 0x1e1e;
        DamageRules.Apply(actors, head, bullet);
        Require(!head.Active && spawn.Pool.Records[9].State == 0x1e1e &&
                spawn.HostileCount == 0 && spawn.FreeRecords == 68 && actors.KillCount == 0,
                "first head terminates damage scan and removes the struck head without common kill");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), Parameters());
        for (int i = 0; i < 70; i++) spawn.Pool.Records[i].State = 1;
        head = spawn.Create(16);
        Require(ReferenceEquals(head, spawn.Pool.Records[78]) && spawn.Pool.Records[70].State == 0x1e1e,
                "compound constructor scans beyond the ordinary gameplay allocation boundary");

        spawn = new SpawnContext(new EntityPool(), new RandomSequence(table), Parameters());
        head = spawn.Create(16);
        actors = new ActorContext(spawn) { PrimaryStatus = 30000, SecondaryStatus = 30000, FreezeTicks = 100 };
        for (int i = 0; i < 61; i++) if (i != 8) spawn.Pool.Records[i].State = 1;
        head[0x0a] = 0x0030;
        ChainActor.Step(actors, 8);
        Require(!spawn.Pool.Records[61].Active && spawn.HostileCount == 1,
                "emitter does not inspect slot sixty-one");
        spawn.Pool.Records[60].State = -1; spawn.Pool.Records[60][0x32] = 444;
        head[0x0a] = 0x0030;
        ChainActor.Step(actors, 8);
        Require(spawn.Pool.Records[60].State == 0x0303 && spawn.Pool.Records[60][0x3a] == 1 &&
                spawn.Pool.Records[60][0x32] == 444 && spawn.HostileCount == 2 && spawn.FreeRecords == 68,
                "last eligible emitter slot uses shared chaser initializer and preserves popup/free counter");

        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: 30);
        game.Actors.PrimaryStatus = 30000; game.Actors.SecondaryStatus = 30000; game.Actors.HitLockTicks = 30000;
        var sprites = new SpriteFrame();
        for (int i = 0; i < 350; i++) { game.Step(0); sprites.Build(game); }
        Require(game.Actors.SpawnComplete && game.Actors.FrameCounter == 350,
                "actual wave thirty-one advances 350 protected logic ticks with chain and render commands");
    }
}


