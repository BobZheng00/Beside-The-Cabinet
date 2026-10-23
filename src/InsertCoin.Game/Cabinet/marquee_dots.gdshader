// The marquee's pixel display: a board of round dots on the cabinet's (perspective) marquee, lit where the dots
// texture is white. inv_h_row0..2 are the rows of the homography from canvas coordinates to the board's UV (0..1).
// Dots too small to tell apart (a small cabinet) blend into an even glow, so the letters still read without shimmering.
shader_type canvas_item;

uniform sampler2D dots : filter_nearest, repeat_disable;
uniform sampler2D dots_soft : filter_linear, repeat_disable;
uniform vec2 grid;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;
uniform vec4 lit : source_color = vec4(1.0, 0.95, 0.99, 1.0);
uniform vec4 halo : source_color = vec4(1.0, 0.45, 0.85, 1.0);
uniform vec4 unlit : source_color = vec4(0.33, 0.24, 0.46, 0.6);

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
	vec2 cell = uv * grid;
	float px = max(length(dFdx(cell)), length(dFdy(cell)));       // cells per screen pixel
	float even = smoothstep(0.3, 0.6, px);
	float d = length(fract(cell) - 0.5);
	float round_dot = 1.0 - smoothstep(0.43 - px * 0.5, 0.43 + px * 0.5, d);
	float on = mix(texture(dots, uv).r, texture(dots_soft, uv).r, even);
	// lit dots run together into solid letters when too small to tell apart; unlit ones into a faint grid
	float shape = mix(round_dot, mix(0.55, 0.95, on), even);
	vec4 dot_colour = mix(unlit, lit, on);
	// a soft glow around the lit dots, from the board blurred by its linear filter
	vec4 glow = vec4(halo.rgb, texture(dots_soft, uv).r * 0.6);
	float a = dot_colour.a * shape;
	COLOR = vec4(mix(glow.rgb, dot_colour.rgb, a / max(a + glow.a * (1.0 - a), 0.001)), a + glow.a * (1.0 - a));
}
