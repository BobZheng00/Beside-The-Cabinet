// The first run's intro: one shaft of light in the dark room, laid over everything. Dithered, as a faint gradient
// over near-black bands otherwise.
// (blend_add takes the colour times the alpha: the alpha carries the light)
shader_type canvas_item;
render_mode blend_add;

uniform vec2 origin;               // where the light comes from, as a fraction of the view
uniform float aspect = 1.78;
uniform float strength = 0.0;
uniform float reach = 1.2;          // how far it goes, in the view's heights: to the floor
const float DIRECTION = 0.53;      // radians from straight down, towards the right
const float SPREAD = 0.17;

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

void fragment() {
	vec2 d = (UV - origin) * vec2(aspect, 1.0);
	float a = atan(d.x, d.y) - DIRECTION;
	float across = smoothstep(SPREAD, SPREAD * 0.3, abs(a));
	float far = length(d) / reach;
	float along = (1.0 - 0.35 * far) * (1.0 - smoothstep(0.84, 1.03, far));
	float streaks = 0.85 + 0.15 * sin(a * 95.0 + 1.3) * sin(a * 41.0);
	float light = across * along * streaks * strength * 0.1;
	// dithered by a step of the viewport's linear buffer (HDR 2D), whose steps near black are large on screen
	float noise = (hash(FRAGCOORD.xy + 3.0) + hash(FRAGCOORD.xy + 29.0) - 1.0) / 255.0 / 0.7;
	COLOR = vec4(vec3(0.62, 0.56, 0.78), max(0.0, light + noise * step(0.0005, light)));
}
