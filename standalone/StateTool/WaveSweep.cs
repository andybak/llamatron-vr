using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Llamatron.Core;

static class WaveSweep
{
    public static int Run(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
        string Get(string name, string fallback) => options.TryGetValue(name, out string? value) ? value : fallback;
        int limit = int.Parse(Get("--ticks", "15000")); int seed = int.Parse(Get("--seed", "17"));
        int firstWave = int.Parse(Get("--first-wave", "1")), lastWave = int.Parse(Get("--last-wave", "100"));
        if (firstWave < 1 || lastWave > 100 || firstWave > lastWave) throw new ArgumentException("Invalid wave range.");
        string control = Get("--control", "demo");
        if (control != "demo" && control != "normal") throw new ArgumentException("Control must be demo or normal.");
        string folder = Path.GetFullPath(Get("--output", "artifacts/level-sweep")); Directory.CreateDirectory(folder);
        string atlas = Get("--atlas", ""); HashSet<int>? availableGraphics = null;
        if (atlas.Length != 0)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(atlas));
            availableGraphics = manifest.RootElement.GetProperty("sprites").EnumerateArray().Select(sprite => sprite.GetProperty("offset").GetInt32()).ToHashSet();
        }
        var usedGraphics = new HashSet<int>();
        var results = new List<object>(); int cleared = 0, warped = 0, faults = 0, timedOut = 0;
        for (int wave = firstWave; wave <= lastWave; wave++)
        {
            var sound = new SoundDriverState(); var random = new Random(seed + wave);
            // Source scripted-demo control supplies movement/aim and suppresses
            // damage. This tests full-wave execution, not human difficulty/parity.
            var game = new GameSimulation(RandomSequence.Shuffle(() => (ushort)random.Next(65536)), wave - 1,
                playerFlags: control == "normal" ? (byte)1 : (byte)0, sound: sound, demoWaves: control == "demo" ? (short)3 : (short)0);
            if (control == "normal") game.Actors.Lives = 1000; // Exercise real deaths/retries without a short game-over cutoff.
            var session = new GameSession(game, new GameCampaign(game), new GameClock(70), sound);
            string result = "timeout";
            try
            {
                session.Campaign.TickDisplay();
                File.WriteAllBytes(Path.Combine(folder, $"wave-{wave:000}-start.llsave"), session.Save());
                for (int tick = 0; tick < limit; tick++)
                {
                    double target = (session.Clock.InterruptCount + 1) / session.Clock.InterruptHz;
                    session.Advance(Math.Max(0, target - session.Clock.Time), control == "normal" ? BotInput(game) : (byte)0);
                    foreach (SpriteCommand sprite in session.Frame.Commands)
                    {
                        if (sprite.IsLine) continue;
                        usedGraphics.Add(sprite.Offset);
                        if (availableGraphics != null && !availableGraphics.Contains(sprite.Offset))
                            throw new InvalidOperationException($"Missing baked sprite {sprite.Offset:x4}.");
                    }
                    if (game.Actors.Outcome == 2 || session.Campaign.Phase == CampaignPhase.WaveClear) { result = "cleared"; cleared++; break; }
                    if (game.Actors.Outcome == 4 || session.Campaign.Phase == CampaignPhase.WarpAudio) { result = "warped"; warped++; break; }
                }
            }
            catch (Exception exception) { result = "fault"; session.Fault = $"{exception.GetType().Name}: {exception.Message}"; faults++; }
            if (result == "timeout") timedOut++;
            File.WriteAllBytes(Path.Combine(folder, $"wave-{wave:000}-{result}.llsave"), session.Save());
            results.Add(new { wave, result, ticks = session.Clock.InterruptCount, lives = game.Actors.Lives, score = game.Actors.Scores.Value(0), hostiles = game.Actors.Spawning.HostileCount,
                herd = game.Actors.Spawning.HerdCount, streamComplete = game.Actors.SpawnComplete, fault = session.Fault,
                states = game.Actors.Spawning.Pool.Records.Take(70).Where(e => e.Active).GroupBy(e => e.Behavior).ToDictionary(g => $"{g.Key}", g => g.Count()) });
            Console.WriteLine($"[LLSWEEP] wave={wave}; {result}; ticks={session.Clock.InterruptCount}; hostiles={game.Actors.Spawning.HostileCount}; herd={game.Actors.Spawning.HerdCount}; fault={session.Fault}");
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new { seed, firstWave, lastWave, tickLimit = limit, control,
                protectedScriptedControl = control == "demo", extraLivesForTesting = control == "normal", atlas,
                usedGraphics = usedGraphics.OrderBy(offset => offset), cleared, warped, faults, timedOut, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"[LLSWEEP] cleared={cleared}; warped={warped}; faults={faults}; timedOut={timedOut}; report={folder}");
        return cleared + warped == lastWave - firstWave + 1 && faults == 0 ? 0 : 1;
    }

    // Legal directional input for a normal droid-mode game. The robot remains
    // controlled by the original AI. This test bot is not shipped player logic.
    private static byte BotInput(GameSimulation game)
    {
        var spawn = game.Actors.Spawning; Entity? target = null; int nearest = int.MaxValue;
        foreach (Entity entity in spawn.Pool.Records.Take(EntityPool.GameplayRecords))
        {
            if (!entity.Active || (spawn.HostileCount > 0 ? entity[0x3e] == 0 : entity.Behavior != 4 && entity.Behavior != 5)) continue;
            int distance = Math.Abs(entity.X - spawn.PlayerX) + Math.Abs(entity.Y - spawn.PlayerY);
            if (distance < nearest) { nearest = distance; target = entity; }
        }
        if (target == null) return (byte)(1 << ((game.Actors.FrameCounter >> 6) & 3));
        int dx = target.X - spawn.PlayerX, dy = target.Y - spawn.PlayerY;
        return (byte)((dx < -3 ? 4 : dx > 3 ? 8 : 0) | (dy < -3 ? 1 : dy > 3 ? 2 : 0));
    }
}
