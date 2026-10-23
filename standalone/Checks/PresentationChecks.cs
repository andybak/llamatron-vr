using System;
using System.Text;
using Llamatron.Core;

static class PresentationChecks
{
    public static void Run()
    {
        var name = new HighScoreNameEntry();
        name.Reset("OLD NAME"); name.Type('N'); name.Type('E'); name.Type('W');
        if (name.Value != "NEW") throw new Exception("[LLPRESENT] Typing must replace a prefilled name.");
        name.Backspace();
        if (name.Value != "NE") throw new Exception("[LLPRESENT] Backspace must edit new typing.");
        name.Reset("OLD NAME"); name.Backspace();
        if (name.Value != "") throw new Exception("[LLPRESENT] Backspace must clear a selected prefilled name.");
        foreach (char c in "123456789") name.Type(c);
        if (name.Value != "12345678") throw new Exception("[LLPRESENT] Names must stop at eight characters.");
        name.Reset("KEEP"); name.Type('\n');
        if (name.Value != "KEEP") throw new Exception("[LLPRESENT] Non-printing input must not replace a name.");
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
        Console.WriteLine("[LLPRESENT] Name editing, high-score ranking, persistence, ties and corruption checks passed.");
    }
}
