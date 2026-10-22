using System;
using Llamatron.Core;

static class AlternatingChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new Exception($"[LLALTERNATE] {message}"); }

    public static void Run()
    {
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), playerFlags: 4);
        var campaign = new GameCampaign(game);
        ActorContext actors = game.Actors;
        actors.Scores.Award(0, 4, 7, 0);
        Entity survivor = actors.Spawning.Pool.Records[0]; survivor.State = 0x0606; survivor[0x3a] = 1;
        actors.Spawning.WaveIndex = 7; actors.Spawning.SpriteVariant = 3;
        actors.LoveTimer = 17; actors.SpecialDropCountdown = 1; actors.PermanentHotBullets = 1;
        actors.Outcome = 1; campaign.TickDisplay();
        Require(campaign.ActivePlayer == 1 && campaign.OtherLives == 4 && actors.Lives == 5 && campaign.WaveNumber == 1,
                "First death must start player two's original wave, retaining player one's four lives.");
        Require(actors.PermanentHotBullets == 0, "First start must take the original new-wave reset path.");
        actors.Scores.Award(1, 8, 7, 0);
        actors.LoveTimer = 99; actors.SpecialDropCountdown = 5;
        actors.PermanentHotBullets = 1; actors.HotBulletTimer = 123;
        actors.Outcome = 1; campaign.TickDisplay();
        Require(campaign.ActivePlayer == 0 && campaign.OtherLives == 4 && actors.Lives == 4 && campaign.WaveNumber == 8 &&
                actors.Spawning.SpriteVariant == 3 && actors.LoveTimer == 17 && actors.SpecialDropCountdown == 1,
                "Campaign packet fields did not return to player one.");
        Require(game.Wave.Groups.Count == 1 && game.Wave.Groups[0].Kind == 1 &&
                actors.Scores.Value(0) == 5 && actors.Scores.Value(1) == 9,
                "Per-player survivor stream or decimal scores were lost.");
        Require(actors.PermanentHotBullets == 1 && actors.HotBulletTimer == 123,
                "Words outside the swapped block must stay shared during survivor retries.");
        // Exhaust one player, then verify their zero-life packet is skipped.
        actors.Lives = 1; actors.Outcome = 1; campaign.TickDisplay();
        Require(campaign.ActivePlayer == 1 && campaign.OtherLives == 0, "Eliminated player's packet was not retained.");
        actors.Outcome = 1; campaign.TickDisplay();
        Require(campaign.ActivePlayer == 1 && actors.Lives == 3, "Dead player must be skipped without consuming another life.");
        actors.Lives = 1; actors.Outcome = 1; campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Continue, "Continue must wait until both players have zero lives.");
        campaign.TickInterrupt(128, acceptContinue: true); campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.Playing && actors.Lives == 5 && campaign.OtherLives == 5 && campaign.ActivePlayer == 0,
                "Accepted continue must revive both packets and perform the original player swap.");
    }
}
