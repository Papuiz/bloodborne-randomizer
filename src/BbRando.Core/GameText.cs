using SoulsFormats;

namespace BbRando.Core;

/// <summary>Textos do jogo (nomes de itens, NPCs e lugares) lidos dos arquivos .fmg.</summary>
public sealed class GameText
{
    // IDs dos arquivos FMG dentro de item.msgbnd / menu.msgbnd do Bloodborne.
    const int GoodsNames = 10, WeaponNames = 11, ArmorNames = 12, AccessoryNames = 13, NpcNames = 18, PlaceNames = 19;
    const int GemNames = 31;

    readonly Dictionary<int, Dictionary<int, string>> _fmgs = new();

    public GameText(IGameSource pack)
    {
        foreach (var lang in new[] { "engus", "enggb" })
        {
            foreach (var bndName in new[] { "item.msgbnd.dcx", "menu.msgbnd.dcx" })
            {
                var rel = $"msg/{lang}/{bndName}";
                if (!pack.Exists(rel))
                    continue;
                foreach (var file in BND4.Read(pack.ReadBytes(rel)).Files)
                {
                    if (_fmgs.ContainsKey(file.ID))
                        continue;
                    var entries = new Dictionary<int, string>();
                    foreach (var entry in FMG.Read(file.Bytes).Entries)
                        if (!string.IsNullOrWhiteSpace(entry.Text))
                            entries[entry.ID] = entry.Text.Trim();
                    _fmgs[file.ID] = entries;
                }
            }
        }
    }

    string? Lookup(int fmg, int id) =>
        _fmgs.TryGetValue(fmg, out var entries) && entries.TryGetValue(id, out var text) && !IsPlaceholder(text) ? text : null;

    static bool IsPlaceholder(string text) => text == "*" || text.StartsWith("en", StringComparison.Ordinal) && text.Length < 8 || text == "NOT NEEDED";

    public string? Goods(int id) => Lookup(GoodsNames, id);
    public string? Weapon(int id) => Lookup(WeaponNames, id);
    public string? Armor(int id) => Lookup(ArmorNames, id);
    public string? Accessory(int id) => Lookup(AccessoryNames, id);
    public string? Gem(int id) => Lookup(GemNames, id);
    public string? Npc(int nameId) => Lookup(NpcNames, nameId);
    public string? Place(int id) => Lookup(PlaceNames, id);
}
