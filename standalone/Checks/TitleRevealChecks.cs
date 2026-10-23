using System;
using System.Collections.Generic;
using Llamatron.Core;

static class TitleRevealChecks
{
    public static void Run()
    {
        var particle = new TitleParticleDefinition { x = 304, y = 0, velocity = 4 << 16, sprites = new[] { 10, 11, 12, 13 } };
        var reveal = new TitleReveal(70, new[] { particle });
        var sounds = new List<byte>();
        reveal.SoundRequested += (sample, rate, priority) => sounds.Add(sample);
        reveal.Restart();
        void At(long tick) => reveal.Advance((tick + 0.25) / OriginalTiming.InterruptHz - reveal.Time);
        if (reveal.Visible(0) || reveal.Counter(9) != 272 || sounds[0] != 21) throw new Exception("[LLTITLE] Initial title state.");
        At(1);
        if (!reveal.Visible(0) || reveal.RowSpacing(0) != 126 || reveal.Visible(1)) throw new Exception("[LLTITLE] First revealed rows.");
        At(63);
        if (reveal.Counter(0) != 2 || sounds.Count != 1) throw new Exception("[LLTITLE] Landing happened early.");
        At(64);
        if (reveal.RowSpacing(0) != 1 || sounds.Count != 2 || sounds[1] != 2) throw new Exception("[LLTITLE] First letter landing.");
        At(136);
        if (!reveal.Settled || sounds.Count != 11) throw new Exception("[LLTITLE] All ten landings.");
        At(137);
        if (sounds[11] != 20 || reveal.IdleMotion) throw new Exception("[LLTITLE] Completion sound/hold.");
        At(216);
        if (sounds[12] != 17 || reveal.Particles[0].X != 304) throw new Exception("[LLTITLE] Hold-end sound before motion.");
        At(217);
        if (reveal.Particles[0].X != 300 || reveal.Particles[0].Sprite != 11) throw new Exception("[LLTITLE] Reflected beastie and facing frame.");
        var whole = new TitleReveal(70, new[] { particle }); var split = new TitleReveal(70, new[] { particle });
        whole.Advance(12.345);
        for (int i = 0; i < 100; i++) split.Advance(12.345 / 100);
        if (whole.InterruptCount != split.InterruptCount || whole.Particles[0].X != split.Particles[0].X || whole.Particles[0].Y != split.Particles[0].Y)
            throw new Exception("[LLTITLE] Host frame rate changed title motion.");
        Console.WriteLine("[LLTITLE] IRQ reveal boundaries, sound order, idle motion and host-rate independence passed.");
    }
}
