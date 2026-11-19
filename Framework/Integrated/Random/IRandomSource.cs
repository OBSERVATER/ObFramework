namespace ObFramework.Integrated.Random
{
    public interface IRandomSource
    {
        int Range(int minInclude, int maxInclude);
        float Range(float minInclude, float maxInclude);
        float RandomValue01 { get; }
        int Seed { get; }
    }

    public abstract class ARandomSource : IRandomSource
    {
        public abstract int Range(int minInclude, int maxInclude);
        public abstract float Range(float minInclude, float maxInclude);
        public abstract float RandomValue01 { get; }
        public int Seed { get; }
        protected ARandomSource(int seed)
        {
            Seed = seed;
        }
    }
}