using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BbRando.Core;

/// <summary>Cópia limpa do jogo: uma pasta (CUSA.../dvdroot_ps4) ou um .zip com ela dentro.</summary>
public sealed class VanillaSource : IDisposable
{
    readonly string? _dvdRoot;
    readonly ZipArchive? _zip;
    readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public string Description { get; }

    public VanillaSource(string path)
    {
        path = Path.GetFullPath(path);
        Description = path;
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _zip = ZipFile.OpenRead(path);
            foreach (var entry in _zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var i = name.IndexOf("dvdroot_ps4/", StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && !name.EndsWith('/'))
                    _entries.TryAdd(name[(i + "dvdroot_ps4/".Length)..], entry);
            }
            if (_entries.Count == 0)
                throw new InvalidDataException("O .zip não contém uma pasta dvdroot_ps4.");
        }
        else
        {
            _dvdRoot = GameInstaller.FindDvdRoot(path)
                ?? throw new DirectoryNotFoundException($"'{path}' não contém a pasta dvdroot_ps4 do jogo.");
        }
    }

    public bool Has(string rel) => _zip != null
        ? _entries.ContainsKey(rel)
        : File.Exists(Path.Combine(_dvdRoot!, rel.Replace('/', Path.DirectorySeparatorChar)));

    public void CopyTo(string rel, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (_zip != null)
            _entries[rel].ExtractToFile(destination, overwrite: true);
        else
            File.Copy(Path.Combine(_dvdRoot!, rel.Replace('/', Path.DirectorySeparatorChar)), destination, overwrite: true);
    }

    public void Dispose() => _zip?.Dispose();
}

public sealed record InstallReport(int Copied, int Restored, int Removed, IReadOnlyList<string> Problems, string StateDir);

/// <summary>
/// Aplica a saída do randomizer na pasta do jogo e desfaz (Unrandomizer). Antes de sobrescrever
/// qualquer arquivo do jogo pela primeira vez, guarda uma cópia dele numa pasta de backup própria.
/// </summary>
public static class GameInstaller
{
    sealed class Manifest
    {
        /// <summary>Arquivo relativo → se ele existia no jogo antes da primeira aplicação.</summary>
        public Dictionary<string, bool> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Aceita a pasta CUSA..., a dvdroot_ps4 ou uma pasta acima delas.</summary>
    public static string? FindDvdRoot(string path)
    {
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path))
            return null;
        if (Directory.Exists(Path.Combine(path, "map", "mapstudio")))
            return path;
        var direct = Path.Combine(path, "dvdroot_ps4");
        if (Directory.Exists(Path.Combine(direct, "map")))
            return direct;
        foreach (var sub in Directory.EnumerateDirectories(path))
        {
            var nested = Path.Combine(sub, "dvdroot_ps4");
            if (Directory.Exists(Path.Combine(nested, "map")))
                return nested;
        }
        return null;
    }

    /// <summary>Pasta de estado (backups e manifesto) de uma instalação do jogo.</summary>
    public static string StateDir(string gameDvdRoot)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gameDvdRoot))))[..12];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BloodborneRandomizer", "jogo-" + hash);
    }

    static Manifest LoadManifest(string stateDir)
    {
        var path = Path.Combine(stateDir, "manifest.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? new() : new();
    }

    static void SaveManifest(string stateDir, Manifest manifest)
    {
        Directory.CreateDirectory(stateDir);
        File.WriteAllText(Path.Combine(stateDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string Native(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    public static InstallReport Apply(string outputDir, string gamePath, IProgress<Progress>? progress = null)
    {
        var outputDvd = Path.Combine(Path.GetFullPath(outputDir), "dvdroot_ps4");
        if (!File.Exists(Path.Combine(outputDir, Randomizer.OutputMarker)) || !Directory.Exists(outputDvd))
            throw new InvalidOperationException("A pasta de saída ainda não tem uma randomização. Clique em \"Randomizar\" primeiro.");
        var gameDvd = FindDvdRoot(gamePath) ?? throw new DirectoryNotFoundException("Pasta do jogo inválida: dvdroot_ps4 não encontrada.");

        var state = StateDir(gameDvd);
        var backup = Path.Combine(state, "backup");
        var manifest = LoadManifest(state);
        var files = Directory.EnumerateFiles(outputDvd, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(outputDvd, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        var copied = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var rel = files[i];
            progress?.Report(new Progress(100.0 * i / files.Count, "Aplicando: " + rel));
            var target = Native(gameDvd, rel);
            if (!manifest.Files.ContainsKey(rel))
            {
                var existed = File.Exists(target);
                if (existed)
                {
                    var saved = Native(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved, overwrite: false);
                }
                manifest.Files[rel] = existed;
                SaveManifest(state, manifest); // grava a cada arquivo: se algo falhar no meio, o Unrandomizer ainda sabe o que foi feito
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Native(outputDvd, rel), target, overwrite: true);
            copied++;
        }
        progress?.Report(new Progress(100, "Randomização aplicada ao jogo."));
        return new InstallReport(copied, 0, 0, [], state);
    }

    /// <summary>
    /// Volta o jogo ao normal. Com uma cópia original (<paramref name="vanillaPath"/>), restaura também
    /// os arquivos de mods aplicados antes deste programa (por exemplo, o randomizer antigo).
    /// </summary>
    public static InstallReport Unrandomize(string gamePath, string? vanillaPath, string? modPackDir, IProgress<Progress>? progress = null)
    {
        var gameDvd = FindDvdRoot(gamePath) ?? throw new DirectoryNotFoundException("Pasta do jogo inválida: dvdroot_ps4 não encontrada.");
        var state = StateDir(gameDvd);
        var backup = Path.Combine(state, "backup");
        var removedDir = Path.Combine(state, "removidos", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        var manifest = LoadManifest(state);
        using var vanilla = string.IsNullOrWhiteSpace(vanillaPath) ? null : new VanillaSource(vanillaPath);

        var targets = new SortedSet<string>(manifest.Files.Keys, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(modPackDir) && Directory.Exists(modPackDir))
            foreach (var rel in new ModPack(modPackDir).GameFiles())
                targets.Add(rel);
        if (targets.Count == 0)
            throw new InvalidOperationException("Nada para desfazer: nenhuma randomização aplicada por este programa foi encontrada.");

        int restored = 0, removed = 0, i = 0;
        var problems = new List<string>();
        foreach (var rel in targets)
        {
            progress?.Report(new Progress(100.0 * i++ / targets.Count, "Restaurando: " + rel));
            var target = Native(gameDvd, rel);
            var saved = Native(backup, rel);
            if (vanilla != null)
            {
                if (vanilla.Has(rel))
                {
                    vanilla.CopyTo(rel, target);
                    restored++;
                }
                else if (File.Exists(target))
                {
                    MoveAside(target, Native(removedDir, rel));
                    removed++;
                }
            }
            else if (manifest.Files.TryGetValue(rel, out var existed))
            {
                if (existed && File.Exists(saved))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(saved, target, overwrite: true);
                    restored++;
                }
                else if (!existed && File.Exists(target))
                {
                    MoveAside(target, Native(removedDir, rel));
                    removed++;
                }
            }
            else
            {
                problems.Add($"{rel}: sem cópia original para restaurar (informe a cópia limpa do jogo).");
            }
        }

        if (problems.Count == 0)
        {
            // Tudo restaurado: o próximo "Aplicar" volta a fazer backup do estado limpo atual.
            File.Delete(Path.Combine(state, "manifest.json"));
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
        }
        progress?.Report(new Progress(100, "Jogo restaurado."));
        return new InstallReport(0, restored, removed, problems, state);
    }

    /// <summary>Arquivos que não existem no jogo original não são apagados: vão para uma pasta de removidos.</summary>
    static void MoveAside(string file, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(file, destination, overwrite: true);
    }
}
