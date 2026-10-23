using System;
using System.Collections.Generic;

namespace Llamatron.Core
{
    [Serializable]
    public sealed class TitleParticleDefinition
    {
        public int x, y, velocity;
        public int[] sprites = Array.Empty<int>();
    }

    public sealed class TitleParticle
    {
        private readonly int startX, startY, startVelocity;
        private readonly int[] sprites;
        private int x, y, velocityX, velocityY;
        public int X => x >> 16;
        public int Y => y >> 16;
        public int Sprite => sprites[((X >> 2) & 1) + (velocityX < 0 ? 0 : 2)];
        public TitleParticle(TitleParticleDefinition definition)
        {
            if (definition.sprites.Length != 4) throw new ArgumentException("A title beastie needs two frames for each direction.");
            startX = definition.x << 16; startY = definition.y << 16; startVelocity = definition.velocity;
            sprites = (int[])definition.sprites.Clone(); Reset();
        }
        internal void Reset() { x = startX; y = startY; velocityX = startVelocity; velocityY = 0; }
        // CS:4571: 16.16 motion, upward acceleration and reflected X boundaries.
        internal void Step()
        {
            unchecked
            {
                y -= velocityY;
                if (y < 0) { y = 0; velocityY = -2 << 16; }
                velocityY += 0x0a00;
                x += velocityX;
                if (x < (16 << 16)) { x = 2 * (16 << 16) - x; velocityX = -velocityX; }
                else if (x > (304 << 16)) { x = 2 * (304 << 16) - x; velocityX = -velocityX; }
            }
        }
    }

    // CS:3ee0/3f6f/402c. Control runs on IRQ 0 (DS:39e4); the display
    // callback latches completed letters onto the original page buffers.
    public sealed class TitleReveal
    {
        public const int LetterCount = 10;
        private readonly int[] counters = new int[LetterCount];
        private readonly TitleParticle[] particles;
        private readonly double displayHz;
        private GameClock clock;
        private int hold;
        public event Action<byte, byte, byte>? SoundRequested;
        public event Action? ServiceAudio;
        public IReadOnlyList<TitleParticle> Particles => particles;
        public long InterruptCount => clock.InterruptCount;
        public double Time => clock.Time;
        public bool Settled => counters[LetterCount - 1] <= 0;
        public bool IdleMotion => hold == 0;
        public int Counter(int letter) => counters[letter];
        public int RowSpacing(int letter) => Math.Max(1, counters[letter]);
        public bool Visible(int letter) => counters[letter] < 0x80;
        public TitleReveal(double displayHz, IReadOnlyList<TitleParticleDefinition>? definitions = null)
        {
            this.displayHz = displayHz; clock = new GameClock(displayHz);
            particles = new TitleParticle[definitions?.Count ?? 0];
            for (int i = 0; i < particles.Length; i++) particles[i] = new TitleParticle(definitions![i]);
            ResetState();
        }
        private void ResetState()
        {
            clock = new GameClock(displayHz); hold = 80;
            for (int i = 0; i < LetterCount; i++) counters[i] = 128 + 16 * i;
            foreach (TitleParticle particle in particles) particle.Reset();
        }
        public void Restart() { ResetState(); SoundRequested?.Invoke(21, 1, 2); }
        public void Advance(double seconds) => clock.Advance(seconds, Interrupt, Display);
        private void Interrupt()
        {
            ServiceAudio?.Invoke();
            if (hold == 80)
            {
                bool moving = false;
                for (int i = 0; i < LetterCount; i++)
                {
                    if (counters[i] <= 0) continue;
                    moving = true; counters[i] -= 2;
                    if (counters[i] == 0) SoundRequested?.Invoke(2, 1, 2);
                }
                if (moving) return;
                hold--; SoundRequested?.Invoke(20, 0, 2); return;
            }
            if (hold > 0)
            {
                if (--hold == 0) SoundRequested?.Invoke(17, 1, 2);
                return;
            }
            foreach (TitleParticle particle in particles) particle.Step();
        }
        private void Display()
        {
            for (int i = 0; i < LetterCount; i++) if (counters[i] == 0) counters[i] = -1;
        }
    }
}
