// A memo's paper projected onto its place on the cabinet (the slot it hangs from, or the side panel it is taped to),
// as the receipt is. The paper is drawn once and shrunk with mipmaps, so its lines stay smooth on a steep side.
// `reveal` is how much paper is out of the slot: the bottom edge emerges first, the top ends up at the slot.
shader_type canvas_item;

uniform sampler2D content : filter_linear_mipmap, repeat_disable;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;
uniform float reveal : hint_range(0.0, 1.0) = 1.0;

varying vec2 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec3 p = vec3(local_pos, 1.0);
	float w = dot(inv_h_row2, p);
	vec2 uv = vec2(dot(inv_h_row0, p), dot(inv_h_row1, p)) / w;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > reveal) {
		discard;
	}
	COLOR = texture(content, vec2(uv.x, uv.y + 1.0 - reveal)) * COLOR;
}
