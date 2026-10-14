using BbRando.Core;

// Linha de comando: útil para testar e para quem prefere o terminal.
// <fonte> = cópia limpa do jogo (pasta ou .zip) ou, no modo legado, a pasta dvdroot_ps4 do mod original
// (detectado pela pasta "Mod Files").

if (args.Length == 0)
{
    Console.WriteLine("""
        Bloodborne Randomizer (linha de comando)
          randomize <fonte> <pasta-de-saida> [--seed TEXTO | --code BBR2-...] [--config arquivo.json]
          catalog <fonte>
          verify <fonte> <pasta-de-saida>
          apply <pasta-de-saida> <pasta-do-jogo>
          unrandomize <pasta-do-jogo> [--vanilla pasta-ou-zip] [--mod pasta-do-mod]
          analyze-mod <pasta-do-mod> <jogo-limpo> <pasta-relatorio>   (desenvolvimento)
          bosses|fields|parts ...                                       (inspeção)
        """);
    return 1;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// A pasta do mod original tem "Mod Files"; qualquer outra coisa é tratada como jogo limpo.
SourceMode Detect(string path) => Directory.Exists(Path.Combine(path, "Mod Files")) ? SourceMode.ModPack : SourceMode.Vanilla;
IGameSource Open(string path) => GameSource.Open(path, Detect(path));

try
{
    switch (args[0])
    {
        case "randomize":
        {
            var settings = Option("--config") is { } cfg ? RandomizerSettings.FromJson(File.ReadAllText(cfg)) : new RandomizerSettings();
            if (Option("--code") is { } code)
                settings = RandomizerSettings.FromSeedCode(code, settings);
            else
                settings.Seed = Option("--seed") is { } s ? RandomizerSettings.SeedFromText(s) : RandomizerSettings.NewSeed();
            var mode = Detect(args[1]);
            if (Option("--code") != null && settings.Source != mode)
                throw new InvalidOperationException($"Este código de seed é para a fonte \"{settings.Source}\", mas a pasta informada é \"{mode}\".");
            settings.Source = mode;
            var result = Randomizer.Run(args[1], args[2], settings, new SyncProgress(p => Console.Error.WriteLine($"[{p.Percent,3:0}%] {p.Message}")));
            Console.WriteLine($"Inimigos trocados: {result.EnemiesChanged}");
            Console.WriteLine($"Itens posicionados: {result.ItemsMoved}");
            Console.WriteLine($"Saída: {result.OutputDvdRoot}");
            Console.WriteLine($"Logs: {result.LogDir}");
            Console.WriteLine($"Código da seed: {result.SeedCode}");
            foreach (var w in result.Warnings)
                Console.WriteLine("Aviso: " + w);
            break;
        }
        case "catalog":
        {
            var catalog = Randomizer.LoadCatalog(args[1], Detect(args[1]));
            Console.WriteLine($"Comuns: {catalog.Regular.Count}  Cálice: {catalog.Chalice.Count}  Chefes: {catalog.Bosses.Count}  Chefes de cálice: {catalog.ChaliceBosses.Count}");
            foreach (var m in catalog.Models())
                Console.WriteLine($"{m.Model}\t{(m.IsBoss ? "CHEFE" : m.IsChalice ? "cálice" : "comum")}\t{(m.SizeBytes is { } b ? $"{b / 1048576.0:0.0} MB" : "?")}\t{m.Name}\t{string.Join(", ", m.Areas)}");
            break;
        }
        case "apply":
        {
            var report = GameInstaller.Apply(args[1], args[2], new SyncProgress(_ => { }));
            Console.WriteLine($"Arquivos aplicados: {report.Copied}. Backups em: {report.StateDir}");
            foreach (var p in report.Problems)
                Console.WriteLine("Aviso: " + p);
            break;
        }
        case "unrandomize":
        {
            var report = GameInstaller.Unrandomize(args[1], Option("--vanilla"), Option("--mod"), new SyncProgress(_ => { }));
            Console.WriteLine($"Restaurados: {report.Restored}  Movidos para 'removidos': {report.Removed}");
            foreach (var p in report.Problems)
                Console.WriteLine("Problema: " + p);
            break;
        }
        case "bosses":
        {
            using var pack = Open(args[1]);
            var text = new GameText(pack);
            var npc = new ParamBundle(pack).Index("NpcParam");
            foreach (var def in GameData.Maps)
            {
                if (!pack.Exists(def.RelativePath)) continue;
                var msb = SoulsFormats.MSBB.Read(pack.ReadBytes(def.RelativePath));
                foreach (var e in msb.Parts.Enemies)
                {
                    var isBoss = GameData.NameMatches(e.Name, GameData.BossNames) || GameData.ChaliceBossNpc.Contains(e.NPCParamID) || e.Name.StartsWith("c2521") || e.Name.StartsWith("c1400");
                    if (!isBoss) continue;
                    var row = npc.TryGetValue(e.NPCParamID, out var r) ? r : null;
                    var name = row != null ? text.Npc(row.GetInt("nameId")) : null;
                    Console.WriteLine($"{def.Id}\t{e.Name}\tnpc={e.NPCParamID}\tthink={e.ThinkParamID}\tent={e.EntityID}\thp={(row != null ? row.GetInt("hp") : -1)}\tsouls={(row != null ? row.GetInt("getSoul") : -1)}\t{name}");
                }
            }
            break;
        }
        case "parts":
        {
            // parts <arquivo.msb.dcx> <filtro>: mostra as partes de inimigo cujo nome contém o filtro
            foreach (var e in SoulsFormats.MSBB.Read(args[1]).Parts.Enemies.Where(e => e.Name.Contains(args[2])))
                Console.WriteLine($"{e.Name} model={e.ModelName} npc={e.NPCParamID} think={e.ThinkParamID} ent={e.EntityID}");
            break;
        }
        case "fields":
        {
            using var pack = Open(args[1]);
            var prm = new ParamBundle(pack)[args[2]];
            foreach (var cell in prm.Rows.First(r => r.ID == long.Parse(args[3])).Cells)
                Console.WriteLine($"{cell.Def.InternalName} = {cell.Value}");
            break;
        }
        case "verify":
        {
            // Relê a saída: todo mapa precisa abrir e todo inimigo precisa ter o seu modelo na tabela do mapa.
            using var pack = Open(args[1]);
            var dvd = Path.Combine(args[2], "dvdroot_ps4");
            var failures = 0;
            foreach (var def in GameData.Maps)
            {
                var path = Path.Combine(dvd, def.RelativePath);
                if (!File.Exists(path))
                    continue;
                var msb = SoulsFormats.MSBB.Read(path);
                var baseModels = SoulsFormats.MSBB.Read(pack.ReadBytes(def.RelativePath)).Models.Enemies.Count;
                var models = msb.Models.Enemies.Select(m => m.Name).ToHashSet();
                var missing = msb.Parts.Enemies.Select(e => e.ModelName).Where(m => !models.Contains(m)).Distinct().ToList();
                failures += missing.Count;
                Console.WriteLine($"{def.Id}: modelos {baseModels} → {msb.Models.Enemies.Count}{(missing.Count > 0 ? "  FALTANDO: " + string.Join(",", missing) : "")}");
            }
            var bnd = SoulsFormats.BND4.Read(Path.Combine(dvd, "param", "gameparam", "gameparam.parambnd.dcx"));
            Console.WriteLine($"gameparam: {bnd.Files.Count} params, ok");
            return failures == 0 ? 0 : 3;
        }
        case "analyze-mod":
        {
            using var vanilla = new VanillaGameSource(args[2]);
            ModAnalysis.Run(new ModPack(args[1]), vanilla, args[3], Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "referencia", "bb-emedf.json"));
            Console.WriteLine("Relatórios em " + args[3]);
            break;
        }
        case "compare-bnd":
        {
            // compare-bnd <a.dcx> <b.dcx>: compara os nomes dos arquivos internos de dois pacotes
            static HashSet<string> Names(string path) => SoulsFormats.BND4.Read(path).Files
                .Select(f => f.Name[(f.Name.LastIndexOfAny(['\\', '/']) + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var a = Names(args[1]);
            var b = Names(args[2]);
            Console.WriteLine($"A: {a.Count}  B: {b.Count}  só em A: {a.Except(b).Count()}  só em B: {b.Except(a).Count()}");
            foreach (var n in a.Except(b).Take(args.Contains("--all") ? int.MaxValue : 15)) Console.WriteLine("  só A: " + n);
            foreach (var n in b.Except(a).Take(args.Contains("--all") ? int.MaxValue : 15)) Console.WriteLine("  só B: " + n);
            break;
        }
        case "export-patches":
        {
            // export-patches <pasta-do-mod> <jogo-limpo> <arquivo.json>: regenera src/BbRando.Core/Data/modpatches.json
            using var vanilla = new VanillaGameSource(args[2]);
            var set = ModAnalysis.ExportPatches(new ModPack(args[1]), vanilla);
            File.WriteAllText(args[3], set.ToJson());
            Console.WriteLine($"Eventos: {set.Events.Sum(e => e.Value.Count)} em {set.Events.Count} arquivos; mapas: {set.Maps.Count}; " +
                $"linhas novas: {set.ParamRows.Count}; campos: {set.ParamFields.Count}; scripts: {set.LuaFiles.Sum(l => l.Value.Count)}; textos: {set.Texts.Count}");
            break;
        }
        default:
            Console.Error.WriteLine("Comando desconhecido: " + args[0]);
            return 1;
    }
    return 0;
}
catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("Erro: " + e.Message);
    return 2;
}

/// <summary>Progress&lt;T&gt; posta no ThreadPool; aqui queremos as mensagens na ordem.</summary>
sealed class SyncProgress(Action<Progress> report) : IProgress<Progress>
{
    public void Report(Progress value) => report(value);
}
