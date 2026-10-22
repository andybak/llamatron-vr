using System;
using System.Collections.Generic;

namespace Llamatron.Core
{
    public readonly struct SpriteCommand
    {
        public readonly short X, Y, RowSpacing, Skew;
        public readonly ushort Offset;
        public readonly bool Transformed, Flat;
        public readonly bool IsLine;
        public readonly short EndX, EndY;
        public readonly byte ColorIndex;
        public SpriteCommand(short x, short y, ushort offset, bool transformed = false, short rowSpacing = 1, short skew = 0, bool flat = false)
        {
            X = x; Y = y; Offset = offset; Transformed = transformed;
            RowSpacing = rowSpacing; Skew = skew; Flat = flat;
            IsLine = false; EndX = 0; EndY = 0; ColorIndex = 0;
        }

        private SpriteCommand(short x, short y, short endX, short endY, byte colorIndex) : this(x, y, 0)
        { IsLine = true; EndX = endX; EndY = endY; ColorIndex = colorIndex; }
        public static SpriteCommand Line(short x, short y, short endX, short endY, byte colorIndex) => new SpriteCommand(x, y, endX, endY, colorIndex);
    }

    // Source render dispatch for the currently ported actor families. Pixel
    // rasterization and 3D presentation remain outside the portable simulation.
    public sealed class SpriteFrame
    {
        public List<SpriteCommand> Commands { get; } = new List<SpriteCommand>(192);
        private static ushort Graphic(int index) => OriginalDamageData.SpriteOffsets[index];

        public void Build(GameSimulation game)
        {
            Commands.Clear();
            ActorContext state = game.Actors;
            // CS:1499: bullets precede actor draws; hot bullets use the +158 bank range.
            foreach (Bullet shot in game.Bullets.Shots)
                if (shot.Active) Commands.Add(new SpriteCommand(shot.X, shot.Y,
                    Graphic(shot.Sprite + (state.HotBulletTimer >= 0 ? 158 : 0)), flat: true));
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity entity = state.Spawning.Pool.Records[i];
                if (!entity.Active) continue;
                ushort offset = entity[0x0c] < 0 ? unchecked((ushort)entity[0x2c]) : Graphic(entity[0x0c]);
                switch (unchecked((byte)entity.RenderState))
                {
                    case 2: case 3: // CS:4779 / 46b0: head, then EGA set/reset color 1.
                        Commands.Add(new SpriteCommand(entity.X, unchecked((short)(entity.Y - 10)), 0x7aa2));
                        if ((entity[0x24] & 0xff00) != 0)
                            Commands.Add(entity.RenderState == 2 ? SpriteCommand.Line(8, entity.Y, 311, entity.Y, 1)
                                                               : SpriteCommand.Line(entity.X, 10, entity.X, 195, 1));
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset)); // CS:4802 -> 44ab/8cbb.
                        break;
                    case 0: case 12: case 15: // CS:4465
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset, true, entity[0x0e])); break;
                    case 1: case 11: // CS:4bab
                        offset = OriginalSpawnGraphics.EnemySpritePairs[state.Spawning.SpriteVariant * 2 + (entity[0x28] & 1)];
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset)); break;
                    case 5: case 6: case 7: // CS:4499
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset, true, entity[0x0e], entity[0x14])); break;
                    case 4: // CS:4646 -> 8cbb -> 4499 uses DI after the blitter changes it.
                        throw new NotSupportedException("Render 4 continuation after the sprite writer requires an original-runtime trace.");
                    case 9: case 10: // CS:4676, render state progresses on each display, not each IRQ.
                        entity.RenderState = unchecked((short)(entity.RenderState + 1));
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset));
                        break;
                    case 14: // CS:474a: oldest ring entry first, four trail sprites, then body.
                        int cursor = entity[0x14] & 12;
                        for (int trail = 0; trail < 4; trail++)
                        {
                            Commands.Add(new SpriteCommand(entity[0x16 + cursor], entity[0x18 + cursor],
                                                           OriginalSpawnGraphics.TrailSprites[trail]));
                            cursor = (cursor + 4) & 12;
                        }
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset));
                        break;
                    case 13: Commands.Add(new SpriteCommand(entity.X, entity.Y, offset)); break;
                    case 16: // CS:468a
                        if (state.LoveTimer >= 0) Commands.Add(new SpriteCommand(entity.X, (short)(entity.Y - 10), 0x92a4));
                        Commands.Add(new SpriteCommand(entity.X, entity.Y, offset)); break;
                    case 8: case 17: case 18: break;
                    default: throw new NotSupportedException($"Unported render state {entity.RenderState}.");
                }
                // CS:4392: score popup lifetime advances in the render path.
                if (entity[0x32] != 0)
                {
                    Commands.Add(new SpriteCommand(entity[0x32], entity[0x34], Graphic(entity[0x36])));
                    entity[0x38] = unchecked((short)(entity[0x38] - 1));
                    if (entity[0x38] == 0) entity[0x32] = 0;
                }
            }
            Player(state, false);
            if ((state.Spawning.PlayerFlags & 1) != 0) Player(state, true);
        }

        private void Player(ActorContext state, bool second)
        {
            if (second && (state.Spawning.PlayerFlags & 2) == 0)
                throw new NotSupportedException("Droid rendering is not ported yet.");
            short x = second ? state.Spawning.SecondPlayerX : state.Spawning.PlayerX;
            short y = second ? state.Spawning.SecondPlayerY : state.Spawning.PlayerY;
            short facing = (second ? state.SecondaryMotion : state.PrimaryMotion).Facing;
            int phase = ((facing < 0 ? y : x) >> 1) & 3;
            int start = second ? 94 : ((state.Spawning.PlayerFlags & 0x10) != 0 && (state.Spawning.PlayerFlags & 2) == 0 ? 94 : 12);
            ushort offset = Graphic(start + Math.Abs((int)facing) + phase);
            if (state.HitLockTicks != 0) Commands.Add(new SpriteCommand(x, (short)(y - 15), 0xbe88));
            short status = second ? state.SecondaryStatus : state.PrimaryStatus;
            if (status != 0)
            {
                short spacing = unchecked((short)Math.Abs((int)status));
                Commands.Add(new SpriteCommand(x, y, offset, true, spacing, 1));
                Commands.Add(new SpriteCommand(x, y, offset, true, spacing, -1));
                Commands.Add(new SpriteCommand(x, y, offset, true, spacing));
            }
            else
            {
                Commands.Add(new SpriteCommand(x, y, offset));
                short labelY = second ? state.SecondaryRescueLabelY : state.PrimaryRescueLabelY;
                if (labelY >= 0) Commands.Add(new SpriteCommand(x, labelY, 0x5296));
            }
        }
    }
}
