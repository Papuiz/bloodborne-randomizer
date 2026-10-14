using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BbRando.Core;
using SoulsFormats;

/// <summary>
/// Compara o pacote do mod original com o jogo limpo e grava relatórios do que os autores mudaram.
/// Ferramenta de desenvolvimento: o resultado orienta o que precisa ser reimplementado.
/// </summary>
static class ModAnalysis
{
    /// <summary>Nomes e tipos de argumento das instruções de evento (referencia/bb-emedf.json, do DarkScript3).</summary>
    static readonly Dictionary<(int Bank, int Id), (string Name, (string Name, int Type)[] Args)> Emedf = new();

    static void LoadEmedf(string path)
    {
        if (!File.Exists(path))
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var cls in doc.RootElement.GetProperty("main_classes").EnumerateArray())
            foreach (var ins in cls.GetProperty("instrs").EnumerateArray())
                Emedf[(cls.GetProperty("index").GetInt32(), ins.GetProperty("index").GetInt32())] = (
                    ins.GetProperty("name").GetString()!,
                    ins.GetProperty("args").EnumerateArray().Select(a => (a.GetProperty("name").GetString()!, a.GetProperty("type").GetInt32())).ToArray());
    }

    public static void Run(ModPack mod, VanillaGameSource vanilla, string outDir, string emedfPath)
    {
        LoadEmedf(emedfPath);
        Directory.CreateDirectory(outDir);
        var summary = new StringBuilder();
        var bnd = new StringBuilder();
        var events = new StringBuilder();
        var fmg = new StringBuilder();
        var msb = new StringBuilder();

        foreach (var rel in mod.Files().OrderBy(f => f, StringComparer.Ordinal))
        {
            var modBytes = mod.ReadBytes(rel);
            if (!vanilla.Exists(rel))
            {
                summary.AppendLine($"NOVO       {rel}");
                continue;
            }
            var vanBytes = vanilla.ReadBytes(rel);
            if (modBytes.AsSpan().SequenceEqual(vanBytes))
                continue;
            summary.AppendLine($"ALTERADO   {rel}");

            if (rel.EndsWith(".emevd.dcx"))
                DiffEvents(events, rel, EMEVD.Read(vanBytes), EMEVD.Read(modBytes));
            else if (rel.EndsWith(".msb.dcx"))
                DiffMsb(msb, rel, MSBB.Read(vanBytes), MSBB.Read(modBytes));
            else if (rel.EndsWith(".msgbnd.dcx"))
                DiffFmg(fmg, rel, ReadBinder(vanBytes), ReadBinder(modBytes));
            else if (!rel.EndsWith(".parambnd.dcx"))
                DiffBinder(bnd, rel, ReadBinder(vanBytes), ReadBinder(modBytes));
        }

        File.WriteAllText(Path.Combine(outDir, "resumo.txt"), summary.ToString());
        File.WriteAllText(Path.Combine(outDir, "eventos.txt"), events.ToString());
        File.WriteAllText(Path.Combine(outDir, "mapas.txt"), msb.ToString());
        File.WriteAllText(Path.Combine(outDir, "textos.txt"), fmg.ToString());
        File.WriteAllText(Path.Combine(outDir, "pacotes.txt"), bnd.ToString());
        File.WriteAllText(Path.Combine(outDir, "params.txt"), DiffParams(mod, vanilla));
    }

    static List<BinderFile> ReadBinder(byte[] bytes)
    {
        try
        {
            return BND4.Read(bytes).Files;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or EndOfStreamException)
        {
            return BND3.Read(bytes).Files;
        }
    }

    static string Short(string name) => ParamBundle.BinderName(name) + Path.GetExtension(name.Replace('\\', '/'));

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..12];

    // ---------------- Pacotes (luabnd, ffxbnd, chrbnd...) ----------------

    static void DiffBinder(StringBuilder sb, string rel, List<BinderFile> van, List<BinderFile> mod)
    {
        string Key(BinderFile f) => $"{f.ID}:{Short(f.Name)}";
        var v = van.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var m = mod.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var added = m.Keys.Except(v.Keys).ToList();
        var removed = v.Keys.Except(m.Keys).ToList();
        var changed = m.Keys.Intersect(v.Keys).Where(k => !m[k].Bytes.AsSpan().SequenceEqual(v[k].Bytes)).ToList();
        sb.AppendLine($"=== {rel}: original {van.Count} arquivos, mod {mod.Count}; +{added.Count} -{removed.Count} ~{changed.Count}");
        foreach (var k in added.Take(400))
            sb.AppendLine($"  + {k} ({m[k].Bytes.Length} bytes)");
        if (added.Count > 400)
            sb.AppendLine($"  ... +{added.Count - 400} adicionados");
        foreach (var k in removed)
            sb.AppendLine($"  - {k}");
        foreach (var k in changed)
            sb.AppendLine($"  ~ {k} ({v[k].Bytes.Length} → {m[k].Bytes.Length} bytes)");
    }

    // ---------------- Textos ----------------

    static void DiffFmg(StringBuilder sb, string rel, List<BinderFile> van, List<BinderFile> mod)
    {
        sb.AppendLine($"=== {rel}");
        foreach (var mf in mod)
        {
            var vf = van.FirstOrDefault(f => f.ID == mf.ID);
            var me = FMG.Read(mf.Bytes).Entries.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First().Text);
            var ve = vf == null ? new() : FMG.Read(vf.Bytes).Entries.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First().Text);
            foreach (var (id, text) in me)
            {
                ve.TryGetValue(id, out var old);
                if (old != text)
                    sb.AppendLine($"  [{mf.ID}] {id}: \"{Flat(old)}\" → \"{Flat(text)}\"");
            }
        }

        static string Flat(string? s) => s == null ? "(nada)" : s.Replace("\n", "\\n");
    }

    // ---------------- Mapas ----------------

    static void DiffMsb(StringBuilder sb, string rel, MSBB van, MSBB mod)
    {
        sb.AppendLine($"=== {rel}");
        void Compare<T>(string kind, IEnumerable<T> vs, IEnumerable<T> ms, Func<T, string> key, Func<T, string> describe)
        {
            var v = vs.GroupBy(key).ToDictionary(g => g.Key, g => describe(g.First()));
            var m = ms.GroupBy(key).ToDictionary(g => g.Key, g => describe(g.First()));
            foreach (var k in m.Keys.Except(v.Keys))
                sb.AppendLine($"  + {kind} {m[k]}");
            foreach (var k in v.Keys.Except(m.Keys))
                sb.AppendLine($"  - {kind} {v[k]}");
            foreach (var k in m.Keys.Intersect(v.Keys).Where(k => m[k] != v[k]))
                sb.AppendLine($"  ~ {kind} {v[k]}\n      → {m[k]}");
        }
        Compare("inimigo", van.Parts.Enemies, mod.Parts.Enemies, e => e.Name,
            e => $"{e.Name} modelo={e.ModelName} npc={e.NPCParamID} ia={e.ThinkParamID} ent={e.EntityID} pos={e.Position}");
        Compare("objeto", van.Parts.Objects, mod.Parts.Objects, o => o.Name, o => $"{o.Name} modelo={o.ModelName} ent={o.EntityID} pos={o.Position}");
        Compare("tesouro", van.Events.Treasures, mod.Events.Treasures, t => t.Name, t => $"{t.Name} lote={t.ItemLot1}");
        Compare("modelo", van.Models.GetEntries(), mod.Models.GetEntries(), m => m.Name, m => m.Name);
        Compare("região", van.Regions.GetEntries(), mod.Regions.GetEntries(), r => r.Name, r => $"{r.Name} pos={r.Position}");
    }

    // ---------------- Eventos ----------------

    static string Instr(EMEVD.Instruction i)
    {
        if (!Emedf.TryGetValue((i.Bank, i.ID), out var def))
            return $"{i.Bank}[{i.ID:00}]({Convert.ToHexString(i.ArgData)})";
        // Tipos do EMEDF: 0 u8, 1 u16, 2 u32, 3 s8, 4 s16, 5 s32, 6 f32 (alinhados ao próprio tamanho).
        var values = new List<string>();
        var offset = 0;
        foreach (var (name, type) in def.Args)
        {
            var size = type switch { 0 or 3 => 1, 1 or 4 => 2, _ => 4 };
            offset = (offset + size - 1) / size * size;
            if (offset + size > i.ArgData.Length)
                break;
            var span = i.ArgData.AsSpan(offset, size);
            values.Add(name + "=" + (type switch
            {
                0 => span[0].ToString(),
                3 => ((sbyte)span[0]).ToString(),
                1 => BitConverter.ToUInt16(span).ToString(),
                4 => BitConverter.ToInt16(span).ToString(),
                2 => BitConverter.ToUInt32(span).ToString(),
                6 => BitConverter.ToSingle(span).ToString(CultureInfo.InvariantCulture),
                _ => BitConverter.ToInt32(span).ToString(),
            }));
            offset += size;
        }
        return $"{def.Name}({string.Join(", ", values)})";
    }

    static void DiffEvents(StringBuilder sb, string rel, EMEVD van, EMEVD mod)
    {
        var v = van.Events.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First());
        var m = mod.Events.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First());
        sb.AppendLine($"=== {rel}: eventos {van.Events.Count} → {mod.Events.Count}");
        foreach (var id in m.Keys.Except(v.Keys))
        {
            sb.AppendLine($"  + evento {id} ({m[id].Instructions.Count} instruções)");
            foreach (var i in m[id].Instructions)
                sb.AppendLine($"      + {Instr(i)}");
        }
        foreach (var id in v.Keys.Except(m.Keys))
            sb.AppendLine($"  - evento {id}");
        foreach (var id in m.Keys.Intersect(v.Keys))
        {
            var a = v[id].Instructions.Select(Instr).ToList();
            var b = m[id].Instructions.Select(Instr).ToList();
            if (a.SequenceEqual(b))
                continue;
            sb.AppendLine($"  ~ evento {id}");
            foreach (var line in LineDiff(a, b))
                sb.AppendLine("      " + line);
        }
    }

    /// <summary>Diferença por LCS entre duas listas de linhas ("+ " / "- ").</summary>
    static IEnumerable<string> LineDiff(List<string> a, List<string> b)
    {
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        int x = 0, y = 0;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && a[x] == b[y])
            {
                x++;
                y++;
            }
            else if (y < b.Count && (x == a.Count || lcs[x, y + 1] >= lcs[x + 1, y]))
                yield return "+ " + b[y++];
            else
                yield return "- " + a[x++];
        }
    }

    // ---------------- Parâmetros ----------------

    static string DiffParams(ModPack mod, VanillaGameSource vanilla)
    {
        var sb = new StringBuilder();
        var van = new ParamBundle(vanilla);
        var mp = new ParamBundle(mod);
        foreach (var name in mp.Names.OrderBy(n => n))
        {
            if (!van.Names.Contains(name))
            {
                sb.AppendLine($"=== {name}: param novo");
                continue;
            }
            var v = van.Index(name);
            var m = mp.Index(name);
            var added = m.Keys.Except(v.Keys).OrderBy(k => k).ToList();
            var removed = v.Keys.Except(m.Keys).ToList();
            var changed = m.Keys.Intersect(v.Keys).OrderBy(k => k)
                .Select(id => (id, fields: ChangedFields(v[id], m[id]))).Where(x => x.fields.Count > 0).ToList();
            if (added.Count + removed.Count + changed.Count == 0)
                continue;
            sb.AppendLine($"=== {name}: +{added.Count} linhas, -{removed.Count}, {changed.Count} alteradas");
            foreach (var range in Ranges(added))
                sb.AppendLine($"  + IDs {range}");
            foreach (var id in removed.Take(50))
                sb.AppendLine($"  - {id}");
            foreach (var (id, fields) in changed.Take(300))
                sb.AppendLine($"  ~ {id} {v[id].Name}: {string.Join(", ", fields)}");
            if (changed.Count > 300)
                sb.AppendLine($"  ... mais {changed.Count - 300} linhas alteradas");
            if (name == "NpcParam")
                DescribeScalingVariants(sb, mod, m);
            else if (added.Count > 0)
                foreach (var id in added.Take(20))
                    sb.AppendLine($"    nova {id} {m[id].Name}: {string.Join(" ", m[id].Cells.Take(12).Select(c => $"{c.Def.InternalName}={c.Value}"))}");
        }
        return sb.ToString();
    }

    static List<string> ChangedFields(PARAM.Row a, PARAM.Row b)
    {
        var result = new List<string>();
        foreach (var cell in b.Cells)
        {
            var old = a[cell.Def.InternalName].Value;
            // Editores de parâmetro regravam floats com arredondamento (0.4 → 0.39999405): não é mudança.
            if (old is float f1 && cell.Value is float f2 && Math.Abs(f1 - f2) < 1e-4)
                continue;
            if (!Equals(Format(old), Format(cell.Value)))
                result.Add($"{cell.Def.InternalName} {Format(old)}→{Format(cell.Value)}");
        }
        return result;
    }

    static string Format(object value) => value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    static IEnumerable<string> Ranges(List<long> ids)
    {
        for (var i = 0; i < ids.Count;)
        {
            var j = i;
            while (j + 1 < ids.Count && ids[j + 1] == ids[j] + 1)
                j++;
            yield return i == j ? $"{ids[i]}" : $"{ids[i]}–{ids[j]} ({j - i + 1})";
            i = j + 1;
        }
    }

    /// <summary>
    /// As linhas 900000000+ são variantes de força: no NPCScalingFile cada ID-base vem seguido de 30 variantes.
    /// Mostra quais campos mudam em relação à base, por nível.
    /// </summary>
    static void DescribeScalingVariants(StringBuilder sb, ModPack mod, Dictionary<long, PARAM.Row> rows)
    {
        var lines = mod.ReadDataFile("NPCScalingFile.txt").Select(l => l.Trim()).Where(l => l.Length > 0).Select(long.Parse).ToList();
        var byLevelField = new SortedDictionary<int, Dictionary<string, Dictionary<string, int>>>();
        long? baseId = null;
        var level = 0;
        foreach (var id in lines)
        {
            if (id < 900_000_000)
            {
                baseId = id;
                level = 0;
                continue;
            }
            if (baseId is not { } b || !rows.TryGetValue(b, out var baseRow) || !rows.TryGetValue(id, out var variant))
                continue;
            if (!byLevelField.TryGetValue(level, out var fields))
                byLevelField[level] = fields = new();
            foreach (var change in ChangedFields(baseRow, variant))
            {
                var field = change[..change.IndexOf(' ')];
                var to = change[(change.IndexOf('→') + 1)..];
                if (!fields.TryGetValue(field, out var values))
                    fields[field] = values = new();
                values[to] = values.GetValueOrDefault(to) + 1;
            }
            level++;
        }
        sb.AppendLine("  Variantes de escalonamento (nível → campo: valores mais comuns):");
        foreach (var (lvl, fields) in byLevelField)
            sb.AppendLine($"    nível {lvl}: " + string.Join("; ", fields.Select(f =>
                $"{f.Key}=" + string.Join("/", f.Value.OrderByDescending(v => v.Value).Take(3).Select(v => $"{v.Key}(×{v.Value})")))));
    }

    // ---------------- Exportação dos patches (Data/modpatches.json) ----------------

    /// <summary>Textos ligados ao mod extra "Great One Beast" (não fazem parte do randomizer).</summary>
    static readonly HashSet<int> GreatOneBeastTexts = [6504, 504000, 504700, 504701];

    /// <summary>
    /// Gera os patches que o modo "jogo limpo" aplica. Regras (resultado do diff, ver CLAUDE.md):
    /// eventos alterados/novos entram inteiros; mapas: remoções e reposicionamentos; NpcThinkParam e
    /// SpEffectParam: linhas novas; NpcParam: campos alterados nas linhas originais, exceto spEffectID6
    /// (o escalonamento é nosso) e ruído de float; scripts de IA novos ou editados; textos fora do Great One Beast.
    /// Ficam de fora: SpEffect 2010 (buff da lanterna), ItemLotParam (recompensa de Loran) e as linhas 900000000+.
    /// </summary>
    public static ModPatchSet ExportPatches(ModPack mod, VanillaGameSource vanilla)
    {
        var set = new ModPatchSet();
        var vanillaLua = new Dictionary<string, HashSet<string>>();
        foreach (var rel in vanilla.Files().Where(f => f.StartsWith("script/") && f.EndsWith(".luabnd.dcx")))
            foreach (var f in ReadBinder(vanilla.ReadBytes(rel)))
            {
                var name = Short(f.Name);
                if (!vanillaLua.TryGetValue(name, out var hashes))
                    vanillaLua[name] = hashes = new();
                hashes.Add(Hash(f.Bytes));
            }

        foreach (var rel in mod.Files().OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!vanilla.Exists(rel))
                continue;
            var modBytes = mod.ReadBytes(rel);
            var vanBytes = vanilla.ReadBytes(rel);
            if (modBytes.AsSpan().SequenceEqual(vanBytes))
                continue;

            if (rel.EndsWith(".emevd.dcx"))
            {
                var v = EMEVD.Read(vanBytes).Events.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First());
                var changed = EMEVD.Read(modBytes).Events
                    .Where(e => !v.TryGetValue(e.ID, out var old) || EventKey(old) != EventKey(e))
                    .Select(EventPatch.From)
                    .ToList();
                if (changed.Count > 0)
                    set.Events[rel] = changed;
            }
            else if (rel.EndsWith(".msb.dcx"))
            {
                var v = MSBB.Read(vanBytes);
                var m = MSBB.Read(modBytes);
                var modEnemies = m.Parts.Enemies.ToDictionary(e => e.Name);
                var patch = new MapPatch(
                    v.Parts.Enemies.Where(e => !modEnemies.ContainsKey(e.Name)).Select(e => e.Name).ToList(),
                    v.Models.GetEntries().Select(x => x.Name).Except(m.Models.GetEntries().Select(x => x.Name)).ToList(),
                    v.Parts.Enemies
                        .Where(e => modEnemies.TryGetValue(e.Name, out var me) && me.Position != e.Position)
                        .ToDictionary(e => e.Name, e => new[] { modEnemies[e.Name].Position.X, modEnemies[e.Name].Position.Y, modEnemies[e.Name].Position.Z }));
                set.Maps[rel] = patch;
            }
            else if (rel.EndsWith(".msgbnd.dcx"))
            {
                var van = ReadBinder(vanBytes);
                foreach (var mf in ReadBinder(modBytes))
                {
                    var vf = van.FirstOrDefault(f => f.ID == mf.ID);
                    var ve = vf == null ? new() : FMG.Read(vf.Bytes).Entries.GroupBy(e => e.ID).ToDictionary(g => g.Key, g => g.First().Text);
                    foreach (var entry in FMG.Read(mf.Bytes).Entries)
                        if (!GreatOneBeastTexts.Contains(entry.ID) && (!ve.TryGetValue(entry.ID, out var old) || old != entry.Text) && entry.Text != null)
                            set.Texts.Add(new TextPatch(rel, mf.ID, entry.ID, entry.Text));
                }
            }
            else if (rel.EndsWith(".luabnd.dcx"))
            {
                foreach (var f in ReadBinder(modBytes))
                {
                    var name = Short(f.Name);
                    if (name.EndsWith(".luainfo") || name.EndsWith(".luagnl"))
                        continue; // índices: o programa gera a união
                    if (vanillaLua.TryGetValue(name, out var hashes) && hashes.Contains(Hash(f.Bytes)))
                        continue;
                    if (!set.LuaFiles.TryGetValue(rel, out var files))
                        set.LuaFiles[rel] = files = new();
                    var hash = Hash(f.Bytes);
                    files[name] = hash;
                    set.Blobs[hash] = Convert.ToBase64String(f.Bytes);
                }
            }
        }

        var vp = new ParamBundle(vanilla);
        var mp = new ParamBundle(mod);
        foreach (var name in new[] { "NpcThinkParam", "SpEffectParam" })
        {
            var v = vp.Index(name);
            foreach (var (id, row) in mp.Index(name).Where(r => !v.ContainsKey(r.Key)))
                set.ParamRows.Add(new ParamRowPatch(name, id, row.Name ?? "",
                    row.Cells.ToDictionary(c => c.Def.InternalName, c => Format(c.Value))));
        }
        foreach (var name in new[] { "NpcThinkParam", "NpcParam" })
        {
            var v = vp.Index(name);
            foreach (var (id, row) in mp.Index(name).Where(r => r.Key < 900_000_000 && v.ContainsKey(r.Key)))
                foreach (var change in ChangedFields(v[id], row))
                {
                    var field = change[..change.IndexOf(' ')];
                    if (name == "NpcParam" && field == "spEffectID6")
                        continue;
                    set.ParamFields.Add(new ParamFieldPatch(name, id, field, Format(row[field].Value)));
                }
        }
        return set;
    }

    static string EventKey(EMEVD.Event e) =>
        string.Join(";", e.Instructions.Select(i => $"{i.Bank}:{i.ID}:{i.Layer}:{Convert.ToHexString(i.ArgData)}"))
        + "|" + string.Join(";", e.Parameters.Select(p => $"{p.InstructionIndex}:{p.TargetStartByte}:{p.SourceStartByte}:{p.ByteCount}"))
        + "|" + (int)e.RestBehavior;
}
