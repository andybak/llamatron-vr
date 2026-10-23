using System;
using System.Collections.Generic;
using System.IO;
using Llamatron.Core;

try
{
    if (args.Length > 0 && args[0] == "campaign") return CampaignSweep.Run(args);
    if (args.Length > 0 && args[0] == "sweep") return WaveSweep.Run(args);
    if (args.Length == 0) throw new ArgumentException("Use create, replay or inspect. Options: --wave 1..100 --seed N --cycle N --mode 0/1/3/4/5/7 --ticks N --input 0..255 --secondary 0..255 --load file.llsave --save file.llsave --twin-stick true/false");
    string command = args[0]; var options = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length || !args[i].StartsWith("--")) throw new ArgumentException($"Expected --option value at {args[i]}");
        options.Add(args[i], args[i + 1]);
    }
    string Get(string key, string fallback = "") => options.TryGetValue(key, out string? value) ? value : fallback;
    int Number(string key, int fallback) => int.Parse(Get(key, $"{fallback}"));
    GameSession session;
    if (command == "create")
    {
        session = GameSession.Create(Number("--wave", 1), Number("--seed", 1), checked((byte)Number("--mode", 0)), checked((short)Number("--cycle", 0)));
        session.TwinStick = bool.Parse(Get("--twin-stick", "false"));
    }
    else if (command == "replay" || command == "inspect") session = GameSession.Load(File.ReadAllBytes(Get("--load")));
    else throw new ArgumentException($"Unknown command {command}");
    int ticks = command == "inspect" ? 0 : Number("--ticks", 0);
    if (ticks < 0 || ticks > 10_000_000) throw new ArgumentException("Tick count must be between zero and ten million.");
    byte primary = checked((byte)Number("--input", 0)), secondary = checked((byte)Number("--secondary", 0));
    for (int i = 0; i < ticks; i++)
    {
        double target = (session.Clock.InterruptCount + 1) / session.Clock.InterruptHz;
        try { session.Advance(Math.Max(0, target - session.Clock.Time), primary, secondary); }
        catch (NotSupportedException exception) { session.Fault = exception.Message; break; }
    }
    string save = Get("--save");
    if (save.Length != 0)
    {
        string path = Path.GetFullPath(save); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, session.Save()); File.Move(temporary, path, true);
        Console.WriteLine($"[LLSAVE] Wrote {path}");
    }
    var actors = session.Simulation.Actors;
    Console.WriteLine($"[LLSAVE] wave={session.Campaign.WaveNumber}; phase={session.Campaign.Phase}; IRQ={session.Clock.InterruptCount}; display={session.Clock.DisplayCount}; lives={actors.Lives}; hostiles={actors.Spawning.HostileCount}; herd={actors.Spawning.HerdCount}; random={actors.Spawning.Random.Index}; fault={session.Fault}");
    return session.Fault.Length == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[LLSAVE] {exception.Message}"); return 2;
}
