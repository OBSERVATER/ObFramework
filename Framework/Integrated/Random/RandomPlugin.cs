using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;

namespace ObFramework.Integrated.Random
{
    public class RandomPlugin : IPlugin
    {
        private IRandomSource _source;
        private int _seed;
        public RandomPlugin(IRandomSource source)
        {
            _source = source;
            _seed = source.Seed;
        }
        public int Seed => _seed;
        /// <summary>Creates a fresh stream from an explicit seed and ordered numeric keys.
        /// With no keys, starts directly at the supplied seed.</summary>
        public static DeterministicRandomStream CreateStream(ulong seed, params ulong[] keys)
            => new DeterministicRandomStream(seed, keys);

        public void Register(PluginRegistration registration)
        {
        }
        public Task InitAsync(IPluginContext plugins, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(IPluginContext plugins) => Task.CompletedTask;
        public int Range(int minInclude, int maxInclude) => _source.Range(minInclude, maxInclude);
        public float Range(float minInclude, float maxInclude) => _source.Range(minInclude, maxInclude);
        public float Value01 => _source.RandomValue01;
        public T GetRandomItem<T>(List<T> origin)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            return origin.Count == 0 ? throw new ArgumentException("Collection cannot be empty.", nameof(origin)) 
                : origin[Range(0, origin.Count - 1)];
        }

        /// <summary>Shuffles the supplied list in place and returns that same list.</summary>
        public List<T> Shuffle<T>(List<T> origin)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            for (int i = origin.Count - 1; i > 0; i--)
            {
                int j = Range(0, i);
                (origin[i], origin[j]) = (origin[j], origin[i]);
            }

            return origin;
        }
        /// <summary>Selects an entry by its positive weight. Finite non-positive weights are ignored.</summary>
        public T GetByWeight<T>(Dictionary<T, float>? map) where T : notnull
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Count == 0) throw new ArgumentException("Weights dictionary cannot be empty.", nameof(map));

            double total = 0d;
            foreach (var kvp in map)
            {
                var weight = kvp.Value;
                if (float.IsNaN(weight) || float.IsInfinity(weight))
                    throw new ArgumentException("Weights must be finite.", nameof(map));
                if (weight > 0f) total += weight;
            }

            if (total <= 0d)
            {
                throw new InvalidOperationException("At least one probability must be greater than 0.");
            }
            
            double randomNumber = Value01 * total;
            double cumulative = 0d;
            T lastValidKey = default!;

            foreach (var kvp in map)
            {
                if (kvp.Value <= 0f) continue;
                
                cumulative += kvp.Value;
                lastValidKey = kvp.Key;

                if (randomNumber < cumulative)
                {
                    return kvp.Key;
                }
            }

            return lastValidKey;
        }
    }
}
