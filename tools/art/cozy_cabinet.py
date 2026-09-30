"""Generates the Cozy skin cabinet (art/cozy/cabinet-v5.svg).

The cabinet is a small 3D model rendered to SVG with a real perspective camera (two-point: verticals stay vertical,
viewer front-right and above). Faces are back-face culled and painted back to front. Flat artwork is authored in each
face's local 2D coordinates: large shapes are projected point by point (exact perspective); small decals and text use
the local affine approximation of the projection at their centre.

    python tools/art/cozy_cabinet.py          # writes art/cozy/cabinet-v5.svg

Version history: v4 (oblique projection) is tagged art-v4-traditional; v3 (boxy) is tagged art-v3-boxy.
"""
import math
from pathlib import Path

# ---- Style -------------------------------------------------------------------------------------------------------
OUT = "#1E1630"
STROKE = f'stroke="{OUT}" stroke-width="3.5" stroke-linejoin="round"'
THIN = f'stroke="{OUT}" stroke-width="2.5" stroke-linejoin="round"'

# ---- Model (world units: x right, y up, z towards the back; kick plate stands on y = 0) ----------------------------
XI, T = 130, 14                     # half inner width, side panel thickness
XO = XI + T
BACK = 170
FLOOR = -14                         # levelling feet raise the cabinet
ROOF = 724

HOOD_TOP, HOOD_BOT = (0, ROOF), (0, 704)
MARQ_BOT = (0, 630)
SCREEN_TOP, SCREEN_BOT = (40, 624), (26, 380)
CP_FRONT, CP_LIP_BOT = (-30, 338), (-30, 306)
BODY_TOP, BODY_BOT = (4, 306), (4, 46)
KICK_TOP, KICK_BOT = (18, 46), (18, 0)
# Front edge of the side panels (z, y), top to bottom; also the outline of the side art.
EDGE = [HOOD_TOP, MARQ_BOT, (20, 384), CP_FRONT, CP_LIP_BOT, BODY_TOP, BODY_BOT, KICK_TOP, KICK_BOT]
SIDE = EDGE + [(BACK, 0), (BACK, ROOF)]


# ---- Vector helpers ----------------------------------------------------------------------------------------------
def add(a, b): return tuple(x + y for x, y in zip(a, b))
def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def mul(a, k): return tuple(x * k for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def unit(a): return mul(a, 1 / math.sqrt(dot(a, a)))


class Camera:
    """Horizontal view direction (zero pitch) with lens shift, so vertical edges stay vertical."""

    def __init__(self, yaw_deg, height, distance, target):
        psi = math.radians(yaw_deg)
        self.f = (-math.sin(psi), 0.0, math.cos(psi))
        self.r = (math.cos(psi), 0.0, math.sin(psi))
        self.c = (target[0] - self.f[0] * distance, height, target[2] - self.f[2] * distance)
        self.focal, self.ox, self.oy = 1.0, 0.0, 0.0

    def depth(self, p):
        return dot(sub(p, self.c), self.f)

    def __call__(self, p):
        d = sub(p, self.c)
        zc = dot(d, self.f)
        return self.ox + self.focal * dot(d, self.r) / zc, self.oy - self.focal * d[1] / zc

    def sees(self, point, normal):
        return dot(normal, sub(self.c, point)) > 0

    def frame(self, points, width, margin):
        raw = [(dot(sub(p, self.c), self.r) / self.depth(p), -(p[1] - self.c[1]) / self.depth(p)) for p in points]
        x0, x1 = min(x for x, _ in raw), max(x for x, _ in raw)
        y0, y1 = min(y for _, y in raw), max(y for _, y in raw)
        self.focal = (width - 2 * margin) / (x1 - x0)
        self.ox = margin - x0 * self.focal
        self.oy = margin - y0 * self.focal
        return round((y1 - y0) * self.focal + 2 * margin)


CAM = Camera(yaw_deg=30, height=900, distance=1900, target=(0, 360, 80))


class Plane:
    """Local 2D frame on a plane: a along u, b along v (v points 'down' the face). Normal = u x v (outward)."""

    def __init__(self, origin, u, v, length=None):
        self.o, self.u, self.v = origin, unit(u), unit(v)
        self.n = cross(self.u, self.v)
        self.length = length

    def at(self, a, b): return add(self.o, add(mul(self.u, a), mul(self.v, b)))
    def p(self, a, b): return CAM(self.at(a, b))
    def visible(self): return CAM.sees(self.o, self.n)

    def affine(self, a, b):
        p0, pu, pv = self.p(a, b), self.p(a + 1, b), self.p(a, b + 1)
        ju, jv = sub(pu, p0), sub(pv, p0)
        e, f = p0[0] - a * ju[0] - b * jv[0], p0[1] - a * ju[1] - b * jv[1]
        return f"matrix({ju[0]:.4f} {ju[1]:.4f} {jv[0]:.4f} {jv[1]:.4f} {e:.2f} {f:.2f})"

    def decal(self, a, b, content):
        return f'<g transform="{self.affine(a, b)}">{content}</g>'


def front_plane(top, bottom):
    (z1, y1), (z2, y2) = top, bottom
    return Plane((-XI, y1, z1), (1, 0, 0), (0, y2 - y1, z2 - z1), math.hypot(z2 - z1, y2 - y1))


def side_plane(x):
    return Plane((x, ROOF, 0), (0, 0, 1), (0, -1, 0))   # local a = z, b = ROOF - y; normal +x


# ---- 2D shape helpers ----------------------------------------------------------------------------------------------
def d(points, closed=True):
    s = "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in points)
    return s + (" Z" if closed else "")


def path(points, fill, extra="", closed=True):
    return f'<path d="{d(points, closed)}" fill="{fill}" {extra}/>'


def rrect(x, y, w, h, r, seg=7):
    pts = []
    for cx, cy, a0 in ((x + w - r, y + r, -90), (x + w - r, y + h - r, 0), (x + r, y + h - r, 90), (x + r, y + r, 180)):
        pts += [(cx + r * math.cos(math.radians(a0 + 90 * i / seg)), cy + r * math.sin(math.radians(a0 + 90 * i / seg)))
                for i in range(seg + 1)]
    return pts


def circle(cx, cy, r, n=44):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def on(plane, pts):
    return [plane.p(a, b) for a, b in pts]


def hull(points):
    pts = sorted(set((round(x, 2), round(y, 2)) for x, y in points))
    def half(seq):
        h = []
        for p in seq:
            while len(h) >= 2 and (h[-1][0] - h[-2][0]) * (p[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (p[0] - h[-2][0]) <= 0:
                h.pop()
            h.append(p)
        return h
    lower, upper = half(pts), half(list(reversed(pts)))
    return lower[:-1] + upper[:-1]


def cylinder(plane, a, b, r, h, side, cap, cap_stroke=STROKE, n=44):
    base = [plane.at(x, y) for x, y in circle(a, b, r, n)]
    top = [add(p, mul(plane.n, h)) for p in base]
    return path(hull([CAM(p) for p in base + top]), side, STROKE) + path([CAM(p) for p in top], cap, cap_stroke)


def box(corner, size, fills):
    """Axis-aligned box; draws the visible faces named in fills (front/right/top/left)."""
    (x, y, z), (w, h, dd) = corner, size
    faces = {
        "front": ([(x, y, z), (x + w, y, z), (x + w, y + h, z), (x, y + h, z)], (0, 0, -1)),
        "right": ([(x + w, y, z), (x + w, y, z + dd), (x + w, y + h, z + dd), (x + w, y + h, z)], (1, 0, 0)),
        "top": ([(x, y + h, z), (x + w, y + h, z), (x + w, y + h, z + dd), (x, y + h, z + dd)], (0, 1, 0)),
        "left": ([(x, y, z), (x, y, z + dd), (x, y + h, z + dd), (x, y + h, z)], (-1, 0, 0)),
    }
    return "".join(path([CAM(p) for p in pts], fills[k], STROKE) for k, (pts, n) in faces.items()
                   if k in fills and CAM.sees(pts[0], n))


# ---- Faces ---------------------------------------------------------------------------------------------------------
hood, marquee = front_plane(HOOD_TOP, HOOD_BOT), front_plane(HOOD_BOT, MARQ_BOT)
screen = front_plane(SCREEN_TOP, SCREEN_BOT)
cp_top, cp_lip = front_plane(SCREEN_BOT, CP_FRONT), front_plane(CP_FRONT, CP_LIP_BOT)
body, kick = front_plane(BODY_TOP, BODY_BOT), front_plane(KICK_TOP, KICK_BOT)
right_side, left_inner = side_plane(XO), side_plane(-XI)
roof = Plane((-XO, ROOF, BACK), (1, 0, 0), (0, 0, -1), BACK)
W = 2 * XI


def face(plane, fill, extra=STROKE):
    return path(on(plane, [(0, 0), (W, 0), (W, plane.length), (0, plane.length)]), fill, extra)


def defs():
    return '''<defs>
  <linearGradient id="sideFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6452A0"/><stop offset="1" stop-color="#46397A"/></linearGradient>
  <linearGradient id="frontFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8674C4"/><stop offset="1" stop-color="#6B59A7"/></linearGradient>
  <linearGradient id="cpFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#A897E0"/><stop offset="1" stop-color="#C6B8F4"/></linearGradient>
  <linearGradient id="innerFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2B2143"/><stop offset="1" stop-color="#3B2E5E"/></linearGradient>
  <linearGradient id="roofFill" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#B3A3EB"/><stop offset="1" stop-color="#9483D3"/></linearGradient>
  <linearGradient id="sign" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#C2B1F7"/><stop offset="1" stop-color="#F7A8DC"/></linearGradient>
  <radialGradient id="crt" cx="46%" cy="42%" r="72%"><stop offset="0" stop-color="#FFD8F6"/><stop offset="0.45" stop-color="#FF8EDB"/><stop offset="1" stop-color="#B8409F"/></radialGradient>
  <radialGradient id="gold" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFF3C0"/><stop offset="0.5" stop-color="#FFC94D"/><stop offset="1" stop-color="#D38A1C"/></radialGradient>
  <radialGradient id="silver" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFFFFF"/><stop offset="0.5" stop-color="#D5D9EA"/><stop offset="1" stop-color="#8E93B3"/></radialGradient>
  <radialGradient id="bronze" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFD9B8"/><stop offset="0.5" stop-color="#E0955E"/><stop offset="1" stop-color="#A45A2E"/></radialGradient>
  <radialGradient id="ball" cx="35%" cy="30%" r="70%"><stop offset="0" stop-color="#FFC6E6"/><stop offset="0.55" stop-color="#FF76C3"/><stop offset="1" stop-color="#C23F8E"/></radialGradient>
  <radialGradient id="cushion" cx="40%" cy="35%" r="70%"><stop offset="0" stop-color="#E7DEFF"/><stop offset="1" stop-color="#9F8BDB"/></radialGradient>
  <radialGradient id="bgGlow" cx="50%" cy="42%" r="62%"><stop offset="0" stop-color="#2C2140"/><stop offset="1" stop-color="#110C19"/></radialGradient>
  <pattern id="scan" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="4" height="1.4" fill="#5A1450" opacity="0.2"/></pattern>
  <filter id="glow" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="16"/></filter>
  <filter id="softGlow" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="4"/></filter>
  <filter id="softShadow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="9"/></filter>
  <filter id="contact" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="3"/></filter>
</defs>'''


def ground_shadow():
    foot = [CAM((x, FLOOR, z)) for x, z in ((-XO - 8, -44), (XO + 8, -44), (XO + 8, BACK + 12), (-XO - 8, BACK + 12))]
    contact = [CAM((x, FLOOR, z)) for x, z in ((-XO, 2), (XO, 2), (XO, BACK), (-XO, BACK))]
    return f'''<g id="ground-shadow">
  {path(foot, "#000", 'opacity="0.3" filter="url(#softShadow)"')}
  {path(contact, "#000", 'opacity="0.45" filter="url(#contact)"')}
</g>'''


def feet():
    fills = {"front": "#2E2542", "right": "#1A1326", "left": "#231A33"}
    return (f'<g id="feet">{box((XO - 58, FLOOR, BACK - 44), (58, -FLOOR, 36), fills)}'
            f'{box((-XO, FLOOR, 8), (58, -FLOOR, 36), fills)}{box((XO - 58, FLOOR, 8), (58, -FLOOR, 36), fills)}</g>')


def inner_wall():
    return f'<g id="inner-wall">{path(on(left_inner, [(z, ROOF - y) for z, y in SIDE]), "url(#innerFill)", STROKE)}</g>'


def screen_face():
    L = screen.length
    crt = rrect(30, 26, W - 60, 178, 26)
    content = f'''<g font-family="Consolas, Lucida Console, monospace" font-weight="bold" fill="#6A1C5E">
      <text x="46" y="52" font-size="14" letter-spacing="1">CREDIT 03</text>
      <path d="M170 42 q4 -8 8 0 q4 -8 8 0 l-8 9 z" fill="#E0457F"/>
      <text x="{W - 46}" y="52" font-size="14" text-anchor="end">x4</text>
      <text x="{W / 2}" y="122" font-size="54" text-anchor="middle" letter-spacing="2">57:47</text>
      <text x="{W / 2}" y="144" font-size="11" text-anchor="middle" letter-spacing="3" opacity="0.8">TIME LEFT</text>
      <rect x="56" y="164" width="{W - 112}" height="11" rx="5.5" fill="#6A1C5E" opacity="0.25"/>
      <rect x="56" y="164" width="{(W - 112) * 0.85:.1f}" height="11" rx="5.5" fill="#6A1C5E"/>
    </g>
    <ellipse cx="86" cy="56" rx="64" ry="24" fill="#FFFFFF" opacity="0.26" transform="rotate(-16 86 56)"/>'''
    holes = "".join(path(on(screen, circle(W / 2 + dx, 226, 3, 16)), "#221A33") for dx in (-24, -8, 8, 24))
    screen_poly = on(screen, [(0, 0), (W, 0), (W, L), (0, L)])
    return f'''<g id="screen">
  {face(screen, "#2E2542")}
  {path(on(screen, rrect(14, 12, W - 28, L - 34, 18)), "#3C3058", 'stroke="#251C38" stroke-width="3"')}
  {path(on(screen, crt), "url(#crt)")}
  <clipPath id="crtClip">{path(on(screen, crt), "#000")}</clipPath>
  <g clip-path="url(#crtClip)">{screen.decal(W / 2, 115, content)}{path(on(screen, crt), "url(#scan)")}</g>
  {path(on(screen, crt), "none", 'stroke="#8E2F7E" stroke-width="2.5" opacity="0.55"')}
  {holes}
  <clipPath id="screenFaceClip">{path(screen_poly, "#000")}</clipPath>
  <g clip-path="url(#screenFaceClip)" style="mix-blend-mode:screen">{path(on(screen, crt), "#FF6FD2", 'opacity="0.5" filter="url(#glow)"')}</g>
</g>'''


def marquee_face():
    L = marquee.length
    lights = "".join(path(on(hood, circle(18 + i * 22.4, 10, 4.2, 18)), "#FFE27A" if i % 2 == 0 else "#7A6A9E", THIN)
                     for i in range(11))
    sign = rrect(12, 8, W - 24, L - 16, 6)
    doodles = f'''<g stroke="{OUT}" stroke-linejoin="round">
      <path transform="translate(-118 2) scale(0.78) translate(112 -20)" d="M-112 20 q-12 0 -12 -10 q0 -10 11 -10 q3 -10 14 -9 q9 -8 19 1 q11 -1 11 10 q8 2 6 10 q-2 8 -11 8 z" fill="#FFFFFF" stroke-width="2.5"/>
      <path d="M92 -16 a10 10 0 1 0 8 17 a8 8 0 1 1 -8 -17 z" fill="#FFF3C4" stroke-width="2.5"/>
      <path d="M72 14 l2.6 5.6 6 .8 -4.4 4.2 1 6 -5.2 -2.8 -5.2 2.8 1 -6 -4.4 -4.2 6 -.8 z" fill="#FFF3C4" stroke-width="2"/>
      <circle cx="-34" cy="-20" r="2.5" fill="#FFF3C4" stroke="none"/><circle cx="46" cy="-18" r="2.2" fill="#FFF3C4" stroke="none"/>
    </g>
    <text x="14" y="9" text-anchor="middle" font-family="Segoe UI Black, Arial Rounded MT Bold, sans-serif" font-size="23"
          font-weight="900" fill="#FFFFFF" stroke="#8E2F7E" stroke-width="6" paint-order="stroke" letter-spacing="1">INSERT COIN</text>'''
    return f'''<g id="marquee">
  {face(hood, "#2E2542")}{lights}
  {face(marquee, "#3A2E52")}
  {path(on(marquee, sign), "url(#sign)", THIN)}
  <clipPath id="signClip">{path(on(marquee, sign), "#000")}</clipPath>
  <g clip-path="url(#signClip)">{marquee.decal(W / 2, L / 2, f'<g transform="translate({W / 2} {L / 2})">{doodles}</g>')}
    <g style="mix-blend-mode:screen">{path(on(marquee, rrect(30, 16, W - 60, L - 32, 10)), "#FFD0F2", 'opacity="0.55" filter="url(#softGlow)"')}</g>
  </g>
</g>'''


def control_panel():
    parts = [face(cp_top, "url(#cpFill)"),
             path(on(cp_top, [(0, cp_top.length - 2), (W, cp_top.length - 2)]), "none", 'stroke="#DCD2FF" stroke-width="3"', closed=False)]
    # Buttons: bezel ring (short dark cylinder) then the cap cylinder, all perpendicular to the panel.
    for a, b, r, cap in ((104, 36, 11, "#FF7AB0"), (196, 26, 9, "#8EF0D0"), (226, 30, 9, "#C6A8FF"),
                         (204, 50, 9, "#FF9EC7"), (234, 54, 9, "#FFE27A")):
        parts.append(cylinder(cp_top, a, b, r + 3.5, 2, "#2E2542", "#3E3257"))
        parts.append(cylinder(cp_top, a, b, r, 5, "#5E4E8E", cap))
    parts.append(cylinder(cp_top, 150, 38, 22, 2, "#2E2542", "#3E3257"))
    parts.append(cylinder(cp_top, 150, 38, 18, 6, "#B7791A", "url(#gold)"))
    top = add(cp_top.at(150, 38), mul(cp_top.n, 6))
    tick = [add(top, add(mul(cp_top.u, du), mul(cp_top.v, dv))) for du, dv in ((-8, 0), (-2, 5), (9, -5))]
    parts.append(path([CAM(p) for p in tick], "none", 'stroke="#8A5410" stroke-width="3.5" stroke-linecap="round"', closed=False))
    # Joystick: base plate, shaft along the panel normal, ball.
    parts.append(cylinder(cp_top, 58, 36, 20, 3, "#241B36", "#3E3257"))
    base = cp_top.at(58, 36)
    shaft = [add(add(base, mul(cp_top.n, 3)), add(mul(cp_top.u, 4 * math.cos(t)), mul(cp_top.v, 4 * math.sin(t))))
             for t in (2 * math.pi * i / 24 for i in range(24))]
    shaft_top = [add(p, mul(cp_top.n, 30)) for p in shaft]
    parts.append(path(hull([CAM(p) for p in shaft + shaft_top]), "#3E3257", STROKE))
    ball = add(base, mul(cp_top.n, 44))
    bx, by = CAM(ball)
    br = 14 * CAM.focal / CAM.depth(ball)
    parts.append(f'<circle cx="{bx:.1f}" cy="{by:.1f}" r="{br:.1f}" fill="url(#ball)" {STROKE}/>'
                 f'<ellipse cx="{bx - br * 0.32:.1f}" cy="{by - br * 0.38:.1f}" rx="{br * 0.34:.1f}" ry="{br * 0.22:.1f}" fill="#FFFFFF" opacity="0.75"/>')
    label = f'<text x="{W - 14}" y="22" text-anchor="end" font-family="Consolas, monospace" font-weight="bold" font-size="10" fill="#C9BCF2" letter-spacing="2">1P</text>'
    lip = (face(cp_lip, "#6A58A6")
           + path(on(cp_lip, [(10, 8), (W - 10, 8)]), "none", 'stroke="#8C79CB" stroke-width="3" stroke-linecap="round"', closed=False)
           + cp_lip.decal(W - 14, 22, label))
    return f'<g id="control-panel">{"".join(parts)}{lip}</g>'


def body_face():
    L = body.length
    parts = [face(kick, "#2A2138"), face(body, "url(#frontFill)"),
             path(on(body, rrect(10, 12, 12, L - 30, 6)), "#A796DE", 'opacity="0.5"'),
             path(on(body, [(0, 0), (W, 0), (W, 16), (0, 16)]), "#000", 'opacity="0.22"'),       # shade under the panel lip
             path(on(body, [(0, L - 26), (W, L - 26), (W, L), (0, L)]), "#5B4A91", 'opacity="0.55"')]

    # Coin door: a plate standing 5 units proud of the body.
    x0, y0, w, h = -XI + 72, BODY_TOP[1] - 24, 116, 104
    zf = BODY_TOP[0] - 5
    parts.append(box((x0, y0 - h, zf), (w, h, 5), {"front": "#5B4A91", "right": "#44376E", "top": "#7462B0"}))
    door = Plane((x0, y0, zf), (1, 0, 0), (0, -1, 0), h)
    parts.append(path(on(door, rrect(8, 8, w - 16, h - 16, 6)), "none", 'stroke="#7E6BC0" stroke-width="2.5"'))
    for sx in (26, 75):
        slot = rrect(sx, 20, 15, 30, 4)
        parts.append(path(on(door, slot), "#FFB347", 'opacity="0.85" filter="url(#softGlow)"'))
        parts.append(path(on(door, slot), "#FFB347", THIN))
        parts.append(path(on(door, circle(sx + 7.5, 64, 3.5, 16)), OUT))
    parts.append(door.decal(w / 2, 88, f'<text x="{w / 2}" y="92" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="10" fill="#C9BCF2" letter-spacing="2">15 MIN</text>'))

    # Coin tray: a recess 30 deep. From above-right we see its back wall, inner left wall and floor.
    ta, tb, tw, th, td = 54, 150, 120, 62, 30
    opening = on(body, [(ta, tb), (ta + tw, tb), (ta + tw, tb + th), (ta, tb + th)])
    zb = BODY_TOP[0]
    xa, xb = -XI + ta, -XI + ta + tw
    ytop, ybot = BODY_TOP[1] - tb, BODY_TOP[1] - tb - th
    back = [CAM(p) for p in ((xa, ytop, zb + td), (xb, ytop, zb + td), (xb, ybot, zb + td), (xa, ybot, zb + td))]
    left = [CAM(p) for p in ((xa, ytop, zb), (xa, ytop, zb + td), (xa, ybot, zb + td), (xa, ybot, zb))]
    floor = [CAM(p) for p in ((xa, ybot, zb), (xb, ybot, zb), (xb, ybot, zb + td), (xa, ybot, zb + td))]
    tray_floor = Plane((xa, ybot, zb + td), (1, 0, 0), (0, 0, -1), td)
    coins = "".join(cylinder(tray_floor, a, b, 13, 3, edge, cap, THIN)
                    for a, b, edge, cap in ((30, 16, "#8A4A22", "url(#bronze)"), (58, 20, "#7B7F9C", "url(#silver)"),
                                            (46, 8, "#B7791A", "url(#gold)"), (88, 14, "#B7791A", "url(#gold)")))
    parts.append(f'''<g id="coin-tray"><clipPath id="trayClip">{path(opening, "#000")}</clipPath>
    <g clip-path="url(#trayClip)">{path(back, "#1A1326")}{path(left, "#2E2542")}{path(floor, "#3A2E52")}{coins}</g>
    {path(opening, "none", STROKE)}</g>''')

    parts.append(path(on(body, rrect(190, 160, 52, 9, 3)), OUT))
    cat = f'''<g transform="rotate(-8)" stroke="{OUT}" stroke-linejoin="round">
      <path d="M-16 -8 l3 -12 8 7 h10 l8 -7 3 12 q3 18 -16 20 q-19 -2 -16 -20 z" fill="#FFFFFF" stroke-width="2.5"/>
      <circle cx="-6" cy="-2" r="2.2" fill="{OUT}" stroke="none"/><circle cx="7" cy="-2" r="2.2" fill="{OUT}" stroke="none"/>
      <path d="M-2 4 q2 2.5 5 0" fill="none" stroke-width="1.8"/>
      <circle cx="-11" cy="3" r="2.6" fill="#FFB3D1" stroke="none"/><circle cx="12" cy="3" r="2.6" fill="#FFB3D1" stroke="none"/></g>'''
    coin = f'''<g transform="rotate(-6)"><circle r="13" fill="url(#gold)" stroke="{OUT}" stroke-width="2.5"/>
      <text y="5" text-anchor="middle" font-family="Segoe UI Black, sans-serif" font-size="13" fill="#8A5410">$</text></g>'''
    parts.append(body.decal(40, 64, f'<g transform="translate(40 64)">{cat}</g>'))
    parts.append(body.decal(222, 66, f'<g transform="translate(222 66)">{coin}</g>'))
    ticket = f'''<g transform="translate(216 165) rotate(4)" stroke="{OUT}" stroke-linejoin="round">
      <path d="M-19 0 H19 V58 l-4.75 5 -4.75 -5 -4.75 5 -4.75 -5 -4.75 5 -4.75 -5 -4.75 5 -4.75 -5 Z" fill="#FFF4E6" stroke-width="2.5"/>
      <text y="22" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="12" fill="#C2185B" stroke="none">IOU</text>
      <text y="38" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="9" fill="#7A4A6E" stroke="none">15m</text>
      <path d="M-13 48 H13" stroke="#E0B8C8" stroke-width="2" stroke-dasharray="3 3"/></g>'''
    parts.append(f'<g id="iou-ticket">{body.decal(216, 190, ticket)}</g>')
    return f'<g id="body">{"".join(parts)}</g>'


def roof_face():
    return f'<g id="roof">{path(on(roof, [(0, 0), (2 * XO, 0), (2 * XO, BACK), (0, BACK)]), "url(#roofFill)", STROKE)}</g>'


def side_panel():
    parts = [path(on(right_side, [(z, ROOF - y) for z, y in SIDE]), "url(#sideFill)", STROKE)]
    stripe = [(z + 12, ROOF - y) for z, y in EDGE[:7]]
    stripe[0] = (12, 14)
    parts.append(path(on(right_side, stripe), "none", 'stroke="#C3B2FF" stroke-width="6" stroke-linejoin="round" stroke-linecap="round" opacity="0.9"', closed=False))
    ea, eb = 96, 300
    parts.append(path(on(right_side, circle(ea, eb, 32)), "#3E3263", STROKE))
    parts.append(path(on(right_side, circle(ea, eb, 23)), "url(#gold)", THIN))
    parts.append(right_side.decal(ea, eb, f'<g transform="translate({ea} {eb})"><path d="M-7 -10 h14 M0 -13 v26 M-8 9 h16" stroke="#8A5410" stroke-width="3.5" stroke-linecap="round"/></g>'))
    for a, b, s in ((64, 470, 6), (122, 520, 4.5)):
        parts.append(path(on(right_side, [(a, b - s), (a + s, b), (a, b + s), (a - s, b)]), "#FFE27A", THIN))
    parts.append(right_side.decal(58, 560, f'<g transform="translate(58 560) rotate(-12)"><path d="M0 10 l-11 -11 a6.5 6.5 0 0 1 11 -7.5 a6.5 6.5 0 0 1 11 7.5 z" fill="#FF7AB0" stroke="{OUT}" stroke-width="2.5"/></g>'))
    for a in (128, 140, 152):
        parts.append(path(on(right_side, [(a, 640), (a, 670)]), "none", 'stroke="#3A2E66" stroke-width="5" stroke-linecap="round"', closed=False))
    return f'<g id="side-panel">{"".join(parts)}</g>'


def trims():
    """Front edges of both side panels (T-molding): one quad per visible profile segment, painted far to near."""
    quads = []
    for x0, x1 in ((-XO, -XI), (XI, XO)):
        for (z1, y1), (z2, y2) in zip(EDGE, EDGE[1:]):
            normal = cross((1, 0, 0), unit((0, y2 - y1, z2 - z1)))
            corners = [(x0, y1, z1), (x1, y1, z1), (x1, y2, z2), (x0, y2, z2)]
            if CAM.sees(corners[0], normal):
                quads.append((sum(CAM.depth(c) for c in corners) / 4, [CAM(c) for c in corners]))
    quads.sort(key=lambda q: -q[0])
    return f'<g id="trims">{"".join(path(q, "#2A2138", STROKE) for _, q in quads)}</g>'


def bezier(p0, p1, p2, p3, n=24):
    return [tuple((1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t ** 2 * c + t ** 3 * e
                  for a, b, c, e in zip(p0, p1, p2, p3)) for t in (i / n for i in range(n + 1))]


def cable():
    # side socket -> sagging drop to the floor -> lying on the floor towards the front, ending in a plug
    hang = bezier((XO + 1, 170, 132), (XO + 64, 160, 146), (XO + 64, FLOOR, 118), (XO + 46, FLOOR, 70))
    lie = bezier((XO + 46, FLOOR, 70), (XO + 34, FLOOR, 36), (XO + 66, FLOOR, 4), (XO + 96, FLOOR, -8))
    pts = [CAM(p) for p in hang + lie[1:]]
    end = lie[-1]
    plug = box((end[0], FLOOR, end[2] - 8), (22, 12, 16), {"front": "#3E3257", "right": "#2E2542", "top": "#54466F"})
    return f'''<g id="cable">
  {path(pts, "none", f'stroke="{OUT}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"', closed=False)}
  {path(pts, "none", 'stroke="#3E3257" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"', closed=False)}
  {plug}
</g>'''


def props():
    sx, sz, seat_y, rad = -262, -80, 300, 58
    seat = Plane((sx, seat_y, sz), (1, 0, 0), (0, 0, -1))          # horizontal plane, normal +y
    ring = Plane((sx, 96, sz), (1, 0, 0), (0, 0, -1))
    legs = []
    for ang in (225, 315, 135, 45):
        a = math.radians(ang)
        top = (sx + 34 * math.cos(a), seat_y - 18, sz + 34 * math.sin(a))
        bot = (sx + 48 * math.cos(a), FLOOR, sz + 48 * math.sin(a))
        legs.append((CAM.depth(bot), [CAM(top), CAM(bot)]))
    legs.sort(key=lambda l: -l[0])

    def leg(seg):
        return (path(seg, "none", f'stroke="{OUT}" stroke-width="10" stroke-linecap="round"', closed=False)
                + path(seg, "none", 'stroke="#4A3C7A" stroke-width="5" stroke-linecap="round"', closed=False))

    ring_pts = on(ring, circle(0, 0, 44, 60))
    ring_svg = path(ring_pts, "none", f'stroke="{OUT}" stroke-width="9"') + path(ring_pts, "none", 'stroke="#4A3C7A" stroke-width="4"')
    top_pts = [seat.at(x, y) for x, y in circle(0, 0, rad, 60)]
    bottom_pts = [add(p, (0, -18, 0)) for p in top_pts]
    cushion = (path(hull([CAM(p) for p in bottom_pts + top_pts]), "#8A74CC", STROKE)
               + path([CAM(p) for p in top_pts], "url(#cushion)", STROKE)
               + "".join(path(on(seat, circle(dx, dy, 3.5, 12)), "#8C77CF") for dx, dy in ((-30, 0), (30, 0), (0, -26), (0, 26)))
               + path(on(seat, circle(0, 0, 6, 16)), "#FF9EC7", THIN))

    def floor_rect(cx, cz, w, h, rot):
        r = math.radians(rot)
        corners = [(cx + dx * math.cos(r) - dz * math.sin(r), cz + dx * math.sin(r) + dz * math.cos(r))
                   for dx, dz in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
        return [CAM((x, FLOOR, z)) for x, z in corners]

    tickets = (path(floor_rect(-180, -140, 40, 20, 18), "#FFF4E6", THIN) + path(floor_rect(-360, -150, 36, 18, -24), "#FFF4E6", THIN)
               + path(floor_rect(-176, -142, 22, 4, 18), "#C2185B"))
    coin = cylinder(Plane((-240, FLOOR, -120), (1, 0, 0), (0, 0, -1)), 0, 0, 12, 3, "#B7791A", "url(#gold)", THIN)
    shadow = [CAM((sx + 66 * math.cos(t), FLOOR, sz + 66 * math.sin(t))) for t in (2 * math.pi * i / 40 for i in range(40))]
    return f'''<g id="props">
  <g id="floor-bits">{tickets}{coin}</g>
  <g id="stool">
    {path(shadow, "#000", 'opacity="0.35" filter="url(#contact)"')}
    {"".join(leg(s) for _, s in legs[:2])}{ring_svg}{"".join(leg(s) for _, s in legs[2:])}
    {cushion}
  </g>
</g>'''


def anchors():
    def poly(name, plane, pts):
        return f'<polygon id="anchor-{name}" points="{" ".join(f"{x:.1f},{y:.1f}" for x, y in on(plane, pts))}"/>'
    return f'''<g id="anchors" display="none">
  {poly("screen", screen, [(30, 26), (W - 30, 26), (W - 30, 204), (30, 204)])}
  {poly("marquee", marquee, [(12, 8), (W - 12, 8), (W - 12, marquee.length - 8), (12, marquee.length - 8)])}
  {poly("check-button", cp_top, [(128, 16), (172, 16), (172, 60), (128, 60)])}
  {poly("joystick", cp_top, [(38, 16), (78, 16), (78, 56), (38, 56)])}
  {poly("coin-tray", body, [(54, 150), (174, 150), (174, 212), (54, 212)])}
  {poly("printer", body, [(190, 160), (242, 160), (242, 169), (190, 169)])}
</g>'''


def frame_points():
    pts = [(x, y, z) for x in (-XO, XO) for z, y in SIDE] + [(XO + 8, FLOOR, BACK + 12), (-XO - 8, FLOOR, -44)]
    pts += [(-262 + 70 * math.cos(t), y, -80 + 70 * math.sin(t)) for t in (i * math.pi / 8 for i in range(16)) for y in (FLOOR, 300)]
    pts += [(-380, FLOOR, -170), (XO + 130, FLOOR, -20), (XO + 70, 170, 140)]
    return pts


def svg(preview_bg=True):
    height = CAM.frame(frame_points(), 600, 26)
    parts = [defs()]
    if preview_bg:
        parts.append(f'<g id="preview-bg"><rect width="600" height="{height}" fill="url(#bgGlow)"/></g>')
    parts += [ground_shadow(), feet(), inner_wall(), screen_face(), marquee_face(), body_face(), control_panel(),
              roof_face(), side_panel(), trims(), cable(), props(), anchors()]
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 {height}" width="600" height="{height}">\n'
            f'<!-- INSERT COIN · Cozy skin · cabinet v5 (generated by tools/art/cozy_cabinet.py). Original artwork. -->\n'
            + "\n".join(parts) + "\n</svg>\n")


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[2]
    out = root / "art" / "cozy" / "cabinet-v5.svg"
    out.write_text(svg(), encoding="utf-8")
    print(f"wrote {out}")
