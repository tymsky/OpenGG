namespace OpenGG.Core.Sim;

/// <summary>Deterministic RNG (mulberry32) whose state lives inside the saved game.</summary>
public sealed class Rng(GameState holder)
{
    public double Next()
    {
        unchecked
        {
            uint t = holder.Rng += 0x6D2B79F5u;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    /// <summary>Integer in [min, max], both inclusive.</summary>
    public int Int(int min, int max) => min + (int)Math.Floor(Next() * (max - min + 1));

    public double Range(double min, double max) => min + Next() * (max - min);

    public T Pick<T>(IReadOnlyList<T> list) => list[(int)Math.Floor(Next() * list.Count)];

    public T Weighted<T>(IReadOnlyList<T> list, Func<T, double> weight)
    {
        double total = list.Sum(weight);
        double r = Next() * total;
        foreach (var t in list)
        {
            r -= weight(t);
            if (r <= 0) return t;
        }
        return list[^1];
    }

    public List<T> Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(Next() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
