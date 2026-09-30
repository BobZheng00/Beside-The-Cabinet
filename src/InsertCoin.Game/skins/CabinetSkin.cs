using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace InsertCoin.Game.Skins;

/// <summary>
/// A cabinet skin exported by tools/art: full-canvas layers plus sprites, all positioned by anchor points.
/// Coordinates are canvas units; textures are rendered at <see cref="Scale"/>× for crispness.
/// </summary>
public sealed class CabinetSkin
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required float Scale { get; init; }
    public required Vector2 Canvas { get; init; }
    public required IReadOnlyDictionary<string, Texture2D> Layers { get; init; }
    public required IReadOnlyDictionary<string, SkinSprite> Sprites { get; init; }
    public required SkinAnchors Anchors { get; init; }

    public static CabinetSkin Load(string id)
    {
        var dir = $"res://skins/{id}";
        var json = Godot.FileAccess.GetFileAsString($"{dir}/skin.json");
        var file = JsonSerializer.Deserialize<SkinFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException($"skin '{id}' has no skin.json");

        return new CabinetSkin
        {
            Id = file.Id,
            Name = file.Name,
            Scale = file.Scale,
            Canvas = V(file.Canvas),
            Layers = file.Layers.ToDictionary(l => l.Key, l => GD.Load<Texture2D>($"{dir}/{l.Value}")),
            Sprites = file.Sprites.ToDictionary(s => s.Key, s => new SkinSprite(GD.Load<Texture2D>($"{dir}/{s.Value.File}"), V(s.Value.Origin))),
            Anchors = new SkinAnchors
            {
                ScreenQuad = [.. file.Anchors.Screen.Quad.Select(V)],
                ScreenSize = V(file.Anchors.Screen.Size),
                Bulbs = [.. file.Anchors.Bulbs.Select(b => (new Vector2(b.X, b.Y), b.R))],
                CheckButton = [.. file.Anchors.CheckButton.Polygon.Select(V)],
                CheckPress = V(file.Anchors.CheckButton.Press),
                CoinSlot = V(file.Anchors.CoinSlot),
                TrayOpening = [.. file.Anchors.Tray.Opening.Select(V)],
                TraySpots = [.. file.Anchors.Tray.Spots.Select(V)],
                TrayStack = V(file.Anchors.Tray.Stack),
                TrayChute = V(file.Anchors.Tray.Chute),
                PrinterSlot = [.. file.Anchors.Printer.Slot.Select(V)],
                TicketOrigin = V(file.Anchors.Printer.Ticket),
                Marquee = [.. file.Anchors.Marquee.Select(V)],
            },
        };
    }

    private static Vector2 V(float[] p) => new(p[0], p[1]);

    // ---- skin.json shape ----
    private sealed record SkinFile(string Id, string Name, float Scale, float[] Canvas, Dictionary<string, string> Layers,
                                   Dictionary<string, SpriteFile> Sprites, AnchorsFile Anchors);
    private sealed record SpriteFile(string File, float[] Origin);
    private sealed record AnchorsFile(ScreenFile Screen, BulbFile[] Bulbs, ButtonFile CheckButton, float[] CoinSlot, TrayFile Tray,
                                      PrinterFile Printer, float[][] Marquee);
    private sealed record ScreenFile(float[][] Quad, float[] Size);
    private sealed record BulbFile(float X, float Y, float R);
    private sealed record ButtonFile(float[][] Polygon, float[] Press);
    private sealed record TrayFile(float[][] Opening, float[][] Spots, float[] Stack, float[] Chute);
    private sealed record PrinterFile(float[][] Slot, [property: JsonPropertyName("ticket")] float[] Ticket);
}

/// <summary>A sprite texture and the canvas-unit point inside it that lands on its anchor.</summary>
public sealed record SkinSprite(Texture2D Texture, Vector2 Origin);

public sealed class SkinAnchors
{
    public required Vector2[] ScreenQuad { get; init; }          // TL, TR, BR, BL of the CRT
    public required Vector2 ScreenSize { get; init; }            // CRT size in face-local units
    public required (Vector2 Centre, float Radius)[] Bulbs { get; init; }
    public required Vector2[] CheckButton { get; init; }
    public required Vector2 CheckPress { get; init; }
    public required Vector2 CoinSlot { get; init; }
    public required Vector2[] TrayOpening { get; init; }
    public required Vector2[] TraySpots { get; init; }            // front row first, then back row
    public required Vector2 TrayStack { get; init; }              // offset of one coin's thickness
    public required Vector2 TrayChute { get; init; }
    public required Vector2[] PrinterSlot { get; init; }
    public required Vector2 TicketOrigin { get; init; }
    public required Vector2[] Marquee { get; init; }
}
