using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Each character's portrait: their head and shoulders, from their own model, in the cabinet's shading on a clear
/// ground. Made once, one at a time, and kept in the profile (<see cref="PortraitFile"/>): made again when their model
/// changes. A model that can't be read, or isn't a person's shape, gets a silhouette instead.
/// </summary>
public partial class Portraits : Node
{
    private const int Size = 256;                           // pixels square

    /// <summary>The profile's portraits folder.</summary>
    public string Folder { get; init; } = "";

    private readonly Queue<(Character Character, string File)> _waiting = [];
    private readonly Dictionary<string, List<Action<Texture2D>>> _wanted = [];     // by file: who waits for it
    private bool _making;

    private static Texture2D? _silhouette;

    /// <summary>A head and shoulders in the paper's lines' colour, for someone without a portrait (yet).</summary>
    public static Texture2D Silhouette => _silhouette ??= ImageTexture.CreateFromImage(SilhouetteImage());

    /// <summary>
    /// A character's portrait as kept; or, until it is made, the silhouette, and <paramref name="made"/> is given the
    /// portrait once it is.
    /// </summary>
    public Texture2D Of(Character character, Action<Texture2D> made)
    {
        var file = FileOf(character);
        if (File.Exists(file) && Image.LoadFromFile(file) is { } kept)
            return ImageTexture.CreateFromImage(kept);
        if (!_wanted.TryGetValue(file, out var waiting))
        {
            _wanted[file] = waiting = [];
            _waiting.Enqueue((character, file));
        }
        waiting.Add(made);
        if (!_making)
            Next();
        return Silhouette;
    }

    /// <summary>Portraits of characters no longer there (a model the player removed) go.</summary>
    public void KeepOnly(IEnumerable<Character> present)
    {
        try
        {
            PortraitFile.KeepOnly(Folder, [.. present.Select(c => c.Id)]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.Print($"Portrait: old ones not cleared: {e.Message}");
        }
    }

    /// <summary>Their portrait's file: a built-in character's is made again with each new build of the app.</summary>
    private string FileOf(Character character) =>
        PortraitFile.For(Folder, character.Id, ProjectSettings.GlobalizePath(character.Path),
                         character.BuiltIn ? File.GetLastWriteTimeUtc(OS.GetExecutablePath()).Ticks.ToString() : "");

    private async void Next()
    {
        _making = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);    // the page that asked is drawn first: a model can take a while to load
        while (_waiting.TryDequeue(out var job))
        {
            Image? taken = null;
            try
            {
                taken = await Take(job.Character);
            }
            catch (Exception e)                             // a model of the player's that won't pose must not take the app down
            {
                GD.Print($"Portrait: {job.Character.Id}: {e.Message}");
            }
            var image = taken ?? SilhouetteImage();
            try
            {
                Directory.CreateDirectory(Folder);
                image.SavePng(job.File);
                PortraitFile.Kept(job.File);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.Print($"Portrait: {job.Character.Id} not kept: {e.Message}");      // shown all the same, and made again next time
            }
            GD.Print($"Portrait: {job.Character.Id} {(taken is null ? "is a silhouette" : "made")}");
            var texture = ImageTexture.CreateFromImage(image);
            foreach (var made in _wanted.Remove(job.File, out var waiting) ? waiting : [])
                made(texture);
        }
        _making = false;
    }

    /// <summary>Their head and shoulders, arms down from where they stand at rest, or null when the model can't give them.</summary>
    private async Task<Image?> Take(Character character)
    {
        if (!character.BuiltIn && !ModelFile.LooksWhole(character.Path))
            return null;
        if (character.Load() is not { } model)
            return null;
        var viewport = new SubViewport
        {
            Size = new Vector2I(Size, Size), TransparentBg = true, OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(model);
        try
        {
            if (model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault() is not { } skeleton)
                return null;
            var humanoid = HumanoidMap.Guess(skeleton);
            if (humanoid is not null && HumanoidMap.Required.Any(bone => !humanoid.ContainsKey(bone)))
                return null;
            var rig = new Rig(skeleton, humanoid, CompanionView.Fit(model, skeleton, humanoid));
            if (!rig.Has("Head") || !rig.Has("LeftUpperArm") || !rig.Has("RightUpperArm"))
                return null;
            skeleton.ResetBonePoses();                      // a model read at run time may come posed: the arms are lowered from rest
            rig.Rotate("LeftUpperArm", Rig.Euler(0, 0, -70));
            rig.Rotate("RightUpperArm", Rig.Euler(0, 0, 70));
            new ToonMaterials().Apply(model, CompanionView.LightDirection, new Vector3(1, 1, 1), 1);

            // framed by their shoulders' width, which a face's size follows more closely than their height does
            var shoulders = rig.RestOffset("RightUpperArm", "LeftUpperArm").Length();
            var target = rig.PositionOf("Head") + new Vector3(0, 0.3f * shoulders, 0);
            const float fov = 24;
            var camera = new Camera3D { Fov = fov, Near = 0.02f, Far = 20, Current = true };
            viewport.AddChild(camera);
            camera.Position = target + new Basis(Vector3.Up, Mathf.DegToRad(16)) * new Vector3(0, 0, shoulders / Mathf.Tan(Mathf.DegToRad(fov / 2)));
            camera.LookAt(target);
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return viewport.GetTexture().GetImage();
        }
        finally
        {
            viewport.QueueFree();
        }
    }

    private static Image SilhouetteImage()
    {
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        var colour = new Color("C9BFE6");
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2(x, y) / Size;
                var head = p.DistanceTo(new Vector2(0.5f, 0.4f)) < 0.17f;
                var shoulders = p.Y > 0.6f && ((p - new Vector2(0.5f, 1.02f)) / 0.4f).Length() < 1;
                if (head || shoulders)
                    image.SetPixel(x, y, colour);
            }
        return image;
    }
}
