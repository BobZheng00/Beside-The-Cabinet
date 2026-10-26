// The marquee's pixel display: a board of round pink LEDs on the cabinet's (perspective) marquee, each lit or dark as
// the dots texture says (0: dark, 1: lit; nothing in between). inv_h_row0..2 are the rows of the homography from canvas
// coordinates to the board's UV (0..1). Unlit LEDs stay faintly there. Dots too small to draw apart (a small cabinet)
// are drawn as square blocks that meet, so the letters stay crisp pixel letters: a dot's edge is eased over one screen
// pixel at most, where it falls between two, never blurred across the dot.
shader_type canvas_item;

uniform sampler2D dots : filter_nearest, repeat_disable;
uniform sampler2D dots_soft : filter_linear, repeat_disable;
uniform vec2 grid;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;
// one LED colour: the lit dot's rim, its hotter middle, the light it throws around it, and an unlit dot
uniform vec4 lit : source_color = vec4(1.0, 0.36, 0.69, 1.0);
uniform vec4 core : source_color = vec4(1.0, 0.66, 0.85, 1.0);
uniform vec4 halo : source_color = vec4(0.95, 0.22, 0.6, 1.0);
uniform vec4 unlit : source_color = vec4(0.36, 0.2, 0.4, 0.55);

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
	vec2 fw = max(fwidth(cell), vec2(0.0001));                    // cells per screen pixel, each way
	float px = max(fw.x, fw.y);
	float even = smoothstep(0.3, 0.6, px);
	float d = length(fract(cell) - 0.5);
	float round_dot = 1.0 - smoothstep(0.43 - px * 0.5, 0.43 + px * 0.5, d);
	// a small dot's own value, read from its middle until the last screen pixel before the next dot
	vec2 seam = floor(cell + 0.5);
	vec2 block = (seam + clamp((cell - seam) / fw, -0.5, 0.5)) / grid;
	float on = mix(texture(dots, uv).r, texture(dots_soft, block).r, even);
	float shape = mix(round_dot, mix(0.55, 1.0, on), even);
	// a lit LED is hotter in its middle; small, it is all one colour
	vec4 led = mix(lit, core, (1.0 - smoothstep(0.0, 0.32, d)) * (1.0 - even));
	vec4 dot_colour = mix(unlit, led, on);
	// a faint glow around the lit dots, from the board blurred by its linear filter
	vec4 glow = vec4(halo.rgb, texture(dots_soft, uv).r * 0.35);
	float a = dot_colour.a * shape;
	COLOR = vec4(mix(glow.rgb, dot_colour.rgb, a / max(a + glow.a * (1.0 - a), 0.001)), a + glow.a * (1.0 - a));
}
