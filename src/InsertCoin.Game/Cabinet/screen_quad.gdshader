// Maps the offscreen CRT content onto the cabinet's (perspective) screen quad.
// inv_h_row0..2 are the rows of the homography from canvas coordinates to content UV (0..1).
shader_type canvas_item;

uniform sampler2D content : filter_linear_mipmap, repeat_disable;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;
uniform float brightness = 1.0;

varying vec2 canvas_pos;

void vertex() {
	canvas_pos = VERTEX;
}

void fragment() {
	vec3 p = vec3(canvas_pos, 1.0);
	float w = dot(inv_h_row2, p);
	vec2 uv = vec2(dot(inv_h_row0, p), dot(inv_h_row1, p)) / w;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		discard;
	}
	vec4 c = texture(content, uv);
	COLOR = vec4(c.rgb * brightness, c.a);
}
