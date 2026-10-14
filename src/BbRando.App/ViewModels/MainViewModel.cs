using System.Collections.ObjectModel;
using System.Text.Json;
using BbRando.Core;

namespace BbRando.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public RandomizerSettings Settings { get; private set; } = new() { Seed = RandomizerSettings.NewSeed() };

    public ObservableCollection<AreaRow> Areas { get; } = new();
    public ObservableCollection<EnemyRow> VisibleEnemies { get; } = new();
    readonly List<EnemyRow> _allEnemies = new();

    static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BloodborneRandomizer", "interface.json");

    sealed class UiState
    {
        public string? ModPath { get; set; }
        public string? OutputPath { get; set; }
        public string? GamePath { get; set; }
        public string? VanillaPath { get; set; }
        public string? Settings { get; set; }
    }

    public MainViewModel()
    {
        try
        {
            if (File.Exists(StateFile) && JsonSerializer.Deserialize<UiState>(File.ReadAllText(StateFile)) is { } state)
            {
                _modPath = state.ModPath ?? "";
                _outputPath = state.OutputPath ?? "";
                _gamePath = state.GamePath ?? "";
                _vanillaPath = state.VanillaPath ?? "";
                if (state.Settings != null)
                    Settings = RandomizerSettings.FromJson(state.Settings);
            }
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            AppendLog("Não foi possível ler as configurações salvas: " + e.Message);
        }
        if (string.IsNullOrEmpty(_outputPath))
            _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BloodborneRandomizer", "saida");
        foreach (var area in GameData.Areas)
        {
            Settings.AreaChance.TryAdd(area.Id, area.DefaultChance);
            Areas.Add(new AreaRow(area, this));
        }
        _seedText = Settings.Seed.ToString();
        _ = LoadCatalogAsync();
    }

    public void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(new UiState
            {
                ModPath = ModPath, OutputPath = OutputPath, GamePath = GamePath, VanillaPath = VanillaPath,
                Settings = Settings.ToJson(),
            }));
        }
        catch (IOException)
        {
            // Não impedir o fechamento por causa disso.
        }
    }

    // ---------------- Pastas ----------------

    string _modPath = "", _outputPath = "", _gamePath = "", _vanillaPath = "";

    public string ModPath
    {
        get => _modPath;
        set
        {
            if (Set(ref _modPath, value))
                _ = LoadCatalogAsync();
        }
    }

    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public string GamePath { get => _gamePath; set => Set(ref _gamePath, value); }
    public string VanillaPath { get => _vanillaPath; set => Set(ref _vanillaPath, value); }

    // ---------------- Seed ----------------

    string _seedText;

    public string SeedText
    {
        get => _seedText;
        set
        {
            if (!Set(ref _seedText, value))
                return;
            Settings.Seed = RandomizerSettings.SeedFromText(value);
            SettingsChanged();
        }
    }

    public string SeedCode => Settings.ToSeedCode();

    public void NewSeed() => SeedText = RandomizerSettings.NewSeed().ToString();

    /// <summary>Cola um código BBR1-... (ou só uma seed) e atualiza toda a tela.</summary>
    public string? ApplySeedCode(string code)
    {
        try
        {
            Settings = RandomizerSettings.FromSeedCode(code, Settings);
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidDataException)
        {
            return "Código inválido: " + e.Message;
        }
        _seedText = Settings.Seed.ToString();
        foreach (var a in Areas)
            a.Refresh();
        foreach (var e in _allEnemies)
            e.Refresh();
        RaiseAll();
        return null;
    }

    public void SettingsChanged() => Raise(nameof(SeedCode));

    // ---------------- Inimigos ----------------

    public bool RandomizeEnemies { get => Settings.RandomizeEnemies; set { Settings.RandomizeEnemies = value; Changed(); } }

    public double EnemyMemoryMultiplier
    {
        get => Settings.EnemyMemoryMultiplier;
        set { Settings.EnemyMemoryMultiplier = Math.Round(value * 10) / 10; Changed(); Raise(nameof(EnemyMemoryLabel)); }
    }

    public string EnemyMemoryLabel => Settings.EnemyMemoryMultiplier >= 10 ? "sem limite" : $"{Settings.EnemyMemoryMultiplier:0.0}×";

    public double MapMemoryMultiplier
    {
        get => Settings.MapMemoryMultiplier;
        set { Settings.MapMemoryMultiplier = Math.Round(value * 10) / 10; Changed(); Raise(nameof(MapMemoryLabel)); }
    }

    public string MapMemoryLabel => Settings.MapMemoryMultiplier <= 0 ? "desligado" : $"{Settings.MapMemoryMultiplier:0.0}×";

    public double MaxNewModelsPerMap
    {
        get => Settings.MaxNewModelsPerMap;
        set { Settings.MaxNewModelsPerMap = (int)Math.Round(value); Changed(); Raise(nameof(MaxNewModelsLabel)); }
    }

    public string MaxNewModelsLabel => Settings.MaxNewModelsPerMap <= 0 ? "sem limite" : Settings.MaxNewModelsPerMap.ToString();

    public double BossChance
    {
        get => Settings.BossChance;
        set { Settings.BossChance = Math.Round(value * 2) / 2; Changed(); Raise(nameof(BossChanceLabel)); }
    }

    public string BossChanceLabel => Settings.BossChance <= 0 ? "desligado" : $"{Settings.BossChance:0.#}%";

    public bool IncludeChaliceEnemies { get => Settings.IncludeChaliceEnemies; set { Settings.IncludeChaliceEnemies = value; Changed(); } }

    public double ChaliceEnemyChance
    {
        get => Settings.ChaliceEnemyChance;
        set { Settings.ChaliceEnemyChance = Math.Round(value); Changed(); Raise(nameof(ChaliceChanceLabel)); }
    }

    public string ChaliceChanceLabel => $"{Settings.ChaliceEnemyChance:0}%";

    public bool IncludeChaliceBosses { get => Settings.IncludeChaliceBosses; set { Settings.IncludeChaliceBosses = value; Changed(); } }
    public bool IncludeLesserBosses { get => Settings.IncludeLesserBosses; set { Settings.IncludeLesserBosses = value; Changed(); } }
    public bool SwapBosses
    {
        get => Settings.SwapBosses;
        set { Settings.SwapBosses = value; Changed(); Raise(nameof(BossInsertEnabled)); }
    }

    /// <summary>No swap, chefes não substituem inimigos comuns (para não repetir).</summary>
    public bool BossInsertEnabled => !Settings.SwapBosses;
    public bool SwapFinalBosses { get => Settings.SwapFinalBosses; set { Settings.SwapFinalBosses = value; Changed(); } }
    public bool ScaleSwappedBosses { get => Settings.ScaleSwappedBosses; set { Settings.ScaleSwappedBosses = value; Changed(); } }
    public bool StatueHelpers { get => Settings.StatueHelpers; set { Settings.StatueHelpers = value; Changed(); } }
    public bool SwapChaliceBosses { get => Settings.SwapChaliceBosses; set { Settings.SwapChaliceBosses = value; Changed(); } }
    public bool ProtectBellMaidens { get => Settings.ProtectBellMaidens; set { Settings.ProtectBellMaidens = value; Changed(); } }
    public bool AvoidSameEnemy { get => Settings.AvoidSameEnemy; set { Settings.AvoidSameEnemy = value; Changed(); } }

    void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        Raise(name);
        SettingsChanged();
    }

    public void ResetAreaChances()
    {
        foreach (var row in Areas)
            row.Chance = row.Area.DefaultChance;
    }

    // Lista de inimigos

    string _enemyFilter = "";
    bool _showBosses = true, _showChalice = true;
    string _catalogStatus = "Escolha a pasta do mod para carregar a lista de inimigos.";

    public string CatalogStatus { get => _catalogStatus; private set => Set(ref _catalogStatus, value); }
    public string EnemyFilter { get => _enemyFilter; set { if (Set(ref _enemyFilter, value)) FilterEnemies(); } }
    public bool ShowBosses { get => _showBosses; set { if (Set(ref _showBosses, value)) FilterEnemies(); } }
    public bool ShowChalice { get => _showChalice; set { if (Set(ref _showChalice, value)) FilterEnemies(); } }

    async Task LoadCatalogAsync()
    {
        _allEnemies.Clear();
        VisibleEnemies.Clear();
        if (string.IsNullOrWhiteSpace(ModPath))
        {
            CatalogStatus = "Escolha a pasta do mod para carregar a lista de inimigos.";
            return;
        }
        CatalogStatus = "Carregando inimigos do mod...";
        var path = ModPath;
        try
        {
            var models = await Task.Run(() => Randomizer.LoadCatalog(path).Models());
            if (path != ModPath)
                return; // a pasta mudou enquanto carregava
            foreach (var m in models)
                _allEnemies.Add(new EnemyRow(m, this));
            FilterEnemies();
            CatalogStatus = $"{_allEnemies.Count} tipos de inimigo encontrados.";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            CatalogStatus = "Não foi possível ler o mod: " + e.Message;
        }
    }

    void FilterEnemies()
    {
        VisibleEnemies.Clear();
        foreach (var row in _allEnemies)
        {
            if (!ShowBosses && row.Info.IsBoss || !ShowChalice && row.Info.IsChalice)
                continue;
            if (EnemyFilter.Length > 0 && !(row.Title + " " + row.Subtitle).Contains(EnemyFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            VisibleEnemies.Add(row);
        }
    }

    /// <summary>Muda o peso de todos os inimigos visíveis (respeita o filtro).</summary>
    public void SetVisibleWeights(double weight)
    {
        foreach (var row in VisibleEnemies)
            row.Weight = weight;
    }

    // ---------------- Itens ----------------

    public bool RandomizeItems { get => Settings.RandomizeItems; set { Settings.RandomizeItems = value; Changed(); } }
    public bool ItemsWithinArea { get => Settings.ItemsWithinArea; set { Settings.ItemsWithinArea = value; Changed(); } }
    public bool IncludeKeyItems { get => Settings.IncludeKeyItems; set { Settings.IncludeKeyItems = value; Changed(); } }
    public bool IncludeWorkshopItems { get => Settings.IncludeWorkshopItems; set { Settings.IncludeWorkshopItems = value; Changed(); } }
    public bool RandomizeEnemyDrops { get => Settings.RandomizeEnemyDrops; set { Settings.RandomizeEnemyDrops = value; Changed(); } }

    // ---------------- Execução ----------------

    bool _busy;
    double _progress;
    string _status = "Pronto.";
    string _log = "";
    string? _lastLogDir;

    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(Idle)); } }
    public bool Idle => !Busy;
    public double ProgressValue { get => _progress; private set => Set(ref _progress, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public string? LastLogDir { get => _lastLogDir; private set { if (Set(ref _lastLogDir, value)) Raise(nameof(HasLogs)); } }
    public bool HasLogs => LastLogDir != null && Directory.Exists(LastLogDir);

    void AppendLog(string line) => Log += (Log.Length > 0 ? Environment.NewLine : "") + line;

    IProgress<BbRando.Core.Progress> MakeProgress() => new Progress<BbRando.Core.Progress>(p =>
    {
        ProgressValue = p.Percent;
        Status = p.Message;
    });

    async Task RunAsync(string title, Func<IProgress<BbRando.Core.Progress>, string> work)
    {
        if (Busy)
            return;
        Busy = true;
        ProgressValue = 0;
        AppendLog($"— {title} —");
        try
        {
            var progress = MakeProgress();
            AppendLog(await Task.Run(() => work(progress)));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException
                                      or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Status = "Erro.";
            AppendLog("ERRO: " + e.Message);
        }
        finally
        {
            Busy = false;
            SaveState();
        }
    }

    public async Task RandomizeAsync()
    {
        var settings = Settings.Clone();
        var mod = ModPath;
        var output = OutputPath;
        string? logDir = null;
        await RunAsync("Randomizar", progress =>
        {
            if (string.IsNullOrWhiteSpace(mod))
                throw new InvalidOperationException("Escolha a pasta do mod original (aba Início).");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("Escolha a pasta de saída (aba Início).");
            var r = Randomizer.Run(mod, output, settings, progress);
            logDir = r.LogDir;
            var lines = new List<string>
            {
                $"Inimigos trocados: {r.EnemiesChanged}",
                $"Itens embaralhados: {r.ItemsMoved}",
                $"Arquivos gerados em: {r.OutputDvdRoot}",
                $"Logs (localização dos itens e inimigos): {r.LogDir}",
                $"Código da seed: {r.SeedCode}",
            };
            lines.AddRange(r.Warnings.Select(w => "Aviso: " + w));
            return string.Join(Environment.NewLine, lines);
        });
        if (logDir != null)
            LastLogDir = logDir; // na thread da interface
    }

    public Task ApplyAsync()
    {
        var output = OutputPath;
        var game = GamePath;
        return RunAsync("Aplicar no jogo", progress =>
        {
            if (string.IsNullOrWhiteSpace(game))
                throw new InvalidOperationException("Escolha a pasta do jogo (aba Início).");
            var r = GameInstaller.Apply(output, game, progress);
            return $"{r.Copied} arquivos copiados para o jogo. Backup dos originais em: {r.StateDir}";
        });
    }

    public Task UnrandomizeAsync()
    {
        var game = GamePath;
        var vanilla = VanillaPath;
        var mod = ModPath;
        return RunAsync("Unrandomizer", progress =>
        {
            if (string.IsNullOrWhiteSpace(game))
                throw new InvalidOperationException("Escolha a pasta do jogo (aba Início).");
            var r = GameInstaller.Unrandomize(game, vanilla, mod, progress);
            var lines = new List<string> { $"Restaurados: {r.Restored} arquivos. Arquivos extras movidos para: {Path.Combine(r.StateDir, "removidos")} ({r.Removed})." };
            lines.AddRange(r.Problems.Take(20).Select(p => "Problema: " + p));
            if (r.Problems.Count > 20)
                lines.Add($"... e mais {r.Problems.Count - 20} problemas.");
            lines.Add(r.Problems.Count == 0
                ? "O jogo voltou ao normal. Seu save continua com os itens que você já pegou."
                : "Alguns arquivos não puderam ser restaurados. Informe a cópia original do jogo (aba Início) e tente de novo.");
            return string.Join(Environment.NewLine, lines);
        });
    }
}
