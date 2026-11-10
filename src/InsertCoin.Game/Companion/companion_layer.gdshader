shader_type canvas_item;
render_mode blend_premul_alpha;

// Shows the companion's 3D viewport as a layer of the cabinet picture and inks its silhouette with the same
// round, constant-width stroke the art uses. The viewport texture is premultiplied (transparent background).

uniform vec4 ink : source_color = vec4(0.118, 0.086, 0.188, 1.0);
uniform float width_px = 7.0;        // stroke width in texture pixels
uniform sampler2D cabinet : filter_linear;   // the cabinet's picture: what they are behind while they arrive
uniform float behind_from = 2.0;     // from this far across (0 to 1) the cabinet hides them; 2: nowhere
uniform float behind_past = 2.0;     // and from this far across (inside the cabinet) nothing of them is drawn at all: a knee past its other edge
uniform float flash = 0.0;           // how white they are: appearing on the stool under a flash

const int RING = 16;

void fragment() {
	vec4 colour = texture(TEXTURE, UV);
	float spread = colour.a;
	for (int i = 0; i < RING; i++) {
		float angle = 6.2831853 * float(i) / float(RING);
		vec2 dir = vec2(cos(angle), sin(angle)) * TEXTURE_PIXEL_SIZE;
		spread = max(spread, texture(TEXTURE, UV + dir * width_px).a);
		spread = max(spread, texture(TEXTURE, UV + dir * width_px * 0.5).a);
	}
	vec4 shown = vec4(colour.rgb + ink.rgb * (spread - colour.a), spread);
	if (UV.x > behind_from)
		shown *= UV.x > behind_past ? 0.0 : 1.0 - smoothstep(0.35, 0.9, texture(cabinet, UV).a);
	COLOR = vec4(mix(shown.rgb, vec3(shown.a), flash), shown.a);
}
