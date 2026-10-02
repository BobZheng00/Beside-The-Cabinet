// Draws a 3D character the way the cabinet art is drawn: two flat tones split by one fixed light, colours pulled
// into the skin's palette, a wash of light from the CRT, and an ink line of constant width on screen.
// Variants are assembled in ToonMaterials.cs with defines: CUTOUT, BLEND, OUTLINE.

uniform sampler2D albedo_tex : source_color, hint_default_white;
uniform vec4 albedo : source_color = vec4(1.0);
uniform float cutoff = 0.5;

uniform vec3 light_dir = vec3(-0.45, 0.75, 0.5);                // world space, towards the light
uniform vec3 shade_color : source_color = vec3(0.74, 0.68, 0.88); // what the unlit tone is multiplied by
uniform float shade_amount = 1.0;                                // 0 = never shaded
uniform float shade_shift = 0.0;                                 // moves the tone boundary towards the light
uniform float shade_edge = 0.04;                                 // softness of the boundary

uniform vec3 tone_dark : source_color = vec3(0.0);               // palette ramp the texture's values are mapped onto
uniform vec3 tone_mid : source_color = vec3(0.5);
uniform vec3 tone_light : source_color = vec3(1.0);
uniform float tone_amount = 0.0;                                 // 0 = texture colours as authored
uniform float tone_below = 2.0;                                  // only texels darker than this value are remapped
uniform vec3 plain_color : source_color = vec3(0.5);             // replaces colourless texels (white cloth painted on skin)
uniform float plain_below = 0.0;                                 // saturation under which a texel counts as colourless

uniform vec3 glow_pos = vec3(0.0);                               // world position of the CRT
uniform vec3 glow_color : source_color = vec3(1.0, 0.56, 0.86);
uniform float glow_amount = 0.0;

uniform vec3 ink : source_color = vec3(0.118, 0.086, 0.188);
uniform float ink_px = 4.0;                                      // line width in viewport pixels

#ifdef OUTLINE
void vertex() {
	// Push the hull out along the normal by a fixed number of pixels, whatever the distance.
	vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 view_normal = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);
	vec4 clip = PROJECTION_MATRIX * view_pos;
	vec4 clip_out = PROJECTION_MATRIX * vec4(view_pos.xyz + view_normal * 0.01, 1.0);
	vec2 dir = (clip_out.xy / clip_out.w - clip.xy / clip.w) * VIEWPORT_SIZE;
	float len = length(dir);
	if (len > 0.0001) {
		clip.xy += dir / len * ink_px * 2.0 / VIEWPORT_SIZE * clip.w;
	}
	POSITION = clip;
}
#endif

void fragment() {
	vec4 tex = texture(albedo_tex, UV) * albedo;
#ifdef CUTOUT
	if (tex.a < cutoff) {
		discard;
	}
#endif
#ifdef OUTLINE
	ALBEDO = ink;
#else
	vec3 base = tex.rgb;
	float value = pow(dot(base, vec3(0.2126, 0.7152, 0.0722)), 1.0 / 2.2);
	float high = max(base.r, max(base.g, base.b));
	float saturation = (high - min(base.r, min(base.g, base.b))) / max(high, 0.001);
	vec3 ramp = value < 0.5 ? mix(tone_dark, tone_mid, value * 2.0) : mix(tone_mid, tone_light, value * 2.0 - 2.0 + 1.0);
	base = mix(base, ramp, tone_amount * (1.0 - smoothstep(tone_below - 0.12, tone_below, value)));
	base = mix(base, plain_color, 1.0 - smoothstep(plain_below * 0.5, plain_below + 0.0001, saturation));

	vec3 normal = normalize(mat3(INV_VIEW_MATRIX) * NORMAL);
	float lit = smoothstep(-shade_edge, shade_edge, dot(normal, normalize(light_dir)) - shade_shift);
	vec3 colour = mix(base * shade_color, base, mix(1.0, lit, shade_amount));

	vec3 at = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float facing = dot(normal, normalize(glow_pos - at));
	colour += glow_color * glow_amount * smoothstep(0.55, 0.8, facing);
	ALBEDO = colour;
#endif
#ifdef BLEND
	ALPHA = tex.a;
#endif
}
