using System.Collections.Generic;

namespace Llamatron.Core
{
    // Modeled fields of the original 200-byte DS:21a6..226d block. Scores,
    // random cursor, actor pool, motion and parameters outside it stay shared.
    internal sealed class CampaignSnapshot
    {
        internal short Lives, SpriteVariant, WaveIndex, ScoreRepeat, LoveTimer;
        internal short Announcement, SpecialDropCountdown, DifficultyIncrement;
        internal Dictionary<ushort, short> Parameters;
        internal WaveDefinition? Survivors;

        internal CampaignSnapshot(ActorContext context, WaveDefinition? survivors)
        {
            SpawnContext spawn = context.Spawning;
            Lives = context.Lives; SpriteVariant = spawn.SpriteVariant; WaveIndex = spawn.WaveIndex;
            ScoreRepeat = context.ScoreRepeat; LoveTimer = context.LoveTimer; Announcement = context.Announcement;
            SpecialDropCountdown = context.SpecialDropCountdown; DifficultyIncrement = spawn.DifficultyIncrement;
            Parameters = spawn.Parameters.CaptureCampaign(); Survivors = survivors;
        }

        internal void Restore(ActorContext context)
        {
            SpawnContext spawn = context.Spawning;
            context.Lives = Lives; spawn.SpriteVariant = SpriteVariant; spawn.WaveIndex = WaveIndex;
            context.ScoreRepeat = ScoreRepeat; context.LoveTimer = LoveTimer; context.Announcement = Announcement;
            context.SpecialDropCountdown = SpecialDropCountdown; spawn.DifficultyIncrement = DifficultyIncrement;
            spawn.Parameters.RestoreCampaign(Parameters);
        }
    }
}
