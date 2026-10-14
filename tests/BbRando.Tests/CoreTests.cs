using BbRando.Core;

namespace BbRando.Tests;

/// <summary>Testes que não precisam dos arquivos do jogo.</summary>
public class CoreTests
{
    [Fact]
    public void Rng_MesmaSeedEFluxo_MesmaSequencia()
    {
        var a = new Rng(42, "enemies/m24_01_00_00");
        var b = new Rng(42, "enemies/m24_01_00_00");
        Assert.Equal(Enumerable.Range(0, 100).Select(_ => a.Next(1000)), Enumerable.Range(0, 100).Select(_ => b.Next(1000)));
    }

    [Fact]
    public void Rng_FluxosDiferentes_SequenciasDiferentes()
    {
        var a = new Rng(42, "items/treasures");
        var b = new Rng(42, "items/drops");
        Assert.NotEqual(Enumerable.Range(0, 20).Select(_ => a.NextULong()), Enumerable.Range(0, 20).Select(_ => b.NextULong()));
    }

    [Fact]
    public void Rng_ValoresConhecidos_NaoMudamEntreVersoes()
    {
        // Se este teste falhar, códigos de seed já compartilhados passam a gerar jogos diferentes.
        var rng = new Rng(123, "teste");
        Assert.Equal(new[] { 571, 885, 398, 762, 313 }, Enumerable.Range(0, 5).Select(_ => rng.Next(1000)).ToArray());
    }

    [Fact]
    public void Rng_Next_FicaNoIntervalo()
    {
        var rng = new Rng(7, "x");
        for (var i = 0; i < 10_000; i++)
            Assert.InRange(rng.Next(13), 0, 12);
    }

    [Fact]
    public void CodigoDeSeed_IdaEVolta_PreservaOpcoes()
    {
        var s = new RandomizerSettings
        {
            Seed = 987654321,
            SwapBosses = true,
            BossChance = 3,
            ItemsWithinArea = true,
            EnemyWeights = { ["c2090"] = 5, ["c1000"] = 0 },
        };
        s.AreaChance["research_hall"] = 10;
        var code = s.ToSeedCode();
        Assert.StartsWith("BBR2-", code);
        var back = RandomizerSettings.FromSeedCode(code, new RandomizerSettings());
        Assert.Equal(s.ToJson(), back.ToJson());
    }

    [Fact]
    public void CodigoBBR1_VaiParaModoLegadoSemEscalonamento()
    {
        // Código gerado pela versão anterior (seed "compat42", opções padrão).
        var s = RandomizerSettings.FromSeedCode("BBR1-q1YKTk1NUbJSMjQ2NTG3NDM3MzSxNDYztzQ1NzZXqgUA", new RandomizerSettings());
        Assert.Equal(SourceMode.ModPack, s.Source);
        Assert.False(s.ScaleEnemiesToArea);
        Assert.Equal(RandomizerSettings.SeedFromText("compat42"), s.Seed);
    }

    [Fact]
    public void SeedDeTexto_EhEstavel()
    {
        Assert.Equal(12345UL, RandomizerSettings.SeedFromText("12345"));
        Assert.Equal(RandomizerSettings.SeedFromText("Caçada da Lua"), RandomizerSettings.SeedFromText("  caçada da lua "));
    }

    [Fact]
    public void PatchesEmbutidos_Carregam()
    {
        var patches = ModPatchSet.Embedded();
        Assert.True(patches.Events.Count >= 16);
        Assert.Contains(patches.ParamRows, r => r.Param == "NpcThinkParam" && r.Id == 551111);
        Assert.All(patches.LuaFiles.Values.SelectMany(f => f.Values), hash => Assert.True(patches.Blobs.ContainsKey(hash)));
    }

    [Fact]
    public void Arenas_SemDuplicatas()
    {
        Assert.Equal(GameData.BossArenas.Count, GameData.BossArenas.Select(a => a.Id).Distinct().Count());
        // Uma parte não pode ser chefe em duas arenas do mesmo mapa.
        var parts = GameData.BossArenas.SelectMany(a => a.Maps.SelectMany(m => a.Phases.Concat(a.HelperParts).Select(p => (m, p)))).ToList();
        Assert.Equal(parts.Count, parts.Distinct().Count());
    }
}
