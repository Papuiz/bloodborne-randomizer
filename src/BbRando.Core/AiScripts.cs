using SoulsFormats;

namespace BbRando.Core;

/// <summary>
/// No Bloodborne a IA dos inimigos fica empacotada por mapa (script/mXX.luabnd.dcx): um inimigo levado
/// para outro mapa só se mexe se o script dele estiver no pacote daquele mapa. Aqui cada pacote do jogo
/// principal recebe a união dos scripts de todos (como o mod original fazia), com os índices
/// LUAINFO (objetivos) e LUAGNL (globais) mesclados sem duplicatas, mais os scripts ajustados pelos
/// autores do mod (<see cref="ModPatchSet.LuaFiles"/>).
/// </summary>
public static class AiScripts
{
    /// <summary>Pacotes de IA do jogo principal que recebem a união.</summary>
    public static readonly IReadOnlyList<string> MergedBundles =
    [
        "m21_00_00_00", "m22_00_00_00", "m23_00_00_00", "m24_00_00_00", "m24_01_00_00", "m24_02_00_00",
        "m25_00_00_00", "m26_00_00_00", "m27_00_00_00", "m28_00_00_00", "m32_00_00_00", "m33_00_00_00",
        "m34_00_00_00", "m35_00_00_00", "m36_00_00_00",
    ];

    const int GnlId = 1000000, InfoId = 1000001, FirstLuaId = 1000;

    static string Rel(string map) => $"script/{map}.luabnd.dcx";

    static string FileName(string name) => name[(name.LastIndexOfAny(['\\', '/']) + 1)..];

    static bool IsIndex(string name) => name.EndsWith(".luainfo", StringComparison.OrdinalIgnoreCase)
                                        || name.EndsWith(".luagnl", StringComparison.OrdinalIgnoreCase);

    /// <summary>Grava os pacotes de IA mesclados e o de cálice (m29) com os ajustes dos autores.</summary>
    public static void Write(IGameSource source, ModPatchSet patches, string outputDvdRoot)
    {
        var bundles = MergedBundles.Where(m => source.Exists(Rel(m)))
            .ToDictionary(m => m, m => BND4.Read(source.ReadBytes(Rel(m))));
        // Os scripts das masmorras de cálice também entram na união (inimigos de cálice no jogo principal).
        const string chalice = "m29_00_00_00";
        var sources = bundles.Values.ToList();
        if (source.Exists(Rel(chalice)))
            sources.Add(BND4.Read(source.ReadBytes(Rel(chalice))));

        // União de todos os scripts .lua (o primeiro com cada nome vale; conflitos viram aviso).
        var scripts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var goals = new Dictionary<int, LUAINFO.Goal>();
        var globals = new List<string>();
        var seenGlobals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bnd in sources)
        {
            foreach (var file in bnd.Files)
            {
                var name = FileName(file.Name);
                if (name.EndsWith(".luainfo", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var goal in LUAINFO.Read(file.Bytes).Goals)
                        goals.TryAdd(goal.ID, goal);
                }
                else if (name.EndsWith(".luagnl", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var g in LUAGNL.Read(file.Bytes).Globals)
                        if (seenGlobals.Add(g))
                            globals.Add(g);
                }
                else
                {
                    // Se o mesmo script existir com conteúdo diferente em dois mapas, vale o primeiro
                    // (os do jogo principal vêm antes do de cálice). Hoje isso acontece com 1 arquivo.
                    scripts.TryAdd(name, file.Bytes);
                }
            }
        }

        foreach (var (map, bnd) in bundles)
        {
            var overrides = patches.LuaFiles.TryGetValue(Rel(map), out var o) ? o : new Dictionary<string, string>();
            var merged = new Dictionary<string, byte[]>(scripts, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, hash) in overrides)
                merged[name] = patches.Blob(hash);

            // Mantém o prefixo de caminho e o formato do pacote original do mapa.
            var sample = bnd.Files.First(f => !IsIndex(f.Name)).Name;
            var prefix = sample[..(sample.LastIndexOfAny(['\\', '/']) + 1)];
            var flags = bnd.Files.First().Flags;
            var originalInfo = LUAINFO.Read(bnd.Files.First(f => f.Name.EndsWith(".luainfo", StringComparison.OrdinalIgnoreCase)).Bytes);
            var originalGnl = LUAGNL.Read(bnd.Files.First(f => f.Name.EndsWith(".luagnl", StringComparison.OrdinalIgnoreCase)).Bytes);
            var infoName = bnd.Files.First(f => f.Name.EndsWith(".luainfo", StringComparison.OrdinalIgnoreCase)).Name;
            var gnlName = bnd.Files.First(f => f.Name.EndsWith(".luagnl", StringComparison.OrdinalIgnoreCase)).Name;

            var info = new LUAINFO(originalInfo.BigEndian, originalInfo.LongFormat);
            info.Goals.AddRange(goals.Values.OrderBy(g => g.ID));
            var gnl = new LUAGNL(originalGnl.BigEndian, originalGnl.LongFormat);
            gnl.Globals.AddRange(globals);

            bnd.Files.Clear();
            var id = FirstLuaId;
            foreach (var (name, bytes) in merged.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                bnd.Files.Add(new BinderFile(flags, id++, prefix + name, bytes));
            bnd.Files.Add(new BinderFile(flags, GnlId, gnlName, gnl.Write()));
            bnd.Files.Add(new BinderFile(flags, InfoId, infoName, info.Write()));
            Save(bnd, outputDvdRoot, Rel(map));
        }

        // Cálice: só os scripts ajustados pelos autores.
        if (source.Exists(Rel(chalice)) && patches.LuaFiles.TryGetValue(Rel(chalice), out var chaliceOverrides))
        {
            var bnd = BND4.Read(source.ReadBytes(Rel(chalice)));
            foreach (var file in bnd.Files)
                if (chaliceOverrides.TryGetValue(FileName(file.Name), out var hash))
                    file.Bytes = patches.Blob(hash);
            Save(bnd, outputDvdRoot, Rel(chalice));
        }
    }

    static void Save(BND4 bnd, string outputDvdRoot, string rel)
    {
        var path = Path.Combine(outputDvdRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bnd.Write(path);
    }
}
