using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Llamatron.Core;

static class CampaignSweep
{
    public static int Run(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
        string Get(string key, string fallback) => options.TryGetValue(key, out string? value) ? value : fallback;
        int seed = int.Parse(Get("--seed", "17")), reloadEvery = int.Parse(Get("--reload-every", "0"));
        int limit = int.Parse(Get("--ticks", "1000000"));
        string folder = Path.GetFullPath(Get("--output", "artifacts/campaign-sweep")); Directory.CreateDirectory(folder);
        var sound = new SoundDriverState(); var random = new Random(seed);
        // Extend the demo counter only for validation, retaining its original
        // scripted movement/aim and damage/score suppression through 100 waves.
        var game = new GameSimulation(RandomSequence.Shuffle(() => (ushort)random.Next(65536)), sound: sound, demoWaves: short.MaxValue);
        var session = new GameSession(game, new GameCampaign(game), new GameClock(70), sound);
        var reached = new List<object>(); int previousWave = 0, completed = 0, cleared = 0, warps = 0; bool pendingWarp = false;
        try
        {
            session.Campaign.TickDisplay();
            for (int tick = 0; tick < limit; tick++)
            {
                game = session.Simulation;
                if (previousWave != session.Campaign.WaveNumber)
                {
                    if (previousWave != 0) { completed++; if (pendingWarp) warps++; else cleared++; }
                    pendingWarp = false;
                    previousWave = session.Campaign.WaveNumber;
                    reached.Add(new { wave = previousWave, ticks = session.Clock.InterruptCount, cycle = game.Actors.CycleShift, random = game.Actors.Spawning.Random.Index });
                    Console.WriteLine($"[LLCAMPAIGN] entered={previousWave}; transitions={completed}; clears={cleared}; warps={warps}; tick={session.Clock.InterruptCount}; cycle={game.Actors.CycleShift}");
                    File.WriteAllBytes(Path.Combine(folder, $"entry-{completed:000}-wave-{previousWave:000}.llsave"), session.Save());
                    if (completed == 100) break;
                }
                pendingWarp |= session.Campaign.Phase == CampaignPhase.WarpAudio;
                if (reloadEvery > 0 && tick % reloadEvery == 0)
                {
                    byte[] bytes = session.Save(); session = GameSession.Load(bytes);
                    if (!bytes.AsSpan().SequenceEqual(session.Save())) throw new InvalidOperationException("Save changed state at a campaign checkpoint.");
                }
                double target = (session.Clock.InterruptCount + 1) / session.Clock.InterruptHz;
                session.Advance(Math.Max(0, target - session.Clock.Time));
            }
        }
        catch (Exception exception) { session.Fault = $"{exception.GetType().Name}: {exception.Message}"; }
        var changedScripts = new List<int>(); byte[][] original = OriginalWaves.CopyScripts();
        for (int i = 0; i < original.Length; i++) if (!original[i].AsSpan().SequenceEqual(session.Simulation.Scripts.CopyScript(i))) changedScripts.Add(i + 1);
        File.WriteAllBytes(Path.Combine(folder, "final.llsave"), session.Save());
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new { seed, reloadEvery, tickLimit = limit,
            transitions = completed, cleared, warps, ticks = session.Clock.InterruptCount, phase = session.Campaign.Phase.ToString(), fault = session.Fault,
            protectedScriptedControl = true, changedScripts, reached }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[LLCAMPAIGN] transitions={completed}; clears={cleared}; warps={warps}; tick={session.Clock.InterruptCount}; fault={session.Fault}; changedScripts={string.Join(",", changedScripts)}; report={folder}");
        return completed == 100 && session.Fault.Length == 0 ? 0 : 1;
    }
}
