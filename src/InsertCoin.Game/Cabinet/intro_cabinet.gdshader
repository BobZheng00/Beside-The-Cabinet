// The first run's intro: the cabinet's picture in a dark room. The room's light on it is the sprite's own modulate; the tube is
// dark, or lit from its middle line outwards; the dust lies on it until the first light.
shader_type canvas_item;

uniform float tube = 1.0;      // how much of the tube's height is lit (0: none)
uniform float flare = 0.0;     // the strike's extra brightness
uniform float scan = 0.0;      // scanlines, close up
uniform float dust = 0.0;
uniform vec2 canvas;
uniform vec3 inv_h_row0;
uniform vec3 inv_h_row1;
uniform vec3 inv_h_row2;

varying vec4 room;

void vertex() {
	room = COLOR;
}

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float cloud(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec4 art = texture(TEXTURE, UV);
	vec3 p = vec3(UV * canvas, 1.0);
	float w = dot(inv_h_row2, p);
	vec2 s = vec2(dot(inv_h_row0, p), dot(inv_h_row1, p)) / w;
	// the glass, by the art's own edge: the bright pixels within the screen's quad
	float glass = step(-0.04, s.x) * step(s.x, 1.04) * step(-0.04, s.y) * step(s.y, 1.04) * smoothstep(0.30, 0.45, art.r);
	float open = tube <= 0.0 ? 0.0 : 1.0 - smoothstep(tube * 0.5, tube * 0.5 + 0.012, abs(s.y - 0.5));
	float lines = 1.0 - 0.1 * scan * (0.5 + 0.5 * sin(FRAGCOORD.y * 2.0944));
	vec3 dark_glass = vec3(0.005, 0.0045, 0.008);
	vec3 shown = mix(art.rgb * room.rgb, mix(dark_glass, art.rgb * (1.0 + flare) * lines, open), glass);
	// a film of grime, thicker in patches, and specks of it
	vec2 cell = UV * canvas;
	float film = cloud(cell / 26.0) * cloud(cell / 8.0 + 5.0);
	float speck = step(0.982, hash(floor(cell * 1.3)));
	shown = mix(shown, room.rgb * vec3(1.0, 0.97, 1.05), dust * (1.0 - 0.35 * glass) * (0.32 * film + 0.7 * speck));
	COLOR = vec4(shown, art.a);
}
