using BbRando.Core;

namespace BbRando.App.ViewModels;

/// <summary>Uma linha da lista de inimigos: o peso dele no sorteio.</summary>
public sealed class EnemyRow(EnemyModelInfo info, MainViewModel owner) : ObservableObject
{
    public EnemyModelInfo Info { get; } = info;
    public string Title => Info.Name == Info.Model ? $"Inimigo {Info.Model}" : Info.Name;
    public string Subtitle =>
        $"{Info.Model} · {(Info.SizeBytes is { } b ? $"{b / 1048576.0:0.0} MB" : "tamanho ?")} · aparece em: {string.Join(", ", Info.Areas)}";
    public string Tag => Info.IsBoss ? "CHEFE" : Info.IsChalice ? "CÁLICE" : "";
    public bool HasTag => Tag.Length > 0;

    public double Weight
    {
        get => owner.Settings.WeightOf(Info.Model);
        set
        {
            value = Math.Round(Math.Clamp(value, 0, 10) * 2) / 2;
            if (value == 1.0)
                owner.Settings.EnemyWeights.Remove(Info.Model);
            else
                owner.Settings.EnemyWeights[Info.Model] = value;
            Raise();
            Raise(nameof(WeightLabel));
            owner.SettingsChanged();
        }
    }

    public string WeightLabel => Weight switch
    {
        0 => "nunca",
        1 => "normal",
        _ => $"{Weight:0.#}×",
    };

    public void Refresh()
    {
        Raise(nameof(Weight));
        Raise(nameof(WeightLabel));
    }
}

/// <summary>Uma linha da lista de áreas: a chance de troca dos inimigos nela.</summary>
public sealed class AreaRow(Area area, MainViewModel owner) : ObservableObject
{
    public Area Area { get; } = area;
    public string Description => Area.Description;

    public double Chance
    {
        get => owner.Settings.ChanceOf(Area.Id);
        set
        {
            owner.Settings.AreaChance[Area.Id] = (int)Math.Round(Math.Clamp(value, 0, 100));
            Raise();
            Raise(nameof(ChanceLabel));
            owner.SettingsChanged();
        }
    }

    public string ChanceLabel => $"{Chance:0}%";

    public void Refresh()
    {
        Raise(nameof(Chance));
        Raise(nameof(ChanceLabel));
    }
}
