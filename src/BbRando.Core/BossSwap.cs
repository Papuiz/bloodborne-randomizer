using System.Numerics;
using SoulsFormats;

namespace BbRando.Core;

/// <summary>Resultado do swap numa arena, para o log.</summary>
public sealed record BossSwapEntry(string Arena, string AreaName, string Original, string Now);

/// <summary>
/// Modo swap: os chefes trocam de arena entre si (uma permutação), então cada chefe aparece
/// exatamente uma vez. Opcionalmente o chefe novo herda a força do chefe original da arena.
/// </summary>
public sealed class BossSwapper(RandomizerSettings settings, ParamBundle param, Func<string, string> modelName)
{
    // IDs novos de NpcParam para os chefes ajustados (contando para baixo, longe dos IDs do jogo).
    const long FirstScaledNpcId = 899_999_999;
    // Campos copiados do chefe original da arena quando o ajuste de força está ligado.
    static readonly string[] InheritedFields =
    [
        "hp", "getSoul", "GameClearSpEffectID",
        "itemLotId_1", "itemLotId_2", "itemLotId_3", "itemLotId_4", "itemLotId_5", "itemLotId_6", "humanityLotId",
    ];

    public List<BossSwapEntry> Swaps { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Arena → arena de onde veio o chefe que luta nela agora (só as arenas que mudaram).</summary>
    public Dictionary<BossArena, BossArena> ArenaChanges { get; } = new();

    /// <summary>Modelos dos chefes que entraram no swap (não podem aparecer como inimigo comum).</summary>
    public HashSet<string> SwappedModels { get; } = new();

    sealed record Phase(int Npc, int Think, string Model);

    sealed record Slot(string Name, string AreaName, BossArena? Arena, List<(MapFile Map, MSBB.Part.Enemy Part)>[] Parts, List<Phase> Boss);

    NpcScaler? _scaler;

    public void Swap(IReadOnlyDictionary<string, MapFile> maps)
    {
        if (!settings.SwapBosses)
            return;
        _scaler = new NpcScaler(param, FirstScaledNpcId);

        var arenas = GameData.BossArenas.Where(a => a.Kind switch
        {
            BossArenaKind.Group => settings.IncludeLesserBosses,
            BossArenaKind.Final => settings.SwapFinalBosses,
            _ => true,
        });
        var slots = new List<Slot>();
        foreach (var arena in arenas)
        {
            if (!arena.Maps.All(maps.ContainsKey))
                continue;
            var parts = arena.Phases
                .Select(name => arena.Maps
                    .Select(id => (Map: maps[id], Part: maps[id].Msb.Parts.Enemies.FirstOrDefault(e => e.Name == name)))
                    .Where(x => x.Part != null)
                    .Select(x => (x.Map, Part: x.Part!))
                    .ToList())
                .ToArray();
            var boss = parts.Where(p => p.Count > 0).Select(p => new Phase(p[0].Part.NPCParamID, p[0].Part.ThinkParamID, p[0].Part.ModelName)).ToList();
            if (boss.Count == 0)
            {
                Warnings.Add($"Arena {arena.Name}: chefe não encontrado nos mapas; fica de fora do swap.");
                continue;
            }
            slots.Add(new Slot(arena.Name, maps[arena.Maps[0]].Area.Name, arena, parts, boss));
        }
        foreach (var slot in slots)
            foreach (var phase in slot.Boss)
                SwappedModels.Add(phase.Model);

        var rng = new Rng(settings.Seed, "bosses/swap");
        Apply(slots, Permute(slots, rng), maps);

        if (settings.SwapChaliceBosses)
            SwapChalice(maps, new Rng(settings.Seed, "bosses/chalice"));
    }

    /// <summary>
    /// Sorteia qual chefe vai para cada arena, sem repetir e respeitando os modelos proibidos.
    /// Tenta primeiro sem deixar nenhum chefe na própria arena.
    /// </summary>
    static int[] Permute(List<Slot> slots, Rng rng)
    {
        bool Allowed(int arena, int boss) =>
            slots[arena].Arena is not { } a || UsedModels(slots[arena], slots[boss].Boss).All(a.Accepts);

        // As arenas com mais restrições são preenchidas primeiro.
        var order = Enumerable.Range(0, slots.Count).ToList();
        rng.Shuffle(order);
        order = order.OrderBy(i => Enumerable.Range(0, slots.Count).Count(b => Allowed(i, b))).ToList();

        foreach (var avoidSelf in new[] { true, false })
        {
            var result = new int[slots.Count];
            var taken = new bool[slots.Count];
            var steps = 0;
            bool Fill(int k)
            {
                if (k == order.Count)
                    return true;
                if (++steps > 200_000)
                    return false;
                var arena = order[k];
                var candidates = Enumerable.Range(0, slots.Count).ToList();
                rng.Shuffle(candidates);
                foreach (var boss in candidates)
                {
                    if (taken[boss] || avoidSelf && boss == arena || !Allowed(arena, boss))
                        continue;
                    taken[boss] = true;
                    result[arena] = boss;
                    if (Fill(k + 1))
                        return true;
                    taken[boss] = false;
                }
                return false;
            }
            if (Fill(0))
                return result;
        }
        return Enumerable.Range(0, slots.Count).ToArray(); // sem solução: ninguém muda
    }

    /// <summary>Modelos que aparecem na arena: a fase i usa a fase i do chefe novo (ou a última que ele tiver).</summary>
    static IEnumerable<string> UsedModels(Slot arena, List<Phase> boss) =>
        Enumerable.Range(0, arena.Parts.Length).Select(i => boss[Math.Min(i, boss.Count - 1)].Model);

    void Apply(List<Slot> slots, int[] assignment, IReadOnlyDictionary<string, MapFile> maps)
    {
        for (var a = 0; a < slots.Count; a++)
        {
            var arena = slots[a];
            var boss = slots[assignment[a]];
            Swaps.Add(new BossSwapEntry(arena.Name, arena.AreaName, arena.Name, assignment[a] == a ? $"{boss.Name} (ficou)" : boss.Name));
            if (assignment[a] == a)
                continue;
            if (arena.Arena != null && boss.Arena != null)
                ArenaChanges[arena.Arena] = boss.Arena;

            for (var i = 0; i < arena.Parts.Length; i++)
            {
                var phase = boss.Boss[Math.Min(i, boss.Boss.Count - 1)];
                foreach (var (map, part) in arena.Parts[i])
                {
                    Place(part, phase);
                    map.Dirty = true;
                }
            }

            if (settings.StatueHelpers && arena.Arena != null)
            {
                foreach (var mapId in arena.Arena.Maps)
                    foreach (var part in maps[mapId].Msb.Parts.Enemies.Where(e => arena.Arena.HelperParts.Contains(e.Name)))
                    {
                        (part.NPCParamID, part.ThinkParamID, part.ModelName) = GameData.Statue;
                        maps[mapId].Dirty = true;
                    }
            }
        }
    }

    void Place(MSBB.Part.Enemy part, Phase phase)
    {
        var originalNpc = part.NPCParamID;
        part.NPCParamID = settings.ScaleSwappedBosses ? ScaledNpc(phase.Npc, originalNpc) : phase.Npc;
        // IAs que o original ajustava ao tirar estes chefes do lugar deles (551000 = Mergo's Wet Nurse,
        // 551111 = a IA dela sem a escuridão da arena).
        part.ThinkParamID = phase.Npc switch
        {
            551000 => 551111,
            507200 => 507200,
            _ => phase.Think,
        };
        part.ModelName = phase.Model;
    }

    /// <summary>
    /// Copia a linha de NpcParam do chefe novo, com vida base, almas, recompensas e o efeito de
    /// escalonamento (vida/dano/defesa) do chefe original da arena.
    /// </summary>
    int ScaledNpc(int newNpc, int arenaNpc)
    {
        if (newNpc == arenaNpc || !_scaler!.TryGet(newNpc, out var source) || !_scaler.TryGet(arenaNpc, out var arena))
            return newNpc;
        return _scaler.Clone(newNpc, arenaNpc.ToString(), row =>
        {
            foreach (var field in InheritedFields)
                row.SetInt(field, arena.GetInt(field));
            // spEffectID6 é o escalonamento da área (70xx no jogo base, 748x no DLC).
            if (NpcScaler.IsAreaScaling(arena.GetInt("spEffectID6")) && NpcScaler.IsAreaScaling(source.GetInt("spEffectID6")))
                row.SetInt("spEffectID6", arena.GetInt("spEffectID6"));
        });
    }

    /// <summary>
    /// Embaralha os chefes das masmorras de cálice entre si, sem repetir. Lutas com vários chefes iguais
    /// (ex.: três Merciless Watchers) contam como uma luta só: o primeiro vira o chefe novo e os outros
    /// viram estátuas (ou cópias do mesmo chefe, se as estátuas estiverem desligadas).
    /// </summary>
    void SwapChalice(IReadOnlyDictionary<string, MapFile> maps, Rng rng)
    {
        var fights = new List<(MapFile Map, List<MSBB.Part.Enemy> Parts, Phase Boss)>();
        foreach (var map in maps.Values.Where(m => m.Area.IsChalice && m.Def.EnemyTarget))
        {
            // Mesmo modelo e perto um do outro (até 40 m) = a mesma luta.
            var groups = new List<List<MSBB.Part.Enemy>>();
            foreach (var e in map.Msb.Parts.Enemies.Where(e => GameData.ChaliceBossNpc.Contains(e.NPCParamID) && e.EntityID != -1))
            {
                var group = groups.FirstOrDefault(g => g[0].ModelName == e.ModelName && Vector3.Distance(g[0].Position, e.Position) < 40);
                if (group != null)
                    group.Add(e);
                else
                    groups.Add([e]);
            }
            foreach (var group in groups)
                fights.Add((map, group, new Phase(group[0].NPCParamID, group[0].ThinkParamID, group[0].ModelName)));
        }

        var bosses = fights.Select(f => f.Boss).ToList();
        rng.Shuffle(bosses);
        for (var i = 0; i < fights.Count; i++)
        {
            var (map, parts, original) = fights[i];
            if (bosses[i] == original)
                continue;
            Place(parts[0], bosses[i]);
            foreach (var extra in parts.Skip(1))
            {
                if (settings.StatueHelpers)
                    (extra.NPCParamID, extra.ThinkParamID, extra.ModelName) = GameData.Statue;
                else
                    Place(extra, bosses[i]);
            }
            map.Dirty = true;
            var label = parts.Count > 1 ? $"{modelName(original.Model)} ×{parts.Count}" : modelName(original.Model);
            Swaps.Add(new BossSwapEntry($"{label} [{map.Def.Id}]", map.Area.Name, modelName(original.Model), modelName(bosses[i].Model)));
        }
    }
}
