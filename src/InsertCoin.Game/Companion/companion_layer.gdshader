shader_type canvas_item;
render_mode blend_premul_alpha;

// Shows the companion's 3D viewport as a layer of the cabinet picture and inks its silhouette with the same
// round, constant-width stroke the art uses. The viewport texture is premultiplied (transparent background).

uniform vec4 ink : source_color = vec4(0.118, 0.086, 0.188, 1.0);
uniform float width_px = 7.0;        // stroke width in texture pixels

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
	COLOR = vec4(colour.rgb + ink.rgb * (spread - colour.a), spread);
}
