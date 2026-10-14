using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SoulsFormats;

namespace BbRando.Core;

/// <summary>
/// Ajustes do mod original (Sepukkake e Smertin123) que o randomizer precisa, guardados como dados e
/// aplicados sobre o jogo limpo. Gerados pelo comando de desenvolvimento <c>export-patches</c>
/// (veja CLAUDE.md); o arquivo fica embutido em Data/modpatches.json.
/// </summary>
public sealed class ModPatchSet
{
    /// <summary>Arquivo .emevd → eventos que substituem (ou se somam a) os do jogo.</summary>
    public Dictionary<string, List<EventPatch>> Events { get; set; } = new();
    public Dictionary<string, MapPatch> Maps { get; set; } = new();
    public List<ParamRowPatch> ParamRows { get; set; } = new();
    public List<ParamFieldPatch> ParamFields { get; set; } = new();
    /// <summary>Pacote luabnd → arquivo interno → hash do conteúdo em <see cref="Blobs"/>.</summary>
    public Dictionary<string, Dictionary<string, string>> LuaFiles { get; set; } = new();
    /// <summary>Conteúdos binários (base64) por hash, para não repetir o mesmo script em cada mapa.</summary>
    public Dictionary<string, string> Blobs { get; set; } = new();

    public byte[] Blob(string hash) => Convert.FromBase64String(Blobs[hash]);
    public List<TextPatch> Texts { get; set; } = new();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Os patches embutidos no programa.</summary>
    public static ModPatchSet Embedded()
    {
        using var stream = typeof(ModPatchSet).Assembly.GetManifestResourceStream("BbRando.Core.Data.modpatches.json");
        return stream == null ? new ModPatchSet() : JsonSerializer.Deserialize<ModPatchSet>(stream) ?? new ModPatchSet();
    }

    // ---------------- Aplicação ----------------

    public void ApplyParams(ParamBundle param)
    {
        foreach (var row in ParamRows)
        {
            var target = param.AddRow(row.Param, row.Id, row.Name);
            foreach (var (field, value) in row.Fields)
                SetCell(target, field, value);
        }
        foreach (var patch in ParamFields)
            if (param.Index(patch.Param).TryGetValue(patch.Id, out var row))
                SetCell(row, patch.Field, patch.Value);
    }

    static void SetCell(PARAM.Row row, string field, string value)
    {
        var cell = row[field];
        cell.Value = cell.Value switch
        {
            byte[] => Convert.FromHexString(value),
            _ => Convert.ChangeType(value, cell.Value.GetType(), CultureInfo.InvariantCulture),
        };
    }

    public void ApplyMaps(IReadOnlyDictionary<string, MapFile> maps)
    {
        foreach (var map in maps.Values)
        {
            if (!Maps.TryGetValue(map.Def.RelativePath, out var patch))
                continue;
            map.Msb.Parts.Enemies.RemoveAll(e => patch.RemoveEnemies.Contains(e.Name));
            var models = map.Msb.Models;
            models.MapPieces.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Objects.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Enemies.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Items.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Players.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Collisions.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            models.Navmeshes.RemoveAll(m => patch.RemoveModels.Contains(m.Name));
            foreach (var e in map.Msb.Parts.Enemies)
                if (patch.MoveEnemies.TryGetValue(e.Name, out var p))
                    e.Position = new Vector3(p[0], p[1], p[2]);
            map.Dirty = true;
        }
    }

    /// <summary>Grava os .emevd com os eventos ajustados.</summary>
    public void WriteEvents(IGameSource source, string outputDvdRoot)
    {
        foreach (var (rel, patches) in Events)
        {
            if (!source.Exists(rel))
                continue;
            var emevd = EMEVD.Read(source.ReadBytes(rel));
            foreach (var patch in patches)
            {
                var ev = patch.ToEvent();
                var index = emevd.Events.FindIndex(e => e.ID == ev.ID);
                if (index >= 0)
                    emevd.Events[index] = ev;
                else
                    emevd.Events.Add(ev);
            }
            var path = Path.Combine(outputDvdRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            emevd.Write(path);
        }
    }

    /// <summary>Grava os textos ajustados (msgbnd).</summary>
    public void WriteTexts(IGameSource source, string outputDvdRoot)
    {
        foreach (var group in Texts.GroupBy(t => t.File))
        {
            if (!source.Exists(group.Key))
                continue;
            var bnd = BND4.Read(source.ReadBytes(group.Key));
            foreach (var file in bnd.Files)
            {
                var edits = group.Where(t => t.Fmg == file.ID).ToList();
                if (edits.Count == 0)
                    continue;
                var fmg = FMG.Read(file.Bytes);
                foreach (var edit in edits)
                {
                    var entry = fmg.Entries.FirstOrDefault(e => e.ID == edit.Id);
                    if (entry != null)
                        entry.Text = edit.Text;
                    else
                        fmg.Entries.Add(new FMG.Entry(edit.Id, edit.Text));
                }
                fmg.Entries.Sort((a, b) => a.ID.CompareTo(b.ID));
                file.Bytes = fmg.Write();
            }
            var path = Path.Combine(outputDvdRoot, group.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bnd.Write(path);
        }
    }
}

public sealed record EventPatch(long Id, int RestBehavior, List<InstructionData> Instructions, List<ParameterData> Parameters)
{
    public static EventPatch From(EMEVD.Event e) => new(
        e.ID,
        (int)e.RestBehavior,
        e.Instructions.Select(i => new InstructionData(i.Bank, i.ID, Convert.ToBase64String(i.ArgData), i.Layer)).ToList(),
        e.Parameters.Select(p => new ParameterData(p.InstructionIndex, p.TargetStartByte, p.SourceStartByte, p.ByteCount, p.UnkID)).ToList());

    public EMEVD.Event ToEvent()
    {
        var e = new EMEVD.Event(Id, (EMEVD.Event.RestBehaviorType)RestBehavior);
        foreach (var i in Instructions)
            e.Instructions.Add(new EMEVD.Instruction(i.Bank, i.Id, Convert.FromBase64String(i.Args)) { Layer = i.Layer });
        foreach (var p in Parameters)
            e.Parameters.Add(new EMEVD.Parameter(p.InstructionIndex, p.TargetStartByte, p.SourceStartByte, p.ByteCount) { UnkID = p.UnkId });
        return e;
    }
}

public sealed record InstructionData(int Bank, int Id, string Args, uint? Layer);

public sealed record ParameterData(long InstructionIndex, long TargetStartByte, long SourceStartByte, int ByteCount, int UnkId);

public sealed record MapPatch(List<string> RemoveEnemies, List<string> RemoveModels, Dictionary<string, float[]> MoveEnemies);

/// <summary>Linha nova de parâmetro: só os campos diferentes do padrão.</summary>
public sealed record ParamRowPatch(string Param, long Id, string Name, Dictionary<string, string> Fields);

public sealed record ParamFieldPatch(string Param, long Id, string Field, string Value);

public sealed record TextPatch(string File, int Fmg, int Id, string Text);
