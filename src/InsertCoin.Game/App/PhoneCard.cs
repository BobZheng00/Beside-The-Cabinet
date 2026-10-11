using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// "Send a photo from your phone": a card with a code to scan, opened from the slip's paper clip. While it shows, the
/// page behind the code (see <see cref="PhonePage"/>) takes photos from a phone on the same Wi-Fi; each lands on the
/// slip. Closing the card, or ten minutes, closes the page.
/// </summary>
public partial class PhoneCard : Window
{
    private static readonly TimeSpan OpenFor = TimeSpan.FromMinutes(10);
    private const int Module = 6, Quiet = 4;

    /// <summary>A photo came from the phone: its name and contents.</summary>
    public event Action<string, byte[]>? Received;

    private PhonePage? _page;
    private Label _said = null!;
    private DateTimeOffset _closesAt;

    /// <summary>Dev: the page's address (with its key), for a pretend phone.</summary>
    public string? DevAddress => _page?.Address;

    /// <param name="name">Hers, for the page.</param>
    /// <param name="thisPcOnly">A test run: the page listens on this PC alone.</param>
    public void Build(string name, bool thisPcOnly)
    {
        Title = "Send a photo from your phone";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;
        _page = PhonePage.Open(name, thisPcOnly);
        _closesAt = DateTimeOffset.Now + OpenFor;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 10);
        page.AddChild(column);
        column.AddChild(new Label { Text = "Send a photo from your phone", ThemeTypeVariation = "Heading", HorizontalAlignment = HorizontalAlignment.Center });
        if (_page is null)
        {
            column.AddChild(Hint("This PC doesn't seem to be on a home network (Wi-Fi or a cable to a router), so a phone can't reach it."));
            return;
        }
        GD.Print($"Phone: the page is open on {_page.Where} for {OpenFor.TotalMinutes:0} minutes");
        column.AddChild(new TextureRect
        {
            Texture = Code(_page.Address), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(240, 240), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        });
        column.AddChild(Hint($"Scan it with your phone's camera, on the same Wi-Fi as this PC, and choose a photo: it lands on the slip, to go " +
                             $"with what you say to {name} next. Or type the address: {_page.Address.Replace("http://", "")}"));
        column.AddChild(Hint("The first time, Windows may ask whether to let the cabinet take connections: allow it on private networks. " +
                             "The page closes with this card, and after ten minutes."));
        _said = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _said.AddThemeColorOverride("font_color", Paper.Good);
        column.AddChild(_said);
        var close = new Button { Text = "Close", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        close.Pressed += QueueFree;
        column.AddChild(close);
    }

    public void Open() => Paper.Open(this, new Vector2(420, 560));

    public override void _Process(double delta)
    {
        if (_page is null)
            return;
        while (_page.Received.TryDequeue(out var photo))
        {
            GD.Print($"Phone: a photo came ({photo.Data.Length / 1024} KB)");
            _said.Text = $"Got {photo.Name}: it's on the slip.";
            Received?.Invoke(photo.Name, photo.Data);
        }
        if (DateTimeOffset.Now > _closesAt)
            QueueFree();
    }

    public override void _ExitTree()
    {
        _page?.Dispose();
        if (_page is not null)
            GD.Print("Phone: the page is closed");
        _page = null;
    }

    /// <summary>The address as a QR code, black on white with its quiet zone, a few pixels to a module (drawn sharp).</summary>
    internal static ImageTexture Code(string address)
    {
        var modules = QrCode.Encode(address);
        var size = modules.GetLength(0);
        var side = (size + 2 * Quiet) * Module;
        var image = Image.CreateEmpty(side, side, false, Image.Format.Rgb8);
        image.Fill(Colors.White);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (modules[y, x])
                    image.FillRect(new Rect2I((x + Quiet) * Module, (y + Quiet) * Module, Module, Module), Colors.Black);
        return ImageTexture.CreateFromImage(image);
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0), HorizontalAlignment = HorizontalAlignment.Center };
}
