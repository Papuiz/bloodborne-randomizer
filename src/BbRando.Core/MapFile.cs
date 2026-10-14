using System.Globalization;
using System.Numerics;
using SoulsFormats;

namespace BbRando.Core;

/// <summary>Um mapa (.msb) carregado, com ajuda para descrever onde fica cada parte.</summary>
public sealed class MapFile
{
    public MapDef Def { get; }
    public MSBB Msb { get; }
    public Area Area => GameData.AreaById(Def.AreaId);

    /// <summary>Marcado quando algo no mapa foi alterado (só esses são regravados).</summary>
    public bool Dirty { get; set; }

    readonly Dictionary<string, MSBB.Part> _parts = new();
    readonly Dictionary<string, short> _collisionPlace = new();
    readonly Dictionary<string, MSBB.Region> _regions = new();

    MapFile(MapDef def, MSBB msb)
    {
        Def = def;
        Msb = msb;
        foreach (var part in msb.Parts.GetEntries())
            _parts.TryAdd(part.Name, part);
        foreach (var col in msb.Parts.Collisions)
            _collisionPlace.TryAdd(col.Name, col.MapNameID);
        foreach (var region in msb.Regions.GetEntries())
            _regions.TryAdd(region.Name, region);
    }

    public static MapFile? Load(ModPack pack, MapDef def)
    {
        if (!pack.Exists(def.RelativePath))
        {
            if (def.Optional)
                return null;
            throw new FileNotFoundException($"Mapa não encontrado no pacote do mod: {def.RelativePath}");
        }
        return new MapFile(def, MSBB.Read(pack.Resolve(def.RelativePath)));
    }

    public void Write(string outputDvdRoot)
    {
        var path = Path.Combine(outputDvdRoot, Def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Msb.Write(path);
    }

    public MSBB.Part? Part(string? name) => name != null && _parts.TryGetValue(name, out var p) ? p : null;

    /// <summary>Local de um tesouro: o objeto ligado a ele ou, na falta, o ponto do evento.</summary>
    public Location DescribeTreasure(GameText text, MSBB.Event.Treasure treasure)
    {
        if ((Part(treasure.PartName2) ?? Part(treasure.PartName)) is { } part)
            return Describe(text, part);
        if (treasure.PointName != null && _regions.TryGetValue(treasure.PointName, out var region))
            return new Location(Area.Name, null, Def.Id, region.Position);
        return Describe(text, null);
    }

    /// <summary>"Área — Sublocal (x, y, z)" para uma parte (objeto de tesouro, inimigo...).</summary>
    public Location Describe(GameText text, MSBB.Part? part)
    {
        string? place = null;
        var collision = part switch
        {
            MSBB.Part.Object o => o.CollisionName,
            MSBB.Part.Enemy e => e.CollisionName,
            _ => null,
        };
        if (collision != null && _collisionPlace.TryGetValue(collision, out var nameId) && nameId > 0)
            place = text.Place(nameId);
        return new Location(Area.Name, place, Def.Id, part?.Position);
    }
}

public sealed record Location(string Area, string? Place, string MapId, Vector3? Position)
{
    public override string ToString()
    {
        var where = Place != null && Place != Area ? $"{Area} — {Place}" : Area;
        return Position is { } p
            ? string.Create(CultureInfo.InvariantCulture, $"{where}  [{MapId} @ {p.X:0.#}, {p.Y:0.#}, {p.Z:0.#}]")
            : $"{where}  [{MapId}]";
    }
}
