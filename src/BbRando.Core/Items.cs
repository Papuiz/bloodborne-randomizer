using SoulsFormats;

namespace BbRando.Core;

/// <summary>Lê o conteúdo dos lotes de itens (ItemLotParam) com nomes legíveis.</summary>
public sealed class ItemCatalog
{
    // Categorias do ItemLotParam no Bloodborne (verificadas contra os textos do jogo).
    const int CatWeapon = 0, CatArmor = 1, CatAccessory = 2, CatGoods = 4, CatGem = 8;
    const int GoodsTypeKey = 1;

    readonly Dictionary<long, PARAM.Row> _lots;
    readonly Dictionary<long, PARAM.Row> _goods;
    readonly GameText _text;

    public ItemCatalog(ParamBundle param, GameText text)
    {
        _lots = param.Index("ItemLotParam");
        _goods = param.Index("EquipParamGoods");
        _text = text;
    }

    public bool Exists(int lot) => _lots.ContainsKey(lot);

    public IEnumerable<(int Category, int Id, int Count)> Contents(int lot)
    {
        if (!_lots.TryGetValue(lot, out var row))
            yield break;
        for (var i = 1; i <= 8; i++)
        {
            var id = row.GetInt($"lotItemId0{i}");
            if (id > 0)
                yield return (row.GetInt($"lotItemCategory0{i}"), id, Math.Max(1, row.GetInt($"lotItemNum0{i}")));
        }
    }

    public string ItemName(int category, int id) => category switch
    {
        CatWeapon => _text.Weapon(id) ?? $"Arma {id}",
        CatArmor => _text.Armor(id) ?? $"Armadura {id}",
        CatAccessory => _text.Accessory(id) ?? _text.Goods(id) ?? $"Acessório {id}",
        CatGoods => _text.Goods(id) ?? $"Item {id}",
        CatGem => "Blood Gem aleatória",
        _ => $"Item {category}:{id}",
    };

    /// <summary>Texto do conteúdo de um lote, por exemplo "Blood Vial x2".</summary>
    public string Describe(int lot)
    {
        var parts = Contents(lot).Select(c => c.Count > 1 ? $"{ItemName(c.Category, c.Id)} x{c.Count}" : ItemName(c.Category, c.Id)).ToList();
        if (parts.Count == 0)
            return "(vazio)";
        // Lotes com vários itens costumam ser sorteios (só um sai); mostramos todos separados por "/".
        return string.Join(" / ", parts.Distinct());
    }

    /// <summary>Chaves, emblemas e insígnias: mover pode travar o progresso.</summary>
    public bool IsKeyLot(int lot) => Contents(lot).Any(c =>
        c.Category == CatGoods && _goods.TryGetValue(c.Id, out var g) && g.GetInt("goodsType") == GoodsTypeKey);
}

/// <summary>Onde um item foi parar, para o log.</summary>
public sealed record ItemPlacement(string Item, Location Where, string Kind, string Replaced);

public sealed class ItemRandomizer(ItemCatalog items, RandomizerSettings settings, GameText text)
{
    public List<ItemPlacement> Placements { get; } = new();
    public List<ItemPlacement> Drops { get; } = new();

    sealed record Slot(MapFile Map, MSBB.Event.Treasure Treasure, int Lot1, int Lot2, int Lot3);

    bool Movable(int lot)
    {
        if (lot <= 0 || !items.Exists(lot) || !items.Contents(lot).Any())
            return false;
        if (GameData.FixedItemLots.Contains(lot))
            return false;
        if (!settings.IncludeWorkshopItems && GameData.WorkshopItemLots.Contains(lot))
            return false;
        if (!settings.IncludeKeyItems && items.IsKeyLot(lot))
            return false;
        return true;
    }

    /// <summary>Embaralha o conteúdo dos tesouros (cadáveres e baús) entre os mapas.</summary>
    public void RandomizeTreasures(IEnumerable<MapFile> maps)
    {
        var slots = new List<Slot>();
        foreach (var map in maps.Where(m => m.Def.ItemTarget))
            foreach (var t in map.Msb.Events.Treasures)
                if (Movable(t.ItemLot1))
                    slots.Add(new Slot(map, t, t.ItemLot1, t.ItemLot2, t.ItemLot3));

        // Um mesmo lugar pode existir em duas versões do mapa (ex.: Yahar'gul antes/depois de um evento),
        // com o mesmo lote. Agrupamos pelo lote original para que todas as versões recebam o mesmo item.
        var rng = new Rng(settings.Seed, "items/treasures");
        var spots = slots.GroupBy(s => (s.Lot1, s.Lot2, s.Lot3)).ToList();
        var groups = settings.ItemsWithinArea
            ? spots.GroupBy(g => g.First().Map.Def.AreaId).Select(g => g.ToList())
            : [spots];
        foreach (var group in groups)
        {
            var contents = group.Select(g => g.Key).ToList();
            rng.Shuffle(contents);
            for (var i = 0; i < group.Count; i++)
            {
                foreach (var slot in group[i])
                {
                    (slot.Treasure.ItemLot1, slot.Treasure.ItemLot2, slot.Treasure.ItemLot3) = contents[i];
                    slot.Map.Dirty = true;
                }
            }
        }

        // O log é feito depois, com todos os tesouros já trocados (uma linha por lugar físico).
        var logged = new HashSet<string>();
        foreach (var slot in slots)
        {
            var where = slot.Map.DescribeTreasure(text, slot.Treasure);
            var kind = slot.Treasure.InChest ? "Baú" : "Cadáver/chão";
            var replaced = items.Describe(slot.Lot1);
            foreach (var lot in new[] { slot.Treasure.ItemLot1, slot.Treasure.ItemLot2, slot.Treasure.ItemLot3 })
            {
                if (lot <= 0 || !items.Contents(lot).Any())
                    continue;
                var key = $"{lot}|{where.Area}|{where.Position?.ToString() ?? slot.Map.Def.Id + slot.Treasure.Name}";
                if (logged.Add(key))
                    Placements.Add(new ItemPlacement(items.Describe(lot), where, kind, replaced));
            }
        }
    }

    /// <summary>Embaralha o primeiro drop dos inimigos (NpcParam.itemLotId_1).</summary>
    public void RandomizeDrops(ParamBundle param)
    {
        var rows = param["NpcParam"].Rows
            .Where(r => !GameData.FixedDropNpc.Contains((int)r.ID) && Movable(r.GetInt("itemLotId_1")))
            .ToList();
        var lots = rows.Select(r => r.GetInt("itemLotId_1")).ToList();
        var rng = new Rng(settings.Seed, "items/drops");
        rng.Shuffle(lots);
        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var before = rows[i].GetInt("itemLotId_1");
            rows[i].SetInt("itemLotId_1", lots[i]);
            if (text.Npc(rows[i].GetInt("nameId")) is not { } enemy)
                continue;
            var item = items.Describe(lots[i]);
            if (seen.Add((enemy, item)))
                Drops.Add(new ItemPlacement(item, new Location(enemy, null, $"NpcParam {rows[i].ID}", null), "Drop", items.Describe(before)));
        }
    }
}
