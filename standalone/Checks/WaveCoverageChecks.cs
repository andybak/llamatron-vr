using System;
using Llamatron.Core;

static class WaveCoverageChecks
{
    public static void Run()
    {
        int complete = 0, cleared = 0, warped = 0, voiceStarts = 0;
        for (int wave = 0; wave < 100; wave++)
        {
            var sound = new SoundDriverState();
            double elapsed = 0, voiceEnd = double.PositiveInfinity;
            sound.Started += (id, rate) => { voiceStarts++; voiceEnd = elapsed + OriginalSoundData.Frames[id - 1] / SoundDriverState.RateHz(rate); };
            sound.Stopped += () => voiceEnd = double.PositiveInfinity;
            var game = new GameSimulation(RandomSequence.Shuffle(() => 0), waveIndex: wave, sound: sound);
            game.Actors.PrimaryStatus = 0; game.Actors.SecondaryStatus = 0; game.Actors.HitLockTicks = 30000;
            var frame = new SpriteFrame();
            for (int tick = 0; tick < 350 && game.Actors.Outcome == 0; tick++)
            {
                elapsed = tick / OriginalTiming.InterruptHz;
                if (sound.ActiveSample != 0 && elapsed >= voiceEnd) sound.Complete();
                sound.Service(); game.Step(0); frame.Build(game);
            }
            if (game.Actors.SpawnComplete) complete++;
            if (game.Actors.Outcome == 2) cleared++;
            if (game.Actors.Outcome == 4) warped++;
            if ((!game.Actors.SpawnComplete && game.Actors.Outcome != 4) ||
                (game.Actors.Outcome != 0 && game.Actors.Outcome != 2 && game.Actors.Outcome != 4))
                throw new Exception($"[LLREBUILD] Wave sweep {wave + 1}: ticks={game.Actors.FrameCounter}, complete={game.Actors.SpawnComplete}, outcome={game.Actors.Outcome}.");
        }
        Console.WriteLine($"[LLREBUILD] 100-wave protected-contact sweep: streams complete={complete}, cleared={cleared}, warped={warped}, modeled voice starts={voiceStarts}; up to 350 logic ticks each.");
    }
}
