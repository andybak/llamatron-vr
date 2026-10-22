using System;
using Llamatron.Core;

static class SoundChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception($"[LLREBUILD] Sound: {message}");
    }

    public static void Run()
    {
        Require(OriginalSoundData.Frames.Count == 32 && OriginalSoundData.Frames[29] == 30270 &&
                OriginalSoundData.Offsets[29] == 126639 && OriginalSoundData.TimeConstants[2] == 126 &&
                Math.Abs(SoundDriverState.RateHz(126) - 1_000_000d / 130) < 0.000001,
                "recovered sample descriptors and DSP time constants");
        var driver = new SoundDriverState();
        int starts = 0, stops = 0; byte lastRate = 0;
        driver.Started += (_, rate) => { starts++; lastRate = rate; }; driver.Stopped += () => stops++;
        driver.Queue(7, 2, 4);
        Require(driver.ActiveSample == 0 && driver.PendingSample == 7 && starts == 0,
                "requests queue until the IRQ service");
        driver.Service();
        Require(driver.ActiveSample == 7 && driver.Priority == 4 && driver.PendingSample == 0 && lastRate == 126,
                "IRQ starts one voice using queued rate and priority");
        driver.Queue(8, 1, 3); driver.Service();
        Require(driver.ActiveSample == 7 && driver.PendingSample == 8 && stops == 0,
                "lower priority remains pending without preempting");
        driver.Queue(9, 4, 2); driver.Complete();
        Require(driver.ActiveSample == 0 && driver.Priority == 4, "DMA completion retains priority status");
        driver.Service();
        Require(driver.ActiveSample == 9 && driver.Priority == 2 && lastRate == 206,
                "latest pending request replaces earlier lower-priority request");
        driver.Queue(10, 0, 2);
        Require(driver.ActiveSample == 9 && driver.PendingStop == 9, "equal priority queues a matching stop");
        driver.Service();
        Require(driver.ActiveSample == 10 && starts == 3 && stops == 1 && lastRate == 1,
                "service stops old voice before starting equal-priority replacement");
        driver.Queue(0, 1, 3); driver.Service();
        Require(driver.ActiveSample == 0 && driver.Priority == 2 && starts == 3,
                "sample zero can preempt but does not start a voice");
        driver.Queue(33, 0, 3); driver.Service();
        Require(driver.ActiveSample == 0 && driver.PendingSample == 33,
                "out-of-range sample request remains queued as in driver 014b");
        driver.Queue(11, 3, 7); driver.Service(); driver.Queue(12, 1, 7); driver.Complete(); driver.Service();
        Require(driver.ActiveSample == 12 && driver.PendingStop == 11,
                "completion before service leaves a mismatched stop identifier intact");
        driver.Complete(); driver.Queue(11, 0, 1); driver.Service();
        Require(driver.ActiveSample == 11 && driver.PendingStop == 11, "stale stop survives until a matching active sample");
        driver.Service();
        Require(driver.ActiveSample == 0 && driver.PendingStop == 0 && driver.Priority == 1,
                "matching stale stop executes on the following IRQ without resetting priority");
        driver.StopAll(); driver.Service();
        Require(driver.PendingStop == 0 && driver.ActiveSample == 0, "stop-all is serviced even while idle");

        driver = new SoundDriverState();
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0), sound: driver);
        var target = new Entity(); target[0x3e] = 6;
        DamageRules.Apply(game.Actors, target, new Bullet());
        Require(driver.PendingSample == 13, "gameplay sound requests reach the attached driver");
        driver.Service();
        Require(game.Actors.SoundStatus!() == 3 && game.Actors.ActiveSoundSample!() == 13,
                "AH=6 priority and sample ID are exposed independently");
        driver = new SoundDriverState();
        game = new GameSimulation(RandomSequence.Shuffle(() => 0), sound: driver);
        var campaign = new GameCampaign(game); campaign.TickDisplay();
        game.Actors.Outcome = 4; campaign.TickDisplay(); driver.Service();
        Require(driver.ActiveSample == 6 && campaign.Phase == CampaignPhase.WarpAudio,
                "warp requests original sample six");
        campaign.TickDisplay(); driver.Complete();
        Require(campaign.Phase == CampaignPhase.WarpAudio && campaign.TransitionTicks == 1,
                "warp requires two fresh retraces before polling completion");
        campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.WipeExpand && game.Actors.Spawning.WaveIndex == 5 &&
                campaign.TransitionTicks == 48, "completed voice advances warp reward/wipe after the minimum retraces");
    }
}
