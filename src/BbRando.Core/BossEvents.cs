using SoulsFormats;

namespace BbRando.Core;

/// <summary>
/// Ajustes de evento ligados ao swap de chefes (modo jogo limpo):
/// <list type="bullet">
/// <item>Os patches de evento do mod só entram nas arenas que mudaram de chefe. Numa arena que ficou igual,
/// o evento original (com animação de entrada etc.) continua valendo.</item>
/// <item>A barra de vida mostra o nome do chefe novo (instrução 2003[11]).</item>
/// </list>
/// Cada evento é ligado a uma arena pelas entidades que ele cita. Eventos do mesmo arquivo que citam as
/// mesmas entidades (ex.: os geradores das Living Failures) são decididos juntos.
/// </summary>
public sealed class BossEvents
{
    const int BankSystem = 2000, InitializeEvent = 0;
    const int BankEvent = 2003, SetEventFlag = 2, DisplayBossHealthBar = 11;
    const string CommonFile = "event/common.emevd.dcx";

    readonly IReadOnlyDictionary<BossArena, BossArena> _changes;
    readonly bool _statues;

    // Entidade → (arena, papel). Papel: índice da fase (0..n), Extra ou Helper.
    const int RoleExtra = -1, RoleHelper = -2;
    readonly Dictionary<int, (BossArena Arena, int Role)> _entities = new();
    readonly Dictionary<BossArena, int[]> _phaseEntities = new();
    // Nome que a barra de vida mostra para cada fase, no jogo limpo (null = a fase não tem barra própria).
    readonly Dictionary<BossArena, int?[]> _phaseBarNames = new();
    readonly Dictionary<BossArena, int?> _mainBarName = new();
    // Arenas com barra numa entidade que não é ajudante (as barras dos ajudantes podem ser desligadas).
    readonly HashSet<BossArena> _hasOwnBar = new();

    /// <param name="changes">Arena → arena de onde veio o chefe que agora luta nela (só as que mudaram).</param>
    public BossEvents(IGameSource source, IReadOnlyDictionary<string, MapFile> maps,
        IReadOnlyDictionary<BossArena, BossArena> changes, bool statueHelpers)
    {
        _changes = changes;
        _statues = statueHelpers;
        foreach (var arena in GameData.BossArenas)
        {
            int Entity(string part) => arena.Maps
                .Where(maps.ContainsKey)
                .Select(id => maps[id].Msb.Parts.Enemies.FirstOrDefault(e => e.Name == part)?.EntityID ?? -1)
                .FirstOrDefault(id => id > 0, -1);
            _phaseEntities[arena] = arena.Phases.Select(Entity).ToArray();
            for (var i = 0; i < arena.Phases.Length; i++)
                Add(_phaseEntities[arena][i], arena, i);
            foreach (var part in arena.ExtraParts)
                Add(Entity(part), arena, RoleExtra);
            foreach (var part in arena.HelperParts)
                Add(Entity(part), arena, RoleHelper);
        }
        ReadBarNames(source);
    }

    void Add(int entity, BossArena arena, int role)
    {
        if (entity > 0)
            _entities.TryAdd(entity, (arena, role));
    }

    public static string EventFile(BossArena arena) => $"event/{arena.Maps[0][..6]}_00_00.emevd.dcx";

    /// <summary>Arquivos de evento que podem precisar ser gravados (os das arenas que mudaram).</summary>
    public IEnumerable<string> Files => _changes.Keys.Select(EventFile).Distinct();

    bool Changed(BossArena arena) => _changes.ContainsKey(arena);

    void ReadBarNames(IGameSource source)
    {
        foreach (var file in GameData.BossArenas.Select(EventFile).Distinct())
        {
            if (!source.Exists(file))
                continue;
            foreach (var ins in EMEVD.Read(source.ReadBytes(file)).Events.SelectMany(e => e.Instructions))
            {
                if (!IsBar(ins) || ins.ArgData[0] != 1 || !_entities.TryGetValue(Int(ins, 4), out var owner))
                    continue;
                var (arena, role) = owner;
                var name = Int(ins, 12);
                if (!_phaseBarNames.TryGetValue(arena, out var names))
                    _phaseBarNames[arena] = names = new int?[arena.Phases.Length];
                if (role >= 0)
                    names[role] ??= name;
                if (role != RoleHelper)
                    _hasOwnBar.Add(arena);
                if (role >= 0 || !_mainBarName.ContainsKey(arena))
                    _mainBarName.TryAdd(arena, name);
            }
        }
        // A primeira barra de uma fase vale mais que a de um ajudante ou parte extra.
        foreach (var (arena, names) in _phaseBarNames)
            if (names.FirstOrDefault(n => n != null) is { } first)
                _mainBarName[arena] = first;
    }

    /// <summary>Nome da barra do chefe de <paramref name="boss"/> na fase <paramref name="phase"/>.</summary>
    int? BarName(BossArena boss, int phase)
    {
        if (phase >= 0 && _phaseBarNames.TryGetValue(boss, out var names) && names.Length > 0
            && names[Math.Min(phase, names.Length - 1)] is { } name)
            return name;
        return _mainBarName.TryGetValue(boss, out var main) ? main : null;
    }

    static bool IsBar(EMEVD.Instruction ins) => ins.Bank == BankEvent && ins.ID == DisplayBossHealthBar && ins.ArgData.Length >= 16;

    static int Int(EMEVD.Instruction ins, int offset) => BitConverter.ToInt32(ins.ArgData, offset);

    static IEnumerable<int> Ints(EMEVD.Event? ev) => ev == null
        ? []
        : ev.Instructions.SelectMany(i => Enumerable.Range(0, i.ArgData.Length / 4).Select(k => BitConverter.ToInt32(i.ArgData, k * 4)));

    // ---------------- Quais patches entram ----------------

    /// <summary>"m24_01" a partir de um ID de evento/flag (1 24 1 xxxx) ou de entidade (24 1 xxxx).</summary>
    static string? MapKey(long id)
    {
        var d = id.ToString();
        return d.Length switch
        {
            8 when d[0] == '1' => $"m{d[1..3]}_0{d[3]}",
            7 => $"m{d[..2]}_0{d[2]}",
            _ => null,
        };
    }

    static string FileKey(string file) => file["event/".Length..][..6];

    List<BossArena> ArenasOf(string key) => GameData.BossArenas.Where(a => a.Maps.Any(m => m.StartsWith(key, StringComparison.Ordinal))).ToList();

    /// <summary>Evento que não cita nenhuma arena: entra se alguma arena desse arquivo mudou.</summary>
    bool FileFallback(string file) => file == CommonFile ? _changes.Count > 0 : ArenasOf(FileKey(file)).Any(Changed);

    /// <summary>
    /// Decide cada evento dos patches: arquivo → ID do evento → entra ou não. Em cada par, Original é o
    /// evento do jogo limpo (null = evento novo do mod).
    /// </summary>
    public Dictionary<string, Dictionary<long, bool>> Decide(IReadOnlyDictionary<string, List<(EMEVD.Event Patched, EMEVD.Event? Original)>> files)
    {
        var result = new Dictionary<string, Dictionary<long, bool>>();
        foreach (var (file, events) in files)
        {
            var decisions = result[file] = new Dictionary<long, bool>();
            // Agrupa os eventos que citam as mesmas entidades deste mapa (union-find simples).
            var key = file == CommonFile ? null : FileKey(file);
            var group = Enumerable.Range(0, events.Count).ToArray();
            int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);
            var firstByEntity = new Dictionary<int, int>();
            var refs = new List<HashSet<int>>();
            for (var i = 0; i < events.Count; i++)
            {
                var ints = Ints(events[i].Patched).Concat(Ints(events[i].Original))
                    .Where(v => _entities.ContainsKey(v) || key != null && v is >= 1_000_000 and <= 9_999_999 && MapKey(v) == key)
                    .ToHashSet();
                refs.Add(ints);
                if (events[i].Patched.ID == 0 || events[i].Patched.ID == 50)
                    continue; // construtores: decididos instrução por instrução
                foreach (var v in ints)
                {
                    if (firstByEntity.TryGetValue(v, out var other))
                        group[Find(i)] = Find(other);
                    else
                        firstByEntity[v] = i;
                }
            }
            var arenasByGroup = new Dictionary<int, HashSet<BossArena>>();
            for (var i = 0; i < events.Count; i++)
            {
                var set = arenasByGroup.TryGetValue(Find(i), out var s) ? s : arenasByGroup[Find(i)] = new();
                foreach (var v in refs[i])
                    if (_entities.TryGetValue(v, out var owner))
                        set.Add(owner.Arena);
            }
            for (var i = 0; i < events.Count; i++)
            {
                var arenas = arenasByGroup[Find(i)];
                decisions[events[i].Patched.ID] = arenas.Count > 0 ? arenas.Any(Changed) : FileFallback(file);
            }
        }
        return result;
    }

    /// <summary>Decisões de todos os arquivos por ID (sem os construtores), para as linhas "Initialize Event"/"Set Event Flag".</summary>
    public static Dictionary<long, bool> ById(Dictionary<string, Dictionary<long, bool>> decisions)
    {
        var all = new Dictionary<long, bool>();
        foreach (var (id, keep) in decisions.Values.SelectMany(d => d))
            if (id is not (0 or 50))
                all[id] = all.TryGetValue(id, out var other) ? other || keep : keep;
        return all;
    }

    /// <summary>
    /// Construtores (evento 0/50): o mod só acrescenta "Initialize Event" e "Set Event Flag". Cada linha
    /// acrescentada segue a decisão do evento que ela inicia (ou cuja flag ela liga). Devolve false se o
    /// evento não for desse tipo (aí ele é decidido inteiro).
    /// </summary>
    public bool TryMerge(string file, EMEVD.Event original, EMEVD.Event patched, IReadOnlyDictionary<long, bool> decisions, out EMEVD.Event merged)
    {
        merged = original;
        if (original.Parameters.Count > 0 || patched.Parameters.Count > 0)
            return false;
        var result = new EMEVD.Event(original.ID, original.RestBehavior);
        var j = 0;
        foreach (var ins in patched.Instructions)
        {
            if (j < original.Instructions.Count && Same(ins, original.Instructions[j]))
            {
                result.Instructions.Add(ins);
                j++;
                continue;
            }
            bool keep;
            if (ins.Bank == BankSystem && ins.ID == InitializeEvent)
                keep = decisions.TryGetValue((uint)Int(ins, 4), out var d) ? d : FileFallback(file);
            else if (ins.Bank == BankEvent && ins.ID == SetEventFlag)
                keep = Flag((uint)Int(ins, 0), file, decisions);
            else
                return false;
            if (keep)
                result.Instructions.Add(ins);
        }
        if (j != original.Instructions.Count)
            return false; // o mod tirou alguma linha: não é só acréscimo
        merged = result;
        return true;
    }

    bool Flag(long flag, string file, IReadOnlyDictionary<long, bool> decisions)
    {
        if (decisions.TryGetValue(flag, out var d))
            return d;
        if (MapKey(flag) is { } key && ArenasOf(key) is { Count: > 0 } arenas)
            return arenas.Any(Changed);
        return FileFallback(file);
    }

    static bool Same(EMEVD.Instruction a, EMEVD.Instruction b) =>
        a.Bank == b.Bank && a.ID == b.ID && a.Layer == b.Layer && a.ArgData.AsSpan().SequenceEqual(b.ArgData);

    // ---------------- Barra de vida ----------------

    /// <summary>Troca o nome (e, se preciso, a entidade) das barras de vida das arenas que mudaram.</summary>
    public bool RenameBars(EMEVD emevd)
    {
        var changed = false;
        foreach (var ins in emevd.Events.SelectMany(e => e.Instructions))
        {
            if (!IsBar(ins) || !_entities.TryGetValue(Int(ins, 4), out var owner) || !_changes.TryGetValue(owner.Arena, out var boss))
                continue;
            var (arena, role) = owner;
            var args = ins.ArgData;
            if (role == RoleHelper)
            {
                if (!_statues)
                    continue; // o ajudante continua o mesmo e a barra dele também
                if (_hasOwnBar.Contains(arena))
                {
                    // Ajudante virou estátua: a barra dele some.
                    if (args[0] != 0)
                    {
                        args[0] = 0;
                        changed = true;
                    }
                    continue;
                }
                // A única barra da luta estava num ajudante (Wet Nurse): passa para o chefe novo.
                if (_phaseEntities[arena][0] <= 0)
                    continue;
                BitConverter.TryWriteBytes(args.AsSpan(4), _phaseEntities[arena][0]);
                role = 0;
                changed = true;
            }
            if (BarName(boss, role) is { } name && name != Int(ins, 12))
            {
                BitConverter.TryWriteBytes(args.AsSpan(12), name);
                changed = true;
            }
        }
        return changed;
    }
}
