using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BbRando.Core;

/// <summary>
/// Tudo que define uma randomização. Junto com a seed, vira o "código de seed" compartilhável:
/// quem colar o mesmo código (com o mesmo pacote do mod) recebe exatamente o mesmo jogo.
/// </summary>
public sealed class RandomizerSettings
{
    public ulong Seed { get; set; }

    // ---- Inimigos ----
    public bool RandomizeEnemies { get; set; } = true;
    /// <summary>Limite de memória por inimigo: o novo inimigo pode pesar até N× o original. 10 = sem limite.</summary>
    public double EnemyMemoryMultiplier { get; set; } = 1.0;
    /// <summary>Limite de memória por mapa: soma dos modelos de inimigo até N× a do mapa original. 0 = desligado.</summary>
    public double MapMemoryMultiplier { get; set; } = 1.5;
    /// <summary>Máximo de tipos de inimigo novos por mapa. 0 = sem limite.</summary>
    public int MaxNewModelsPerMap { get; set; } = 15;
    /// <summary>Chance (%) de cada inimigo ser trocado, por área.</summary>
    public Dictionary<string, int> AreaChance { get; set; } = GameData.Areas.ToDictionary(a => a.Id, a => a.DefaultChance);
    /// <summary>Inclui os inimigos das masmorras de cálice no sorteio e randomiza as masmorras principais.</summary>
    public bool IncludeChaliceEnemies { get; set; }
    /// <summary>Com cálices ligados, chance (%) de um inimigo do jogo principal virar um de cálice.</summary>
    public double ChaliceEnemyChance { get; set; } = 20;
    /// <summary>Chance (%) de um inimigo comum virar um chefe.</summary>
    public double BossChance { get; set; }
    public bool IncludeChaliceBosses { get; set; }
    public bool IncludeLesserBosses { get; set; }
    public bool ProtectBellMaidens { get; set; } = true;
    /// <summary>Peso de cada modelo no sorteio (0 = nunca aparece, 1 = normal, até 10). Ausente = 1.</summary>
    public Dictionary<string, double> EnemyWeights { get; set; } = new();
    /// <summary>Evita escolher o mesmo inimigo que já estava no lugar.</summary>
    public bool AvoidSameEnemy { get; set; } = true;

    // ---- Swap de chefes ----
    /// <summary>Troca os chefes de arena entre si: cada chefe aparece uma única vez no jogo.</summary>
    public bool SwapBosses { get; set; }
    /// <summary>Inclui Gehrman e Moon Presence no swap.</summary>
    public bool SwapFinalBosses { get; set; } = true;
    /// <summary>O chefe novo herda vida, escalonamento, almas e recompensa do chefe original da arena.</summary>
    public bool ScaleSwappedBosses { get; set; } = true;
    /// <summary>Nas lutas em grupo, os ajudantes viram estátuas inofensivas quando entra outro chefe.</summary>
    public bool StatueHelpers { get; set; } = true;
    /// <summary>Troca também os chefes das masmorras de cálice entre si.</summary>
    public bool SwapChaliceBosses { get; set; }

    // ---- Itens ----
    public bool RandomizeItems { get; set; } = true;
    /// <summary>Embaralha só dentro da mesma área (mais previsível) ou no jogo todo.</summary>
    public bool ItemsWithinArea { get; set; }
    /// <summary>Inclui chaves, emblemas e insígnias. Pode travar o progresso!</summary>
    public bool IncludeKeyItems { get; set; }
    public bool IncludeWorkshopItems { get; set; }
    public bool RandomizeEnemyDrops { get; set; }

    public double WeightOf(string model) => EnemyWeights.TryGetValue(model, out var w) ? w : 1.0;

    public int ChanceOf(string areaId) =>
        AreaChance.TryGetValue(areaId, out var c) ? c : GameData.AreaById(areaId).DefaultChance;

    public RandomizerSettings Clone() => FromJson(ToJson());

    // ---- Código de seed ----

    const string Prefix = "BBR1-";

    static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static RandomizerSettings FromJson(string json) =>
        JsonSerializer.Deserialize<RandomizerSettings>(json, Json) ?? new RandomizerSettings();

    /// <summary>Gera o código compartilhável (só guarda o que difere do padrão, comprimido).</summary>
    public string ToSeedCode()
    {
        var defaults = new RandomizerSettings();
        var diff = new Dictionary<string, object>();
        foreach (var prop in typeof(RandomizerSettings).GetProperties())
        {
            var mine = JsonSerializer.Serialize(prop.GetValue(this), Json);
            if (mine != JsonSerializer.Serialize(prop.GetValue(defaults), Json))
                diff[prop.Name] = JsonDocument.Parse(mine).RootElement;
        }
        diff[nameof(Seed)] = Seed.ToString();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(diff));
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize))
            deflate.Write(bytes);
        return Prefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Aceita um código "BBR1-..." ou qualquer texto/número, que vira só a seed (com as opções atuais).
    /// </summary>
    public static RandomizerSettings FromSeedCode(string code, RandomizerSettings current)
    {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var copy = current.Clone();
            copy.Seed = SeedFromText(code);
            return copy;
        }
        var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        var settings = FromJson(reader.ReadToEnd());
        // Áreas ausentes no código usam o padrão.
        foreach (var area in GameData.Areas)
            settings.AreaChance.TryAdd(area.Id, area.DefaultChance);
        return settings;
    }

    /// <summary>Números viram a própria seed; textos ("caçada da lua") viram um hash.</summary>
    public static ulong SeedFromText(string text) =>
        ulong.TryParse(text.Trim(), out var n) ? n : Rng.Fnv1a(text.Trim().ToLowerInvariant());

    public static ulong NewSeed() => (ulong)Random.Shared.NextInt64(1, 999_999_999_999);
}
