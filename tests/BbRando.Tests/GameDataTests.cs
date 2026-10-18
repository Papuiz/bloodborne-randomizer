using BbRando.Core;

namespace BbRando.Tests;

/// <summary>
/// Testes com os arquivos do jogo. Defina BBR_VANILLA com a cópia limpa (pasta ou .zip) para rodá-los;
/// sem ela, eles passam sem fazer nada.
/// </summary>
public class GameDataTests
{
    static readonly string? Vanilla = Environment.GetEnvironmentVariable("BBR_VANILLA");

    static string Run(RandomizerSettings settings)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbr-teste-" + Guid.NewGuid().ToString("N"));
        Randomizer.Run(Vanilla!, dir, settings);
        return dir;
    }

    static Dictionary<string, string> Hashes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f),
                f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void MesmaSeed_MesmoResultado()
    {
        if (Vanilla == null)
            return;
        var settings = new RandomizerSettings { Seed = 777, SwapBosses = true, IncludeLesserBosses = true, RandomizeEnemyDrops = true };
        var a = Run(settings);
        var b = Run(settings.Clone());
        try
        {
            Assert.Equal(Hashes(a), Hashes(b));
        }
        finally
        {
            Directory.Delete(a, true);
            Directory.Delete(b, true);
        }
    }

    [Fact]
    public void Swap_CadaChefeUmaVez()
    {
        if (Vanilla == null)
            return;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var dir = Run(new RandomizerSettings { Seed = seed, SwapBosses = true, IncludeLesserBosses = true, RandomizeEnemies = false, RandomizeItems = false });
            try
            {
                var lines = File.ReadAllLines(Path.Combine(dir, "logs", "inimigos.txt")).Where(l => l.StartsWith("    Arena de ")).ToList();
                var bosses = lines.Select(l => l[(l.IndexOf('→') + 1)..].Trim()).ToList();
                Assert.Equal(GameData.BossArenas.Count, lines.Count);
                Assert.Equal(bosses.Count, bosses.Distinct().Count());
                Assert.DoesNotContain(bosses, b => b.EndsWith("(ficou)"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    static List<(long Id, int Item, int Type, int Value)> Lineup(ParamBundle p) =>
        p["ShopLineupParam"].Rows.Select(r => (r.ID, r.GetInt("equipId"), r.GetInt("equipType"), r.GetInt("value"))).ToList();

    [Fact]
    public void Lojas_PermutacaoPorLojaEFaixa_ItensFixosNoLugar()
    {
        if (Vanilla == null)
            return;
        using var pack = GameSource.Open(Vanilla, SourceMode.Vanilla);
        var text = new GameText(pack);
        var before = Lineup(new ParamBundle(pack));
        var param = new ParamBundle(pack);
        var shops = new ShopRandomizer(param, new RandomizerSettings { Seed = 42, RandomizeShops = true }, text);
        shops.RandomizeLineup();
        var after = Lineup(param);
        Assert.NotEmpty(shops.Changes);

        // Cada loja e faixa vende o mesmo conjunto de itens (com o mesmo preço por item), só em outra ordem.
        foreach (var group in before.GroupBy(r => r.Id / 10000))
        {
            var a = after.Where(r => r.Id / 10000 == group.Key);
            Assert.Equal(group.Select(r => (r.Item, r.Type, r.Value)).Order(), a.Select(r => (r.Item, r.Type, r.Value)).Order());
        }
        var old = before.ToDictionary(r => r.Id);
        foreach (var r in after)
        {
            Assert.Equal(old[r.Id].Type, r.Type);
            if (r.Type == 3 && GameData.FixedShopGoods.Contains(old[r.Id].Item) || GameData.StartingGuns.Contains(old[r.Id].Item) || r.Id < 100000)
                Assert.Equal(old[r.Id].Item, r.Item);
            // Todas as faixas da mesma loja recebem o mesmo item no mesmo lugar.
            var baseId = r.Id / 100000 * 100000 + (r.Id / 100000 is 5 or 6 ? 10000 : 0) + r.Id % 10000;
            if (r.Id >= 100000 && r.Id < 900000)
                Assert.Equal(after.First(x => x.Id == baseId).Item, r.Item);
        }
    }

    [Fact]
    public void ArmasIniciais_DeTruqueDistintas_RequisitosBaixos()
    {
        if (Vanilla == null)
            return;
        using var pack = GameSource.Open(Vanilla, SourceMode.Vanilla);
        var text = new GameText(pack);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var param = new ParamBundle(pack);
            var shops = new ShopRandomizer(param, new RandomizerSettings { Seed = seed, KeepStartingGuns = seed % 2 == 0 }, text);
            shops.RandomizeStartingWeapons();
            var shop = param.Index("ShopLineupParam");
            var weapons = param.Index("EquipParamWeapon");
            var melee = new[] { 2000, 2001, 2002 }.Select(id => shop[id].GetInt("equipId")).ToList();
            var guns = new[] { 2010, 2011 }.Select(id => shop[id].GetInt("equipId")).ToList();
            Assert.Equal(3, melee.Distinct().Count());
            Assert.All(melee, id => Assert.Equal(1, weapons[id].GetInt("rightHandEquipable")));
            Assert.All(guns, id => Assert.Equal(1, weapons[id].GetInt("leftHandEquipable")));
            Assert.Equal(2, guns.Distinct().Count());
            if (seed % 2 == 0)
                Assert.Equal([14000000, 6000000], guns);
            // A mais exigente das armas iniciais originais pede 9 de força e 9 de habilidade (Hunter Axe/Cane).
            foreach (var id in melee.Concat(guns))
                for (var level = 0; level <= 10; level++)
                    if (weapons.TryGetValue(id + 100 * level, out var row))
                        Assert.True(row.GetInt("properStrength") <= 9 && row.GetInt("properAgility") <= 9
                            && row.GetInt("properMagic") <= 5 && row.GetInt("properFaith") == 0, $"{id}+{level}");
        }
    }

    /// <summary>Lê os mapas e monta o BossEvents para um conjunto de trocas (arena → arena de origem do chefe).</summary>
    static (IGameSource Pack, BossEvents Events, Dictionary<string, List<(SoulsFormats.EMEVD.Event, SoulsFormats.EMEVD.Event?)>> Files)
        Events(Dictionary<string, string> swaps)
    {
        var pack = GameSource.Open(Vanilla!, SourceMode.Vanilla);
        var maps = new Dictionary<string, MapFile>();
        foreach (var def in GameData.Maps)
            if (MapFile.Load(pack, def) is { } map)
                maps[def.Id] = map;
        var arenas = GameData.BossArenas.ToDictionary(a => a.Id);
        var changes = swaps.ToDictionary(kv => arenas[kv.Key], kv => arenas[kv.Value]);
        var events = new BossEvents(pack, maps, changes, statueHelpers: true);
        var files = ModPatchSet.Embedded().Events.ToDictionary(kv => kv.Key, kv =>
        {
            var emevd = SoulsFormats.EMEVD.Read(pack.ReadBytes(kv.Key));
            return kv.Value.Select(p => p.ToEvent()).Select(ev => (ev, (SoulsFormats.EMEVD.Event?)emevd.Events.FirstOrDefault(e => e.ID == ev.ID))).ToList();
        });
        return (pack, events, files);
    }

    [Fact]
    public void EventosDoMod_SemSwapNenhumEntra_ComTodasAsArenasTrocadasTodosEntram()
    {
        if (Vanilla == null)
            return;
        var none = Events(new());
        using (none.Pack)
            Assert.All(none.Events.Decide(none.Files).Values.SelectMany(d => d.Values), Assert.False);

        // Todas as arenas mudam (rotação): tem que ficar igual ao mod (todos os eventos, construtores completos).
        var ids = GameData.BossArenas.Select(a => a.Id).ToList();
        var all = Events(ids.Select((id, i) => (id, ids[(i + 1) % ids.Count])).ToDictionary(x => x.id, x => x.Item2));
        using (all.Pack)
        {
            var decisions = all.Events.Decide(all.Files);
            Assert.All(decisions.Values.SelectMany(d => d.Values), Assert.True);
            var byId = BossEvents.ById(decisions);
            foreach (var (file, events) in all.Files)
                foreach (var (patched, original) in events.Where(e => e.Item2 != null && e.Item1.ID is 0 or 50))
                    if (all.Events.TryMerge(file, original!, patched, byId, out var merged))
                        Assert.Equal(patched.Instructions.Count, merged.Instructions.Count);
        }
    }

    [Fact]
    public void EventosDoMod_MapaCompartilhado_SoAArenaQueMudou()
    {
        if (Vanilla == null)
            return;
        var (pack, events, files) = Events(new() { ["living_failures"] = "amelia", ["amelia"] = "living_failures" });
        using (pack)
        {
            var m35 = events.Decide(files)["event/m35_00_00_00.emevd.dcx"];
            Assert.False(m35[13504802]); // Maria ficou
            Assert.True(m35[13504852]);
            Assert.True(m35[91919194]);
            Assert.True(m35[13505662]); // geradores: decididos junto com as Living Failures
        }
    }

    [Fact]
    public void BarraDeVida_MostraONomeDoChefeNovo()
    {
        if (Vanilla == null)
            return;
        var (pack, events, _) = Events(new() { ["cleric_beast"] = "gascoigne", ["gascoigne"] = "cleric_beast", ["wet_nurse"] = "rom", ["rom"] = "wet_nurse" });
        using (pack)
        {
            List<(int On, int Entity, int Name)> Bars(string file)
            {
                var emevd = SoulsFormats.EMEVD.Read(pack.ReadBytes(file));
                Assert.True(events.RenameBars(emevd));
                return emevd.Events.SelectMany(e => e.Instructions).Where(i => i.Bank == 2003 && i.ID == 11)
                    .Select(i => ((int)i.ArgData[0], BitConverter.ToInt32(i.ArgData, 4), BitConverter.ToInt32(i.ArgData, 12))).ToList();
            }
            var m24 = Bars("event/m24_01_00_00.emevd.dcx");
            Assert.Contains((1, 2410800, 271000), m24);              // Cleric Beast → Gascoigne
            Assert.All(m24.Where(b => b.Entity is 2410810 or 2410811), b => Assert.Equal(500000, b.Name));
            Assert.Contains((1, 2600800, 510000), Bars("event/m26_00_00_00.emevd.dcx")); // barra da Wet Nurse passa para o chefe
            Assert.Contains((1, 3200800, 551000), Bars("event/m32_00_00_00.emevd.dcx"));
        }
    }

    [Fact]
    public void SemSwap_EventosDoJogoNaoSaoGravados()
    {
        if (Vanilla == null)
            return;
        var dir = Run(new RandomizerSettings { Seed = 3, RandomizeItems = false });
        try
        {
            Assert.False(Directory.Exists(Path.Combine(dir, "dvdroot_ps4", "event")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
