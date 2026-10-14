using SoulsFormats;

namespace BbRando.Core;

/// <summary>
/// Os efeitos visuais de cada inimigo ficam no pacote de efeitos da área dele
/// (sfx/frpg_sfxbnd_mXX.ffxbnd.dcx). Para um inimigo levado a outra área continuar com os efeitos,
/// o pacote comum (carregado em todo lugar) recebe dos pacotes de área só o que é de personagem
/// (como no mod original): efeitos .fxr de ID baixo (&lt; 10000) ou de personagem (600000–699999) e as
/// texturas/modelos. Os efeitos de cenário de cada mapa ficam de fora, para não pesar na memória.
/// </summary>
public static class SfxMerge
{
    const string Common = "sfx/frpg_sfxbnd_commoneffects.ffxbnd.dcx";

    static string FileName(string name) => name[(name.LastIndexOfAny(['\\', '/']) + 1)..];

    static bool IsCharacterResource(string name)
    {
        if (!name.EndsWith(".fxr", StringComparison.OrdinalIgnoreCase))
            return true;
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var id) && (id < 10_000 || id is >= 600_000 and < 700_000);
    }

    public static void Write(IGameSource source, string outputDvdRoot)
    {
        var common = BND4.Read(source.ReadBytes(Common));
        var names = common.Files.Select(f => FileName(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextId = common.Files.Max(f => f.ID) + 1;

        var areaBundles = source.Files()
            .Where(f => f.StartsWith("sfx/frpg_sfxbnd_m", StringComparison.OrdinalIgnoreCase)
                        && f.EndsWith(".ffxbnd.dcx", StringComparison.OrdinalIgnoreCase))
            .Append("sfx/frpg_sfxbnd_dlceffects.ffxbnd.dcx")
            .Where(source.Exists)
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var rel in areaBundles)
        {
            var bytes = source.ReadBytes(rel);
            if (bytes.Length < 1024)
                continue; // pacotes vazios (só cabeçalho)
            foreach (var file in BND4.Read(bytes).Files)
            {
                var name = FileName(file.Name);
                if (!IsCharacterResource(name) || !names.Add(name))
                    continue;
                common.Files.Add(new BinderFile(file.Flags, nextId++, file.Name, file.Bytes));
            }
        }

        var path = Path.Combine(outputDvdRoot, Common.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        common.Write(path);
    }
}
