using System;

namespace BasketballManager.Simulation
{
    public sealed class SimulationRandom
    {
        private readonly Random _random;

        public SimulationRandom(int seed)
        {
            _random = new Random(seed);
        }

        public SimulationRandom()
        {
            _random = new Random();
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return (float)(_random.NextDouble() * (maxInclusive - minInclusive) + minInclusive);
        }

        public double NextDouble()
        {
            return _random.NextDouble();
        }

        public bool Chance(float probability)
        {
            return NextDouble() < probability;
        }
    }
}
