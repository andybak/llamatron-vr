using System;
using Llamatron.Core;

static class DemoChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new Exception($"[LLDEMO] {message}"); }

    public static void Run()
    {
        byte[] values = new byte[512]; values[0] = 0x80; values[1] = 0x80;
        var game = new GameSimulation(new RandomSequence(values), demoWaves: 3);
        ActorContext actors = game.Actors;
        actors.FreezeTicks = 0; actors.FrameCounter = 64; actors.Droid.AimCountdown = 0;
        actors.Spawning.Pool.Reset();
        PlayerMotion.Step(actors, 0, 0, false);
        Require(actors.Droid.RandomTargetX == 32 && actors.Droid.RandomTargetY == 36,
                "Demo target must use signed X and arithmetic-shifted signed Y samples.");
        Require(actors.Spawning.PlayerX == actors.Spawning.SecondPlayerX &&
                actors.Spawning.PlayerY == actors.Spawning.SecondPlayerY &&
                actors.PrimaryMotion.FireX == actors.SecondaryMotion.FireX &&
                actors.PrimaryMotion.ProjectileSprite == actors.SecondaryMotion.ProjectileSprite,
                "Scripted primary must copy the droid without taking an extra movement step.");
        var campaign = new GameCampaign(game);
        PlayerMotion.Step(actors, 128, 0, false);
        Require(actors.ScriptedMode == 1 && actors.Outcome == 2, "Fire must shorten the demo to its final clear.");
        campaign.TickDisplay();
        for (int i = 0; i < 11; i++) campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Menu && actors.ScriptedMode == 0,
                "Final scripted clear must return to menu before drawing a wipe.");
    }
}
