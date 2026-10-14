using BbRando.Core;

namespace BbRando.Tests;

/// <summary>
/// Testes com os arquivos do jogo. Defina BBR_VANILLA com a cópia limpa (pasta ou .zip) para rodá-los;
/// sem ela, eles passam sem fazer nada.
/// </summary>
public class GameDataTests
{
    static readonly string? Vanilla = Environment.GetEnvironmentVariable("BBR_VANILLA");

    static string Run(RandomizerSettings settings)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbr-teste-" + Guid.NewGuid().ToString("N"));
        Randomizer.Run(Vanilla!, dir, settings);
        return dir;
    }

    static Dictionary<string, string> Hashes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f),
                f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void MesmaSeed_MesmoResultado()
    {
        if (Vanilla == null)
            return;
        var settings = new RandomizerSettings { Seed = 777, SwapBosses = true, IncludeLesserBosses = true, RandomizeEnemyDrops = true };
        var a = Run(settings);
        var b = Run(settings.Clone());
        try
        {
            Assert.Equal(Hashes(a), Hashes(b));
        }
        finally
        {
            Directory.Delete(a, true);
            Directory.Delete(b, true);
        }
    }

    [Fact]
    public void Swap_CadaChefeUmaVez()
    {
        if (Vanilla == null)
            return;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var dir = Run(new RandomizerSettings { Seed = seed, SwapBosses = true, IncludeLesserBosses = true, RandomizeEnemies = false, RandomizeItems = false });
            try
            {
                var lines = File.ReadAllLines(Path.Combine(dir, "logs", "inimigos.txt")).Where(l => l.StartsWith("    Arena de ")).ToList();
                var bosses = lines.Select(l => l[(l.IndexOf('→') + 1)..].Trim()).ToList();
                Assert.Equal(GameData.BossArenas.Count, lines.Count);
                Assert.Equal(bosses.Count, bosses.Distinct().Count());
                Assert.DoesNotContain(bosses, b => b.EndsWith("(ficou)"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
