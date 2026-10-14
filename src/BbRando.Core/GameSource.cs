using System.IO.Compression;

namespace BbRando.Core;

/// <summary>De onde vêm os arquivos-base do jogo.</summary>
public enum SourceMode
{
    /// <summary>Cópia limpa do jogo (pasta ou .zip). Padrão.</summary>
    Vanilla,
    /// <summary>Pacote do mod original 5.2 (modo legado).</summary>
    ModPack,
}

/// <summary>Arquivos-base do jogo, lidos por caminho relativo à dvdroot_ps4 (com "/").</summary>
public interface IGameSource : IDisposable
{
    string Description { get; }
    SourceMode Mode { get; }
    bool Exists(string relative);
    byte[] ReadBytes(string relative);
    /// <summary>Todos os arquivos da fonte (relativos, com "/").</summary>
    IEnumerable<string> Files();
    /// <summary>"Peso" em memória de cada modelo de personagem (cNNNN → bytes).</summary>
    Dictionary<string, long> ModelSizes();
}

public static class GameSource
{
    /// <summary>Os textos e nomes dos arquivos do jogo usam Shift-JIS, que o .NET só tem com este provedor.</summary>
#pragma warning disable CA2255 // intencional: precisa valer antes de qualquer leitura, venha de onde vier
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterEncodings() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    public static IGameSource Open(string path, SourceMode mode) => mode switch
    {
        SourceMode.ModPack => new ModPack(path),
        _ => new VanillaGameSource(path),
    };
}

/// <summary>Cópia limpa do jogo: uma pasta (CUSA.../dvdroot_ps4) ou um .zip com ela dentro.</summary>
public sealed class VanillaGameSource : IGameSource
{
    readonly string? _dvdRoot;
    readonly ZipArchive? _zip;
    readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public string Description { get; }
    public SourceMode Mode => SourceMode.Vanilla;

    public VanillaGameSource(string path)
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
        if (!Exists("map/mapstudio/m21_00_00_00.msb.dcx") || !Exists("param/gameparam/gameparam.parambnd.dcx"))
            throw new InvalidDataException($"'{path}' não parece ser o Bloodborne (mapas ou parâmetros não encontrados).");
    }

    string Native(string rel) => Path.Combine(_dvdRoot!, rel.Replace('/', Path.DirectorySeparatorChar));

    public bool Exists(string relative) => _zip != null ? _entries.ContainsKey(relative) : File.Exists(Native(relative));

    public byte[] ReadBytes(string relative)
    {
        if (_zip == null)
            return File.ReadAllBytes(Native(relative));
        using var stream = _entries[relative].Open();
        var buffer = new byte[_entries[relative].Length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    public IEnumerable<string> Files() => _zip != null
        ? _entries.Keys
        : Directory.EnumerateFiles(_dvdRoot!, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_dvdRoot!, f).Replace(Path.DirectorySeparatorChar, '/'));

    long Size(string relative) => _zip != null ? _entries[relative].Length : new FileInfo(Native(relative)).Length;

    /// <summary>O peso de um modelo é o tamanho do chr/cNNNN.chrbnd.dcx (igual à tabela Sizes.txt do mod).</summary>
    public Dictionary<string, long> ModelSizes()
    {
        var sizes = new Dictionary<string, long>();
        foreach (var rel in Files().Where(f => f.StartsWith("chr/", StringComparison.OrdinalIgnoreCase)
                                               && f.EndsWith(".chrbnd.dcx", StringComparison.OrdinalIgnoreCase)))
            sizes[Path.GetFileName(rel)[..^".chrbnd.dcx".Length]] = Size(rel);
        return sizes;
    }

    /// <summary>Extrai um arquivo para o disco (usado pelo Unrandomizer).</summary>
    public void CopyTo(string relative, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (_zip != null)
            _entries[relative].ExtractToFile(destination, overwrite: true);
        else
            File.Copy(Native(relative), destination, overwrite: true);
    }

    public void Dispose() => _zip?.Dispose();
}
