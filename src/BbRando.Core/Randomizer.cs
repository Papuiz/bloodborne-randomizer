using System.Text;

namespace BbRando.Core;

public sealed record Progress(double Percent, string Message);

public sealed record RandomizeResult(
    string OutputDvdRoot, string LogDir, string SeedCode, int EnemiesChanged, int ItemsMoved, IReadOnlyList<string> Warnings);

/// <summary>Executa a randomização completa: lê a fonte (jogo limpo ou pacote do mod) e grava numa pasta de saída separada.</summary>
public static class Randomizer
{
    /// <summary>Arquivo que marca uma pasta como saída deste programa (só essas são limpas automaticamente).</summary>
    public const string OutputMarker = ".bloodborne-randomizer-output";

    public static RandomizeResult Run(string sourcePath, string outputDir, RandomizerSettings settings,
        IProgress<Progress>? progress = null, CancellationToken cancel = default)
    {
        void Report(double p, string m) => progress?.Report(new Progress(p, m));

        using var pack = GameSource.Open(sourcePath, settings.Source);
        var outputDvd = PrepareOutput(pack.Description, outputDir);

        Report(2, "Lendo textos e parâmetros do jogo...");
        var text = new GameText(pack);
        var param = new ParamBundle(pack);

        Report(8, "Lendo mapas...");
        var maps = LoadMaps(pack, cancel);
        cancel.ThrowIfCancellationRequested();

        // Jogo limpo: aplica os ajustes do mod original guardados como dados (no modo legado eles já
        // estão nos arquivos do pacote).
        var patches = pack.Mode == SourceMode.Vanilla ? ModPatchSet.Embedded() : new ModPatchSet();
        patches.ApplyParams(param);
        patches.ApplyMaps(maps);

        Report(25, "Montando a lista de inimigos...");
        // O catálogo é lido dos mapas ainda intactos, antes de qualquer troca.
        var catalog = new EnemyCatalog(pack, param, text, maps);
        var warnings = new List<string>();
        var itemCatalog = new ItemCatalog(param, text);
        var items = new ItemRandomizer(itemCatalog, settings, text);

        // Drops antes do swap: o chefe novo herda a recompensa (já embaralhada) do chefe da arena.
        if (settings.RandomizeEnemyDrops)
        {
            Report(27, "Embaralhando drops de inimigos...");
            items.RandomizeDrops(param);
        }

        // Escalonamento lido dos mapas antes das trocas (cada mapa: o efeito dos inimigos nativos).
        var scaling = new AreaScaling(param, maps);
        var bosses = new BossSwapper(settings, param, catalog.DisplayName);
        if (settings.SwapBosses)
        {
            Report(29, "Trocando os chefes de arena...");
            bosses.Swap(maps);
            warnings.AddRange(bosses.Warnings);
        }

        var enemies = new EnemyRandomizer(catalog, settings, text) { ExcludedModels = bosses.SwappedModels, Scaling = scaling };
        if (settings.RandomizeEnemies)
        {
            var targets = maps.Values.Where(m => m.Def.EnemyTarget).ToList();
            for (var i = 0; i < targets.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                Report(30 + 30.0 * i / targets.Count, $"Randomizando inimigos: {targets[i].Area.Name} ({targets[i].Def.Id})");
                enemies.Randomize(targets[i]);
            }
        }

        if (settings.RandomizeItems)
        {
            Report(62, "Embaralhando itens...");
            items.RandomizeTreasures(maps.Values);
        }

        var shops = new ShopRandomizer(param, settings, text);
        if (settings.RandomizeStartingWeapons)
        {
            Report(64, "Sorteando as armas iniciais...");
            shops.RandomizeStartingWeapons();
        }
        if (settings.RandomizeShops)
        {
            Report(66, "Embaralhando as lojas...");
            shops.RandomizeLineup();
        }

        foreach (var map in maps.Values.Where(m => m.Dirty))
            ModelTable.EnsureModels(map, catalog, warnings);

        if (pack is ModPack modPack)
        {
            // Modo legado: os arquivos que os autores alteraram (eventos, IA, efeitos...) vão junto.
            Report(70, "Copiando arquivos do mod...");
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "param/gameparam/gameparam.parambnd.dcx",
            };
            foreach (var map in maps.Values.Where(m => m.Dirty))
                written.Add(map.Def.RelativePath);
            foreach (var rel in modPack.GameFiles())
            {
                cancel.ThrowIfCancellationRequested();
                if (written.Contains(rel))
                    continue;
                var dest = Path.Combine(outputDvd, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(modPack.Resolve(rel), dest, overwrite: true);
            }
        }

        Report(80, "Gravando mapas...");
        foreach (var map in maps.Values.Where(m => m.Dirty))
        {
            cancel.ThrowIfCancellationRequested();
            map.Write(outputDvd);
        }
        Report(88, "Gravando parâmetros...");
        param.Write(Path.Combine(outputDvd, "param", "gameparam", "gameparam.parambnd.dcx"));

        if (pack.Mode == SourceMode.Vanilla)
        {
            Report(90, "Gravando eventos e textos...");
            patches.WriteEvents(pack, outputDvd, new BossEvents(pack, maps, bosses.ArenaChanges, settings.StatueHelpers));
            patches.WriteTexts(pack, outputDvd);
            Report(92, "Juntando os scripts de IA...");
            AiScripts.Write(pack, patches, outputDvd);
            Report(94, "Juntando os efeitos visuais...");
            SfxMerge.Write(pack, outputDvd);
        }

        Report(96, "Gravando logs...");
        var seedCode = settings.ToSeedCode();
        var logDir = Path.Combine(outputDir, "logs");
        Directory.CreateDirectory(logDir);
        SpoilerLog.WriteItems(Path.Combine(logDir, "itens.txt"), seedCode, items, shops);
        SpoilerLog.WriteEnemies(Path.Combine(logDir, "inimigos.txt"), seedCode, enemies, bosses, settings.ScaleEnemiesToArea ? scaling : null, warnings,
            barNamesFixed: pack.Mode == SourceMode.Vanilla);
        File.WriteAllText(Path.Combine(outputDir, "seed.txt"), seedCode + Environment.NewLine);
        File.WriteAllText(Path.Combine(outputDir, "configuracao.json"), settings.ToJson());

        Report(100, "Pronto!");
        return new RandomizeResult(outputDvd, logDir, seedCode, enemies.Swaps.Count, items.Placements.Count, warnings);
    }

    static Dictionary<string, MapFile> LoadMaps(IGameSource pack, CancellationToken cancel)
    {
        var maps = new Dictionary<string, MapFile>();
        foreach (var def in GameData.Maps)
        {
            cancel.ThrowIfCancellationRequested();
            if (MapFile.Load(pack, def) is { } map)
                maps[def.Id] = map;
        }
        return maps;
    }

    /// <summary>Lê só o catálogo de inimigos (para montar a lista da interface).</summary>
    public static EnemyCatalog LoadCatalog(string sourcePath, SourceMode mode)
    {
        using var pack = GameSource.Open(sourcePath, mode);
        var text = new GameText(pack);
        var param = new ParamBundle(pack);
        return new EnemyCatalog(pack, param, text, LoadMaps(pack, default));
    }

    /// <summary>
    /// Cria/limpa a pasta de saída. Por segurança, só apaga o conteúdo se a pasta estiver vazia
    /// ou tiver sido criada por este programa.
    /// </summary>
    static string PrepareOutput(string sourceRoot, string outputDir)
    {
        outputDir = Path.GetFullPath(outputDir);
        if (IsInside(outputDir, sourceRoot) || IsInside(sourceRoot, outputDir))
            throw new InvalidOperationException("A pasta de saída não pode ficar dentro da pasta de origem (nem conter ela).");
        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any()
            && !File.Exists(Path.Combine(outputDir, OutputMarker)))
            throw new InvalidOperationException(
                $"A pasta de saída '{outputDir}' já tem arquivos que não são deste programa. Escolha uma pasta vazia.");

        Directory.CreateDirectory(outputDir);
        foreach (var sub in new[] { "dvdroot_ps4", "logs" })
        {
            var path = Path.Combine(outputDir, sub);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        File.WriteAllText(Path.Combine(outputDir, OutputMarker), "Pasta gerada pelo Bloodborne Randomizer.\n");
        var dvd = Path.Combine(outputDir, "dvdroot_ps4");
        Directory.CreateDirectory(dvd);
        return dvd;
    }

    internal static bool IsInside(string path, string folder)
    {
        var rel = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return rel == "." || !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }
}

static class SpoilerLog
{
    public static void WriteItems(string path, string seedCode, ItemRandomizer items, ShopRandomizer shops)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BLOODBORNE RANDOMIZER — LOCALIZAÇÃO DOS ITENS");
        sb.AppendLine($"Código da seed: {seedCode}");
        sb.AppendLine("Coordenadas: [mapa @ x, y, z]. \"Antes\" = o que ficava nesse lugar no jogo normal.");
        sb.AppendLine();

        if (items.Placements.Count == 0 && items.Drops.Count == 0 && shops.Changes.Count == 0 && shops.StartingWeapons.Count == 0)
        {
            sb.AppendLine("Itens não foram randomizados nesta seed.");
            File.WriteAllText(path, sb.ToString());
            return;
        }

        if (shops.StartingWeapons.Count > 0)
        {
            sb.AppendLine("==================== ARMAS INICIAIS (Mensageiros) ====================");
            foreach (var w in shops.StartingWeapons)
                sb.AppendLine($"    {w.Slot}: {w.Weapon}  (no lugar de {w.Replaced})" +
                    (w.Requirements != null ? $"  — requisitos For/Hab/San/Arc {w.Requirements}" : ""));
            sb.AppendLine();
        }

        if (shops.Changes.Count > 0)
        {
            sb.AppendLine("==================== LOJAS ====================");
            sb.AppendLine("O item novo aparece quando o item antigo apareceria (mesma insígnia/condição). O preço é o do item, na faixa inicial.");
            foreach (var shop in shops.Changes.GroupBy(c => c.Shop))
            {
                sb.AppendLine($"--- {shop.Key} ---");
                foreach (var c in shop)
                    sb.AppendLine($"    {c.Item}  ({c.Price} {c.Currency})  ←  no lugar de {c.Replaced}");
                sb.AppendLine();
            }
        }

        if (items.Placements.Count == 0 && items.Drops.Count == 0)
        {
            File.WriteAllText(path, sb.ToString());
            return;
        }

        sb.AppendLine("==================== POR ITEM (A–Z) ====================");
        foreach (var group in items.Placements.Concat(items.Drops).GroupBy(p => p.Item).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(group.Key);
            foreach (var p in group)
                sb.AppendLine(p.Kind == "Drop"
                    ? $"    - Drop de {p.Where.Area}  (antes soltava: {p.Replaced})"
                    : $"    - {p.Where}  ({p.Kind}; antes: {p.Replaced})");
        }

        sb.AppendLine();
        sb.AppendLine("==================== POR ÁREA ====================");
        foreach (var area in items.Placements.GroupBy(p => p.Where.Area))
        {
            sb.AppendLine($"--- {area.Key} ---");
            foreach (var p in area.OrderBy(p => p.Where.Place).ThenBy(p => p.Where.MapId))
                sb.AppendLine($"    {p.Item}  →  {p.Where}  (antes: {p.Replaced})");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    static string Label(EnemyKind? k) => k == null ? "?" : k.Name == k.Model ? k.Model : $"{k.Name} ({k.Model})";

    public static void WriteEnemies(string path, string seedCode, EnemyRandomizer enemies, BossSwapper bosses, AreaScaling? scaling, List<string> warnings,
        bool barNamesFixed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BLOODBORNE RANDOMIZER — INIMIGOS");
        sb.AppendLine($"Código da seed: {seedCode}");
        sb.AppendLine();
        if (bosses.Swaps.Count > 0)
        {
            sb.AppendLine("==================== CHEFES (SWAP) ====================");
            sb.AppendLine("Arena (onde fica)  →  chefe que você vai enfrentar lá");
            foreach (var s in bosses.Swaps)
                sb.AppendLine($"    Arena de {s.Arena} ({s.AreaName})  →  {s.Now}");
            sb.AppendLine(barNamesFixed
                ? "A barra de vida mostra o nome do chefe novo (exceto nas masmorras de cálice)."
                : "Obs.: a barra de vida pode mostrar o nome do chefe original da arena.");
            sb.AppendLine();
        }
        sb.AppendLine($"Inimigos trocados: {enemies.Swaps.Count}  (★ = chefe no lugar de inimigo comum)");
        sb.AppendLine();
        foreach (var area in enemies.Swaps.GroupBy(s => s.Where.Area))
        {
            sb.AppendLine($"--- {area.Key} ---");
            foreach (var s in area)
                sb.AppendLine($"    {(s.BecameBoss ? "★ " : "")}{Label(s.Old)}  →  {Label(s.New)}    {(s.Where.Place != s.Where.Area ? s.Where.Place : null)} [{s.Where.MapId} {s.PartName}]");
            sb.AppendLine();
        }
        if (scaling != null)
        {
            sb.AppendLine("--- Força de reserva por mapa (usada quando o inimigo substituído não tem escalonamento) ---");
            foreach (var (map, scale) in scaling.Table.OrderBy(kv => kv.Key))
                sb.AppendLine($"    {map}: SpEffect {scale.Area} (NG+ {scale.NewGame})");
            sb.AppendLine();
        }
        if (warnings.Count > 0)
        {
            sb.AppendLine("--- Avisos ---");
            foreach (var w in warnings)
                sb.AppendLine("    " + w);
        }
        File.WriteAllText(path, sb.ToString());
    }
}
