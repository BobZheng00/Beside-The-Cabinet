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
    public required SkinWorld World { get; init; }

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
                ReceiptQuad = [.. file.Anchors.Printer.Receipt.Select(V)],
                ReceiptSize = V(file.Anchors.Printer.ReceiptSize),
                Marquee = [.. file.Anchors.Marquee.Select(V)],
                MemoPins = file.Anchors.Memos is { } memos ? [.. memos.Pins.Select(q => q.Select(V).ToArray())] : [],
                MemoSize = file.Anchors.Memos is { } m ? V(m.Size) : Vector2.Zero,
            },
            World = new SkinWorld
            {
                UnitsPerMetre = file.World.UnitsPerMetre,
                CameraPosition = V3(file.World.Camera.Position),
                CameraYawDegrees = file.World.Camera.Yaw,
                Focal = file.World.Camera.Focal,
                Offset = V(file.World.Camera.Offset),
                Floor = file.World.Floor,
                Seat = V3(file.World.Seat),
                SeatRadius = file.World.SeatRadius,
                FootRest = file.World.FootRest,
                Screen = V3(file.World.Screen),
                Slot = file.World.Slot is { } slot ? V3(slot) : V3(file.World.Screen) - new Vector3(0, 0.9f * file.World.UnitsPerMetre, 0),
            },
        };
    }

    private static Vector2 V(float[] p) => new(p[0], p[1]);
    private static Vector3 V3(float[] p) => new(p[0], p[1], p[2]);

    // ---- skin.json shape ----
    private sealed record SkinFile(string Id, string Name, float Scale, float[] Canvas, Dictionary<string, string> Layers,
                                   Dictionary<string, SpriteFile> Sprites, AnchorsFile Anchors, WorldFile World);
    private sealed record SpriteFile(string File, float[] Origin);
    private sealed record AnchorsFile(ScreenFile Screen, BulbFile[] Bulbs, ButtonFile CheckButton, float[] CoinSlot, TrayFile Tray,
                                      PrinterFile Printer, float[][] Marquee, MemosFile? Memos);
    private sealed record ScreenFile(float[][] Quad, float[] Size);
    private sealed record BulbFile(float X, float Y, float R);
    private sealed record ButtonFile(float[][] Polygon, float[] Press);
    private sealed record TrayFile(float[][] Opening, float[][] Spots, float[] Stack, float[] Chute);
    private sealed record WorldFile(float UnitsPerMetre, CameraFile Camera, float Floor, float[] Seat, float SeatRadius, float FootRest, float[] Screen, float[]? Slot);
    private sealed record CameraFile(float[] Position, float Yaw, float Focal, float[] Offset);
    private sealed record MemosFile(float[][][] Pins, float[] Size);
    private sealed record PrinterFile(float[][] Slot, [property: JsonPropertyName("ticket")] float[] Ticket, float[][] Receipt, float[] ReceiptSize);
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
    public required Vector2[] ReceiptQuad { get; init; }          // TL, TR, BR, BL of the paper hanging from the slot
    public required Vector2 ReceiptSize { get; init; }
    public required Vector2[] Marquee { get; init; }
    public required Vector2[][] MemoPins { get; init; }           // where memos are pinned on the side (TL, TR, BR, BL each, tape included); none for a skin without
    public required Vector2 MemoSize { get; init; }              // a pinned memo's size in face-local units, tape included
}

/// <summary>
/// The 3D scene the art was drawn from, so live 3D can be rendered into the same picture.
/// Model units: x right, y up, z towards the back of the cabinet. A point lands on the canvas at
/// Offset + Focal * (right, -up) / depth for a camera that looks horizontally (yaw only, lens shifted).
/// </summary>
public sealed class SkinWorld
{
    public required float UnitsPerMetre { get; init; }
    public required Vector3 CameraPosition { get; init; }
    public required float CameraYawDegrees { get; init; }
    public required float Focal { get; init; }
    public required Vector2 Offset { get; init; }
    public required float Floor { get; init; }
    public required Vector3 Seat { get; init; }                   // centre of the stool's seat top
    public required float SeatRadius { get; init; }
    public required float FootRest { get; init; }                 // height of the stool's foot ring
    public required Vector3 Screen { get; init; }                 // centre of the CRT
    public required Vector3 Slot { get; init; }                   // the printer's slot (a skin without: below the screen)

    /// <summary>Model point to Godot metres (right-handed: z flips).</summary>
    public Vector3 ToMetres(Vector3 model) => new Vector3(model.X, model.Y, -model.Z) / UnitsPerMetre;
}
