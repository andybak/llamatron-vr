using System;
using Llamatron.Core;

static class DroidChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new Exception($"[LLDROID] {message}"); }

    public static void Run()
    {
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), playerFlags: 1);
        ActorContext context = game.Actors;
        SpawnContext spawn = context.Spawning;
        context.FreezeTicks = 0; context.FrameCounter = 0;
        context.Droid.AimCountdown = 0;
        context.PrimaryMotion.CompanionX = 80; context.PrimaryMotion.CompanionY = 60;
        spawn.SecondPlayerX = 180; spawn.SecondPlayerY = 104;
        Entity hostile = spawn.Pool.Records[0];
        hostile.State = 0x2b2b; hostile.X = 100; hostile.Y = 50; hostile[0x10] = 1;
        PlayerMotion.Step(context, 0, 0, false);
        Require(spawn.SecondPlayerX == 178 && spawn.SecondPlayerY == 103 && context.SecondaryMotion.MoveX == -2 &&
                context.SecondaryMotion.MoveY == -1, "Original-runtime following vector differs.");
        Require(context.SecondaryMotion.FireX == -3 && context.SecondaryMotion.FireY == -3 &&
                context.SecondaryMotion.ProjectileSprite == 32 && context.SecondaryMotion.AimMask == 5 &&
                context.Droid.AimCountdown == 20 && context.Droid.TargetRecord == -2,
                "Original-runtime firing state differs.");
        // The first herd acquisition returns without moving, then pursues on
        // the next eligible even IRQ; slot 69 is outside the search boundary.
        Entity herd = spawn.Pool.Records[1]; herd.State = 0x0505; herd.X = 220; herd.Y = 150;
        context.Droid.TargetRecord = -1; context.FrameCounter = 2;
        PlayerMotion.Step(context, 0, 0, false);
        Require(context.Droid.TargetRecord == 1 && spawn.SecondPlayerX == 178, "Acquisition must spend one movement opportunity.");
        context.FrameCounter = 4;
        PlayerMotion.Step(context, 0, 0, false);
        Require(spawn.SecondPlayerX > 178 && spawn.SecondPlayerY > 103, "Droid did not pursue its retained herd record.");
        context.FreezeTicks = 1;
        short x = spawn.SecondPlayerX, aim = context.Droid.AimCountdown;
        PlayerMotion.Step(context, 0, 0, false);
        Require(spawn.SecondPlayerX == x && context.Droid.TargetRecord == -1 && context.Droid.AimCountdown == aim - 1,
                "Freeze must stop following while the aim clock continues.");
        context.PrimaryStatus = 0; context.SecondaryStatus = 0;
        new SpriteFrame().Build(game); // The original secondary sprite bank also renders the droid.
    }
}
