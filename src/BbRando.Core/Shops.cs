using SoulsFormats;

namespace BbRando.Core;

/// <summary>Uma troca numa loja, para o log.</summary>
public sealed record ShopChange(string Shop, string Item, string Replaced, int Price, string Currency);

/// <summary>Arma inicial nova, para o log.</summary>
public sealed record StartingWeapon(string Slot, string Weapon, string Replaced, string? Requirements);

/// <summary>
/// Lojas (ShopLineupParam) e armas iniciais (as linhas grátis 2000–2011 dos Mensageiros).
/// Cada loja é uma centena de milhar do ID (1xxxxx, 2xxxxx...) e se repete em faixas de preço
/// (dezena de milhar): os itens são os mesmos em todas as faixas, só o preço muda.
/// </summary>
public sealed class ShopRandomizer(ParamBundle param, RandomizerSettings settings, GameText text)
{
    const int TypeWeapon = 0, TypeArmor = 1, TypeGoods = 3;
    const int ShopTypeInsight = 5;
    const int GoodsTypeKey = 1;
    static readonly string[] Requirements = ["properStrength", "properAgility", "properMagic", "properFaith"];

    public List<ShopChange> Changes { get; } = new();
    public List<StartingWeapon> StartingWeapons { get; } = new();

    /// <summary>O que vai junto com o item quando ele muda de lugar. O lugar fica com o ID e o desbloqueio (qwcId).</summary>
    sealed record Offer(int EquipId, int Value, int MtrlId, int EventFlag, int SellQuantity);

    static Offer OfferOf(PARAM.Row r) =>
        new(r.GetInt("equipId"), r.GetInt("value"), r.GetInt("mtrlId"), r.GetInt("eventFlag"), r.GetInt("sellQuantity"));

    static void SetOffer(PARAM.Row r, Offer o)
    {
        r.SetInt("equipId", o.EquipId);
        r.SetInt("value", o.Value);
        r.SetInt("mtrlId", o.MtrlId);
        r.SetInt("eventFlag", o.EventFlag);
        r.SetInt("sellQuantity", o.SellQuantity);
    }

    static int Series(long id) => (int)(id / 100000);
    static int Tier(long id) => (int)(id / 10000 % 10);
    static int Offset(long id) => (int)(id % 10000);

    string ItemName(int type, int id) => type switch
    {
        TypeWeapon => text.Weapon(id) ?? $"Arma {id}",
        TypeArmor => text.Armor(id) ?? $"Armadura {id}",
        TypeGoods => text.Goods(id) ?? $"Item {id}",
        _ => $"Item {type}:{id}",
    };

    bool Fixed(PARAM.Row row, IReadOnlyDictionary<long, PARAM.Row> goods)
    {
        var type = row.GetInt("equipType");
        var id = row.GetInt("equipId");
        if (type == TypeGoods)
            return GameData.FixedShopGoods.Contains(id) || goods.TryGetValue(id, out var g) && g.GetInt("goodsType") == GoodsTypeKey;
        if (type == TypeWeapon)
            return settings.KeepStartingGuns && GameData.StartingGuns.Contains(id);
        return false;
    }

    /// <summary>
    /// Embaralha os itens dentro de cada loja (arma com arma, armadura com armadura, item com item). O preço,
    /// o limite de compra e a marca de "já comprado" acompanham o item; o desbloqueio (insígnia) fica no lugar.
    /// </summary>
    public void RandomizeLineup()
    {
        var rows = param["ShopLineupParam"].Rows;
        var goods = param.Index("EquipParamGoods");
        var rng = new Rng(settings.Seed, "shops/lineup");
        foreach (var (series, shopName) in GameData.Shops)
        {
            var shopRows = rows.Where(r => Series(r.ID) == series).ToList();
            if (shopRows.Count == 0)
                continue;
            var tiers = shopRows.GroupBy(r => Tier(r.ID)).OrderBy(g => g.Key)
                .Select(g => g.ToDictionary(r => Offset(r.ID)))
                .ToList();
            var baseTier = tiers[0];

            // Mapeamento lugar → lugar de origem, sorteado na primeira faixa e repetido nas outras.
            var mapping = new List<(int Target, int Source)>();
            foreach (var type in new[] { TypeWeapon, TypeArmor, TypeGoods })
            {
                var spots = baseTier.Values
                    .Where(r => r.GetInt("equipType") == type && !Fixed(r, goods))
                    .Select(r => Offset(r.ID))
                    .Order()
                    .ToList();
                var sources = spots.ToList();
                rng.Shuffle(sources);
                mapping.AddRange(spots.Zip(sources));
            }

            var original = baseTier.ToDictionary(kv => kv.Key, kv => OfferOf(kv.Value));
            foreach (var tier in tiers)
            {
                var before = tier.ToDictionary(kv => kv.Key, kv => OfferOf(kv.Value));
                foreach (var (target, source) in mapping)
                    if (tier.TryGetValue(target, out var row) && before.TryGetValue(source, out var offer))
                        SetOffer(row, offer);
            }

            var currency = baseTier.Values.First().GetInt("shopType") == ShopTypeInsight ? "insight" : "ecos";
            foreach (var (target, source) in mapping.OrderBy(m => m.Target))
            {
                if (target == source)
                    continue;
                var type = baseTier[target].GetInt("equipType");
                var now = original[source];
                Changes.Add(new ShopChange(shopName, ItemName(type, now.EquipId), ItemName(type, original[target].EquipId), now.Value, currency));
            }
        }
    }

    /// <summary>
    /// Troca as armas grátis dos Mensageiros: 3 armas de truque distintas e, se a opção deixar, 2 armas de fogo.
    /// </summary>
    public void RandomizeStartingWeapons()
    {
        var shop = param.Index("ShopLineupParam");
        var weapons = param.Index("EquipParamWeapon");
        // Candidatas: armas vendidas em alguma loja, só a versão normal (sem Uncanny/Lost), com nome no jogo.
        var sold = param["ShopLineupParam"].Rows
            .Where(r => r.GetInt("equipType") == TypeWeapon && GameData.Shops.Any(s => s.Series == Series(r.ID)))
            .Select(r => r.GetInt("equipId"))
            .Where(id => id / 10000 % 10 == 0 && weapons.ContainsKey(id) && text.Weapon(id) != null)
            .Distinct()
            .Order()
            .ToList();
        var right = sold.Where(id => weapons[id].GetInt("rightHandEquipable") == 1).ToList();
        var left = sold.Where(id => weapons[id].GetInt("leftHandEquipable") == 1).ToList();

        var rng = new Rng(settings.Seed, "shops/starting");
        foreach (var (rowId, isGun) in GameData.StartingWeaponRows)
        {
            if (isGun && settings.KeepStartingGuns)
                continue;
            if (!shop.TryGetValue(rowId, out var row))
                continue;
            var pool = isGun ? left : right;
            if (pool.Count == 0)
                continue;
            var index = rng.Next(pool.Count);
            var chosen = pool[index];
            pool.RemoveAt(index);

            var old = row.GetInt("equipId");
            row.SetInt("equipId", chosen);
            string? requirements = null;
            if (settings.EasyStartingWeapons && weapons.TryGetValue(old, out var oldRow))
                requirements = LowerRequirements(weapons, chosen, oldRow);
            StartingWeapons.Add(new StartingWeapon(isGun ? "Arma de fogo" : "Arma de truque",
                ItemName(TypeWeapon, chosen), ItemName(TypeWeapon, old), requirements));
        }
    }

    /// <summary>
    /// Requisito da arma nova (e dos níveis +1..+10, que são linhas próprias) = o menor entre ela e a arma
    /// original do lugar, que qualquer origem consegue usar. Devolve o texto "antes → agora" ou null.
    /// </summary>
    static string? LowerRequirements(Dictionary<long, PARAM.Row> weapons, int weapon, PARAM.Row original)
    {
        var limit = Requirements.Select(original.GetInt).ToArray();
        var before = Requirements.Select(weapons[weapon].GetInt).ToArray();
        var after = before.Zip(limit, (a, b) => Math.Min(a, b)).ToArray();
        if (before.SequenceEqual(after))
            return null;
        for (var level = 0; level <= 10; level++)
        {
            if (!weapons.TryGetValue(weapon + 100 * level, out var row))
                continue;
            for (var i = 0; i < Requirements.Length; i++)
                row.SetInt(Requirements[i], Math.Min(row.GetInt(Requirements[i]), limit[i]));
        }
        return $"{string.Join("/", before)} → {string.Join("/", after)}";
    }
}
