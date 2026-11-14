namespace DeepSpaceQoL;

/// <summary>Read-only presentation geometry; never changes an item's inventory pose.</summary>
internal static class DisplayFixtureLayout
{
    internal readonly struct Size
    {
        internal readonly float Width, Height;
        internal Size(float width, float height) { Width = Math.Max(1, width); Height = Math.Max(1, height); }
    }
    internal readonly struct Pose
    {
        internal readonly float X, Y, Width, Height, Cord, Scale;
        internal Pose(float x, float y, Size size, float scale, float cord)
        { X = x; Y = y; Width = size.Width * scale; Height = size.Height * scale; Cord = cord; Scale = scale; }
    }

    // Two banks leave the central shutter and the customer's face clear.
    internal static Pose[] Hanging(float width, float height, IReadOnlyList<Size> sizes)
    {
        if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height)) return Array.Empty<Pose>();
        var result = new Pose[sizes.Count];
        if (sizes.Count == 0) return result;
        // The calendar begins around 19% of the scene width. Reserve that whole
        // vertical strip rather than letting a tall bottle cover its date text.
        var bankWidths = new[] { width * .115f, width * .22f };
        float availableHeight = height * .105f;
        float scale = 1;
        for (int attempt = 0; attempt < 80; attempt++, scale *= .93f)
            if (TryPack(scale)) return result;
        return result;

        bool TryPack(float factor)
        {
            float gap = Math.Max(.5f, 3 * factor), cord = Math.Max(.5f, 9 * factor);
            var x = new float[2]; var y = new float[2]; var rowHeight = new float[2];
            for (int i = 0; i < sizes.Count; i++)
            {
                float w = sizes[i].Width * factor, h = sizes[i].Height * factor;
                if (w > Math.Max(bankWidths[0], bankWidths[1])) return false;
                int bank = x[0] / bankWidths[0] + y[0] / availableHeight <= x[1] / bankWidths[1] + y[1] / availableHeight ? 0 : 1;
                // Prefer the other bank before starting a second row.
                if (x[bank] + w > bankWidths[bank] && x[1 - bank] + w <= bankWidths[1 - bank]) bank = 1 - bank;
                if (w > bankWidths[bank]) bank = 1 - bank;
                if (x[bank] + w > bankWidths[bank]) { y[bank] += rowHeight[bank] + gap; x[bank] = 0; rowHeight[bank] = 0; }
                float drop = cord + (i % 3) * factor;
                if (y[bank] + drop + h > availableHeight) return false;
                result[i] = new Pose(width * (bank == 0 ? .07f : .70f) + x[bank], height * .025f + y[bank] + drop, sizes[i], factor, drop);
                x[bank] += w + gap;
                rowHeight[bank] = Math.Max(rowHeight[bank], h + drop);
            }
            return true;
        }
    }

    // The outlined desktop patch in the user's screenshot: left of the recorder,
    // above the inventory and away from the transfer buttons. Clamp the whole grid.
    internal static (float X, float Y) Lamps(float width, float height, float gridWidth, float gridHeight)
    {
        float right = width * .91f, bottom = height * .61f;
        return (Math.Max(2, Math.Min(width - gridWidth - 2, right - gridWidth)),
            Math.Max(2, Math.Min(height - gridHeight - 2, bottom - gridHeight)));
    }
}
