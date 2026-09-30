// Draws a sprite only inside a convex quad given in canvas coordinates (the coin tray opening).
// Used instead of clip_children, which renders through a back buffer and loses window transparency.
shader_type canvas_item;

uniform vec2 q0;
uniform vec2 q1;
uniform vec2 q2;
uniform vec2 q3;

varying vec2 canvas_pos;

void vertex() {
	canvas_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

float side(vec2 a, vec2 b, vec2 p) {
	return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
}

void fragment() {
	float s0 = side(q0, q1, canvas_pos);
	float s1 = side(q1, q2, canvas_pos);
	float s2 = side(q2, q3, canvas_pos);
	float s3 = side(q3, q0, canvas_pos);
	bool inside = (s0 >= 0.0 && s1 >= 0.0 && s2 >= 0.0 && s3 >= 0.0) || (s0 <= 0.0 && s1 <= 0.0 && s2 <= 0.0 && s3 <= 0.0);
	if (!inside) {
		discard;
	}
}
