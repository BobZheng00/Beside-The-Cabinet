using Godot;
using InsertCoin.Game.Desktop;

namespace InsertCoin.Game.App;

/// <summary>
/// MUSIC on the CRT: what the player's music app is playing (see <see cref="Music"/>), back, play or pause, and next,
/// Spotify starting for them when nothing is open; their focus playlist, opened from here, and saved by pasting its link
/// (copied in Spotify: Share, Copy link); and whether it plays along with focus sessions.
/// </summary>
public partial class MusicApp(Music music, Focus focus, SettingsFile settings) : CrtApp
{
    private string? _note;                                  // a word after pasting, for a few seconds
    private ulong _noteUntil;

    public override string Id => "music";
    public override string Label => "MUSIC";
    public override Color Tint => new("E3D7FF");

    public override void _Ready() => music.Changed += () =>
    {
        if (IsVisibleInTree())
            QueueRedraw();
    };

    public override void Opened() => QueueRedraw();
    public override void Click(Vector2 at) => Press(at);
    public override void Hover(Vector2? at) => Point(at);

    public override void _Process(double delta)
    {
        if (_note is not null && Time.GetTicksMsec() > _noteUntil)
        {
            _note = null;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        ClearButtons();
        if (music.Now is { } now)
        {
            Centred($"{now.App.ToUpperInvariant()} - {(now.Playing ? "PLAYING" : "PAUSED")}", Pixel, 12, Accent, new Rect2(0, 12, Size.X, 22));
            Centred(now.Title.Length > 0 ? now.Title : "-", Round, 26, Ink, new Rect2(24, 36, Size.X - 48, 34));
            Centred(now.Artist, Round, 19, Ink with { A = 0.7f }, new Rect2(24, 70, Size.X - 48, 26));
        }
        else
        {
            Centred("NOTHING PLAYING", Pixel, 14, Ink with { A = 0.75f }, new Rect2(0, 22, Size.X, 24));
            Centred(Music.SpotifyHere ? "Press play: Spotify starts" : "No Spotify on this PC", Round, 21, Ink with { A = 0.7f }, new Rect2(0, 54, Size.X, 30));
        }
        RoundButton(new Vector2(128, 136), 26, (c, r, ink) => Skip(c, r, ink, forward: false), () => _ = music.Previous());
        RoundButton(new Vector2(200, 136), 32, (c, r, ink) => PlayOrPause(c, r, ink, music.Now is { Playing: true }), () => _ = music.Toggle());
        RoundButton(new Vector2(272, 136), 26, (c, r, ink) => Skip(c, r, ink, forward: true), () => _ = music.Next());

        var link = settings.Now.Playlist;
        Button(new Rect2(40, 180, 320, 44), link is null ? "PASTE A PLAYLIST LINK" : "OPEN MY PLAYLIST", link is null ? Paste : () => _ = music.OpenPlaylist(link),
               chosen: link is not null, font: Round, size: 21);
        var line = _note ?? (link is null ? "In Spotify: Share, Copy link" : Music.Shown(link));
        Centred(line, Round, 16, _note is null ? Ink with { A = 0.6f } : Accent, new Rect2(24, 226, Size.X - 48, 22));
        Button(new Rect2(40, 252, 150, 40), "PASTE LINK", Paste, font: Round, size: 18);
        Button(new Rect2(206, 252, 154, 40), focus.WithMusic ? "WITH FOCUS" : "NOT IN FOCUS", () => focus.WithMusic = !focus.WithMusic,
               chosen: focus.WithMusic, font: Round, size: 18);
    }

    /// <summary>Saves the playlist link on the clipboard (copied in the music app); playing it along with focus sessions then.</summary>
    private void Paste()
    {
        if (Companion.Paper.OffScreen)
        {
            GD.Print("Music: a test run never reads the clipboard (musiclink:<link> pretends)");
            return;
        }
        Save(DisplayServer.ClipboardGet());
    }

    /// <summary>Keeps a playlist link, if it is one.</summary>
    public void Save(string link)
    {
        if (Music.Address(link) is null)
        {
            Say("No playlist link on the clipboard");
            return;
        }
        settings.Save(settings.Now with { Playlist = link.Trim(), FocusMusic = true });
        GD.Print($"Music: playlist saved ({Music.Shown(link)})");
        Say("Saved: it plays when you focus");
    }

    private void Say(string note)
    {
        _note = note;
        _noteUntil = Time.GetTicksMsec() + 3500;
        QueueRedraw();
    }

    private void PlayOrPause(Vector2 c, float r, Color ink, bool playing)
    {
        var s = r * 0.42f;
        if (playing)
        {
            DrawRect(new Rect2(c + new Vector2(-s * 0.8f, -s), new Vector2(s * 0.55f, s * 2)), ink);
            DrawRect(new Rect2(c + new Vector2(s * 0.25f, -s), new Vector2(s * 0.55f, s * 2)), ink);
        }
        else
            DrawColoredPolygon([c + new Vector2(-s * 0.7f, -s), c + new Vector2(s * 1.05f, 0), c + new Vector2(-s * 0.7f, s)], ink);
    }

    private void Skip(Vector2 c, float r, Color ink, bool forward)
    {
        var s = r * 0.4f;
        var d = forward ? 1 : -1;
        DrawColoredPolygon([c + new Vector2(-s * d, -s), c + new Vector2(s * 0.5f * d, 0), c + new Vector2(-s * d, s)], ink);
        DrawRect(new Rect2(c + new Vector2(forward ? s * 0.5f : -s * 0.5f - s * 0.35f, -s), new Vector2(s * 0.35f, s * 2)), ink);
    }

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a cassette: its shell, the label, the two reels and the window between them
        var shell = new Rect2(box.Position + new Vector2(box.Size.X * 0.06f, box.Size.Y * 0.2f), new Vector2(box.Size.X * 0.88f, box.Size.Y * 0.6f));
        var style = new StyleBoxFlat { BgColor = new Color("FFF7EC"), BorderColor = Ink, AntiAliasing = true };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(6);
        on.DrawStyleBox(style, shell);
        on.DrawRect(new Rect2(shell.Position + new Vector2(shell.Size.X * 0.1f, shell.Size.Y * 0.12f), new Vector2(shell.Size.X * 0.8f, shell.Size.Y * 0.22f)), Accent);
        var y = shell.Position.Y + shell.Size.Y * 0.62f;
        on.DrawRect(new Rect2(shell.Position.X + shell.Size.X * 0.3f, y - shell.Size.Y * 0.13f, shell.Size.X * 0.4f, shell.Size.Y * 0.26f), Ink with { A = 0.25f });
        foreach (var x in new[] { 0.3f, 0.7f })
        {
            var reel = new Vector2(shell.Position.X + shell.Size.X * x, y);
            on.DrawCircle(reel, shell.Size.Y * 0.17f, Colors.White);
            on.DrawArc(reel, shell.Size.Y * 0.17f, 0, Mathf.Tau, 20, Ink, 3, true);
            on.DrawCircle(reel, shell.Size.Y * 0.05f, Ink);
        }
    }
}
