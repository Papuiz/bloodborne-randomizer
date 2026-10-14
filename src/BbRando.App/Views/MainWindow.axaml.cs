using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BbRando.App.ViewModels;

namespace BbRando.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Mantém o log rolado até o fim.
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    // ---------------- Pastas ----------------

    async Task<string?> PickFolder(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    async void PickModFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Pasta dvdroot_ps4 do mod original") is { } path)
            Vm.ModPath = path;
    }

    async void PickOutputFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Pasta de saída") is { } path)
            Vm.OutputPath = path;
    }

    async void PickGameFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Pasta do jogo (CUSA03173)") is { } path)
            Vm.GamePath = path;
    }

    async void PickVanillaFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Cópia original do jogo") is { } path)
            Vm.VanillaPath = path;
    }

    async void PickVanillaZip(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Cópia original do jogo (.zip)",
            FileTypeFilter = [new FilePickerFileType("Arquivo zip") { Patterns = ["*.zip"] }],
        });
        if (result.Count > 0 && result[0].TryGetLocalPath() is { } path)
            Vm.VanillaPath = path;
    }

    // ---------------- Seed ----------------

    void NewSeed(object? sender, RoutedEventArgs e) => Vm.NewSeed();

    async void CopySeedCode(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null)
            await Clipboard.SetTextAsync(Vm.SeedCode);
        ShowSeedMessage("Código copiado.");
    }

    async void PasteSeedCode(object? sender, RoutedEventArgs e)
    {
        var current = Clipboard != null ? await Clipboard.TryGetTextAsync() : null;
        var code = await AskText("Colar código de seed", "Cole o código BBR1-... (ou só uma seed):", current?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(code))
            return;
        ShowSeedMessage(Vm.ApplySeedCode(code) ?? "Código aplicado: opções e seed atualizadas.");
    }

    void ShowSeedMessage(string text)
    {
        SeedMessage.Text = text;
        SeedMessage.IsVisible = true;
    }

    // ---------------- Inimigos ----------------

    void ResetAreas(object? sender, RoutedEventArgs e) => Vm.ResetAreaChances();
    void WeightsNormal(object? sender, RoutedEventArgs e) => Vm.SetVisibleWeights(1);
    void WeightsZero(object? sender, RoutedEventArgs e) => Vm.SetVisibleWeights(0);

    // ---------------- Execução ----------------

    async void Randomize(object? sender, RoutedEventArgs e) => await Vm.RandomizeAsync();

    async void Apply(object? sender, RoutedEventArgs e)
    {
        if (await Confirm("Aplicar no jogo",
                $"Copiar a randomização para:\n{Vm.GamePath}\n\nOs arquivos substituídos ganham backup automático e podem ser restaurados com o Unrandomizer."))
            await Vm.ApplyAsync();
    }

    async void Unrandomize(object? sender, RoutedEventArgs e)
    {
        var source = string.IsNullOrWhiteSpace(Vm.VanillaPath)
            ? "os backups feitos por este programa"
            : Vm.VanillaPath;
        if (await Confirm("Unrandomizer",
                $"Restaurar os arquivos originais do jogo em:\n{Vm.GamePath}\n\nFonte: {source}\n\nArquivos que não existem no jogo original são movidos para uma pasta de removidos, não apagados."))
            await Vm.UnrandomizeAsync();
    }

    async void OpenLogs(object? sender, RoutedEventArgs e)
    {
        if (Vm.LastLogDir is { } dir && Directory.Exists(dir))
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
    }

    // ---------------- Diálogos simples ----------------

    Task<bool> Confirm(string title, string message)
    {
        var dialog = BuildDialog(title, message, null, out _);
        return dialog.ShowDialog<bool>(this);
    }

    async Task<string?> AskText(string title, string message, string initial)
    {
        var dialog = BuildDialog(title, message, initial, out var box);
        return await dialog.ShowDialog<bool>(this) ? box?.Text : null;
    }

    static Window BuildDialog(string title, string message, string? initialText, out TextBox? box)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        box = null;
        if (initialText != null)
        {
            box = new TextBox { Text = initialText, FontFamily = "monospace" };
            panel.Children.Add(box);
        }
        var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok },
        });
        dialog.Content = panel;
        return dialog;
    }
}
