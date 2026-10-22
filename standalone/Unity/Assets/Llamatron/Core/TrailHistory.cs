namespace Llamatron.Core
{
    public readonly struct TrailPoint
    {
        public short X { get; }
        public short Y { get; }
        public short Direction { get; }

        public TrailPoint(short x, short y, short direction)
        {
            X = x; Y = y; Direction = direction;
        }
    }

    // CS:0501 initializes 128 eight-byte records. CS:1961-19ce advances only
    // when the player's position changes; direction-only changes do not append.
    public sealed class TrailHistory
    {
        private readonly TrailPoint[] points = new TrailPoint[128];
        public int ByteCursor { get; private set; }

        public TrailHistory(short x, short y)
        {
            Reset(x, y);
        }

        public void Reset(short x, short y)
        {
            ByteCursor = 0;
            for (int i = 0; i < points.Length; i++) points[i] = new TrailPoint(x, y, 0);
        }

        public TrailPoint AtOffset(int offset) => points[((ByteCursor + offset) & 0x3f8) / 8];

        public void Record(short x, short y, short direction)
        {
            TrailPoint current = AtOffset(0);
            if (current.X == x && current.Y == y) return;
            ByteCursor = (ByteCursor + 8) & 0x3f8;
            points[ByteCursor / 8] = new TrailPoint(x, y, direction);
        }

        // CS:4bfb. The entity delay is in bytes, including the original masking.
        public void Follow(Entity entity)
        {
            TrailPoint point = AtOffset(-entity[0x14]);
            entity.X = point.X; entity.Y = point.Y;
            short position = point.Direction == 0 || point.Direction == 6 ? point.X : point.Y;
            entity[0x0c] = unchecked((short)(((position >> 2) & 1) + point.Direction + entity[0x30]));
        }
    }
}
