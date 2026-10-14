using System.Text;

namespace BbRando.Core;

/// <summary>
/// Pasta dvdroot_ps4 do mod original (somente leitura). Para cada arquivo, a cópia ".bak"
/// é a base distribuída pelo autor; o arquivo sem ".bak" pode já ter sido randomizado.
/// </summary>
public sealed class ModPack
{
    static ModPack() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public string Root { get; }

    public ModPack(string root)
    {
        Root = Path.GetFullPath(root);
        if (!File.Exists(Resolve("map/mapstudio/m21_00_00_00.msb.dcx")))
            throw new InvalidDataException(
                $"'{Root}' não parece ser a pasta dvdroot_ps4 do randomizer original (map/mapstudio/m21_00_00_00.msb.dcx não encontrado).");
        if (!File.Exists(Resolve("param/gameparam/gameparam.parambnd.dcx")))
            throw new InvalidDataException($"'{Root}' não contém param/gameparam/gameparam.parambnd.dcx.");
    }

    /// <summary>Caminho absoluto do arquivo-base para um caminho relativo (com "/").</summary>
    public string Resolve(string relative)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path + ".bak") ? path + ".bak" : path;
    }

    public bool Exists(string relative) => File.Exists(Resolve(relative));

    /// <summary>
    /// Arquivos que o mod precisa no jogo (relativos, com "/"), sem backups, logs e dados auxiliares.
    /// </summary>
    public IEnumerable<string> GameFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith("Mod Files/", StringComparison.OrdinalIgnoreCase))
                continue;
            var bakIndex = rel.IndexOf(".bak", StringComparison.OrdinalIgnoreCase);
            if (bakIndex >= 0)
                rel = rel[..bakIndex];
            if (seen.Add(rel))
                yield return rel;
        }
    }

    /// <summary>Arquivos de dados do mod em "Mod Files/NPC Scaling File".</summary>
    public string[] ReadDataFile(string name) =>
        File.ReadAllLines(Path.Combine(Root, "Mod Files", "NPC Scaling File", name));
}
