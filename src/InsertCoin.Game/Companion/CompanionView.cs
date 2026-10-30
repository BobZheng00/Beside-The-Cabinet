using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Companion;

/// <summary>
/// A 3D character sitting on the skin's stool, drawn as one more layer of the cabinet picture: an off-screen 3D
/// viewport whose camera is the one the art was drawn with, shaded and inked in the art's style.
/// </summary>
public partial class CompanionView : Node2D
{
    private const float BodyYawDegrees = 16;          // turned a little from the cabinet's facing towards the viewer
    private const float InkCanvasPixels = 1.6f;       // silhouette stroke, canvas units: finer than the cabinet's 3.5, she is a smaller, softer shape
    private const float InnerInkCanvasPixels = 0.8f;  // lines inside the figure
    private const double PointerRestSeconds = 5;      // she stops following a pointer that has been still this long
    private static readonly Vector3 LightDirection = new Vector3(-0.45f, 0.75f, 0.5f).Normalized();   // towards the art's light: above, viewer's left

    // Her outline for hit-testing: capsules between joints, radius in metres.
    private static readonly (string From, string To, float Radius)[] Body =
    [
        ("Hips", "Neck", 0.16f), ("Neck", "Head", 0.10f),
        ("LeftUpperArm", "LeftLowerArm", 0.05f), ("LeftLowerArm", "LeftHand", 0.05f),
        ("RightUpperArm", "RightLowerArm", 0.05f), ("RightLowerArm", "RightHand", 0.05f),
        ("LeftUpperLeg", "LeftLowerLeg", 0.09f), ("LeftLowerLeg", "LeftFoot", 0.06f), ("LeftFoot", "LeftToes", 0.06f),
        ("RightUpperLeg", "RightLowerLeg", 0.09f), ("RightLowerLeg", "RightFoot", 0.06f), ("RightFoot", "RightToes", 0.06f),
    ];

    internal static readonly string[] Formats = ["*.vrm", "*.glb", "*.gltf"];     // in order of preference

    private CabinetSkin _skin = null!;
    private Rig _rig = null!;
    private Node3D _character = null!;
    private Camera3D _camera = null!;
    private Puppet _puppet = null!;
    private Face _face = null!;
    private ToonMaterials _materials = null!;
    private Vector2I _lastMouse;
    private double _mouseStill = 1e9;
    private float _glow = 0.5f;
    private SubViewport _viewport = null!;
    private SpeechBubble _bubble = null!;
    private Vector2? _devPointer;
    private bool _active = true;

    /// <param name="character">A character from <see cref="Available"/>.</param>
    public void Build(CabinetSkin skin, Character character)
    {
        _skin = skin;
        var world = skin.World;
        var viewport = _viewport = new SubViewport
        {
            Size = (Vector2I)(skin.Canvas * skin.Scale),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        // The art's camera looks horizontally and shifts the lens instead of pitching, so verticals stay vertical:
        // an off-centre frustum. Near-plane metres per canvas unit = near / focal.
        const float near = 0.5f;
        var perUnit = near / world.Focal;
        // She stays at the 3D scene's origin and the camera is placed around her: the VRM plugin's hair physics
        // goes wrong for a character that is moved or turned in the world.
        var seat = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(BodyYawDegrees), 0)), world.ToMetres(world.Seat));
        var toHer = seat.AffineInverse();
        var view = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(world.CameraYawDegrees), 0)), world.ToMetres(world.CameraPosition));
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Frustum,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = skin.Canvas.Y * perUnit,
            FrustumOffset = new Vector2(skin.Canvas.X / 2 - world.Offset.X, world.Offset.Y - skin.Canvas.Y / 2) * perUnit,
            Near = near,
            Far = 30,
            Transform = toHer * view,
            Current = true,
        };
        viewport.AddChild(_camera);

        _character = character.Load() ?? throw new InvalidOperationException($"cannot load character '{character.Id}'");
        LetClothHang(_character);
        viewport.AddChild(_character);

        var screen = toHer * world.ToMetres(world.Screen);
        var slot = toHer * world.ToMetres(world.Slot);
        _materials = new ToonMaterials();
        _materials.Apply(_character, toHer.Basis * LightDirection, screen, InnerInkCanvasPixels * skin.Scale);
        var skeleton = _character.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault()
                       ?? throw new InvalidOperationException("the model has no skeleton, so it cannot be posed");
        var humanoid = HumanoidMap.Guess(skeleton);
        if (humanoid is not null && HumanoidMap.Required.FirstOrDefault(bone => !humanoid.ContainsKey(bone)) is { } missing)
            throw new InvalidOperationException($"the skeleton is not one this app recognises as a person (no {missing} found)");
        var fit = Fit(_character, skeleton, humanoid);
        _rig = new Rig(skeleton, humanoid, fit);
        var followers = humanoid is not null ? LetLooseBonesSway(skeleton, humanoid, fit, _rig.Size) : [];
        var footRestDrop = (world.Seat.Y - world.FootRest) / world.UnitsPerMetre;
        _face = new Face(_character);
        _puppet = new Puppet(_rig, _face, footRestDrop) { Viewer = _camera.Position, Screen = screen, Slot = slot, Followers = followers };
        BuildPadAndPencil(viewport);

        _lastMouse = DisplayServer.MouseGetPosition();
        var layer = new ShaderMaterial { Shader = GD.Load<Shader>("res://Companion/companion_layer.gdshader") };
        layer.SetShaderParameter("width_px", InkCanvasPixels * skin.Scale);
        AddChild(new Sprite2D { Texture = viewport.GetTexture(), Centered = false, Scale = Vector2.One / skin.Scale, Material = layer });
        var marquee = skin.Anchors.Marquee;
        _bubble = new SpeechBubble { Marquee = new Rect2(marquee[0], Vector2.Zero).Expand(marquee[1]).Expand(marquee[2]).Expand(marquee[3]) };
        // a window of its own, so it reads as well by a small cabinet as by a large one (see SpeechBubble)
        var window = new Window
        {
            Title = "Beside the Cabinet bubble", Borderless = true, Transparent = true, TransparentBg = true, Transient = true,
            Unfocusable = true, Visible = false,           // it never takes the keyboard
        };
        window.AddChild(_bubble);
        AddChild(window);
        if (SpeechBubble.Recording)                         // a clip records only the cabinet's window: the bubble is mirrored into it
        {
            _mirror = new Sprite2D { Texture = window.GetTexture(), Centered = false, ZIndex = 64, ZAsRelative = false, Visible = false };
            AddChild(_mirror);
            GD.Print("SpeechBubble: a clip: the bubble is mirrored into the cabinet's window, and kept inside it");
        }
    }

    private Sprite2D? _mirror;

    public void Play(Moment moment) => _puppet.Play(moment);

    /// <summary>How they sit and carry themselves (see <see cref="Companion.Manner"/>); changed, they move into it.</summary>
    public Manner Manner
    {
        set => _puppet.Manner = value;
    }

    /// <summary>A few seconds of how they carry themselves, to see a manner as it is chosen.</summary>
    public void ShowManner() => _puppet.ShowManner();

    /// <summary>How far her mouth is open as she speaks aloud (0–1), set every frame by her voice.</summary>
    public float Mouth
    {
        set => _face.Mouth = _puppet.Voice = value;
    }

    /// <summary>Shows a line in a speech bubble over her head.</summary>
    public void Say(string line)
    {
        if (_active)
            _bubble.Say(line, OverHead(), _skin.Canvas);
    }

    /// <summary>A reply is on its way: she thinks, with dots over her head, until <see cref="Continue"/> brings its words.</summary>
    public void BeginReply()
    {
        _thinking = true;
        _puppet.Play(Moment.Think);
        if (_active)
            _bubble.Begin(OverHead(), _skin.Canvas);
    }

    public void Continue(string words)
    {
        Answering();
        _bubble.Append(words);
    }

    private bool _thinking;

    /// <summary>How long after she sends a memo from her phone it comes out of the cabinet's slot.</summary>
    public const double SendSeconds = 0.8;

    /// <summary>She writes a memo on her phone, for as long as it is being written.</summary>
    public void Typing()
    {
        _thinking = true;
        _puppet.Play(Moment.Type);
    }

    /// <summary>The memo is written: she sends it, and glances at the slot as it prints.</summary>
    /// <returns>Seconds until the memo should come out of the slot (longer when she wasn't seen writing it: she types a moment first).</returns>
    public double SendMemo()
    {
        var typing = _puppet.Typing;
        _puppet.Play(Moment.Send);
        return (typing ? 0 : Puppet.SendAfterTyping) + SendSeconds;
    }

    /// <summary>She is using a tool mid-reply: she thinks again, until the rest of her reply comes.</summary>
    public void Working()
    {
        _thinking = true;
        _puppet.Play(Moment.Think);
    }

    /// <summary>The words of her reply have begun (or it ended without any): she stops thinking.</summary>
    private void Answering()
    {
        if (_thinking)
            _puppet.Play(Moment.Speak);
        _thinking = false;
    }

    /// <summary>How far her voice is through what it is saying (0 to 1), or null when silent: her bubble turns its pages with it.</summary>
    public Func<double?> Spoken
    {
        set => _bubble.Spoken = value;
    }

    /// <summary>Whether her bubble is a subtitle (her voice speaks another language): each page waits for the player to read it.</summary>
    public Func<bool> Subtitled
    {
        set => _bubble.Subtitled = value;
    }

    /// <summary>She waits for the player to answer her call: her bubble keeps it, with "Yes?" and "Not now" to click.</summary>
    public bool Waiting
    {
        set => _bubble.Waiting = value;
    }

    /// <summary>An answer on her bubble was clicked while she waits on her call: true for "Yes?", false for "Not now".</summary>
    public Action<bool> Answered
    {
        set => _bubble.Answered = value;
    }

    /// <summary>Something drawn over everything on the cabinet is over this canvas rectangle: her bubble gives way to it.</summary>
    public Func<Rect2, bool> BubbleUnder
    {
        set => _bubble.Under = value;
    }

    /// <summary>Her bubble was clicked, not on an answer, while she waits on her call.</summary>
    public Action BubbleClicked
    {
        set => _bubble.Clicked = value;
    }

    /// <summary>Dev: as if an answer on her bubble were clicked.</summary>
    public void DevAnswer(bool yes) => _bubble.DevAnswer(yes);

    /// <summary>Dev: as if her bubble's words were clicked.</summary>
    public void DevClickBubble() => _bubble.DevClick();

    /// <summary>Whether a screen point is on her bubble while it takes clicks (it waits on her call).</summary>
    public bool BubbleCovers(Vector2I screenPoint) => _active && _bubble.Covers(screenPoint);

    /// <summary>Her bubble's window in Windows, or 0 while it isn't showing.</summary>
    public nint BubbleWindow => _bubble.Handle;

    public void EndReply()
    {
        Answering();
        _bubble.End();
    }

    /// <summary>How much of the reply in her bubble the player has seen (see <see cref="SpeechBubble.Seen"/>).</summary>
    public int? Seen => _bubble.Seen;

    /// <summary>Her reply was cut off <paramref name="at"/> characters in: the bubble ends it there.</summary>
    public void Cut(int at) => _bubble.Cut(at);

    /// <summary>Stops talking mid-sentence: the bubble goes away now.</summary>
    public void Hush() => _bubble.Clear();

    // ---- The pad and pencil she takes notes with -------------------------------------------------------------------

    private static readonly string[] HandJoints =
        ["Hand", "IndexProximal", "MiddleProximal", "LittleProximal", "IndexIntermediate", "IndexDistal", "ThumbProximal", "ThumbDistal"];
    private Node3D? _pad, _pencil, _phone;

    /// <summary>
    /// A little paper pad with a pink binding and a yellow pencil, flat colours with an ink outline like hers. Only
    /// for characters with finger bones: the props are placed from the hands' joints every frame.
    /// </summary>
    private void BuildPadAndPencil(SubViewport viewport)
    {
        if (!HandJoints.All(j => _rig.Has("Left" + j) && _rig.Has("Right" + j)))
            return;
        var size = _rig.Size;
        _pad = new Node3D { Visible = false };
        // a small notebook, big enough to read as one at the cabinet's scale
        Inked(_pad, new BoxMesh { Size = new Vector3(0.10f, 0.008f, 0.13f) * size }, Paper.Sheet, Vector3.Zero);
        Inked(_pad, new BoxMesh { Size = new Vector3(0.102f, 0.011f, 0.018f) * size }, new Color("FF76C3"), new Vector3(0, 0.001f, 0.058f) * size);
        _pencil = new Node3D { Visible = false };
        Inked(_pencil, new CylinderMesh { TopRadius = 0.005f * size, BottomRadius = 0.005f * size, Height = 0.10f * size, RadialSegments = 8 },
              new Color("FFC94D"), Vector3.Zero);
        Inked(_pencil, new CylinderMesh { TopRadius = 0.005f * size, BottomRadius = 0.001f * size, Height = 0.016f * size, RadialSegments = 8 },
              new Color("F2D2A9"), new Vector3(0, -0.058f, 0) * size);
        viewport.AddChild(_pad);
        viewport.AddChild(_pencil);

        // her phone, in a pastel mint case with rounded corners, its screen towards her; on its back, the side the
        // viewer sees, the camera: a dark island in the top corner with two lenses and a flash. Its own axes: x across
        // to her left, y out of the screen, z up its length.
        _phone = new Node3D { Visible = false };
        Rounded(_phone, new Vector3(0.072f, 0.009f, 0.15f) * size, 0.011f * size, new Color("A6E3D0"), Vector3.Zero, inked: true);
        Rounded(_phone, new Vector3(0.064f, 0.002f, 0.142f) * size, 0.008f * size, new Color("2E2748"), new Vector3(0, 0.0046f, 0) * size, inked: false);
        var island = new Vector3(-0.0165f, -0.0058f, 0.0555f) * size;              // her right, at the top: the viewer's top left
        Rounded(_phone, new Vector3(0.027f, 0.003f, 0.027f) * size, 0.006f * size, new Color("3A3352"), island, inked: true);
        foreach (var (x, z, radius, colour) in new[] { (-0.0055f, 0.0060f, 0.0050f, "8D86A8"), (-0.0055f, -0.0060f, 0.0050f, "8D86A8"), (0.0065f, 0.0060f, 0.0022f, "FFF1C2") })
        {
            var at = island + new Vector3(x, -0.0017f, z) * size;
            _phone.AddChild(Plain(new CylinderMesh { TopRadius = radius * size, BottomRadius = radius * size, Height = 0.0012f * size, RadialSegments = 14 }, colour, at));
            if (colour == "8D86A8")                                                    // the lens glass inside its rim
                _phone.AddChild(Plain(new CylinderMesh { TopRadius = 0.0031f * size, BottomRadius = 0.0031f * size, Height = 0.0012f * size, RadialSegments = 14 },
                                      "141020", at - new Vector3(0, 0.0004f, 0) * size));
        }
        viewport.AddChild(_phone);
    }

    /// <summary>
    /// A rounded slab (a rounded rectangle across x and z, as thick as y): two crossed boxes and a cylinder at each corner,
    /// each outlined like the other props when <paramref name="inked"/>.
    /// </summary>
    private static void Rounded(Node3D parent, Vector3 size, float radius, Color colour, Vector3 at, bool inked)
    {
        var parts = new List<(PrimitiveMesh Mesh, Vector3 At)>
        {
            (new BoxMesh { Size = new Vector3(size.X, size.Y, size.Z - 2 * radius) }, at),
            (new BoxMesh { Size = new Vector3(size.X - 2 * radius, size.Y, size.Z) }, at),
        };
        foreach (var x in new[] { -1, 1 })
            foreach (var z in new[] { -1, 1 })
                parts.Add((new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = size.Y, RadialSegments = 12 },
                           at + new Vector3(x * (size.X / 2 - radius), 0, z * (size.Z / 2 - radius))));
        foreach (var (mesh, place) in parts)
        {
            if (inked)
                Inked(parent, mesh, colour, place);
            else
                parent.AddChild(Plain(mesh, colour.ToHtml(false), place));
        }
    }

    private static MeshInstance3D Plain(PrimitiveMesh mesh, string colour, Vector3 at) => new()
    {
        Mesh = mesh, Position = at,
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(colour) },
    };

    /// <summary>
    /// The phone lies between her palms, a little towards her and up from them (her hands hold its lower half), its
    /// screen facing where both palms face (their sideways parts cancel out: towards her); it pops in and out with the pose.
    /// </summary>
    private void PlacePhone()
    {
        if (_phone is null)
            return;
        var shown = Mathf.SmoothStep(0.35f, 0.9f, _puppet.Phoning);
        _phone.Visible = shown > 0.01f;
        if (!_phone.Visible)
            return;
        var size = _rig.Size;
        Vector3 PalmOf(string side) => (_rig.PositionOf(side + "Hand") + _rig.PositionOf(side + "MiddleProximal")) / 2;
        var (left, right) = (PalmOf("Left"), PalmOf("Right"));
        var across = (left - right).Normalized();
        var between = (left + right) / 2;
        var facing = HandFrame("Left").Palm + HandFrame("Right").Palm;
        var screen = (facing - across * facing.Dot(across)).Normalized();
        var along = across.Cross(screen);
        if (along.Y < 0)
            (across, along) = (-across, -along);
        var centre = between + screen * 0.010f * size + along * 0.035f * size;
        _phone.GlobalTransform = new Transform3D(new Basis(across, screen, along).Scaled(Vector3.One * shown), centre);
    }

    /// <summary>A flat-coloured part with an inverted-hull outline in the cabinet's ink.</summary>
    private static void Inked(Node3D parent, PrimitiveMesh mesh, Color colour, Vector3 at)
    {
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour } });
        var extent = mesh.GetAabb().Size;
        const float ink = 0.0016f;
        var grow = new Vector3((extent.X + 2 * ink) / extent.X, (extent.Y + 2 * ink) / extent.Y, (extent.Z + 2 * ink) / extent.Z);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh, Position = at, Scale = grow,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Paper.Ink, CullMode = BaseMaterial3D.CullModeEnum.Front },
        });
    }

    /// <summary>
    /// The notebook lies on her left palm, its page facing the way the palm does, and goes wherever the hand goes;
    /// the pencil is pinched between her right thumb and index fingertips, leaning about 50° to the page with its point
    /// on it, so it moves with her writing hand. Both pop in and out with the pose.
    /// </summary>
    private void PlacePadAndPencil()
    {
        if (_pad is null || _pencil is null)
            return;
        var shown = Mathf.SmoothStep(0.35f, 0.9f, _puppet.Noting);
        _pad.Visible = _pencil.Visible = shown > 0.01f;
        if (!_pad.Visible)
            return;
        var size = _rig.Size;

        // her frame: she faces +z, +x is her left
        var (fingers, palm) = HandFrame("Left");
        var face = palm;
        var centre = _rig.PositionOf("LeftMiddleProximal") + fingers * 0.02f * size + face * 0.024f * size;   // on palm and fingers, under her thumb
        var across = fingers;                                                            // the fingers run across the page
        var upPage = across.Cross(face);                                                 // where the binding is: the top, away from her
        if (upPage.Z < 0)
            (across, upPage) = (-across, -upPage);
        _pad.GlobalTransform = new Transform3D(new Basis(across, face, upPage).Scaled(Vector3.One * shown), centre);

        // the tripod grip: from between the thumb and index fingertips the point goes on past the fingertips and down to
        // the page, and the pencil's other end leans back over the web between thumb and index
        var grip = (Fingertip("RightIndexIntermediate", "RightIndexDistal") + Fingertip("RightThumbProximal", "RightThumbDistal")) / 2;
        var (penFingers, penPalm) = HandFrame("Right");
        var thumbSide = penFingers.Cross(penPalm);
        var down = (penFingers * 0.6f + penPalm * 0.6f - thumbSide * 0.25f).Normalized();
        var page = centre + face * 0.006f * size;
        var reach = Mathf.Clamp((page - grip).Dot(face) / Mathf.Min(down.Dot(face), -0.2f), 0.005f * size, 0.06f * size);
        var tip = grip + down * reach;
        var side = down.Cross(Vector3.Up).Normalized();
        var penBasis = new Basis(side, -down, side.Cross(-down).Normalized()).Scaled(Vector3.One * shown);
        _pencil.GlobalTransform = new Transform3D(penBasis, tip - down * 0.066f * size);
    }

    /// <summary>A posed hand's finger direction (wrist to knuckles) and the way its palm faces, in her space.</summary>
    private (Vector3 Fingers, Vector3 Palm) HandFrame(string side)
    {
        var wrist = _rig.PositionOf(side + "Hand");
        var fingers = (_rig.PositionOf(side + "MiddleProximal") - wrist).Normalized();
        var thumbward = _rig.PositionOf(side + "IndexProximal") - _rig.PositionOf(side + "LittleProximal");
        var palm = side == "Left" ? fingers.Cross(thumbward) : thumbward.Cross(fingers);
        return (fingers, (palm - fingers * palm.Dot(fingers)).Normalized());
    }

    /// <summary>Roughly where a finger ends: its last bone carried on as far again.</summary>
    private Vector3 Fingertip(string before, string last)
    {
        var joint = _rig.PositionOf(last);
        return joint + (joint - _rig.PositionOf(before)) * 0.9f;
    }

    /// <summary>Canvas point just above the top of her hair, where a speech bubble's tail points.</summary>
    private Vector2 OverHead()
    {
        var neck = _rig.PositionOf(_rig.Has("Neck") ? "Neck" : "Spine");
        var head = _rig.PositionOf("Head");
        var crown = head + (head - neck).Normalized() * (0.24f * _rig.Size);     // top of her hair, roughly
        return _camera.UnprojectPosition(crown) / _skin.Scale - new Vector2(0, 4);
    }

    /// <summary>
    /// Characters that can sit on the stool: the ones built in (every folder in res://characters holding a .vrm of
    /// the same name) and any model the user has put in their own characters folder.
    /// </summary>
    public static Character[] Available(string userFolder)
    {
        var builtIn = DirAccess.GetDirectoriesAt("res://characters")
            .Where(id => ResourceLoader.Exists($"res://characters/{id}/{id}.vrm"))
            .Select(id => new Character(id, $"res://characters/{id}/{id}.vrm", BuiltIn: true));
        // The user's own: a model file in the folder, or a folder holding one (how Sketchfab's glTF downloads unpack).
        string? ModelIn(string folder) => Formats.SelectMany(format => Directory.GetFiles(folder, format)).FirstOrDefault();
        var own = Directory.Exists(userFolder)
            ? Formats.SelectMany(format => Directory.GetFiles(userFolder, format))
                .Select(file => new Character(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), file, BuiltIn: false))
                .Concat(Directory.GetDirectories(userFolder).Where(folder => ModelIn(folder) is not null)
                    .Select(folder => new Character(Path.GetFileName(folder).ToLowerInvariant(), ModelIn(folder)!, BuiltIn: false)))
            : [];
        return [.. builtIn.Concat(own).DistinctBy(c => c.Id).OrderBy(c => c.Id)];
    }

    /// <summary>Shows her and keeps her moving, or hides her and stops drawing the 3D scene.</summary>
    public void SetActive(bool active)
    {
        _active = Visible = active;
        if (!active)
            _bubble?.Clear();                               // in a window of its own, it doesn't hide with her
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
    }

    /// <summary>Dev: which feet hang free of the stool's ring ("left", "right", "both", "ring").</summary>
    public void DevLegs(string which) => _puppet.Hang(which is "left" or "both", which is "right" or "both");

    /// <summary>Dev: a tune in their head now, as an idle moment has it.</summary>
    public void DevHum() => _puppet.DevHum();

    /// <summary>Dev: follow a pretend pointer at this canvas point from now on.</summary>
    public void DevPointer(Vector2 canvasPoint) => _devPointer = canvasPoint;

    private bool _closeUp;
    private (float Size, Vector2 Offset)? _lens;

    /// <summary>
    /// Dev: the same camera, its view narrowed onto her hands (and kept on them), to see fingers at a size where they
    /// can be judged; false puts the lens back.
    /// </summary>
    public void DevCloseUp(bool on)
    {
        _lens ??= (_camera.Size, _camera.FrustumOffset);
        _closeUp = on;
        if (!on)
            (_camera.Size, _camera.FrustumOffset) = _lens.Value;
    }

    private void FrameHands()
    {
        var hands = (_rig.PositionOf("LeftHand") + _rig.PositionOf("RightHand")) / 2;
        var local = _camera.Transform.AffineInverse() * hands;            // the camera looks along -z
        var near = _camera.Near / -local.Z;
        _camera.Size = 0.34f * _rig.Size * near;                          // about a forearm's length, top to bottom
        _camera.FrustumOffset = new Vector2(local.X, local.Y) * near;     // centred on them, at the near plane
    }

    /// <summary>
    /// A plain GLB has no physics, but game characters often carry chains of bones for hair, skirts and coat tails
    /// that the game would have simulated. Found by name, they get Godot's spring bones: no gravity (their rest
    /// shape already hangs right), just give, so they trail and settle when she moves. Her legs and the seat are
    /// solid to them, so a skirt lies over her thighs instead of passing through.
    /// </summary>
    /// <returns>Skirt panels and how far each should swing forward with the thigh below it (see Puppet.Followers).</returns>
    private static List<(string Bone, string Side, float Follow)> LetLooseBonesSway(Skeleton3D skeleton, IReadOnlyDictionary<string, int> humanoid, Transform3D toCharacter, float size)
    {
        var followers = new List<(string Bone, string Side, float Follow)>();
        string[] words = ["hair", "skirt", "piao", "tail", "ribbon", "cape", "cloth", "scarf", "dress", "bang", "ahoge"];
        var posed = humanoid.Values.ToHashSet();
        bool Loose(int bone) => bone >= 0 && !posed.Contains(bone) && words.Any(skeleton.GetBoneName(bone).ToLowerInvariant().Contains);
        int Next(int bone) => skeleton.GetBoneChildren(bone).Where(Loose).DefaultIfEmpty(-1).First();

        // Split each loose tree into paths: a path follows the first loose child; other children start their own.
        var chains = new List<(int Root, int End)>();
        for (var bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            var parent = skeleton.GetBoneParent(bone);
            if (!Loose(bone) || (Loose(parent) && Next(parent) == bone))
                continue;
            var end = bone;
            while (Next(end) >= 0)
                end = Next(end);
            if (end != bone)
                chains.Add((bone, end));
        }
        if (chains.Count == 0)
            return followers;

        // A skirt is made for standing: its panels hang straight down from the waist, which is through her thighs
        // once she sits. Each panel is swung forward with the thigh under it, fully at the front, not at all at
        // the back, and only as far as lays its hem on the thigh.
        var hips = toCharacter * skeleton.GetBoneGlobalRest(humanoid["Hips"]).Origin;
        foreach (var (root, _) in chains)
        {
            var name = skeleton.GetBoneName(root);
            if (!name.ToLowerInvariant().Contains("skirt") && !name.ToLowerInvariant().Contains("dress"))
                continue;
            var from = toCharacter * skeleton.GetBoneGlobalRest(root).Origin - hips;
            var around = Mathf.Atan2(Mathf.Abs(from.X), from.Z);        // 0 in front of her, pi behind
            followers.Add((name, from.X >= 0 ? "Left" : "Right", 0.78f * (Mathf.Cos(around) + 1) / 2));
        }

        var perMetre = 1 / toCharacter.Basis.Scale.X;                   // the skeleton's own units in a metre
        var springs = new SpringBoneSimulator3D();
        skeleton.AddChild(springs);
        springs.SettingCount = chains.Count;
        for (var i = 0; i < chains.Count; i++)
        {
            springs.SetRootBone(i, chains[i].Root);
            springs.SetEndBone(i, chains[i].End);
            springs.SetStiffness(i, 2.2f);
            springs.SetDrag(i, 0.35f);
            springs.SetGravity(i, 0);
            springs.SetRadius(i, 0.02f * size * perMetre);
        }

        // What the loose bones must stay out of. Legs are solid to everything. Arms are solid to hair only:
        // clothes lie on the body they belong to, and being pushed off it pulls a sleeve or a collar out of place.
        // Tried and dropped: the torso (it sweeps hair worn over the shoulders round to the back), the head (it
        // parts the fringe) and the seat (it throws coat tails upwards).
        // Each capsule is shrunk until every joint of the resting hair or cloth it applies to is outside it, and
        // left out if that makes it too thin to matter: a shape that swallows part of a chain throws it about.
        bool IsHair(int root) => new[] { "hair", "bang", "ahoge" }.Any(skeleton.GetBoneName(root).ToLowerInvariant().Contains);
        List<Vector3> Joints(bool hairOnly)
        {
            var joints = new List<Vector3>();
            foreach (var (root, tip) in chains)
                if (!hairOnly || IsHair(root))
                    for (var bone = tip; bone != root; bone = skeleton.GetBoneParent(bone))
                        joints.Add(skeleton.GetBoneGlobalRest(bone).Origin);
            return joints;
        }
        var everyJoint = Joints(hairOnly: false);
        var hairJoints = Joints(hairOnly: true);
        var clearance = 1.5f * 0.02f * size * perMetre;
        var forHairOnly = new List<SpringBoneCollision3D>();

        void Limb(string from, string to, float radius, bool hairOnly)
        {
            if (!humanoid.TryGetValue(from, out var bone) || !humanoid.TryGetValue(to, out var child) || skeleton.GetBoneParent(child) != bone)
                return;
            var joints = hairOnly ? hairJoints : everyJoint;
            if (joints.Count == 0)
                return;
            Vector3 a = skeleton.GetBoneGlobalRest(bone).Origin, b = skeleton.GetBoneGlobalRest(child).Origin;
            var wanted = radius * size * perMetre;
            var fitted = Mathf.Min(wanted, joints.Min(joint => Geometry3D.GetClosestPointToSegment(joint, a, b).DistanceTo(joint)) - clearance);
            if (fitted < 0.35f * wanted)
                return;
            var along = skeleton.GetBoneRest(child).Origin;             // the next joint, in this bone's own space
            var capsule = new SpringBoneCollisionCapsule3D { Name = $"{from}Solid" };
            springs.AddChild(capsule);
            capsule.Bone = bone;
            capsule.PositionOffset = along / 2;
            capsule.RotationOffset = along.Normalized().Dot(Vector3.Up) > -0.999f
                ? new Quaternion(Vector3.Up, along.Normalized())
                : new Quaternion(Vector3.Right, Mathf.Pi);
            capsule.Radius = fitted;
            capsule.Height = along.Length() + 2 * fitted;
            if (hairOnly)
                forHairOnly.Add(capsule);
        }
        foreach (var side in new[] { "Left", "Right" })
        {
            Limb(side + "UpperLeg", side + "LowerLeg", 0.08f, hairOnly: false);
            Limb(side + "LowerLeg", side + "Foot", 0.055f, hairOnly: false);
            Limb(side + "UpperArm", side + "LowerArm", 0.05f, hairOnly: true);
            Limb(side + "LowerArm", side + "Hand", 0.045f, hairOnly: true);
        }
        for (var i = 0; i < chains.Count; i++)
        {
            if (IsHair(chains[i].Root))
                continue;
            springs.SetExcludeCollisionCount(i, forHairOnly.Count);
            for (var k = 0; k < forHairOnly.Count; k++)
                springs.SetExcludeCollisionPath(i, k, springs.GetPathTo(forHairOnly[k]));
        }
        return followers;
    }

    /// <summary>
    /// Sizes and turns a model that is not a VRM so that it is in metres and faces +z, and returns what takes its
    /// skeleton's space to character space. (A VRM is in metres and faces +z by the format's rules.)
    /// </summary>
    private static Transform3D Fit(Node3D root, Skeleton3D skeleton, IReadOnlyDictionary<string, int>? humanoid)
    {
        var inRoot = Transform3D.Identity;
        for (Node node = skeleton; node != root && node is not null; node = node.GetParent())
            if (node is Node3D spatial)
                inRoot = spatial.Transform * inRoot;
        if (humanoid is null)
            return inRoot;

        Vector3 At(string bone) => inRoot * skeleton.GetBoneGlobalRest(humanoid[bone]).Origin;
        var leg = At("LeftUpperLeg").DistanceTo(At("LeftLowerLeg")) + At("LeftLowerLeg").DistanceTo(At("LeftFoot"));
        var scale = leg is > 0.3f and < 1.3f ? 1 : 0.8f / leg;          // centimetres, inches or a giant: make it person-sized
        var forward = (At("LeftUpperArm") - At("RightUpperArm")).Cross(Vector3.Up);
        var yaw = Mathf.Atan2(forward.X, forward.Z);
        root.Transform = new Transform3D(new Basis(Vector3.Up, -yaw).Scaled(Vector3.One * scale), Vector3.Zero);
        return root.Transform * inRoot;
    }

    /// <summary>
    /// VRoid exports hair and skirts with no gravity, only stiffness: fine standing still, but seated the skirt
    /// would stick out like a plate and a tilted head would carry its hair with it. Give both some weight.
    /// </summary>
    private static void LetClothHang(Node3D character)
    {
        if (character.GetNodeOrNull("secondary")?.Get("spring_bones").AsGodotArray() is not { } springs)
            return;
        foreach (var spring in springs.Select(s => s.AsGodotObject()))
        {
            if (spring.Get("gravity_scale").AsSingle() != 0)
                continue;                               // the model's author already gave it weight
            var skirt = spring.Get("joint_nodes").AsStringArray().FirstOrDefault("").Contains("Skirt");
            spring.Set("gravity_scale", skirt ? 0.7f : 0.3f);
        }
    }

    /// <summary>True when the canvas point is on her (so the window takes the click instead of passing it through).</summary>
    public bool Hits(Vector2 canvasPoint)
    {
        if (!_active)
            return false;
        Vector2 Canvas(Vector3 point) => _camera.UnprojectPosition(point) / _skin.Scale;
        var hips = _rig.PositionOf("Hips");
        var perMetre = (Canvas(hips + _camera.Basis.X) - Canvas(hips)).Length();
        var neck = _rig.PositionOf(_rig.Has("Neck") ? "Neck" : "Spine");
        var head = _rig.PositionOf("Head");
        var crown = head + (head - neck).Normalized() * 0.10f;
        if (canvasPoint.DistanceTo(Canvas(crown)) < 0.15f * perMetre)
            return true;
        foreach (var (from, to, radius) in Body)
        {
            if (!_rig.Has(from) || !_rig.Has(to))
                continue;
            var closest = Geometry2D.GetClosestPointToSegment(canvasPoint, Canvas(_rig.PositionOf(from)), Canvas(_rig.PositionOf(to)));
            if (canvasPoint.DistanceTo(closest) < radius * perMetre)
                return true;
        }
        return false;
    }

    /// <summary>How brightly the CRT lights her, 0..1 (follows the screen's power).</summary>
    public void SetScreenLight(float power) => _glow = power;

    public override void _Process(double delta)
    {
        if (!_active)
            return;
        if (_mirror is not null)
        {
            var shown = _bubble.OnCanvas;
            _mirror.Visible = shown is not null;
            if (shown is { } at)
            {
                _mirror.Position = at.Position;
                _mirror.Scale = at.Size / (Vector2)_bubble.GetWindow().Size;
            }
        }
        _puppet.Pointer = PointerTarget(delta);
        _puppet.Update(delta);
        PlacePadAndPencil();
        PlacePhone();
        if (_closeUp)
            FrameHands();
        // A faint, uneven draught keeps hair and skirt from ever hanging dead still.
        var t = Time.GetTicksMsec() / 1000f;
        var draught = new Vector3(0.07f * Mathf.Sin(t * 0.83f) + 0.04f * Mathf.Sin(t * 2.1f + 1), 0, 0.04f * Mathf.Sin(t * 0.61f + 2));
        _character.Set("springbone_add_force", draught);
        foreach (var material in _materials.All)
            material.SetShaderParameter("glow_amount", 0.14f * _glow);
    }

    /// <summary>
    /// A point for her to look at so that she appears to follow the mouse pointer: the viewer's position moved
    /// sideways and up by the pointer's offset from her head on screen.
    /// </summary>
    private Vector3? PointerTarget(double delta)
    {
        if (Paper.OffScreen && _devPointer is null)
            return null;                                    // a test run never follows the player's own pointer
        var mouse = DisplayServer.MouseGetPosition();
        _mouseStill = mouse == _lastMouse ? _mouseStill + delta : 0;
        _lastMouse = mouse;
        if (_mouseStill > PointerRestSeconds && _devPointer is null)
            return null;

        var windowScale = DisplayServer.WindowGetSize().X / _skin.Canvas.X;
        var cursor = _devPointer ?? (Vector2)(mouse - DisplayServer.WindowGetPosition()) / windowScale;
        var head = _camera.UnprojectPosition(_puppet.Head) / _skin.Scale;
        var offset = (cursor - head).LimitLength(1200) * 0.005f;                  // metres at the viewer per canvas unit
        var basis = _camera.Basis;
        return _camera.Position + basis.X * offset.X - basis.Y * offset.Y;
    }
}

/// <summary>A character that can sit on the stool: built into the app, or a model file in the user's folder.</summary>
public sealed record Character(string Id, string Path, bool BuiltIn)
{
    public string Name => Id.Length > 0 ? char.ToUpperInvariant(Id[0]) + Id[1..].Replace('_', ' ').Replace('-', ' ') : Id;

    /// <summary>
    /// Who they are, in their author's words. A character card in the user's characters folder comes first
    /// (<see cref="CardIn"/>); then <c>persona.md</c> in a built-in character's folder or in a user character's own
    /// folder, or <c>&lt;file name&gt;.md</c> beside a loose model file. Without either, a plain friendly companion. Each
    /// knows the name the player asked to be called by (the {{user}} of a card or a persona file becomes it too). Beside a
    /// persona file, their own cards in other languages: <c>persona.zh.md</c>, or <c>&lt;file name&gt;.zh.md</c> (see
    /// <see cref="InsertCoin.Mind.Persona.Languages"/>); a character card stands for them in every language.
    /// </summary>
    public InsertCoin.Mind.Persona Persona(string userFolder, string? playerName)
    {
        if (CardIn(userFolder) is { } cardFile && InsertCoin.Mind.CharacterCard.Read(File.ReadAllBytes(cardFile)) is { } card)
            return card.ToPersona(playerName);
        var text = Written("");
        var languages = InsertCoin.Mind.SpokenLanguage.Told.Select(language => (language, Text: Written($".{language}")))
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .ToDictionary(c => c.language, c => InsertCoin.Mind.Persona.Written(Name, c.Text!, playerName, c.language));
        return (string.IsNullOrWhiteSpace(text) ? InsertCoin.Mind.Persona.Plain(Name) with { Player = playerName } : InsertCoin.Mind.Persona.Written(Name, text, playerName))
            with { Languages = languages };
    }

    /// <summary>Their persona file's text, <c>persona&lt;ending&gt;.md</c> (or beside a loose model, its own name), or null.</summary>
    private string? Written(string ending)
    {
        if (BuiltIn)
        {
            var file = $"res://characters/{Id}/persona{ending}.md";
            return Godot.FileAccess.FileExists(file) ? Godot.FileAccess.GetFileAsString(file) : null;
        }
        var folder = System.IO.Path.GetDirectoryName(Path)!;
        var candidates = new[] { System.IO.Path.ChangeExtension(Path, $"{ending}.md") }
            .Concat(System.IO.Path.GetFileName(folder).Equals(Id, StringComparison.OrdinalIgnoreCase) ? [System.IO.Path.Combine(folder, $"persona{ending}.md")] : []);
        return candidates.Where(File.Exists).Select(File.ReadAllText).FirstOrDefault();
    }

    /// <summary>
    /// This character's card, if one has been given: <c>&lt;id&gt;.card.png</c> or <c>.json</c> in the user's
    /// characters folder, or <c>card.png</c> / <c>card.json</c> in the character's own folder there. Built-in
    /// characters can have one too.
    /// </summary>
    public string? CardIn(string userFolder) =>
        new[] { $"{Id}.card.png", $"{Id}.card.json", System.IO.Path.Combine(Id, "card.png"), System.IO.Path.Combine(Id, "card.json") }
            .Select(name => System.IO.Path.Combine(userFolder, name)).FirstOrDefault(File.Exists);

    /// <summary>Built-in characters were imported when the app was built; the user's own are read from disk now.</summary>
    public Node3D? Load()
    {
        if (BuiltIn)
            return GD.Load<PackedScene>(Path).Instantiate<Node3D>();
        if (Path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
            return GD.Load<GDScript>("res://Companion/vrm_loader.gd").Call("load_vrm", Path).As<Node3D>();
        // plain glTF / GLB: no VRM labels, the bones are worked out from their names (HumanoidMap)
        var document = new GltfDocument();
        var state = new GltfState();
        return document.AppendFromFile(Path, state) == Error.Ok ? document.GenerateScene(state) as Node3D : null;
    }
}
