using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Swaps a VRM's MToon materials for the cabinet-style toon shader, keeping each one's texture, alpha mode and
/// draw order. What a material is (skin, hair, cloth, face detail) is read from VRoid's material names.
/// </summary>
internal sealed class ToonMaterials
{
    private const string Include = "res://Companion/toon.gdshaderinc";
    private static readonly Color Ink = new("1E1630");               // the art's outline colour

    private readonly Dictionary<string, Shader> _shaders = [];
    private readonly Dictionary<Material, ShaderMaterial> _converted = [];

    /// <summary>Every surface material made (outlines excluded), for per-frame parameters.</summary>
    public IReadOnlyCollection<ShaderMaterial> All => _converted.Values;

    public void Apply(Node3D character, Vector3 lightDirection, Vector3 glowPosition, float inkPixels)
    {
        foreach (var mesh in character.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            for (var s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                if (mesh.Mesh.SurfaceGetMaterial(s) is ShaderMaterial source)
                    mesh.SetSurfaceOverrideMaterial(s, Convert(source, lightDirection, glowPosition, inkPixels));
    }

    private ShaderMaterial Convert(ShaderMaterial source, Vector3 lightDirection, Vector3 glowPosition, float inkPixels)
    {
        if (_converted.TryGetValue(source, out var done))
            return done;

        var variant = source.Shader.ResourcePath.GetFile();
        var blend = variant.Contains("_trans");
        var cutout = variant.Contains("cutout");
        var twoSided = variant.Contains("cull_off");
        var modes = "unshaded, " + (twoSided ? "cull_disabled" : "cull_back") + (variant.Contains("zwrite") ? ", depth_draw_always" : "");
        var define = blend ? "BLEND" : cutout ? "CUTOUT" : "";

        var material = new ShaderMaterial { Shader = Variant(modes, define), RenderPriority = source.RenderPriority };
        var texture = source.GetShaderParameter("_MainTex");
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("albedo", source.GetShaderParameter("_Color"));
        material.SetShaderParameter("cutoff", source.GetShaderParameter("_Cutoff"));
        material.SetShaderParameter("light_dir", lightDirection);
        material.SetShaderParameter("glow_pos", glowPosition);

        var name = source.ResourceName;
        var kind = name.Contains("_SKIN") ? (name.Contains("Face") ? Kind.Face : Kind.Skin)
                 : name.Contains("_HAIR") ? Kind.Hair
                 : name.Contains("_CLOTH") ? Kind.Cloth
                 : name.Contains("_EYE") || name.Contains("_FACE") ? Kind.Detail
                 : Kind.Cloth;
        switch (kind)
        {
            case Kind.Skin:
                material.SetShaderParameter("shade_color", new Color("E9C4DA"));
                Tone(material, "2B2143", "4A3C7A", "6B59A7", 0.9f, below: 0.6f);    // socks painted on the skin texture
                material.SetShaderParameter("plain_color", new Color("4A3C7A"));   // and white cloth, as dark shorts
                material.SetShaderParameter("plain_below", 0.09f);
                break;
            case Kind.Face:
                material.SetShaderParameter("shade_amount", 0f);                   // a face reads best flat
                break;
            case Kind.Detail:
                material.SetShaderParameter("shade_amount", 0f);
                break;
            case Kind.Hair:
                Tone(material, "1E1630", "4A3C7A", "A897E0", 0.8f);
                material.SetShaderParameter("cutoff", 0.8f);                       // trims the feathered strand tips to a clean edge
                break;
            case Kind.Cloth when name.Contains("Accessory"):
                Tone(material, "B8328A", "FF76C3", "FFD0EA", 1f);                  // the bow, in the joystick's pink
                break;
            case Kind.Cloth when name.Contains("Shoes"):
                Tone(material, "2A2138", "5B4B94", "C6B8F4", 0.9f);
                break;
            case Kind.Cloth when name.Contains("Bottoms"):
                Tone(material, "231A33", "5B4B94", "A897E0", 0.9f);
                break;
            case Kind.Cloth:
                Tone(material, "2B2143", "6B59A7", "FBF7FF", 0.9f);
                break;
        }

        if (source.NextPass is not null && kind != Kind.Detail)
        {
            var outline = new ShaderMaterial { Shader = Variant("unshaded, cull_front", cutout ? "OUTLINE\n#define CUTOUT" : "OUTLINE") };
            outline.SetShaderParameter("albedo_tex", texture);
            outline.SetShaderParameter("cutoff", source.GetShaderParameter("_Cutoff"));
            outline.SetShaderParameter("ink", Ink);
            outline.SetShaderParameter("ink_px", inkPixels);
            material.NextPass = outline;
        }
        _converted[source] = material;
        return material;
    }

    /// <summary>Maps the texture's values onto a dark-mid-light ramp of palette colours.</summary>
    private static void Tone(ShaderMaterial material, string dark, string mid, string light, float amount, float below = 2f)
    {
        material.SetShaderParameter("tone_dark", new Color(dark));
        material.SetShaderParameter("tone_mid", new Color(mid));
        material.SetShaderParameter("tone_light", new Color(light));
        material.SetShaderParameter("tone_amount", amount);
        material.SetShaderParameter("tone_below", below);
    }

    private Shader Variant(string renderModes, string define)
    {
        var key = renderModes + "|" + define;
        if (!_shaders.TryGetValue(key, out var shader))
        {
            var defines = define.Length > 0 ? $"#define {define}\n" : "";
            _shaders[key] = shader = new Shader { Code = $"shader_type spatial;\nrender_mode {renderModes};\n{defines}#include \"{Include}\"\n" };
        }
        return shader;
    }

    private enum Kind { Skin, Face, Hair, Cloth, Detail }
}
