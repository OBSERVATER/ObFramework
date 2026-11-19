namespace ObFramework.Integrated.Random
{
    public class SystemRandomSource : ARandomSource
    {
        private System.Random _random;
        public SystemRandomSource(int seed) : base(seed)
        {
            _random = new System.Random(seed);
        }
        public override int Range(int minInclude, int maxInclude)
        {
            if (minInclude > maxInclude)
                throw new System.ArgumentOutOfRangeException(nameof(maxInclude), "Maximum must be greater than or equal to minimum.");

            if (maxInclude < int.MaxValue)
                return _random.Next(minInclude, maxInclude + 1);

            // Shift the interval down before sampling so the exclusive bound cannot overflow.
            if (minInclude > int.MinValue)
                return _random.Next(minInclude - 1, maxInclude) + 1;

            // The full int domain has 2^32 values; combine two independent 16-bit draws.
            return (_random.Next(0, 1 << 16) << 16) | _random.Next(0, 1 << 16);
        }
        public override float Range(float minInclude, float maxInclude) 
            => (float)(_random.NextDouble()*((double)maxInclude-minInclude)+minInclude);
        public override float RandomValue01 => (float)_random.NextDouble();
    }
}
