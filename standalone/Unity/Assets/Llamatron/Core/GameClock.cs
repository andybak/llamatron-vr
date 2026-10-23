using System;

namespace Llamatron.Core
{
    // Deliver PIT and display events in timestamp order, independently of the
    // host's Update rate. Recalculate deadlines from counts to avoid drift from
    // repeatedly adding a rounded period. The initial hardware phase still
    // requires a captured DOS trace for a cycle-identical replay.
    public sealed partial class GameClock
    {
        public double InterruptHz => OriginalTiming.InterruptHz;
        public double DisplayHz { get; }
        public double Time { get; private set; }
        public long InterruptCount { get; private set; }
        public long DisplayCount { get; private set; }

        public GameClock(double displayHz)
        {
            if (double.IsNaN(displayHz) || double.IsInfinity(displayHz) || displayHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(displayHz));
            DisplayHz = displayHz;
        }

        public void Advance(double seconds, Action interrupt, Action display, Func<bool>? stop = null)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (interrupt == null) throw new ArgumentNullException(nameof(interrupt));
            if (display == null) throw new ArgumentNullException(nameof(display));
            double end = Time + seconds;
            while (true)
            {
                double nextInterrupt = (InterruptCount + 1) / InterruptHz;
                double nextDisplay = (DisplayCount + 1) / DisplayHz;
                double next = Math.Min(nextInterrupt, nextDisplay);
                if (next > end + 1e-12) { Time = Math.Max(Time, end); return; }
                Time = next;
                if (nextInterrupt <= nextDisplay) { InterruptCount++; interrupt(); }
                else { DisplayCount++; display(); }
                if (stop != null && stop()) return;
            }
        }
    }
}
