using System;
using System.Collections.Generic;
using Llamatron.Core;

static class PaletteChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new Exception($"[LLPALETTE] {message}"); }

    public static void Run()
    {
        var game = new GameSimulation(RandomSequence.Shuffle(() => 0));
        var campaign = new GameCampaign(game);
        PaletteState palette = game.Palette;
        game.Actors.PaletteSequenceOffset = 0x0713;
        // Original runtime CS:e9c0 captures: CX = green:blue, DH = red.
        int[] red = { 56, 56, 32, 0, 0, 0, 0 };
        int[] green = { 0, 0, 0, 32, 56, 56, 0 };
        int[] blue = { 0, 56, 56, 56, 56, 0, 0 };
        for (int i = 0; i < 7; i++)
        {
            campaign.TickDisplay();
            Require(palette[0].Red == red[i] && palette[0].Green == green[i] && palette[0].Blue == blue[i],
                    $"Original damage-flash BIOS color {i} differs.");
        }
        Require(game.Actors.PaletteSequenceOffset == 0, "Terminator did not clear the pointer.");
        Require(palette.ReloadCountdown == 249, "Initially zero byte countdown must underflow, not reload at startup.");
        game.Actors.PaletteSequenceOffset = 0x06cd;
        campaign.TickDisplay(); // White.
        campaign.TickDisplay(); // 0x1000 is a black frame, not a terminator.
        Require(game.Actors.PaletteSequenceOffset == 0x06d1 && palette[0].Red == 0, "Black delay word terminated early.");
        game.Actors.PaletteSequenceOffset = 0x06c9;
        campaign.TickDisplay();
        game.Actors.PaletteSequenceOffset = 0; // Cancellation leaves the last color latched.
        campaign.TickDisplay();
        Require(palette[0].Red == 56, "Cancelling a sequence must not write black.");
        while (palette.ReloadCountdown != 1) campaign.TickDisplay();
        campaign.TickDisplay();
        Require(palette.ReloadCountdown == 30 && palette[0].Red == 0, "Periodic reload did not restore the baseline.");
        palette.Effects = PaletteEffects.None;
        game.Actors.PaletteSequenceOffset = 0x0713;
        byte countdown = palette.ReloadCountdown;
        campaign.TickDisplay(); campaign.TickInterrupt(0);
        Require(game.Actors.PaletteSequenceOffset == 0x0713 && palette.ReloadCountdown == countdown,
                "Disabled effects must retain sequence and reload state.");
        palette.Effects = PaletteEffects.Cycle;
        campaign.TickInterrupt(0);
        PaletteState.Color expected = PaletteState.Decode(OriginalPaletteData.Cycle[game.Actors.FrameCounter & 15]);
        Require(palette[1].Red == expected.Red && palette[1].Green == expected.Green && palette[1].Blue == expected.Blue,
                "Entry-one cycle must follow the IRQ counter independently of display sequencing.");

        var wipes = new List<WipeStep>();
        campaign.WipeReady += wipes.Add;
        game.Actors.Outcome = 2;
        campaign.TickDisplay();
        for (int i = 0; i < 11; i++) campaign.TickDisplay();
        Require(campaign.Phase == CampaignPhase.WipeExpand, "Clear delay did not reach wipe.");
        short frame = game.Actors.FrameCounter;
        for (int i = 0; i < 95; i++) campaign.TickDisplay();
        Require(wipes.Count == 95 && game.Actors.FrameCounter == frame, "Wipe must draw 48+47 rectangles without gameplay ticks.");
        Require(wipes[0].Left == 94 && wipes[0].Top == 102 && wipes[0].Right == 224 && wipes[0].Bottom == 104,
                "First wipe rectangle differs.");
        Require(wipes[47].Left == 0 && wipes[47].Top == 8 && wipes[47].Right == 318 && wipes[47].Bottom == 198,
                "Expansion must reach the final 2x2 blocks at the arena edges.");
        Require(wipes[48].ColorIndex == 0 && wipes[94].Left == 2 && wipes[94].Bottom == 196,
                "Erasure must stop one rectangle before the outermost border.");
    }
}
