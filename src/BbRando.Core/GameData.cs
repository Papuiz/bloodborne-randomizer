namespace BbRando.Core;

/// <summary>Uma área do jogo, com a chance padrão de troca de inimigos herdada do mod original.</summary>
public sealed record Area(string Id, string Name, string Description, int DefaultChance, bool IsChalice = false);

/// <summary>
/// Um arquivo de mapa. <see cref="EnemyTarget"/>: os inimigos dele são trocados;
/// <see cref="EnemySource"/>: os inimigos dele entram na lista de sorteio;
/// <see cref="ItemTarget"/>: os tesouros dele entram no embaralhamento de itens.
/// </summary>
public sealed record MapDef(string Id, string AreaId, bool EnemyTarget, bool EnemySource, bool ItemTarget, bool Optional = false)
{
    public string RelativePath => Id.StartsWith("m29_", StringComparison.Ordinal)
        ? $"map/mapstudio/{Id[..10]}00/{Id}.msb.dcx"
        : $"map/mapstudio/{Id}.msb.dcx";
}

/// <summary>Listas de dados do Bloodborne levantadas a partir do mod original (com os erros corrigidos).</summary>
public static class GameData
{
    public static readonly IReadOnlyList<Area> Areas =
    [
        new("dream", "Hunter's Dream", "O Sonho do Caçador e a Oficina abandonada.", 100),
        new("hemwick", "Hemwick Charnel Lane", "Vila das bruxas de Hemwick.", 100),
        new("old_yharnam", "Old Yharnam", "Old Yharnam, a cidade queimada.", 100),
        new("cathedral", "Cathedral Ward", "Cathedral Ward e arredores.", 100),
        new("central", "Central Yharnam", "Área inicial do jogo.", 100),
        new("upper_cathedral", "Upper Cathedral Ward", "Área pesada em memória. O mod original usava 60%.", 60),
        new("cainhurst", "Forsaken Cainhurst Castle", "Castelo de Cainhurst. O mod original usava 60%.", 60),
        new("mensis", "Nightmare of Mensis", "Pesadelo de Mensis.", 100),
        new("woods", "Forbidden Woods", "Floresta Proibida.", 100),
        new("yahargul", "Yahar'gul, Unseen Village", "Vila Invisível de Yahar'gul.", 100),
        new("byrgenwerth", "Byrgenwerth + Lecture Building", "Byrgenwerth e o Prédio de Palestras.", 100),
        new("frontier", "Nightmare Frontier", "Fronteira do Pesadelo.", 100),
        new("hunters_nightmare", "Hunter's Nightmare", "DLC: Pesadelo do Caçador. O mod original usava 90%.", 90),
        new("research_hall", "Research Hall", "DLC: Salão de Pesquisa, a área mais instável. O mod original usava 30%.", 30),
        new("hamlet", "Fishing Hamlet", "DLC: Vila de Pescadores.", 100),
        new("chalice", "Chalice Dungeons (história)", "Masmorras de Cálice principais.", 100, IsChalice: true),
    ];

    public static Area AreaById(string id) => Areas.First(a => a.Id == id);

    public static readonly IReadOnlyList<MapDef> Maps =
    [
        new("m21_00_00_00", "dream", true, true, false),
        new("m21_01_00_00", "dream", true, true, true),
        new("m22_00_00_00", "hemwick", true, true, true),
        new("m23_00_00_00", "old_yharnam", true, true, true),
        new("m23_00_00_01", "old_yharnam", true, true, true),
        new("m24_00_00_00", "cathedral", true, true, true),
        new("m24_00_00_01", "cathedral", true, true, true),
        new("m24_01_00_00", "central", true, true, true),
        new("m24_01_00_01", "central", true, true, true),
        new("m24_01_00_11", "central", true, true, true),
        new("m24_02_00_00", "upper_cathedral", true, true, true),
        new("m24_02_00_01", "upper_cathedral", true, true, true),
        new("m25_00_00_00", "cainhurst", true, true, true),
        new("m26_00_00_00", "mensis", true, true, true),
        new("m27_00_00_00", "woods", true, true, true),
        new("m27_00_00_01", "woods", true, true, true),
        new("m28_00_00_00", "yahargul", true, true, true),
        new("m28_00_00_01", "yahargul", true, true, true),
        new("m32_00_00_00", "byrgenwerth", true, true, true),
        new("m32_00_00_01", "byrgenwerth", true, true, true),
        new("m33_00_00_00", "frontier", true, true, true),
        new("m34_00_00_00", "hunters_nightmare", true, true, true),
        new("m35_00_00_00", "research_hall", true, true, true),
        new("m36_00_00_00", "hamlet", true, true, true),
        // Cálices: os inimigos do sorteio vêm do "_00" e a troca acontece no "_01" (como no original).
        new("m29_10_90_00", "chalice", false, true, false),
        new("m29_10_90_01", "chalice", true, false, false),
        new("m29_20_90_00", "chalice", false, true, false),
        new("m29_20_90_01", "chalice", true, false, false),
        new("m29_21_90_00", "chalice", true, true, false),
        new("m29_30_90_00", "chalice", false, true, false),
        new("m29_30_90_01", "chalice", true, false, false),
        new("m29_31_90_00", "chalice", true, true, false),
        new("m29_40_90_00", "chalice", false, true, false),
        new("m29_40_90_01", "chalice", true, false, false),
        new("m29_42_90_00", "chalice", false, true, false),
        new("m29_42_90_01", "chalice", true, false, false),
        new("m29_50_90_00", "chalice", false, true, false),
        new("m29_50_90_01", "chalice", true, false, false),
        new("m29_52_90_00", "chalice", false, true, false),
        new("m29_52_90_01", "chalice", true, false, false),
        new("m29_53_90_00", "chalice", false, true, false),
        new("m29_53_90_01", "chalice", true, false, false),
        // Great One Beast: só existe se o mod extra "Great One Beast" foi copiado para a pasta do mod.
        new("m29_50_40_00", "chalice", true, true, false, Optional: true),
    ];

    /// <summary>Partes de chefes nos mapas (o original tinha "5090" sem o "c").</summary>
    public static readonly IReadOnlyList<string> BossNames =
    [
        "c5090", "c2090_0003", "c2100_0000", "c2100_0001", "c2120_0000", "c2120_0001", "c2120_0002",
        "c2320_0000", "c2500_0000", "c2570_0001", "c2510_0000", "c2710_0000", "c2720_0000", "c4520_0002",
        "c4030_0000", "c4030_0001", "c4030_0002", "c4030_0003", "c4030_0004", "c4500_0000", "c4510_0000",
        "c4540_0000", "c4541_0000", "c5000_0000", "c5020_0000", "c5070_0000", "c5080_0000", "c5100_0000",
        "c5120_0001", "c5400_0000", "c5510_0000", "c8050_0000", "c3130_0000", "c5010_0000", "c4510_0002",
        "c3050_0000", "c3060_0000", "c5110",
    ];

    /// <summary>Partes cujo nome contém um destes trechos nunca entram no sorteio nem são trocadas.</summary>
    public static readonly IReadOnlyList<string> ProtectedNames =
    [
        "c2120_9999", "c2120_9998", "c0000", "c0", "c1020", "c1030", "c1080", "c2030", "c2300", "c2310",
        "c2800", "c2810", "c2910", "c3110", "c4150", "c5030", "c5110", "c5140", "c5150", "c5400", "c5420",
        "c5500", "c5501", "c5502", "c5520", "c5521", "c5522", "c7000", "c7010", "c7100", "c8010", "c8020",
        "c2501", "c90", "c9030", "c2571", "c8030_0000", "c8040_0000", "c9020_0000", "c9020_0001",
        "c9020_0002", "c9020_0003", "c5072_0000", "c4023_0000", "c4160_0000", "c4160_0001", "c4160_0002",
        "c4160_0003", "c4550_0000", "c7110_0000", "c5071_0000", "c4511_0000", "c5510_0001", "c5510_0002",
        "c4540_0000", "c4543_0000", "c8070_0000", "c5130_0000", "c4031_0000", "c4520_0000", "c1190",
        "c1180", "c1060_0002",
        .. BossNames,
    ];

    /// <summary>Podem ser trocados, mas não entram no sorteio (no original: o RemoveAt(3) da lista).</summary>
    public static readonly IReadOnlyList<string> PoolOnlyExcluded = ["c2560", "c2561"];

    /// <summary>Mulheres do sino (opção "proteger mulheres do sino").</summary>
    public static readonly IReadOnlyList<string> BellMaidenNames = ["c1050", "c1051", "c1055"];

    /// <summary>"Chefes menores" (opção própria no original).</summary>
    public static readonly IReadOnlyList<string> LesserBossNames = ["c5070"];

    /// <summary>NpcParam de chefes que não podem ser inseridos no lugar de inimigos.</summary>
    public static readonly IReadOnlySet<int> BossInsertBlockedNpc = new HashSet<int> { 250060, 250070, 250090, 212600, 212610, 212620 };

    /// <summary>NpcParam dos chefes das masmorras de cálice (sem duplicatas do original).</summary>
    public static readonly IReadOnlySet<int> ChaliceBossNpc = new HashSet<int>
    {
        10313090, 110304090, 110304098, 110304096, 210501006, 310501006, 10750090, 110216090, 210305006,
        310305006, 10127090, 110313080, 210209096, 310209096, 10304090, 10304096, 10304098, 110313070,
        210510096, 310509006, 10106090, 210305016, 10216090, 110501000, 210512096, 110209090, 210504000,
        310504000, 10305006, 110509010, 10218090, 110504000, 210508006, 110257000, 210251096, 310305016,
        210306016, 511000, 210306015,
    };

    /// <summary>Em Yahar'gul estas partes são sempre trocadas, ignorando a chance da área.</summary>
    public static readonly IReadOnlyList<string> AlwaysRandomizeYahargul =
        ["c1050_0110", "c1050_0112", "c1050_0114", "c1050_0115", "c1050_0117", "c1050_0119"];

    /// <summary>Modelos proibidos em mapas específicos (travavam o jogo no original).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BannedModelsByMap = new Dictionary<string, string[]>
    {
        ["m24_02"] = ["c1000", "c5040", "c2630", "c1051", "c1180"],
        ["m35"] = ["c1000", "c5040", "c2630", "c1051", "c1180", "c1111", "c2620", "c2090", "c1250", "c1260"],
    };

    /// <summary>Lotes de tesouro que o original nunca embaralhava, e os do Sonho/oficina (opção própria).</summary>
    public static readonly IReadOnlySet<int> FixedItemLots = new HashSet<int>
    {
        2600550, 2400450, 3500800, 2800290, 3200720, 3200810, 2410990, 3502000, 3401810,
    };

    public static readonly IReadOnlySet<int> WorkshopItemLots = new HashSet<int> { 2411000, 2200360 };

    /// <summary>NpcParam com drops que o original não embaralhava.</summary>
    public static readonly IReadOnlySet<int> FixedDropNpc = new HashSet<int> { 252100, 6071 };

    /// <summary>
    /// Arenas de chefe do jogo principal para o modo swap. <c>Phases</c>: partes que formam o chefe
    /// (fases ou partes ligadas, na ordem); <c>Helpers</c>: ajudantes de lutas em grupo;
    /// <c>Banned</c>: modelos que não podem entrar nessa arena (travavam no original).
    /// </summary>
    public static readonly IReadOnlyList<BossArena> BossArenas =
    [
        new("cleric_beast", "Cleric Beast", BossArenaKind.Normal, ["m24_01_00_00", "m24_01_00_01", "m24_01_00_11"], ["c5000_0000"]),
        new("gascoigne", "Father Gascoigne", BossArenaKind.Normal, ["m24_01_00_00", "m24_01_00_01", "m24_01_00_11"], ["c2710_0000", "c2720_0000"]),
        new("blood_starved_beast", "Blood-starved Beast", BossArenaKind.Normal, ["m23_00_00_00", "m23_00_00_01"], ["c2090_0003"]),
        new("paarl", "Darkbeast Paarl", BossArenaKind.Normal, ["m23_00_00_00", "m23_00_00_01"], ["c5080_0000"]),
        new("amelia", "Vicar Amelia", BossArenaKind.Normal, ["m24_00_00_00", "m24_00_00_01"], ["c5020_0000"], Banned: ["c4500", "c4520"]),
        new("ebrietas", "Ebrietas, Daughter of the Cosmos", BossArenaKind.Normal, ["m24_02_00_00", "m24_02_00_01"], ["c2510_0000"],
            Banned: ["c5100", "c8050", "c4520", "c4500", "c3060"]),
        new("logarius", "Martyr Logarius", BossArenaKind.Normal, ["m25_00_00_00"], ["c2320_0000"]),
        new("micolash", "Micolash, Host of the Nightmare", BossArenaKind.Normal, ["m26_00_00_00"], ["c5510_0000", "c5510_0001", "c5510_0002"]),
        new("rom", "Rom, the Vacuous Spider", BossArenaKind.Normal, ["m32_00_00_00", "m32_00_00_01"], ["c5100_0000"]),
        new("amygdala", "Amygdala", BossArenaKind.Normal, ["m33_00_00_00"], ["c5120_0001"]),
        new("laurence", "Laurence, the First Vicar", BossArenaKind.Normal, ["m34_00_00_00"], ["c4500_0000"], Banned: ["c3060"]),
        new("ludwig", "Ludwig", BossArenaKind.Normal, ["m34_00_00_00"], ["c4510_0000", "c4510_0002"], Banned: ["c3060"]),
        new("maria", "Lady Maria of the Astral Clocktower", BossArenaKind.Normal, ["m35_00_00_00"], ["c4520_0002", "c4520_0000"]),
        new("orphan", "Orphan of Kos", BossArenaKind.Normal, ["m36_00_00_00"], ["c4540_0000", "c4541_0000"]),
        new("moon_presence", "Moon Presence", BossArenaKind.Final, ["m21_00_00_00"], ["c5400_0000"]),
        new("gehrman", "Gehrman, the First Hunter", BossArenaKind.Final, ["m21_00_00_00"], ["c8050_0000"]),
        new("witch_hemwick", "The Witch of Hemwick", BossArenaKind.Group, ["m22_00_00_00"], ["c2100_0000"], Helpers: ["c2100_0001"]),
        new("emissary", "Celestial Emissary", BossArenaKind.Group, ["m24_02_00_00", "m24_02_00_01"], ["c2500_0000", "c2570_0001"],
            Helpers: [.. Enumerable.Range(1, 10).Select(i => $"c2500_{i:0000}")],
            Banned: ["c5100", "c8050", "c4520", "c4500", "c3060"]),
        new("shadows", "Shadow of Yharnam", BossArenaKind.Group, ["m27_00_00_00", "m27_00_00_01"], ["c2120_0000"], Helpers: ["c2120_0001", "c2120_0002"]),
        new("one_reborn", "The One Reborn", BossArenaKind.Group, ["m28_00_00_00", "m28_00_00_01"], ["c5070_0000"]),
    ];

    /// <summary>Estátua inofensiva (2 de vida) que o original usava no lugar dos ajudantes no "modo fácil".</summary>
    public static readonly (int NpcParam, int ThinkParam, string Model) Statue = (252100, 252100, "c2521");

    public static bool NameMatches(string partName, IEnumerable<string> fragments) =>
        fragments.Any(f => partName.Contains(f, StringComparison.Ordinal));
}

public enum BossArenaKind
{
    /// <summary>Chefe comum.</summary>
    Normal,
    /// <summary>Luta em grupo ou "chefe menor" (entra com a opção de chefes menores).</summary>
    Group,
    /// <summary>Chefes do final (Sonho do Caçador).</summary>
    Final,
}

public sealed record BossArena(
    string Id, string Name, BossArenaKind Kind, string[] Maps, string[] Phases,
    string[]? Helpers = null, string[]? Banned = null)
{
    public string[] HelperParts => Helpers ?? [];
    public bool Accepts(string model) => Banned == null || !Banned.Contains(model);
}
