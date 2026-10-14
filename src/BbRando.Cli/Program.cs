using BbRando.Core;

// Linha de comando: útil para testar e para quem prefere o terminal.
//   randomize <pasta-do-mod> <pasta-de-saida> [--seed TEXTO | --code BBR1-...] [--config arquivo.json]
//   catalog <pasta-do-mod>
//   apply <pasta-de-saida> <pasta-do-jogo>
//   unrandomize <pasta-do-jogo> [--vanilla pasta-ou-zip] [--mod pasta-do-mod]

if (args.Length == 0)
{
    Console.WriteLine("""
        Bloodborne Randomizer (linha de comando)
          randomize <pasta-do-mod> <pasta-de-saida> [--seed TEXTO | --code BBR1-...] [--config arquivo.json]
          catalog <pasta-do-mod>
          apply <pasta-de-saida> <pasta-do-jogo>
          unrandomize <pasta-do-jogo> [--vanilla pasta-ou-zip] [--mod pasta-do-mod]
        """);
    return 1;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}


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
            var catalog = Randomizer.LoadCatalog(args[1]);
            Console.WriteLine($"Comuns: {catalog.Regular.Count}  Cálice: {catalog.Chalice.Count}  Chefes: {catalog.Bosses.Count}  Chefes de cálice: {catalog.ChaliceBosses.Count}");
            foreach (var m in catalog.Models())
                Console.WriteLine($"{m.Model}\t{(m.IsBoss ? "CHEFE" : m.IsChalice ? "cálice" : "comum")}\t{(m.SizeBytes is { } b ? $"{b / 1048576.0:0.0} MB" : "?")}\t{m.Name}\t{string.Join(", ", m.Areas)}");
            break;
        }
        case "apply":
        {
            var report = GameInstaller.Apply(args[1], args[2], new SyncProgress(_ => { }));
            Console.WriteLine($"Arquivos aplicados: {report.Copied}. Backups em: {report.StateDir}");
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
            var pack = new ModPack(args[1]);
            var text = new GameText(pack);
            var npc = new ParamBundle(pack).Index("NpcParam");
            foreach (var def in GameData.Maps)
            {
                if (!pack.Exists(def.RelativePath)) continue;
                var msb = SoulsFormats.MSBB.Read(pack.Resolve(def.RelativePath));
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
            var prm = new ParamBundle(new ModPack(args[1]))[args[2]];
            foreach (var cell in prm.Rows.First(r => r.ID == long.Parse(args[3])).Cells)
                Console.WriteLine($"{cell.Def.InternalName} = {cell.Value}");
            break;
        }
        case "verify":
        {
            // Relê a saída: todo mapa precisa abrir e todo inimigo precisa ter o seu modelo na tabela do mapa.
            var pack = new ModPack(args[1]);
            var dvd = Path.Combine(args[2], "dvdroot_ps4");
            var failures = 0;
            foreach (var def in GameData.Maps)
            {
                var path = Path.Combine(dvd, def.RelativePath);
                if (!File.Exists(path))
                    continue;
                var msb = SoulsFormats.MSBB.Read(path);
                var baseModels = SoulsFormats.MSBB.Read(pack.Resolve(def.RelativePath)).Models.Enemies.Count;
                var models = msb.Models.Enemies.Select(m => m.Name).ToHashSet();
                var missing = msb.Parts.Enemies.Select(e => e.ModelName).Where(m => !models.Contains(m)).Distinct().ToList();
                failures += missing.Count;
                Console.WriteLine($"{def.Id}: modelos {baseModels} → {msb.Models.Enemies.Count}{(missing.Count > 0 ? "  FALTANDO: " + string.Join(",", missing) : "")}");
            }
            var bnd = SoulsFormats.BND4.Read(Path.Combine(dvd, "param", "gameparam", "gameparam.parambnd.dcx"));
            Console.WriteLine($"gameparam: {bnd.Files.Count} params, ok");
            return failures == 0 ? 0 : 3;
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
