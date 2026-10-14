using SoulsFormats;

namespace BbRando.Core;

/// <summary>Um inimigo sorteável: os três valores que o mod troca numa parte do mapa.</summary>
public sealed record EnemyKind(int NpcParam, int ThinkParam, string Model, string Name, bool IsBoss, bool IsChalice);

/// <summary>Resumo por modelo, para a lista de personalização da interface.</summary>
public sealed record EnemyModelInfo(string Model, string Name, long? SizeBytes, bool IsBoss, bool IsChalice, IReadOnlyList<string> Areas);

/// <summary>Lista de inimigos e chefes sorteáveis, levantada dos mapas-base do mod.</summary>
public sealed class EnemyCatalog
{
    public List<EnemyKind> Regular { get; } = new();
    public List<EnemyKind> Chalice { get; } = new();
    public List<EnemyKind> Bosses { get; } = new();
    public List<EnemyKind> ChaliceBosses { get; } = new();

    /// <summary>Peso (bytes) de cada modelo: o tamanho do chr/cNNNN.chrbnd.dcx.</summary>
    public Dictionary<string, long> ModelSizes { get; }

    /// <summary>Uma entrada de modelo de cada inimigo, para copiar para mapas onde ele não existia.</summary>
    public Dictionary<string, MSBB.Model.Enemy> ModelTemplates { get; } = new();

    readonly Dictionary<string, SortedSet<string>> _modelAreas = new();
    readonly Dictionary<string, string> _modelNames = new();

    public EnemyCatalog(IGameSource pack, ParamBundle param, GameText text, IReadOnlyDictionary<string, MapFile> maps)
    {
        ModelSizes = pack.ModelSizes();

        var npcRows = param.Index("NpcParam");
        string NameOf(MSBB.Part.Enemy e)
        {
            if (npcRows.TryGetValue(e.NPCParamID, out var row) && text.Npc(row.GetInt("nameId")) is { } n)
                return n;
            return e.ModelName;
        }

        foreach (var map in maps.Values)
            foreach (var model in map.Msb.Models.Enemies)
                ModelTemplates.TryAdd(model.Name, model);
        // Modelos que só existem numa sub-masmorra (o original também lia este arquivo).
        if (pack.Exists("map/mapstudio/m29_52_01_00/m29_52_01_91.msb.dcx"))
            foreach (var model in MSBB.Read(pack.ReadBytes("map/mapstudio/m29_52_01_00/m29_52_01_91.msb.dcx")).Models.Enemies)
                ModelTemplates.TryAdd(model.Name, model);

        foreach (var map in maps.Values.Where(m => m.Def.EnemySource))
        {
            var isChalice = map.Area.IsChalice;
            var seenNpc = new HashSet<int>();
            foreach (var e in map.Msb.Parts.Enemies)
            {
                if (e.NPCParamID <= 1 || e.ThinkParamID <= 1)
                    continue;
                var kind = new EnemyKind(e.NPCParamID, e.ThinkParamID, e.ModelName, NameOf(e), false, isChalice);

                if (IsBossCandidate(map, e))
                {
                    var list = isChalice ? ChaliceBosses : Bosses;
                    if (!list.Any(b => b.NpcParam == e.NPCParamID))
                        list.Add(kind with { IsBoss = true });
                    Track(kind, map);
                }

                if (GameData.NameMatches(e.Name, GameData.ProtectedNames) || GameData.NameMatches(e.Name, GameData.PoolOnlyExcluded))
                    continue;
                if (isChalice && GameData.ChaliceBossNpc.Contains(e.NPCParamID))
                    continue;
                // Alfredo/NPC falante que reaproveita o modelo c1110.
                if (e.Name.Contains("c1110_0000", StringComparison.Ordinal) && e.TalkID == 111010)
                    continue;
                if (!seenNpc.Add(e.NPCParamID))
                    continue;
                var pool = isChalice ? Chalice : Regular;
                if (!pool.Contains(kind))
                    pool.Add(kind);
                Track(kind, map);
            }
        }
        // Inimigo de cálice que o original adicionava manualmente.
        var c1230 = new EnemyKind(110123000, 123000, "c1230", "c1230", false, true);
        if (!Chalice.Any(k => k.NpcParam == c1230.NpcParam))
            Chalice.Add(c1230 with { Name = npcRows.TryGetValue(110123000, out var r) && text.Npc(r.GetInt("nameId")) is { } n ? n : "c1230" });
    }

    bool IsBossCandidate(MapFile map, MSBB.Part.Enemy e)
    {
        if (map.Area.IsChalice)
            return GameData.ChaliceBossNpc.Contains(e.NPCParamID);
        if (!GameData.NameMatches(e.Name, GameData.BossNames))
            return false;
        if (GameData.BossInsertBlockedNpc.Contains(e.NPCParamID) || e.EntityID == -1)
            return false;
        if (e.ModelName == "c5400")
            return false; // removido da inserção no original
        if (e.Name.Contains("c3060", StringComparison.Ordinal) && e.NPCParamID != 210306016)
            return false;
        if (map.Def.Id.StartsWith("m34") && e.NPCParamID == 210030)
            return false;
        if (map.Def.Id.StartsWith("m28") && e.Name.Contains("c2100", StringComparison.Ordinal))
            return false;
        return true;
    }

    void Track(EnemyKind kind, MapFile map)
    {
        if (!_modelAreas.TryGetValue(kind.Model, out var areas))
            _modelAreas[kind.Model] = areas = new SortedSet<string>();
        areas.Add(map.Area.Name);
        if (!_modelNames.ContainsKey(kind.Model) || _modelNames[kind.Model] == kind.Model)
            _modelNames[kind.Model] = kind.Name;
    }

    public long? SizeOf(string model) => ModelSizes.TryGetValue(model, out var s) ? s : null;

    public string DisplayName(string model) => _modelNames.TryGetValue(model, out var n) ? n : model;

    /// <summary>Um item por modelo, para a interface.</summary>
    public IReadOnlyList<EnemyModelInfo> Models()
    {
        var all = Regular.Concat(Chalice).Concat(Bosses).Concat(ChaliceBosses);
        return all.GroupBy(k => k.Model)
            .Select(g => new EnemyModelInfo(
                g.Key,
                DisplayName(g.Key),
                SizeOf(g.Key),
                g.Any(k => k.IsBoss) && !g.Any(k => !k.IsBoss),
                g.All(k => k.IsChalice),
                _modelAreas.TryGetValue(g.Key, out var a) ? a.ToList() : []))
            .OrderBy(m => m.IsBoss).ThenBy(m => m.IsChalice).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>Uma troca feita num mapa, para o log.</summary>
public sealed record EnemySwap(Location Where, string PartName, EnemyKind? Old, EnemyKind New, bool BecameBoss);

/// <summary>Troca os inimigos dos mapas, respeitando os limites de memória e as preferências.</summary>
public sealed class EnemyRandomizer(EnemyCatalog catalog, RandomizerSettings settings, GameText text)
{
    public List<EnemySwap> Swaps { get; } = new();
    /// <summary>Modelos que não podem ser sorteados (no swap: os chefes, para não repetir).</summary>
    public HashSet<string> ExcludedModels { get; init; } = new();

    /// <summary>Ajusta a força do inimigo à área de destino (opção "ScaleEnemiesToArea").</summary>
    public AreaScaling? Scaling { get; init; }

    public void Randomize(MapFile map)
    {
        var area = map.Area;
        if (area.IsChalice && !settings.IncludeChaliceEnemies)
            return;
        var rng = new Rng(settings.Seed, "enemies/" + map.Def.Id);
        var areaChance = settings.ChanceOf(area.Id);
        var banned = GameData.BannedModelsByMap
            .Where(kv => map.Def.Id.StartsWith(kv.Key, StringComparison.Ordinal))
            .SelectMany(kv => kv.Value)
            .ToHashSet();

        var enemies = map.Msb.Parts.Enemies;
        var usedModels = enemies.Select(e => e.ModelName).ToHashSet();
        var vanillaBudget = usedModels.Sum(m => catalog.SizeOf(m) ?? 0);
        var budgetLimit = settings.MapMemoryMultiplier > 0 ? vanillaBudget * settings.MapMemoryMultiplier : double.MaxValue;
        var currentBudget = (double)vanillaBudget;
        var newModels = new HashSet<string>();

        foreach (var e in enemies)
        {
            if (!CanReplace(map, e))
                continue;
            var forced = map.Def.Id.StartsWith("m28") && GameData.NameMatches(e.Name, GameData.AlwaysRandomizeYahargul);
            if (!forced && !rng.Chance(areaChance))
                continue;

            var original = new EnemyKind(e.NPCParamID, e.ThinkParamID, e.ModelName, catalog.DisplayName(e.ModelName), false, area.IsChalice);
            var originalSize = catalog.SizeOf(e.ModelName);

            bool Fits(EnemyKind k)
            {
                if (settings.WeightOf(k.Model) <= 0 || banned.Contains(k.Model) || ExcludedModels.Contains(k.Model))
                    return false;
                if (settings.ProtectBellMaidens && GameData.NameMatches(k.Model, GameData.BellMaidenNames))
                    return false;
                if (settings.AvoidSameEnemy && k.Model == e.ModelName)
                    return false;
                var size = catalog.SizeOf(k.Model);
                if (settings.EnemyMemoryMultiplier < 10)
                {
                    // Corrige o original: modelo sem tamanho conhecido contava como peso 0 e sempre passava.
                    if (size == null || originalSize == null)
                        return false;
                    if (size > originalSize * settings.EnemyMemoryMultiplier)
                        return false;
                }
                if (!usedModels.Contains(k.Model))
                {
                    if (settings.MaxNewModelsPerMap > 0 && newModels.Count >= settings.MaxNewModelsPerMap)
                        return false;
                    if (currentBudget + (size ?? 0) > budgetLimit)
                        return false;
                }
                return true;
            }

            EnemyKind? Choose(IReadOnlyList<EnemyKind> pool)
            {
                var options = pool.Where(Fits).ToList();
                if (options.Count == 0)
                    return null;
                var index = rng.PickWeighted(options.Select(k => settings.WeightOf(k.Model)).ToList());
                return index < 0 ? null : options[index];
            }

            EnemyKind? choice = null;
            var becameBoss = false;
            // No modo swap cada chefe aparece uma vez só, então não há chefes no lugar de inimigos comuns.
            if (!settings.SwapBosses && settings.BossChance > 0 && rng.Chance(settings.BossChance))
            {
                var bossPool = new List<EnemyKind>(catalog.Bosses.Where(b => settings.IncludeLesserBosses || !GameData.NameMatches(b.Model, GameData.LesserBossNames)));
                if (settings.IncludeChaliceBosses || area.IsChalice)
                    bossPool.AddRange(catalog.ChaliceBosses);
                choice = Choose(bossPool);
                becameBoss = choice != null;
            }
            if (choice == null)
            {
                IReadOnlyList<EnemyKind> primary = catalog.Regular, secondary = catalog.Chalice;
                if (area.IsChalice || settings.IncludeChaliceEnemies && rng.Chance(settings.ChaliceEnemyChance))
                    (primary, secondary) = (catalog.Chalice, catalog.Regular);
                choice = Choose(primary) ?? (settings.IncludeChaliceEnemies ? Choose(secondary) : null);
            }
            if (choice == null)
                continue; // Nada cabe nos limites: o inimigo original fica.

            e.NPCParamID = settings.ScaleEnemiesToArea && Scaling != null ? Scaling.Scale(choice.NpcParam, original.NpcParam, map) : choice.NpcParam;
            // Corrige o original: esse chefe precisa da IA 551111, que era calculada e nunca aplicada.
            e.ThinkParamID = choice.NpcParam == 551000 ? 551111 : choice.ThinkParam;
            e.ModelName = choice.Model;
            if (usedModels.Add(choice.Model))
            {
                newModels.Add(choice.Model);
                currentBudget += catalog.SizeOf(choice.Model) ?? 0;
            }
            map.Dirty = true;
            Swaps.Add(new EnemySwap(map.Describe(text, e), e.Name, original, choice, becameBoss));
        }
    }

    bool CanReplace(MapFile map, MSBB.Part.Enemy e)
    {
        if (e.NPCParamID <= 1 || e.ThinkParamID <= 1)
            return false;
        if (GameData.NameMatches(e.Name, GameData.ProtectedNames))
            return false;
        if (settings.ProtectBellMaidens && GameData.NameMatches(e.Name, GameData.BellMaidenNames))
            return false;
        if (map.Area.IsChalice && (GameData.ChaliceBossNpc.Contains(e.NPCParamID) || e.ModelName.Contains("1050", StringComparison.Ordinal)))
            return false;
        return true;
    }
}

public static class ModelTable
{
    /// <summary>
    /// Adiciona ao mapa só os modelos que as partes passaram a usar. O original copiava os modelos de
    /// todos os mapas para todos (33 → 1018 entradas em Central Yharnam), estourando a memória.
    /// </summary>
    public static void EnsureModels(MapFile map, EnemyCatalog catalog, List<string> warnings)
    {
        var table = map.Msb.Models.Enemies;
        var present = table.Select(m => m.Name).ToHashSet();
        foreach (var model in map.Msb.Parts.Enemies.Select(e => e.ModelName).Distinct())
        {
            if (present.Contains(model))
                continue;
            if (catalog.ModelTemplates.TryGetValue(model, out var template))
            {
                table.Add(new MSBB.Model.Enemy(template));
                present.Add(model);
            }
            else
            {
                warnings.Add($"{map.Def.Id}: modelo {model} sem entrada-modelo; o inimigo pode não aparecer.");
            }
        }
    }
}
