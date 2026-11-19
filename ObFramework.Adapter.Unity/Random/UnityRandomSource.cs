using System;
using ObFramework.Integrated.Random;
using UnityRandom = UnityEngine.Random;

namespace ObFramework.Adapter.Unity.Random
{
    /// <summary>A main-thread random source with instance-owned Unity random state.</summary>
    public class UnityRandomSource : ARandomSource
    {
        private UnityRandom.State _state;

        public UnityRandomSource(int seed) : base(seed)
        {
            var previous = UnityRandom.state;
            try
            {
                UnityRandom.InitState(seed);
                _state = UnityRandom.state;
            }
            finally
            {
                UnityRandom.state = previous;
            }
        }

        public override int Range(int minInclude, int maxInclude)
        {
            if (minInclude > maxInclude)
                throw new ArgumentOutOfRangeException(nameof(maxInclude), "Maximum must be greater than or equal to minimum.");

            var previous = UnityRandom.state;
            try
            {
                UnityRandom.state = _state;
                if (maxInclude < int.MaxValue)
                    return UnityRandom.Range(minInclude, maxInclude + 1);

                // Shift the interval down before sampling so the exclusive bound cannot overflow.
                if (minInclude > int.MinValue)
                    return UnityRandom.Range(minInclude - 1, maxInclude) + 1;

                // The full int domain has 2^32 values; combine two independent 16-bit draws.
                return (UnityRandom.Range(0, 1 << 16) << 16) | UnityRandom.Range(0, 1 << 16);
            }
            finally
            {
                _state = UnityRandom.state;
                UnityRandom.state = previous;
            }
        }

        public override float Range(float minInclude, float maxInclude)
        {
            var previous = UnityRandom.state;
            try
            {
                UnityRandom.state = _state;
                return UnityRandom.Range(minInclude, maxInclude);
            }
            finally
            {
                _state = UnityRandom.state;
                UnityRandom.state = previous;
            }
        }

        public override float RandomValue01
        {
            get
            {
                var previous = UnityRandom.state;
                try
                {
                    UnityRandom.state = _state;
                    return UnityRandom.value;
                }
                finally
                {
                    _state = UnityRandom.state;
                    UnityRandom.state = previous;
                }
            }
        }
    }
}
