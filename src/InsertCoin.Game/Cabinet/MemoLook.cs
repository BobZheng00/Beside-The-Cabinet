using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// How memos look, wherever they are drawn (on the cabinet, carried, in the reader's choices): the papers they come
/// on, and the stickers, drawn in flat colours with the cabinet's ink outline.
/// </summary>
public static class MemoLook
{
    public static readonly Color Ink = new("1e1630");

    /// <summary>A paper's colour, by its name (see <see cref="Mind.Memo.Colours"/>).</summary>
    public static Color Paper(string colour) => colour switch
    {
        "yellow" => new Color("FFF0B0"),
        "pink" => new Color("FFD9E8"),
        "mint" => new Color("D2F2E4"),
        "lilac" => new Color("E6DEFF"),
        "blue" => new Color("D7E9FF"),
        _ => new Color("FFF7EC"),
    };

    /// <summary>Draws a sticker (see <see cref="Mind.Memo.Stickers"/>) centred on a point, about <paramref name="size"/> across.</summary>
    public static void Sticker(CanvasItem on, string sticker, Vector2 centre, float size)
    {
        var r = size / 2;
        var ink = Mathf.Max(1.2f, size * 0.07f);
        switch (sticker)
        {
            case "star":
                Shape(on, [.. Enumerable.Range(0, 10).Select(i => centre + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * (i % 2 == 0 ? r : r * 0.45f))],
                      new Color("FFD54A"), ink);
                break;
            case "heart":
                Shape(on, [.. Enumerable.Range(0, 32).Select(i =>
                {
                    var t = i * Mathf.Tau / 32;
                    var x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
                    var y = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                    return centre + new Vector2(x, -y - 2) * (r / 17);
                })], new Color("FF7AB0"), ink);
                break;
            case "coin":
                Circle(on, centre, r, new Color("F5B83D"), ink);
                on.DrawArc(centre, r * 0.62f, 0, Mathf.Tau, 24, new Color("C98316"), ink * 0.8f, true);
                on.DrawLine(centre + new Vector2(0, -r * 0.35f), centre + new Vector2(0, r * 0.35f), new Color("C98316"), ink, true);
                break;
            case "cat":
                // a white cat's face, like the sticker on the cabinet's front: ears, eyes, pink cheeks
                Shape(on, [centre + new Vector2(-r * 0.85f, -r * 0.15f), centre + new Vector2(-r * 0.7f, -r * 0.95f), centre + new Vector2(-r * 0.25f, -r * 0.55f),
                           centre + new Vector2(r * 0.25f, -r * 0.55f), centre + new Vector2(r * 0.7f, -r * 0.95f), centre + new Vector2(r * 0.85f, -r * 0.15f),
                           centre + new Vector2(r * 0.6f, r * 0.7f), centre + new Vector2(-r * 0.6f, r * 0.7f)], Colors.White, ink);
                on.DrawCircle(centre + new Vector2(-r * 0.32f, r * 0.05f), r * 0.11f, Ink);
                on.DrawCircle(centre + new Vector2(r * 0.32f, r * 0.05f), r * 0.11f, Ink);
                on.DrawCircle(centre + new Vector2(-r * 0.5f, r * 0.35f), r * 0.12f, new Color("FFB3D1"));
                on.DrawCircle(centre + new Vector2(r * 0.5f, r * 0.35f), r * 0.12f, new Color("FFB3D1"));
                break;
            case "flower":
                for (var i = 0; i < 5; i++)
                    Circle(on, centre + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Tau / 5) * r * 0.5f, r * 0.42f, new Color("FFB3D1"), ink);
                Circle(on, centre, r * 0.34f, new Color("FFD54A"), ink);
                break;
            case "bolt":
                Shape(on, [centre + new Vector2(r * 0.15f, -r), centre + new Vector2(-r * 0.55f, r * 0.1f), centre + new Vector2(-r * 0.02f, r * 0.1f),
                           centre + new Vector2(-r * 0.2f, r), centre + new Vector2(r * 0.6f, -r * 0.15f), centre + new Vector2(r * 0.05f, -r * 0.15f)],
                      new Color("FFD54A"), ink);
                break;
        }
    }

    private static void Shape(CanvasItem on, Vector2[] points, Color fill, float ink)
    {
        on.DrawColoredPolygon(points, fill);
        on.DrawPolyline([.. points, points[0]], Ink, ink, true);
    }

    private static void Circle(CanvasItem on, Vector2 centre, float radius, Color fill, float ink)
    {
        on.DrawCircle(centre, radius, fill);
        on.DrawArc(centre, radius, 0, Mathf.Tau, 32, Ink, ink, true);
    }
}
