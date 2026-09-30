"""Generates the Cozy skin cabinet (art/cozy/cabinet-v4.svg).

The cabinet is modelled, not drawn: a traditional arcade side profile is extruded between two side panels and
projected obliquely (viewer above-right). Flat artwork for each front face (marquee, screen, control panel, lower
body) and for the right side panel is authored in local 2D coordinates and mapped onto its plane with an affine
matrix, so perspective stays consistent everywhere.

    python tools/art/cozy_cabinet.py          # writes art/cozy/cabinet-v4.svg
"""
import math
from pathlib import Path

# ---- Projection -------------------------------------------------------------------------------------------------
A, B = 0.62, 0.30           # screen offset per unit of depth z: right A, up B
L, R, T = 190, 450, 13      # front opening between the side panels (x), side panel thickness
W = R - L
BACK = 104                  # cabinet depth
FLOOR = 842

# Front profile (z, y) from top to bottom; z grows backwards. This is also the side panels' front edge.
HOOD_TOP, HOOD_LIP = (0, 118), (0, 136)
MARQ_BOT = (8, 214)
SCREEN_TOP, SCREEN_BOT = (40, 222), (22, 472)
CP_BACK, CP_FRONT, CP_LIP = (18, 480), (-26, 520), (-26, 546)
BODY_TOP, BODY_BOT = (4, 560), (4, 800)
KICK_TOP, KICK_BOT = (16, 800), (16, FLOOR)
EDGE = [HOOD_TOP, HOOD_LIP, MARQ_BOT, (20, 474), CP_FRONT, CP_LIP, BODY_TOP, BODY_BOT, KICK_TOP, KICK_BOT]
SIDE = EDGE + [(BACK, FLOOR), (BACK, HOOD_TOP[1])]

OUT = "#1E1630"             # outline
STROKE = f'stroke="{OUT}" stroke-width="3.5" stroke-linejoin="round"'


def P(x, z, y):
    return x + A * z, y - B * z


def pts(points):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in points)


def poly(points, fill, extra=""):
    return f'<polygon points="{pts(points)}" fill="{fill}" {extra}/>'


class Face:
    """A front face between two profile points. Local coords: u across (0..W), v down the face (0..length)."""

    def __init__(self, top, bottom):
        (self.z1, self.y1), (self.z2, self.y2) = top, bottom
        self.length = math.hypot(self.z2 - self.z1, self.y2 - self.y1)

    def to_screen(self, u, v):
        k = v / self.length
        return P(L + u, self.z1 + (self.z2 - self.z1) * k, self.y1 + (self.y2 - self.y1) * k)

    @property
    def matrix(self):
        dz, dy = (self.z2 - self.z1) / self.length, (self.y2 - self.y1) / self.length
        return f"matrix(1 0 {A * dz:.5f} {dy - B * dz:.5f} {L + A * self.z1:.3f} {self.y1 - B * self.z1:.3f})"

    def quad(self, x0=L, x1=R):
        return [P(x0, self.z1, self.y1), P(x1, self.z1, self.y1), P(x1, self.z2, self.y2), P(x0, self.z2, self.y2)]

    def group(self, content, lift=0):
        g = f'<g transform="{self.matrix}">{content}</g>'
        return f'<g transform="translate(0 {-lift})">{g}</g>' if lift else g


SIDE_MATRIX = f"matrix({A} {-B} 0 1 {R + T} 0)"   # right panel outer face, local (z, y)
LEFT_INNER = f"matrix({A} {-B} 0 1 {L} 0)"          # left panel inner face, local (z, y)

hood, marquee = Face(HOOD_TOP, HOOD_LIP), Face(HOOD_LIP, MARQ_BOT)
screen = Face(SCREEN_TOP, SCREEN_BOT)
cp_top, cp_lip = Face(CP_BACK, CP_FRONT), Face(CP_FRONT, CP_LIP)
body, kick = Face(BODY_TOP, BODY_BOT), Face(KICK_TOP, KICK_BOT)


def defs():
    return f'''<defs>
  <linearGradient id="sideFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#5E4E96"/><stop offset="1" stop-color="#46397A"/></linearGradient>
  <linearGradient id="frontFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8471C0"/><stop offset="1" stop-color="#6C5AA8"/></linearGradient>
  <linearGradient id="cpFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#A493DD"/><stop offset="1" stop-color="#C3B5F2"/></linearGradient>
  <linearGradient id="sign" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#BFAEF6"/><stop offset="1" stop-color="#F7A8DC"/></linearGradient>
  <radialGradient id="crt" cx="46%" cy="42%" r="72%"><stop offset="0" stop-color="#FFD6F5"/><stop offset="0.45" stop-color="#FF8EDB"/><stop offset="1" stop-color="#B8409F"/></radialGradient>
  <radialGradient id="gold" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFF3C0"/><stop offset="0.5" stop-color="#FFC94D"/><stop offset="1" stop-color="#D38A1C"/></radialGradient>
  <radialGradient id="silver" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFFFFF"/><stop offset="0.5" stop-color="#D5D9EA"/><stop offset="1" stop-color="#8E93B3"/></radialGradient>
  <radialGradient id="bronze" cx="35%" cy="30%" r="75%"><stop offset="0" stop-color="#FFD9B8"/><stop offset="0.5" stop-color="#E0955E"/><stop offset="1" stop-color="#A45A2E"/></radialGradient>
  <radialGradient id="cushion" cx="40%" cy="35%" r="70%"><stop offset="0" stop-color="#E4DAFF"/><stop offset="1" stop-color="#9F8BDB"/></radialGradient>
  <radialGradient id="bgGlow" cx="50%" cy="42%" r="62%"><stop offset="0" stop-color="#2C2140"/><stop offset="1" stop-color="#110C19"/></radialGradient>
  <pattern id="scan" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="4" height="1.4" fill="#5A1450" opacity="0.2"/></pattern>
  <filter id="glow" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="16"/></filter>
  <filter id="softGlow" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="4"/></filter>
  <filter id="softShadow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="9"/></filter>
  <filter id="contact" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="3"/></filter>
</defs>'''


def ground_shadow():
    foot = [P(L - T - 8, -34, FLOOR + 10), P(R + T + 8, -34, FLOOR + 10), P(R + T + 8, BACK + 10, FLOOR + 10), P(L - T - 8, BACK + 10, FLOOR + 10)]
    contact = [P(L, 4, FLOOR + 14), P(R, 4, FLOOR + 14), P(R, 30, FLOOR + 14), P(L, 30, FLOOR + 14)]
    return f'''<g id="ground-shadow">
  {poly(foot, "#000", 'opacity="0.32" filter="url(#softShadow)"')}
  {poly(contact, "#000", 'opacity="0.5" filter="url(#contact)"')}
</g>'''


def side_panel():
    outline = [P(R + T, z, y) for z, y in SIDE]
    inset = [(z + 9, y) for z, y in EDGE[:8]]   # pastel pinstripe following the front edge
    stripe = " ".join(f"{z:.1f},{y:.1f}" for z, y in inset)
    return f'''<g id="side-panel">
  {poly(outline, "url(#sideFill)", STROKE)}
  <g transform="{SIDE_MATRIX}">
    <polyline points="{stripe}" fill="none" stroke="#C3B2FF" stroke-width="6" stroke-linejoin="round" stroke-linecap="round" opacity="0.9"/>
    <g id="side-emblem" transform="translate(62 318)">
      <circle r="27" fill="#3E3263" stroke="{OUT}" stroke-width="3.5"/>
      <circle r="20" fill="url(#gold)" stroke="{OUT}" stroke-width="3"/>
      <path d="M-6 -9 h12 M0 -12 v24 M-7 8 h14" stroke="#8A5410" stroke-width="3.5" stroke-linecap="round"/>
    </g>
    <path d="M44 600 l6 -6 6 6 -6 6 z M78 640 l4 -4 4 4 -4 4 z" fill="#FFE27A" stroke="{OUT}" stroke-width="2"/>
    <g id="side-sticker-heart" transform="translate(40 700) rotate(-12)">
      <path d="M0 10 l-11 -11 a6.5 6.5 0 0 1 11 -7.5 a6.5 6.5 0 0 1 11 7.5 z" fill="#FF7AB0" stroke="{OUT}" stroke-width="2.5"/>
    </g>
    <g stroke="#3A2E66" stroke-width="5" stroke-linecap="round">
      <path d="M72 770 v26"/><path d="M82 770 v26"/><path d="M92 770 v26"/>
    </g>
  </g>
</g>'''


def top_and_inner():
    top = [P(L - T, 0, HOOD_TOP[1]), P(R + T, 0, HOOD_TOP[1]), P(R + T, BACK, HOOD_TOP[1]), P(L - T, BACK, HOOD_TOP[1])]
    inner = [P(L, z, y) for z, y in SIDE]
    return f'''<g id="top">{poly(top, "#A392DE", STROKE)}</g>
<g id="inner-wall">{poly(inner, "#3A2D5C", STROKE)}</g>'''


def screen_face():
    q = screen.quad()
    content = f'''
    <rect x="0" y="0" width="{W}" height="{screen.length:.1f}" fill="#2E2542"/>
    <rect x="14" y="12" width="{W - 28}" height="{screen.length - 34:.1f}" rx="18" fill="#3C3058" stroke="#251C38" stroke-width="3"/>
    <rect x="30" y="26" width="{W - 60}" height="178" rx="26" fill="url(#crt)"/>
    <g id="screen-content" font-family="Consolas, Lucida Console, monospace" font-weight="bold" fill="#6A1C5E">
      <text x="46" y="52" font-size="14" letter-spacing="1">CREDIT 03</text>
      <path d="M168 42 q4 -8 8 0 q4 -8 8 0 l-8 9 z" fill="#E0457F"/>
      <text x="{W - 46}" y="52" font-size="14" text-anchor="end">x4</text>
      <text x="{W / 2}" y="122" font-size="54" text-anchor="middle" letter-spacing="2">57:47</text>
      <text x="{W / 2}" y="144" font-size="11" text-anchor="middle" letter-spacing="3" opacity="0.8">TIME LEFT</text>
      <rect x="56" y="164" width="{W - 112}" height="11" rx="5.5" fill="#6A1C5E" opacity="0.25"/>
      <rect x="56" y="164" width="{(W - 112) * 0.85:.1f}" height="11" rx="5.5" fill="#6A1C5E"/>
    </g>
    <rect x="30" y="26" width="{W - 60}" height="178" rx="26" fill="url(#scan)"/>
    <ellipse cx="86" cy="56" rx="64" ry="24" fill="#FFFFFF" opacity="0.26" transform="rotate(-16 86 56)"/>
    <rect x="30" y="26" width="{W - 60}" height="178" rx="26" fill="none" stroke="#8E2F7E" stroke-width="2.5" opacity="0.55"/>
    <g fill="#241B36"><circle cx="{W / 2 - 24}" cy="226" r="3"/><circle cx="{W / 2 - 8}" cy="226" r="3"/><circle cx="{W / 2 + 8}" cy="226" r="3"/><circle cx="{W / 2 + 24}" cy="226" r="3"/></g>'''
    return f'<g id="screen">{poly(q, "#2E2542", STROKE)}{screen.group(content)}</g>'


def marquee_face():
    hood_lights = "".join(
        f'<circle cx="{18 + i * 22}" cy="9" r="4.2" fill="{"#FFE27A" if i % 2 == 0 else "#7A6A9E"}" stroke="{OUT}" stroke-width="1.8"/>'
        for i in range(11))
    sign = f'''
    <rect x="0" y="0" width="{W}" height="{marquee.length:.1f}" fill="#3A2E52"/>
    <rect x="12" y="9" width="{W - 24}" height="{marquee.length - 18:.1f}" rx="7" fill="url(#sign)" stroke="{OUT}" stroke-width="3"/>
    <g id="marquee-doodle" stroke="{OUT}" stroke-linejoin="round">
      <path d="M30 60 q-12 0 -12 -10 q0 -10 11 -10 q3 -10 14 -9 q9 -8 19 1 q11 -1 11 10 q8 2 6 10 q-2 8 -11 8 z" fill="#FFFFFF" stroke-width="2.5"/>
      <path d="M{W - 36} 26 a10 10 0 1 0 8 17 a8 8 0 1 1 -8 -17 z" fill="#FFF3C4" stroke-width="2.5"/>
      <path d="M{W - 60} 58 l2.6 5.6 6 .8 -4.4 4.2 1 6 -5.2 -2.8 -5.2 2.8 1 -6 -4.4 -4.2 6 -.8 z" fill="#FFF3C4" stroke-width="2"/>
      <circle cx="96" cy="22" r="2.5" fill="#FFF3C4" stroke="none"/><circle cx="{W - 84}" cy="24" r="2.2" fill="#FFF3C4" stroke="none"/>
    </g>
    <text x="{W / 2 + 12}" y="54" text-anchor="middle" font-family="Segoe UI Black, Arial Rounded MT Bold, sans-serif" font-size="25"
          font-weight="900" fill="#FFFFFF" stroke="#8E2F7E" stroke-width="6" paint-order="stroke" letter-spacing="1">INSERT COIN</text>'''
    glow = poly([marquee.to_screen(12, 9), marquee.to_screen(W - 12, 9), marquee.to_screen(W - 12, marquee.length - 9),
                 marquee.to_screen(12, marquee.length - 9)], "#FFB6EA", 'opacity="0.55" filter="url(#glow)"')
    return f'''<g id="marquee">
  {glow}
  {poly(hood.quad(), "#2E2542", STROKE)}{hood.group(hood_lights)}
  {poly(marquee.quad(), "#3A2E52", STROKE)}{marquee.group(sign)}
</g>'''


def control_panel():
    joy_u, joy_v = 58, 30
    jx, jy = cp_top.to_screen(joy_u, joy_v)
    base = f'''
    <rect x="0" y="0" width="{W}" height="{cp_top.length:.1f}" fill="url(#cpFill)"/>
    <path d="M0 {cp_top.length - 4:.1f} H{W}" stroke="#DCD2FF" stroke-width="3"/>
    <ellipse cx="{joy_u}" cy="{joy_v}" rx="20" ry="17" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="108" cy="32" r="12" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="150" cy="32" r="20" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="196" cy="26" r="10" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="224" cy="30" r="10" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="206" cy="46" r="10" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="234" cy="50" r="10" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>'''
    caps = f'''
    <path d="M108 40 l-9 -9 a5.2 5.2 0 0 1 9 -6 a5.2 5.2 0 0 1 9 6 z" fill="#FF7AB0" stroke="{OUT}" stroke-width="2.5"/>
    <circle id="check-button" cx="150" cy="32" r="17" fill="url(#gold)" stroke="{OUT}" stroke-width="3"/>
    <path d="M141 32 l6 5 11 -10" fill="none" stroke="#8A5410" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
    <circle cx="196" cy="26" r="8" fill="#8EF0D0" stroke="{OUT}" stroke-width="2.5"/>
    <circle cx="224" cy="30" r="8" fill="#C6A8FF" stroke="{OUT}" stroke-width="2.5"/>
    <circle cx="206" cy="46" r="8" fill="#FF9EC7" stroke="{OUT}" stroke-width="2.5"/>
    <circle cx="234" cy="50" r="8" fill="#FFE27A" stroke="{OUT}" stroke-width="2.5"/>'''
    lip = f'''
    <rect x="0" y="0" width="{W}" height="{cp_lip.length:.1f}" fill="#6A58A6"/>
    <path d="M10 8 H{W - 10}" stroke="#8C79CB" stroke-width="3" stroke-linecap="round"/>
    <text x="{W - 14}" y="21" text-anchor="end" font-family="Consolas, monospace" font-weight="bold" font-size="10" fill="#C9BCF2" letter-spacing="2">1P</text>'''
    stick = f'''<g id="joystick">
    <rect x="{jx - 4.5:.1f}" y="{jy - 30:.1f}" width="9" height="30" rx="4" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
    <circle cx="{jx:.1f}" cy="{jy - 34:.1f}" r="14" fill="#FF7AC6" stroke="{OUT}" stroke-width="3.5"/>
    <ellipse cx="{jx - 4:.1f}" cy="{jy - 39:.1f}" rx="5" ry="3.5" fill="#FFFFFF" opacity="0.7"/>
  </g>'''
    return f'''<g id="control-panel">
  {poly(cp_top.quad(), "#B7A7EC", STROKE)}{cp_top.group(base)}{cp_top.group(caps, lift=4)}
  {poly(cp_lip.quad(), "#6A58A6", STROKE)}{cp_lip.group(lip)}
  {stick}
</g>'''


def body_face():
    content = f'''
    <rect x="0" y="0" width="{W}" height="{body.length:.1f}" fill="url(#frontFill)"/>
    <rect x="10" y="12" width="12" height="{body.length - 24:.1f}" rx="6" fill="#A796DE" opacity="0.5"/>
    <g id="coin-door" stroke="{OUT}" stroke-linejoin="round">
      <rect x="72" y="22" width="116" height="104" rx="9" fill="#5B4A91" stroke-width="3.5"/>
      <rect x="80" y="30" width="100" height="88" rx="6" fill="none" stroke="#7E6BC0" stroke-width="2.5"/>
      <rect x="98" y="42" width="15" height="30" rx="4" fill="#FFB347" stroke-width="2.5"/>
      <rect x="147" y="42" width="15" height="30" rx="4" fill="#FFB347" stroke-width="2.5"/>
      <rect x="98" y="42" width="15" height="30" rx="4" fill="#FFB347" opacity="0.85" filter="url(#softGlow)" stroke="none"/>
      <rect x="147" y="42" width="15" height="30" rx="4" fill="#FFB347" opacity="0.85" filter="url(#softGlow)" stroke="none"/>
      <circle cx="105.5" cy="86" r="3.5" fill="{OUT}" stroke="none"/><circle cx="154.5" cy="86" r="3.5" fill="{OUT}" stroke="none"/>
      <text x="130" y="110" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="10" fill="#C9BCF2" stroke="none" letter-spacing="2">15 MIN</text>
    </g>
    <g id="coin-tray" stroke="{OUT}" stroke-linejoin="round">
      <rect x="54" y="150" width="120" height="62" rx="3" fill="#1A1326" stroke-width="3.5"/>
      <path d="M56 210 L66 202 L172 202 L172 210 Z" fill="#3A2E52"/>
      <path d="M56 152 L66 152 L66 202 L56 210 Z" fill="#2E2542"/>
      <rect x="54" y="150" width="120" height="62" rx="3" fill="none" stroke-width="3.5"/>
      <g id="coins" stroke-width="2.5">
        <ellipse cx="86" cy="203" rx="14" ry="5.5" fill="url(#bronze)"/>
        <ellipse cx="114" cy="205" rx="14" ry="5.5" fill="url(#silver)"/>
        <ellipse cx="100" cy="197" rx="14" ry="5.5" fill="url(#gold)"/>
        <ellipse cx="142" cy="204" rx="14" ry="5.5" fill="url(#gold)"/>
      </g>
    </g>
    <rect id="printer-slot" x="190" y="160" width="52" height="9" rx="3" fill="{OUT}"/>
    <g id="sticker-cat" transform="translate(40 60) rotate(-8)" stroke="{OUT}" stroke-linejoin="round">
      <path d="M-16 -8 l3 -12 8 7 h10 l8 -7 3 12 q3 18 -16 20 q-19 -2 -16 -20 z" fill="#FFFFFF" stroke-width="2.5"/>
      <circle cx="-6" cy="-2" r="2.2" fill="{OUT}" stroke="none"/><circle cx="7" cy="-2" r="2.2" fill="{OUT}" stroke="none"/>
      <path d="M-2 4 q2 2.5 5 0" fill="none" stroke-width="1.8"/>
      <circle cx="-11" cy="3" r="2.6" fill="#FFB3D1" stroke="none"/><circle cx="12" cy="3" r="2.6" fill="#FFB3D1" stroke="none"/>
    </g>
    <g id="sticker-coin" transform="translate(222 64) rotate(-6)">
      <circle r="13" fill="url(#gold)" stroke="{OUT}" stroke-width="2.5"/>
      <text y="5" text-anchor="middle" font-family="Segoe UI Black, sans-serif" font-size="13" fill="#8A5410">$</text>
    </g>
    <rect x="0" y="{body.length - 26:.1f}" width="{W}" height="26" fill="#5B4A91" opacity="0.55"/>'''
    tx, ty = body.to_screen(216, 168)
    ticket = f'''<g id="iou-ticket" transform="translate({tx:.1f} {ty:.1f}) rotate(4)" stroke="{OUT}" stroke-linejoin="round">
    <path d="M-19 0 H19 V58 l-4.75 5 -4.75 -5 -4.75 5 -4.75 -5 -4.75 5 -4.75 -5 -4.75 5 -4.75 -5 Z" fill="#FFF4E6" stroke-width="2.5"/>
    <text y="22" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="12" fill="#C2185B" stroke="none">IOU</text>
    <text y="38" text-anchor="middle" font-family="Consolas, monospace" font-weight="bold" font-size="9" fill="#7A4A6E" stroke="none">15m</text>
    <path d="M-13 48 H13" stroke="#E0B8C8" stroke-width="2" stroke-dasharray="3 3"/>
  </g>'''
    return f'''<g id="body">
  {poly(body.quad(), "#7B68B5", STROKE)}{body.group(content)}
  {poly(kick.quad(), "#2A2138", STROKE)}
  {ticket}
</g>'''


def trims():
    def strip(x0, x1):
        return "".join(poly([P(x0, z1, y1), P(x1, z1, y1), P(x1, z2, y2), P(x0, z2, y2)], "#2A2138", STROKE)
                       for (z1, y1), (z2, y2) in zip(EDGE, EDGE[1:]))
    return f'<g id="trim-left">{strip(L - T, L)}</g>\n<g id="trim-right">{strip(R, R + T)}</g>'


def feet():
    def box(x0, x1):
        front = [P(x0, 2, FLOOR), P(x1, 2, FLOOR), P(x1, 2, FLOOR + 14), P(x0, 2, FLOOR + 14)]
        side = [P(x1, 2, FLOOR), P(x1, 30, FLOOR), P(x1, 30, FLOOR + 14), P(x1, 2, FLOOR + 14)]
        return poly(side, "#1A1326", STROKE) + poly(front, "#2E2542", STROKE)
    return f'<g id="feet">{box(L - T + 4, L + 60)}{box(R - 60, R + T - 4)}</g>'


def cable():
    sx, sy = P(R + T, 92, 690)
    return f'''<g id="cable" fill="none" stroke-linecap="round">
  <path d="M{sx:.1f} {sy:.1f} C {sx + 40:.1f} {sy + 10:.1f}, {sx + 44:.1f} {sy + 120:.1f}, {sx + 20:.1f} {FLOOR + 6} S {sx + 70:.1f} {FLOOR + 22}, {sx + 96:.1f} {FLOOR + 18}" stroke="{OUT}" stroke-width="8"/>
  <path d="M{sx:.1f} {sy:.1f} C {sx + 40:.1f} {sy + 10:.1f}, {sx + 44:.1f} {sy + 120:.1f}, {sx + 20:.1f} {FLOOR + 6} S {sx + 70:.1f} {FLOOR + 22}, {sx + 96:.1f} {FLOOR + 18}" stroke="#3E3257" stroke-width="3.5"/>
  <rect x="{sx + 92:.1f}" y="{FLOOR + 9}" width="26" height="16" rx="5" fill="#3E3257" stroke="{OUT}" stroke-width="3"/>
</g>'''


def props():
    cx, cy = 104, 660
    legs = "".join(f'<path d="M{cx + dx} {cy + 22} L{cx + dx * 1.35:.1f} {FLOOR + 22}" stroke="{OUT}" stroke-width="10" stroke-linecap="round"/>'
                   f'<path d="M{cx + dx} {cy + 22} L{cx + dx * 1.35:.1f} {FLOOR + 22}" stroke="#4A3C7A" stroke-width="5" stroke-linecap="round"/>'
                   for dx in (-34, 34, -6))
    tufts = "".join(f'<circle cx="{cx + dx}" cy="{cy - 4 + dy}" r="3" fill="#8C77CF"/>' for dx, dy in ((-26, 0), (26, 0), (0, -10), (0, 10)))
    return f'''<g id="props">
  <g id="floor-tickets" stroke="{OUT}" stroke-linejoin="round">
    <path d="M196 872 l34 -6 6 18 -34 6 z" fill="#FFF4E6" stroke-width="2.5"/>
    <path d="M204 874 l18 -3" stroke="#C2185B" stroke-width="2.5"/>
    <path d="M40 884 l30 4 -3 16 -30 -4 z" fill="#FFF4E6" stroke-width="2.5"/>
    <ellipse cx="170" cy="894" rx="11" ry="4.5" fill="url(#gold)" stroke-width="2"/>
  </g>
  <g id="stool">
    <ellipse cx="{cx + 8}" cy="{FLOOR + 26}" rx="62" ry="10" fill="#000" opacity="0.35" filter="url(#contact)"/>
    {legs}
    <ellipse cx="{cx}" cy="{cy + 110}" rx="44" ry="10" fill="none" stroke="{OUT}" stroke-width="9"/>
    <ellipse cx="{cx}" cy="{cy + 110}" rx="44" ry="10" fill="none" stroke="#4A3C7A" stroke-width="4"/>
    <path d="M{cx - 62} {cy} v16 a62 20 0 0 0 124 0 v-16 z" fill="#8A74CC" stroke="{OUT}" stroke-width="3.5"/>
    <ellipse cx="{cx}" cy="{cy}" rx="62" ry="20" fill="url(#cushion)" stroke="{OUT}" stroke-width="3.5"/>
    {tufts}
    <circle cx="{cx}" cy="{cy - 4}" r="5" fill="#FF9EC7" stroke="{OUT}" stroke-width="2"/>
  </g>
</g>'''


def screen_glow():
    q = [screen.to_screen(30, 26), screen.to_screen(W - 30, 26), screen.to_screen(W - 30, 204), screen.to_screen(30, 204)]
    return f'<g id="screen-glow" style="mix-blend-mode:screen">{poly(q, "#FF6FD2", "opacity=\"0.5\" filter=\"url(#glow)\"")}</g>'


def anchors():
    def rect(name, face, u, v, w, h):
        q = [face.to_screen(u, v), face.to_screen(u + w, v), face.to_screen(u + w, v + h), face.to_screen(u, v + h)]
        return f'<polygon id="anchor-{name}" points="{pts(q)}"/>'
    return f'''<g id="anchors" display="none">
  {rect("screen", screen, 30, 26, W - 60, 178)}
  {rect("marquee", marquee, 12, 9, W - 24, marquee.length - 18)}
  {rect("check-button", cp_top, 133, 15, 34, 34)}
  {rect("joystick", cp_top, 38, 12, 40, 36)}
  {rect("coin-slot", body, 98, 42, 15, 30)}
  {rect("coin-tray", body, 58, 154, 112, 54)}
  {rect("printer", body, 190, 160, 52, 9)}
</g>'''


def svg(preview_bg=True):
    parts = [defs()]
    if preview_bg:
        parts.append('<g id="preview-bg"><rect width="600" height="920" fill="url(#bgGlow)"/></g>')
    parts += [ground_shadow(), cable(), side_panel(), top_and_inner(), screen_face(), marquee_face(), body_face(),
              control_panel(), trims(), feet(), screen_glow(), props(), anchors()]
    body_xml = "\n".join(parts)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 920" width="600" height="920">\n'
            f'<!-- INSERT COIN · Cozy skin · cabinet v4 (generated by tools/art/cozy_cabinet.py). Original artwork. -->\n'
            f'{body_xml}\n</svg>\n')


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[2]
    out = root / "art" / "cozy" / "cabinet-v4.svg"
    out.write_text(svg(), encoding="utf-8")
    print(f"wrote {out}")
