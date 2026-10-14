namespace BbRando.Core;

/// <summary>
/// Gerador determinístico (xoshiro256**) para que a mesma seed gere sempre o mesmo resultado,
/// em qualquer sistema e versão do .NET. Cada etapa usa um fluxo próprio derivado da seed, assim
/// mudar a opção de itens não altera os inimigos sorteados (e vice-versa).
/// </summary>
public sealed class Rng
{
    ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed, string stream)
    {
        var x = seed ^ Fnv1a(stream);
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    static ulong SplitMix(ref ulong x)
    {
        var z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Fnv1a(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public ulong NextULong()
    {
        var result = ulong.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = ulong.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Inteiro em [0, max).</summary>
    public int Next(int max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max));
        // Rejeição para evitar viés de módulo.
        var limit = ulong.MaxValue - ulong.MaxValue % (ulong)max;
        ulong value;
        do value = NextULong(); while (value >= limit);
        return (int)(value % (ulong)max);
    }

    /// <summary>Número em [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Verdadeiro com a porcentagem dada (0–100).</summary>
    public bool Chance(double percent) => NextDouble() * 100.0 < percent;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Sorteio ponderado; retorna -1 se todos os pesos forem zero.</summary>
    public int PickWeighted(IReadOnlyList<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights)
            total += Math.Max(0, w);
        if (total <= 0)
            return -1;
        var roll = NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0)
                return i;
        }
        return weights.Count - 1;
    }
}
