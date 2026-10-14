using SoulsFormats;

namespace BbRando.Core;

/// <summary>gameparam.parambnd.dcx carregado com os paramdefs, pronto para ler e gravar.</summary>
public sealed class ParamBundle
{
    readonly BND4 _bnd;
    readonly Dictionary<string, (BinderFile File, PARAM Param)> _params = new(StringComparer.OrdinalIgnoreCase);

    public ParamBundle(ModPack pack)
    {
        var defs = BND4.Read(pack.Resolve("paramdef/paramdef.paramdefbnd.dcx")).Files
            .Select(f => PARAMDEF.Read(f.Bytes))
            .ToList();
        _bnd = BND4.Read(pack.Resolve("param/gameparam/gameparam.parambnd.dcx"));
        foreach (var file in _bnd.Files)
        {
            var param = PARAM.Read(file.Bytes);
            var def = defs.Find(d => d.ParamType == param.ParamType);
            if (def == null)
                continue;
            param.ApplyParamdef(def);
            _params[BinderName(file.Name)] = (file, param);
        }
    }

    /// <summary>Nome sem pasta nem extensão. Os nomes internos usam "\", que o Path do Linux não separa.</summary>
    public static string BinderName(string name)
    {
        var slash = name.LastIndexOfAny(['\\', '/']);
        var baseName = slash >= 0 ? name[(slash + 1)..] : name;
        var dot = baseName.IndexOf('.');
        return dot >= 0 ? baseName[..dot] : baseName;
    }

    public PARAM this[string name] => _params[name].Param;

    /// <summary>Linhas por ID. Alguns params têm IDs repetidos; vale a primeira, como no jogo.</summary>
    public Dictionary<long, PARAM.Row> Index(string name)
    {
        var index = new Dictionary<long, PARAM.Row>();
        foreach (var row in this[name].Rows)
            index.TryAdd(row.ID, row);
        return index;
    }

    /// <summary>
    /// Copia uma linha com um ID novo e livre (contando para baixo a partir de <paramref name="startId"/>).
    /// A linha é inserida na ordem dos IDs, porque o jogo procura as linhas por busca binária.
    /// </summary>
    public PARAM.Row CloneRow(string name, PARAM.Row source, long startId)
    {
        var param = this[name];
        var used = param.Rows.Select(r => r.ID).ToHashSet();
        var id = startId;
        while (used.Contains(id))
            id--;
        var row = new PARAM.Row(source) { ID = id };
        var index = param.Rows.FindIndex(r => r.ID > id);
        param.Rows.Insert(index < 0 ? param.Rows.Count : index, row);
        return row;
    }

    public void Write(string path)
    {
        foreach (var (file, param) in _params.Values)
            file.Bytes = param.Write();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _bnd.Write(path);
    }
}

public static class ParamRowExtensions
{
    public static int GetInt(this PARAM.Row row, string field) => Convert.ToInt32(row[field].Value);

    public static void SetInt(this PARAM.Row row, string field, int value)
    {
        var cell = row[field];
        cell.Value = Convert.ChangeType(value, cell.Value.GetType());
    }
}
