using System;
using System.Text;
using Llamatron.Core;

static class PresentationChecks
{
    public static void Run()
    {
        var bytes = new byte[160];
        for (int i = 0; i < 10; i++) Encoding.ASCII.GetBytes($"{1000 - i * 100:D8}PLAYER{i} ").CopyTo(bytes, i * 16);
        var table = new HighScoreTable(bytes);
        if (!table.Save().AsSpan().SequenceEqual(bytes)) throw new Exception("[LLPRESENT] Score format round trip.");
        if (table.Rank(1000) != 1 || table.Rank(100) != -1) throw new Exception("[LLPRESENT] Equal-score placement.");
        if (table.Insert(1500, "LONG PLAYER NAME") != 0 || table.Name(0) != "LONG PLA" || table.Score(1) != 1000)
            throw new Exception("[LLPRESENT] Score insertion and eight-character name.");
        if (new HighScoreTable(table.Save()).Score(9) != 200) throw new Exception("[LLPRESENT] Last score eviction.");
        bytes[0] = (byte)'x';
        try { _ = new HighScoreTable(bytes); throw new Exception("[LLPRESENT] Corrupt scores accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("[LLPRESENT] High-score ranking, persistence, ties and corruption checks passed.");
    }
}
