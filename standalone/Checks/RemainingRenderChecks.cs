using System;
using Llamatron.Core;

static class RemainingRenderChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Remaining render: {message}");
    }

    public static void Run()
    {
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0));
        foreach (Entity entry in game.Actors.Spawning.Pool.Records) entry.State = -1;
        Entity entity = game.Actors.Spawning.Pool.Records[0];
        entity.State = 1; entity.RenderState = 9; entity.X = 160; entity.Y = 100;
        entity[0x0c] = 12; entity[0x0e] = 3; entity[0x14] = -2;
        var frame = new SpriteFrame();
        entity.RenderState = 0x1209; frame.Build(game);
        Require(entity.RenderState == 0x120a && !frame.Commands[0].Transformed,
                "render nine increments the full word while dispatching on its low byte");
        frame.Build(game);
        Require(entity.RenderState == 0x120b && !frame.Commands[0].Transformed,
                "render ten advances to eleven on the next display independently of actor update");
        entity.RenderState = 14; entity[0x14] = 8;
        for (int i = 0; i < 4; i++) { entity[0x16 + i * 4] = (short)(10 + i * 10); entity[0x18 + i * 4] = (short)(11 + i * 10); }
        frame.Build(game);
        short[] x = { 30, 40, 10, 20 }; ushort[] sprites = { 0x8156, 0x8180, 0x81aa, 0x81d4 };
        for (int i = 0; i < 4; i++)
            Require(frame.Commands[i].X == x[i] && frame.Commands[i].Y == x[i] + 1 && frame.Commands[i].Offset == sprites[i],
                    "render fourteen reads circular history in source order with four recovered sprite offsets");
        Require(frame.Commands[4].X == 160 && frame.Commands[4].Y == 100 && entity.RenderState == 14 && entity[0x14] == 8,
                "trail drawing precedes body and leaves its ring cursor unchanged");
    }
}
