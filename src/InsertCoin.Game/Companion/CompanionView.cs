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
    private const float InkCanvasPixels = 3.0f;       // silhouette stroke, canvas units (the art's is 3.5)
    private const float InnerInkCanvasPixels = 1.25f; // lines inside the figure
    private const double PointerRestSeconds = 5;
    private static readonly Vector3 LightDirection = new Vector3(-0.45f, 0.75f, 0.5f).Normalized();   // towards the art's light: above, viewer's left      // she stops following a pointer that has been still this long

    // Her outline for hit-testing: capsules between joints, radius in metres.
    private static readonly (string From, string To, float Radius)[] Body =
    [
        ("Hips", "Neck", 0.16f), ("Neck", "Head", 0.10f),
        ("LeftUpperArm", "LeftLowerArm", 0.05f), ("LeftLowerArm", "LeftHand", 0.05f),
        ("RightUpperArm", "RightLowerArm", 0.05f), ("RightLowerArm", "RightHand", 0.05f),
        ("LeftUpperLeg", "LeftLowerLeg", 0.09f), ("LeftLowerLeg", "LeftFoot", 0.06f), ("LeftFoot", "LeftToes", 0.06f),
        ("RightUpperLeg", "RightLowerLeg", 0.09f), ("RightLowerLeg", "RightFoot", 0.06f), ("RightFoot", "RightToes", 0.06f),
    ];

    private CabinetSkin _skin = null!;
    private Rig _rig = null!;
    private Node3D _character = null!;
    private Camera3D _camera = null!;
    private Puppet _puppet = null!;
    private ToonMaterials _materials = null!;
    private Vector2I _lastMouse;
    private double _mouseStill = 1e9;
    private float _glow = 0.5f;
    private SubViewport _viewport = null!;
    private Vector2? _devPointer;
    private bool _active = true;

    public void Build(CabinetSkin skin, string characterId)
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

        _character = GD.Load<PackedScene>($"res://characters/{characterId}/{characterId}.vrm").Instantiate<Node3D>();
        LetClothHang(_character);
        viewport.AddChild(_character);

        var screen = toHer * world.ToMetres(world.Screen);
        _materials = new ToonMaterials();
        _materials.Apply(_character, toHer.Basis * LightDirection, screen, InnerInkCanvasPixels * skin.Scale);
        var skeleton = _character.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().First();
        _rig = new Rig(skeleton);
        _puppet = new Puppet(_rig, new Face(_character)) { Viewer = _camera.Position, Screen = screen };

        _lastMouse = DisplayServer.MouseGetPosition();
        var layer = new ShaderMaterial { Shader = GD.Load<Shader>("res://Companion/companion_layer.gdshader") };
        layer.SetShaderParameter("width_px", InkCanvasPixels * skin.Scale);
        AddChild(new Sprite2D { Texture = viewport.GetTexture(), Centered = false, Scale = Vector2.One / skin.Scale, Material = layer });
    }

    public void Play(Moment moment) => _puppet.Play(moment);

    /// <summary>Shows her and keeps her moving, or hides her and stops drawing the 3D scene.</summary>
    public void SetActive(bool active)
    {
        _active = Visible = active;
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
    }

    /// <summary>Dev: follow a pretend pointer at this canvas point from now on.</summary>
    public void DevPointer(Vector2 canvasPoint) => _devPointer = canvasPoint;

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
        var neck = _rig.PositionOf("Neck");
        var head = _rig.PositionOf("Head");
        var crown = head + (head - neck).Normalized() * 0.10f;
        if (canvasPoint.DistanceTo(Canvas(crown)) < 0.15f * perMetre)
            return true;
        foreach (var (from, to, radius) in Body)
        {
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
        _puppet.Pointer = PointerTarget(delta);
        _puppet.Update(delta);
        foreach (var material in _materials.All)
            material.SetShaderParameter("glow_amount", 0.14f * _glow);
    }

    /// <summary>
    /// A point for her to look at so that she appears to follow the mouse pointer: the viewer's position moved
    /// sideways and up by the pointer's offset from her head on screen.
    /// </summary>
    private Vector3? PointerTarget(double delta)
    {
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
