// Maps the offscreen CRT content onto the cabinet's (perspective) screen quad.
// inv_h_row0..2 are the rows of the homography from canvas coordinates to content UV (0..1).
shader_type canvas_item;

uniform sampler2D content : filter_linear_mipmap, repeat_disable;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;
uniform float brightness = 1.0;
// 0 on the cabinet; 1 lifted out to face the player, where it shows the CRT up close: the glass bulging a little, its
// corners rounded, scanlines and a darker rim
uniform float lifted = 0.0;

varying vec2 canvas_pos;

void vertex() {
	canvas_pos = VERTEX;
}

void fragment() {
	vec3 p = vec3(canvas_pos, 1.0);
	float w = dot(inv_h_row2, p);
	vec2 uv = vec2(dot(inv_h_row0, p), dot(inv_h_row1, p)) / w;
	float up = clamp(lifted, 0.0, 1.0);
	vec2 off = uv - 0.5;
	uv = 0.5 + off * (1.0 + 0.05 * up * dot(off, off));
	vec2 corner = max(abs(uv - 0.5) - (0.5 - 0.045 * up), 0.0);
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || length(corner) > 0.045 * up) {
		discard;
	}
	vec4 c = texture(content, uv);
	float scan = 1.0 - 0.07 * up * (0.5 + 0.5 * sin(FRAGCOORD.y * 2.0944));      // every third pixel of the window
	float rim = 1.0 - 0.16 * up * smoothstep(0.3, 0.72, length(off));
	COLOR = vec4(c.rgb * brightness * scan * rim, c.a);
}
