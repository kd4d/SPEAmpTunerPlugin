#nullable enable

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// Maps CAT frequency (kHz) to SPE band index 0–10. 4 m is out of scope (returns -1).
    /// Half-open intervals <c>[fMin, fMax)</c> except 60 m envelope (inclusive) per plan.
    /// </summary>
    internal static class SpeBandLookup
    {
        /// <summary>
        /// Returns band index 0–10, or -1 if frequency does not map (including 4 m and gaps).
        /// </summary>
        public static int DeriveBandIndex(int frequencyKhz)
        {
            // 60 m — inclusive envelope 5330.5–5406.5 kHz (use ×2 integer math for half-kHz edges).
            if (frequencyKhz * 2 >= 10661 && frequencyKhz * 2 <= 10813)
                return 2;

            if (InHalfOpen(frequencyKhz, 1800, 2000)) return 0;   // 160 m
            if (InHalfOpen(frequencyKhz, 3500, 4000)) return 1;   // 80 m
            if (InHalfOpen(frequencyKhz, 7000, 7300)) return 3;   // 40 m
            if (InHalfOpen(frequencyKhz, 10100, 10150)) return 4; // 30 m
            if (InHalfOpen(frequencyKhz, 14000, 14350)) return 5; // 20 m
            if (InHalfOpen(frequencyKhz, 18068, 18168)) return 6; // 17 m
            if (InHalfOpen(frequencyKhz, 21000, 21450)) return 7; // 15 m
            if (InHalfOpen(frequencyKhz, 24890, 24990)) return 8; // 12 m
            if (InHalfOpen(frequencyKhz, 28000, 29700)) return 9; // 10 m
            if (InHalfOpen(frequencyKhz, 50000, 54000)) return 10; // 6 m

            // 4 m (approx 70 MHz) — not implemented
            return -1;
        }

        private static bool InHalfOpen(int f, int min, int max) => f >= min && f < max;
    }
}
