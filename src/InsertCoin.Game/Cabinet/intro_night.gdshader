// The first run's intro: the dark room behind the cabinet, and the screen's light on it once it is on. Worked out in display values,
// then made linear for the viewport's HDR 2D and dithered there (near-black gradients band otherwise).
shader_type canvas_item;

uniform float aspect = 1.78;
uniform float fade = 1.0;          // 0: gone, the desktop shows
uniform vec2 glow_at = vec2(0.5);
uniform float glow_size = 0.6;     // as a fraction of the view's height
uniform float glow = 0.0;
uniform vec2 pool_at = vec2(0.5);  // the pool of light the beam leaves on the floor
uniform vec2 pool_size = vec2(0.5, 0.1);
uniform float pool = 0.0;

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

vec3 linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void fragment() {
	float away = length((UV - vec2(0.56, 0.46)) * vec2(aspect, 1.0));
	vec3 room = mix(vec3(0.085, 0.066, 0.115), vec3(0.02, 0.016, 0.032), smoothstep(0.0, 1.05, away));
	float d = length((UV - glow_at) * vec2(aspect, 1.0)) / glow_size;
	float near = clamp(1.0 - d, 0.0, 1.0);
	room += vec3(0.2, 0.085, 0.2) * glow * near * near;
	vec2 e = (UV - pool_at) * vec2(aspect, 1.0) / pool_size;
	float spot = clamp(1.0 - dot(e, e), 0.0, 1.0);
	room += vec3(0.16, 0.145, 0.2) * pool * spot * spot;
	// dithered by a step of the linear buffer either way: its first steps up from black show as 0, 12, 21... of 255
	float noise = (hash(FRAGCOORD.xy) + hash(FRAGCOORD.xy + 17.0) - 1.0) / 255.0;
	COLOR = vec4(max(linear(max(room, vec3(0.0))) + noise, vec3(0.0)), fade);
}
