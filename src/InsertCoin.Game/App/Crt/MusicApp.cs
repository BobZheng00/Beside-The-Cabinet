using Godot;
using InsertCoin.Game.Desktop;

namespace InsertCoin.Game.App;

/// <summary>
/// MUSIC on the CRT, in two views. NOW: what the player's music app is playing (see <see cref="Music"/>), back,
/// play or pause (what was playing carries on; Spotify starts for them when nothing is open), next; their playlists;
/// and whether music plays along with focus sessions, and which. PLAYLISTS: the ones they saved (a link copied in
/// Spotify: Share, Copy link, then ADD; the name comes from Spotify), each started from its beginning with START,
/// starred to start focus sessions with, or taken off the list; three to a page.
/// </summary>
public partial class MusicApp(Music music, Focus focus, SettingsFile settings) : CrtApp
{
    private const int PerPage = 3;
    private bool _listing;                                  // the PLAYLISTS view
    private int _page;
    private string? _note;                                  // a word after doing something, for a few seconds
    private ulong _noteUntil;

    public override string Id => "music";
    public override string Label => "MUSIC";
    public override Color Tint => new("E3D7FF");

    public override void _Ready() => music.Changed += () =>
    {
        if (IsVisibleInTree())
            QueueRedraw();
    };

    public override void Opened()
    {
        _listing = false;
        _page = 0;
        QueueRedraw();
    }

    public override void Click(Vector2 at) => Press(at);
    public override void Hover(Vector2? at) => Point(at);

    private SavedPlaylist[] Saved => settings.Now.Playlists;
    private int Pages => Math.Max(1, (Saved.Length + PerPage - 1) / PerPage);

    public override (int Page, int Pages) Paging => _listing ? (_page, Pages) : (0, 1);

    public override bool Swipe(int step)
    {
        if (!_listing || Math.Clamp(_page + step, 0, Pages - 1) == _page)
            return false;
        _page += step;
        QueueRedraw();
        return true;
    }

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
        if (_listing)
            DrawPlaylists();
        else
            DrawNow();
    }

    // ---- NOW ---------------------------------------------------------------------------------------------------------

    private void DrawNow()
    {
        if (music.Now is { } now)
        {
            Centred($"{now.App.ToUpperInvariant()} - {(now.Playing ? "PLAYING" : "PAUSED")}", Pixel, 12, Accent, new Rect2(0, 10, Size.X, 22));
            Centred(now.Title.Length > 0 ? now.Title : "-", Round, 25, Ink, new Rect2(24, 32, Size.X - 48, 34));
            Centred(now.Artist, Round, 18, Ink with { A = 0.7f }, new Rect2(24, 64, Size.X - 48, 24));
        }
        else
        {
            Centred("NOTHING PLAYING", Pixel, 14, Ink with { A = 0.75f }, new Rect2(0, 16, Size.X, 24));
            Centred(Music.SpotifyHere ? "Press play: Spotify starts" : "No Spotify on this PC", Round, 21, Ink with { A = 0.7f }, new Rect2(0, 46, Size.X, 30));
        }
        RoundButton(new Vector2(128, 124), 26, (c, r, ink) => Skip(c, r, ink, forward: false), () => _ = music.Previous());
        RoundButton(new Vector2(200, 124), 31, (c, r, ink) => PlayOrPause(c, r, ink, music.Now is { Playing: true }), () => _ = music.Toggle());
        RoundButton(new Vector2(272, 124), 26, (c, r, ink) => Skip(c, r, ink, forward: true), () => _ = music.Next());

        Button(new Rect2(40, 166, 320, 44), Saved.Length == 0 ? "ADD A PLAYLIST" : $"MY PLAYLISTS ({Saved.Length})", () =>
        {
            _listing = true;
            _page = 0;
        }, font: Round, size: 21);
        var starts = focus.FocusPlaylist is { } playlist ? $"FOCUS STARTS {playlist.Name}" : "FOCUS: MUSIC PLAYS ON";
        Button(new Rect2(40, 220, 320, 40), focus.WithMusic ? starts : "NO MUSIC IN FOCUS", () => focus.WithMusic = !focus.WithMusic,
               chosen: focus.WithMusic, font: Round, size: 18);
        var line = _note ?? (focus.WithMusic && focus.FocusPlaylist is null && Saved.Length > 0 ? "Star a playlist to start focus with it" : null);
        if (line is not null)
            Centred(line, Round, 15, _note is null ? Ink with { A = 0.6f } : Accent, new Rect2(24, 266, Size.X - 48, 22));
    }

    // ---- PLAYLISTS ---------------------------------------------------------------------------------------------------

    private void DrawPlaylists()
    {
        if (Saved.Length == 0)
        {
            Centred("NO PLAYLISTS YET", Pixel, 14, Ink with { A = 0.75f }, new Rect2(0, 50, Size.X, 24));
            Centred("In Spotify: Share, Copy link,", Round, 20, Ink with { A = 0.7f }, new Rect2(0, 86, Size.X, 28));
            Centred("then ADD here", Round, 20, Ink with { A = 0.7f }, new Rect2(0, 114, Size.X, 28));
        }
        var chosen = settings.Now.FocusPlaylist;
        for (var i = 0; i < PerPage && _page * PerPage + i < Saved.Length; i++)
        {
            var playlist = Saved[_page * PerPage + i];
            var y = 6 + i * 58f;
            Box(new Rect2(14, y, Size.X - 28, 52), Glow, Ink with { A = 0.5f }, 3, 12);
            var starred = playlist.Link == chosen;
            RoundButton(new Vector2(40, y + 26), 17, (c, r, ink) => Star(c, r * 0.72f, starred ? Accent : ink, starred), () => Choose(starred ? null : playlist));
            DrawString(Round, new Vector2(66, y + 34), Fit(playlist.Name, Round, 19, 186), HorizontalAlignment.Left, -1, 19, Ink);
            Button(new Rect2(258, y + 8, 82, 36), "START", () => Start(playlist), chosen: true, font: Round, size: 17);
            RoundButton(new Vector2(363, y + 26), 13, (c, r, ink) =>
            {
                DrawLine(c + new Vector2(-r * 0.4f, -r * 0.4f), c + new Vector2(r * 0.4f, r * 0.4f), ink, 3, true);
                DrawLine(c + new Vector2(-r * 0.4f, r * 0.4f), c + new Vector2(r * 0.4f, -r * 0.4f), ink, 3, true);
            }, () => Remove(playlist));
        }
        if (_note is not null)
            Centred(_note, Round, 15, Accent, new Rect2(24, 182, Size.X - 48, 22));
        if (Pages > 1)
            for (var p = 0; p < Pages; p++)
                DrawCircle(new Vector2(Size.X / 2 + (p - (Pages - 1) / 2f) * 20, 218), p == _page ? 6 : 4.5f, p == _page ? Accent : Ink with { A = 0.35f });
        Button(new Rect2(14, 236, 230, 54), "+ ADD (PASTE LINK)", Paste, font: Round, size: 19);
        Button(new Rect2(256, 236, 130, 54), "BACK", () =>
        {
            _listing = false;
            _note = null;
        }, size: 14);
    }

    /// <summary>START: the playlist from its beginning (Spotify's own play button, pressed for them).</summary>
    private void Start(SavedPlaylist playlist)
    {
        Say($"Starting {playlist.Name}…", 8000);
        _ = Started();

        async Task Started()
        {
            var started = await music.StartPlaylist(playlist.Link, playlist.Name);
            Say(started ? $"Playing {playlist.Name}" : "Opened: press play on it in Spotify");
        }
    }

    /// <summary>The star: this one starts focus sessions (null: none; what played last carries on).</summary>
    private void Choose(SavedPlaylist? playlist)
    {
        settings.Save(settings.Now with { FocusPlaylist = playlist?.Link, FocusMusic = playlist is not null || settings.Now.FocusMusic });
        Say(playlist is null ? "Focus: music plays on" : $"Focus starts {playlist.Name}");
    }

    private void Remove(SavedPlaylist playlist)
    {
        var chosen = settings.Now.FocusPlaylist == playlist.Link ? null : settings.Now.FocusPlaylist;
        settings.Save(settings.Now with { Playlists = [.. Saved.Where(p => p != playlist)], FocusPlaylist = chosen });
        _page = Math.Min(_page, Pages - 1);
        Say($"Taken off: {playlist.Name}");
    }

    /// <summary>ADD: the playlist link on the clipboard (copied in the music app).</summary>
    private void Paste()
    {
        if (Companion.Paper.OffScreen)
        {
            GD.Print("Music: a test run never reads the clipboard (musiclink:<link> pretends)");
            return;
        }
        _ = Save(DisplayServer.ClipboardGet());
    }

    /// <summary>Keeps a playlist link, if it is one, under the name Spotify gives it. The first one kept starts focus sessions.</summary>
    public async Task Save(string link)
    {
        link = link.Trim();
        if (Music.Address(link) is null)
        {
            Say("No playlist link on the clipboard");
            return;
        }
        if (Saved.Any(p => Music.Address(p.Link) == Music.Address(link)))
        {
            Say("Saved already");
            return;
        }
        Say("Adding…");
        var name = await Music.NameOf(link) ?? $"Playlist {Saved.Length + 1}";
        var first = Saved.Length == 0;
        settings.Save(settings.Now with
        {
            Playlists = [.. Saved, new SavedPlaylist(link, name)],
            FocusPlaylist = first ? link : settings.Now.FocusPlaylist,
            FocusMusic = first || settings.Now.FocusMusic,
        });
        _listing = true;
        _page = Pages - 1;
        GD.Print($"Music: playlist saved, \"{name}\" ({Music.Shown(link)})");
        Say(first ? $"Added {name}: it starts your focus" : $"Added {name}");
    }

    private void Say(string note, ulong forMs = 3500)
    {
        _note = note;
        _noteUntil = Time.GetTicksMsec() + forMs;
        QueueRedraw();
    }

    // ---- Pictures ----------------------------------------------------------------------------------------------------

    private void Star(Vector2 c, float r, Color ink, bool filled)
    {
        var points = Enumerable.Range(0, 10).Select(i => c + Vector2.Up.Rotated(Mathf.Pi / 5 * i) * (i % 2 == 0 ? r : r * 0.45f)).ToArray();
        if (filled)
            DrawColoredPolygon(points, ink);
        DrawPolyline([.. points, points[0]], ink, 2.5f, true);
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
