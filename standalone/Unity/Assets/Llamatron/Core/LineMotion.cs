using System;

namespace Llamatron.Core
{
    public static class LineMotion
    {
        private static short Word(int value) => unchecked((short)value);
        // CS:32fe-33df. Local words replace the routine's private scratch globals.
        // BP=0 advances one raster step. Exhausting the line returns (1,1),
        // including coincident endpoints, rather than returning the target.
        public static (short X, short Y) Advance(short x, short y, short targetX, short targetY, short steps)
        {
            short ax = x, bx = y, cx = targetX, dx = targetY;
            short direction, originalMajor, major, minor;
            bool swapped = false, reverseMinor;
            int swaps = 0;
            while (true)
            {
                direction = -1; originalMajor = ax;
                if (Word(cx - ax) < 0) { direction = 1; (cx, ax) = (ax, cx); }
                major = Word(cx - ax); minor = Word(dx - bx); reverseMinor = minor < 0;
                if (reverseMinor) minor = Word(bx - dx);
                if (Word(minor - major) <= 0) break;
                if (direction >= 0) (cx, ax) = (ax, cx);
                (bx, ax) = (ax, bx); (cx, dx) = (dx, cx); swapped = true;
                if (++swaps > 2) throw new InvalidOperationException("Original line-axis selection would repeat for these coordinates.");
            }
            short twiceMinor = Word(minor << 1), error = Word((minor << 1) - major);
            short diagonal = Word((minor - major) << 1), position = bx, cursor = ax;
            while (true)
            {
                if (Word(cursor - cx) >= 0) return (1, 1);
                cursor = Word(cursor + 1); originalMajor = Word(originalMajor + (direction < 0 ? 1 : -1));
                if (error < 0) error = Word(error + twiceMinor);
                else { position = Word(position + (reverseMinor ? -1 : 1)); error = Word(error + diagonal); }
                steps = Word(steps - 1);
                if (steps < 0) return swapped ? (position, originalMajor) : (originalMajor, position);
            }
        }
    }
}
