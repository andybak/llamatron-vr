using System;
using System.IO;
using Llamatron.Core;

static class SaveChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new Exception($"[LLSAVE] {message}"); }
    private static void Equal(GameSession first, GameSession second, string context)
    { Require(first.Save().AsSpan().SequenceEqual(second.Save()), $"State differs after {context}"); }
    private static void RoundTrip(GameSession original, string context, int frames = 80)
    {
        byte[] bytes = original.Save(); var restored = GameSession.Load(bytes);
        Equal(original, restored, $"loading {context}");
        for (int i = 0; i < frames; i++)
        {
            byte input = (byte)(i % 24 < 12 ? 5 : 10);
            original.Advance(1d / 70, input, (byte)(input ^ 15));
            restored.Advance(1d / 70, input, (byte)(input ^ 15));
            Equal(original, restored, $"replaying {context}, frame {i}");
        }
    }
    public static void Run()
    {
        foreach (int wave in new[] { 1, 19, 22, 31, 46, 61, 69, 100 })
        {
            var session = GameSession.Create(wave, seed: 917 + wave, playerFlags: 5);
            session.Simulation.Actors.PrimaryStatus = 30000;
            session.Simulation.Actors.SecondaryStatus = 30000;
            session.Advance(1.73, 5, 10);
            session.Simulation.Actors.Spawning.Pool.Records[99][0x32] = 777;
            session.Simulation.Actors.Spawning.Pool.TargetDummy[0x3a] = 123;
            RoundTrip(session, $"wave {wave}");
        }
        var paused = GameSession.Create(); paused.Advance(.83); paused.Campaign.Paused = true;
        RoundTrip(paused, "pause");
        var alternating = GameSession.Create(playerFlags: 7); alternating.Advance(.7);
        alternating.Simulation.Actors.Outcome = 1; alternating.Campaign.TickDisplay();
        RoundTrip(alternating, "alternating-player survivor state");
        var warp = GameSession.Create(); warp.Simulation.Actors.Outcome = 4; warp.Campaign.TickDisplay(); warp.Advance(.15);
        RoundTrip(warp, "active warp sample", 250);
        foreach (int displaySteps in new[] { 3, 12, 24, 65 })
        {
            var wipe = GameSession.Create(); wipe.Advance(.8);
            wipe.Simulation.Actors.Outcome = 2; wipe.Simulation.Actors.Spawning.WaveIndex++;
            for (int i = 0; i < displaySteps; i++) wipe.Campaign.TickDisplay();
            RoundTrip(wipe, $"wave transition display {displaySteps}", 120);
        }
        var continued = GameSession.Create(); continued.Simulation.Actors.Lives = 1;
        continued.Simulation.Actors.Outcome = 1; continued.Campaign.TickDisplay();
        RoundTrip(continued, "continue countdown");
        byte[] good = continued.Save(); byte[] corrupt = (byte[])good.Clone(); corrupt[70] ^= 1;
        try { GameSession.Load(corrupt); throw new Exception("Corrupt save was accepted"); }
        catch (InvalidDataException) { }
        try { GameSession.Load(new byte[20]); throw new Exception("Truncated save was accepted"); }
        catch (InvalidDataException) { }
        Require(good.AsSpan().SequenceEqual(continued.Save()), "Rejected load modified the running session");
        Console.WriteLine($"[LLSAVE] Exact replay passed for eight waves, pauses, alternating players, warp audio, wipes and continue; example save={good.Length} bytes.");
    }
}
