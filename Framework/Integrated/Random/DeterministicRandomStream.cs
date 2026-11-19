using System;

namespace ObFramework.Integrated.Random
{
    /// <summary>A reproducible SplitMix64 stream owned by one caller.</summary>
    public sealed class DeterministicRandomStream
    {
        private ulong _state;

        internal DeterministicRandomStream(ulong seed, ulong[] keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            _state = seed;
            if (keys.Length == 0) return;

            // FNV-1a over the seed and keys, eight little-endian bytes per value.
            _state = Fold(14695981039346656037UL, seed);
            foreach (var key in keys)
                _state = Fold(_state, key);
        }

        private static ulong Fold(ulong hash, ulong value)
        {
            unchecked
            {
                for (var i = 0; i < 8; i++)
                {
                    hash ^= (byte)value;
                    hash *= 1099511628211UL;
                    value >>= 8;
                }
                return hash;
            }
        }

        /// <summary>Draws the next 64 random bits.</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var value = _state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        /// <summary>Draws an integer from zero up to, but excluding, exclusiveMax.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            var bound = (ulong)exclusiveMax;
            var limit = ulong.MaxValue - ulong.MaxValue % bound;
            ulong value;
            do { value = NextUInt64(); } while (value >= limit);
            return (int)(value % bound);
        }
    }
}
