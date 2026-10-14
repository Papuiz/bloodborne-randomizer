using SoulsFormats;

namespace BbRando.Core;

/// <summary>
/// Cria cópias de linhas de NpcParam com alguns campos trocados (força, almas...), com cache e IDs
/// novos contando para baixo a partir de uma faixa própria. Usado pelo swap de chefes e pelo
/// escalonamento por área.
/// </summary>
public sealed class NpcScaler(ParamBundle param, long firstId)
{
    readonly Dictionary<long, PARAM.Row> _original = param.Index("NpcParam");
    readonly Dictionary<(int, string), int> _cache = new();
    long _nextId = firstId;

    public bool TryGet(int npc, out PARAM.Row row) => _original.TryGetValue(npc, out row!);

    /// <summary>Cópia de <paramref name="npc"/> com <paramref name="modify"/> aplicado (uma por chave).</summary>
    public int Clone(int npc, string key, Action<PARAM.Row> modify)
    {
        if (!_original.TryGetValue(npc, out var source))
            return npc;
        if (_cache.TryGetValue((npc, key), out var cached))
            return cached;
        var row = param.CloneRow("NpcParam", source, _nextId);
        _nextId = row.ID - 1;
        modify(row);
        _cache[(npc, key)] = (int)row.ID;
        return (int)row.ID;
    }

    /// <summary>spEffectID6 com valor 70xx/74xx é o efeito de escalonamento de área (vida, dano, defesa).</summary>
    public static bool IsAreaScaling(int spEffect) => spEffect is >= 7000 and < 7500;
}

/// <summary>
/// Escalonamento de força para inimigos comuns. O inimigo novo recebe uma cópia da sua linha de
/// NpcParam com o efeito de escalonamento (spEffectID6, e o de NG+) do inimigo que ocupava aquele lugar,
/// que reflete a dificuldade daquele ponto do jogo. Se o inimigo original não tiver escalonamento, vale
/// o mais comum do mapa. O mod original fazia algo parecido com 30 variantes pré-geradas por inimigo e
/// um nível fixo por área (NPCScalingFile), o que errava em mapas com regiões de dificuldades diferentes.
/// </summary>
public sealed class AreaScaling
{
    const long FirstId = 879_999_999;

    readonly NpcScaler _scaler;
    readonly Dictionary<string, (int Area, int NewGame)> _byMap = new();

    /// <summary>Tem que ser criado com os mapas ainda intactos (antes de qualquer troca).</summary>
    public AreaScaling(ParamBundle param, IReadOnlyDictionary<string, MapFile> maps)
    {
        _scaler = new NpcScaler(param, FirstId);
        foreach (var map in maps.Values)
        {
            var effects = map.Msb.Parts.Enemies
                .Where(e => e.NPCParamID > 1 && e.ThinkParamID > 1)
                .Select(e => _scaler.TryGet(e.NPCParamID, out var row) ? row : null)
                .OfType<PARAM.Row>()
                .Where(row => NpcScaler.IsAreaScaling(row.GetInt("spEffectID6")))
                .Select(row => (row.GetInt("spEffectID6"), row.GetInt("GameClearSpEffectID")))
                .ToList();
            if (effects.Count == 0)
                continue;
            // O efeito mais comum entre os inimigos nativos (empate: o menor, para ser determinístico).
            _byMap[map.Def.Id] = effects.GroupBy(x => x)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
                .First().Key;
        }
    }

    /// <summary>Efeito de escalonamento de cada mapa (para o log).</summary>
    public IReadOnlyDictionary<string, (int Area, int NewGame)> Table => _byMap;

    /// <summary>
    /// NpcParam do inimigo <paramref name="npc"/> com a força do inimigo <paramref name="replacedNpc"/> que
    /// estava no lugar (ou a do mapa). Retorna o próprio ID se já estiver na força certa.
    /// </summary>
    public int Scale(int npc, int replacedNpc, MapFile target)
    {
        if (!_scaler.TryGet(npc, out var row))
            return npc;
        (int Area, int NewGame) scale;
        if (_scaler.TryGet(replacedNpc, out var replaced) && NpcScaler.IsAreaScaling(replaced.GetInt("spEffectID6")))
            scale = (replaced.GetInt("spEffectID6"), replaced.GetInt("GameClearSpEffectID"));
        else if (!_byMap.TryGetValue(target.Def.Id, out scale))
            return npc;
        if (!NpcScaler.IsAreaScaling(row.GetInt("spEffectID6")) || row.GetInt("spEffectID6") == scale.Area)
            return npc;
        return _scaler.Clone(npc, $"{scale.Area}/{scale.NewGame}", r =>
        {
            r.SetInt("spEffectID6", scale.Area);
            if (scale.NewGame > 0)
                r.SetInt("GameClearSpEffectID", scale.NewGame);
        });
    }
}
