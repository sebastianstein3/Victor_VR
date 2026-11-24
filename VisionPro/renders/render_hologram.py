"""Renders of the V.I.C.T.O.R. hologram (HologramAssistant.cs) as seen in the headset.

The geometry is the code's, number for number: the projector base with its three turning segmented rings
and the radar sweep, the beam, the globe's meridians and parallels, the web of filaments and nodes inside
it with packets of light running along them, the crystal shell of 80 facets that glows at its edges, the
core with its lens flare, the three rings with their dots, the vertical dial, the equalizer ticks, the
waves of light and the dust, and the status panel beside it. Everything is drawn the way Hologram.shader does: each
line, dot or triangle adds colour * vertex alpha * tint * intensity * shimmer to what is behind it, with no
depth. Lines are one pixel wide (drawn with 2x supersampling, as a GPU would with MSAA); dots are soft
Gaussian discs whose radius is in metres. The camera is one eye of the phone's screen: 1311 x 1206 px at a
horizontal field of view of about 90 degrees, a user seated at a desk. The room is a stand-in (in the
headset it is the camera's picture of yours); the panels are laid out in dp as the M3 kit does.

Things that cannot be shown here: the real room behind it, head movement, and the exact display colour
response (additive blending is done on the display values).

usage: render_hologram.py <unity project> <output folder>
"""
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = sys.argv[1]
OUT = sys.argv[2]
FONTS = ROOT + "/Assets/_Project/UI/Fonts/"

W, H = 1311, 1206
SS = 2
F = 655.0  # px, ~90 degrees across 1311 px

CYAN = np.array([0.25, 0.85, 1.0])
ORANGE = np.array([1.0, 0.55, 0.15])
WHITE = np.array([0.92, 0.98, 1.0])
HOT = np.array([1.0, 0.75, 0.35])
GOLD = np.array([1.0, 0.72, 0.22])
HOTG = np.array([1.0, 0.88, 0.55])
AMBER = np.array([1.0, 0.5, 0.12])

SCALE = 1.2  # HologramAssistant.k_Scale: the whole figure
CONTROLS_SCALE = 0.7  # the buttons' panel is 0.7 of a window's size
BASE_R = 0.14
HEIGHT = 0.27
RAD = 0.085
TICKS = 72
DP = 1.39e-3  # metres per dp at the panels' distance of 1.0 (M3Theme: 0.868e-3 * distance * 1.6)


# ---- Unity's rotations (left-handed: Euler = Ry * Rx * Rz) ----
def rx(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def ry(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rz(a):
    c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
    return np.array([[c, s, 0], [-s, c, 0], [0, 0, 1]])


def euler(x, y, z):
    return ry(y) @ rx(x) @ rz(z)


class Camera:
    def __init__(self, eye, target):
        self.eye = np.array(eye, float)
        fwd = np.array(target, float) - self.eye
        self.fwd = fwd / np.linalg.norm(fwd)
        self.right = np.cross([0, 1, 0], self.fwd)
        self.right /= np.linalg.norm(self.right)
        self.up = np.cross(self.fwd, self.right)

    def project(self, p, ss=SS):
        d = np.atleast_2d(p) - self.eye
        z = d @ self.fwd
        x = d @ self.right
        y = d @ self.up
        z = np.maximum(z, 1e-3)
        return (W * ss / 2 + F * ss * x / z), (H * ss / 2 - F * ss * y / z), z


class Frame:
    """The additive accumulator at SS x resolution."""

    def __init__(self, camera, flicker_time=1.234):
        self.cam = camera
        self.acc = np.zeros((H * SS, W * SS, 3), np.float32)
        self.t = flicker_time
        self.s = SCALE
        self.warm = 0.0  # Hologram.shader's _Warm: amber, keeping the brightness

    def recolor(self, color):
        """_Warm in the shader: lerp(colour, luminance * (1.9, 1.1, 0.5), warm)."""
        color = np.asarray(color, float)
        if self.warm <= 0:
            return color
        lum = color @ np.array([0.3, 0.59, 0.11])
        warm = np.asarray(lum)[..., None] * np.array([1.9, 1.1, 0.5])
        return color * (1 - self.warm) + warm * self.warm

    def shimmer(self, world):
        return 1.0 - 0.06 * (0.5 + 0.5 * np.sin(self.t * 37.0 + world[..., 1] * 120.0 + world[..., 0] * 70.0))

    def line(self, a, b, ca, cb, intensity, tint, pulse=None, alpha=1.0):
        """A segment of colour ca (rgb * alpha) to cb, as the shader draws a line. pulse: (strength, head):
        a packet of light at `head` along the line (Hologram.shader's _Pulse)."""
        a, b = np.asarray(a, float) * self.s, np.asarray(b, float) * self.s
        xa, ya, za = self.cam.project(a)
        xb, yb, zb = self.cam.project(b)
        length = math.hypot(xb[0] - xa[0], yb[0] - ya[0])
        n = max(2, int(length / 0.5) + 1)
        t = np.linspace(0, 1, n)[:, None]
        world = a[None] * (1 - t) + b[None] * t
        color = self.recolor(ca[None] * (1 - t) + cb[None] * t)
        px = xa[0] * (1 - t[:, 0]) + xb[0] * t[:, 0]
        py = ya[0] * (1 - t[:, 0]) + yb[0] * t[:, 0]
        weight = (length / (n - 1) if n > 1 else 1.0) * SS  # 1 px lines at the final size
        base = tint[None] * intensity * self.shimmer(world)[:, None]
        if pulse is not None and pulse[0] > 0:
            p = np.exp(-(((t[:, 0] - pulse[1]) * 14.0) ** 2)) * pulse[0]
            value = (color * base * (1 + 5 * p[:, None]) + np.array([0.5, 0.45, 0.35])[None] * (p * alpha * intensity)[:, None]) * weight
        else:
            value = color * base * weight
        x0 = np.floor(px).astype(int)
        y0 = np.floor(py).astype(int)
        fx = (px - x0)[:, None]
        fy = (py - y0)[:, None]
        for dx, dy, w in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
            xi, yi = x0 + dx, y0 + dy
            ok = (xi >= 0) & (xi < W * SS) & (yi >= 0) & (yi < H * SS)
            np.add.at(self.acc, (yi[ok], xi[ok]), (value * w)[ok])

    def lines(self, segments, intensity, tint):
        for a, b, ca, cb in segments:
            self.line(a, b, ca, cb, intensity, tint)

    def dot(self, center, radius, color, intensity, tint, radius_y=None):
        """A soft dot; with radius_y a streak (UV1 = radius across and up)."""
        center = np.asarray(center, float) * self.s
        rx_m = radius * self.s
        ry_m = (radius_y if radius_y is not None else radius) * self.s
        x, y, z = self.cam.project(center)
        r = F * SS * rx_m / z[0]
        ry = F * SS * ry_m / z[0]
        cx, cy = x[0], y[0]
        x0, x1 = int(max(0, cx - r)), int(min(W * SS, cx + r + 1))
        y0, y1 = int(max(0, cy - ry)), int(min(H * SS, cy + ry + 1))
        if x1 <= x0 or y1 <= y0:
            return
        xs = (np.arange(x0, x1) - cx) / max(r, 1e-6)
        ys = (np.arange(y0, y1) - cy) / max(ry, 1e-6)
        d2 = xs[None, :] ** 2 + ys[:, None] ** 2
        soft = np.exp(-3.5 * d2) * np.clip(1.0 - d2, 0, 1)  # Hologram.shader: zero at the dot's edge
        shimmer = self.shimmer(np.asarray(center, float))
        self.acc[y0:y1, x0:x1] += soft[..., None] * (self.recolor(color) * tint * intensity * shimmer)[None, None]

    def triangle(self, pa, pb, pc, ca, cb, cc, intensity, tint, fresnel=0.0, smooth_from=None):
        """A filled triangle with a colour (rgb * alpha) per vertex. fresnel > 0: the shell's rim light."""
        pa, pb, pc = (np.asarray(q, float) * self.s for q in (pa, pb, pc))
        xs, ys, _ = self.cam.project(np.array([pa, pb, pc]))
        x0, x1 = int(max(0, xs.min())), int(min(W * SS - 1, xs.max() + 1))
        y0, y1 = int(max(0, ys.min())), int(min(H * SS - 1, ys.max() + 1))
        if x1 <= x0 or y1 <= y0:
            return
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(d) < 1e-9:
            return
        w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
        w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        color = self.recolor(w0[..., None] * ca + w1[..., None] * cb + w2[..., None] * cc)
        mid = (np.array(pa) + np.array(pb) + np.array(pc)) / 3
        rim = 1.0
        if fresnel > 0.01:
            if smooth_from is not None:  # a smooth ball: the normal points away from its centre
                normal = mid - np.asarray(smooth_from, float) * self.s
            else:
                normal = np.cross(pb - pa, pc - pa)
            normal = normal / max(np.linalg.norm(normal), 1e-9)
            to_camera = self.cam.eye - mid
            to_camera = to_camera / np.linalg.norm(to_camera)
            rim = 0.12 + max(0.0, 1.0 - abs(float(normal @ to_camera))) ** fresnel
        value = color * (tint * intensity * rim * self.shimmer(mid))
        region = self.acc[y0:y1 + 1, x0:x1 + 1]
        region += np.where(inside[..., None], value, 0).astype(np.float32)

    def result(self):
        small = self.acc.reshape(H, SS, W, SS, 3).mean(axis=(1, 3))
        return small


def premult(color, alpha):
    return np.asarray(color, float) * alpha


# ---- The hologram, as HologramAssistant.cs builds it ----
def icosphere(subdivisions):
    """HoloMesh.Icosphere: a unit icosphere (12 corners, 20 faces; each subdivision splits a face in four)."""
    t = (1 + math.sqrt(5)) / 2
    corners = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t),
               (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    corners = [np.array(c, float) / np.linalg.norm(c) for c in corners]
    faces = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
             3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1]
    for _ in range(subdivisions):
        middles = {}

        def middle(a, b):
            key = (min(a, b), max(a, b))
            if key not in middles:
                m = (corners[a] + corners[b]) / 2
                corners.append(m / np.linalg.norm(m))
                middles[key] = len(corners) - 1
            return middles[key]

        split = []
        for i in range(0, len(faces), 3):
            a, b, c = faces[i:i + 3]
            ab, bc, ca = middle(a, b), middle(b, c), middle(c, a)
            split += [a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]
        faces = split
    return corners, [tuple(faces[i:i + 3]) for i in range(0, len(faces), 3)]


def on_sphere(lat, lon, r):
    return np.array([math.cos(lat) * math.cos(lon) * r, math.sin(lat) * r, math.cos(lat) * math.sin(lon) * r])


def fragment_color(rnd, alpha):
    pick = rnd.rand()
    base = HOTG if pick < 0.12 else AMBER if pick < 0.35 else GOLD
    return base * alpha


class Hologram:
    """The glass planet inside and the golden fragment halo, comets and base outside (HologramAssistant.cs)."""

    COMETS = [  # radius (planet radii), span, tilt, speed
        (1.30, 210, (20, 0, 35), 46), (1.45, 160, (-50, 0, 10), -38), (1.60, 250, (75, 0, -20), 29),
        (1.75, 190, (10, 0, -60), -52), (1.90, 230, (-30, 0, 70), 24), (2.05, 140, (60, 0, 45), -33),
    ]

    def __init__(self):
        self.tick_noise = np.random.RandomState(3).rand(TICKS)
        self.build_dust(np.random.RandomState(5))
        rnd = np.random.RandomState(21)
        self.halo = [self.sphere_fragments(rnd, 430, RAD * 1.55), self.sphere_fragments(rnd, 350, RAD * 1.85)]
        self.debris = self.disc_fragments(np.random.RandomState(34))
        self.comet_tail = np.random.RandomState(8).rand(len(self.COMETS), 6)
        self.glass_corners, self.glass_faces = icosphere(2)

    @staticmethod
    def sphere_fragments(rnd, count, radius):
        """SphereFragments: runs that walk the latitude / longitude grid, turning by right angles."""
        out = []
        for _ in range(count):
            lat = math.asin(rnd.rand() * 2 - 1)
            lon = rnd.rand() * 2 * math.pi
            r = radius * (0.94 + 0.12 * rnd.rand())
            brightness = 0.15 + 0.85 * rnd.rand() ** 2
            phase = rnd.rand()
            steps = 2 + rnd.randint(4)
            direction = rnd.randint(4)
            prev = on_sphere(lat, lon, r)
            for s_ in range(steps):
                if rnd.rand() < 0.4:
                    direction = (direction + (1 if rnd.randint(2) == 0 else 3)) % 4
                step = 0.02 + 0.06 * rnd.rand()
                if direction == 0:
                    lon += step / max(math.cos(lat), 0.25)
                elif direction == 1:
                    lat = min(lat + step, 1.35)
                elif direction == 2:
                    lon -= step / max(math.cos(lat), 0.25)
                else:
                    lat = max(lat - step, -1.35)
                nxt = on_sphere(lat, lon, r)
                color = fragment_color(rnd, brightness * (1 - s_ * 0.12))
                out.append((prev, nxt, color, color * 0.85, phase + 0.13 * s_))
                prev = nxt
        return out

    @staticmethod
    def disc_fragments(rnd):
        """BuildDebris: circuit-like runs on a polar grid."""
        out = []
        for _ in range(150):
            ring = rnd.randint(7)
            angle = rnd.rand() * 2 * math.pi
            brightness = 0.15 + 0.75 * rnd.rand() ** 2
            phase = rnd.rand()
            along = rnd.randint(2) == 0
            sign = 1 if rnd.randint(2) == 0 else -1
            prev = Hologram.polar(BASE_R * (0.36 + ring * 0.085), angle, 0.003)
            for s_ in range(2 + rnd.randint(4)):
                if rnd.rand() < 0.45:
                    along = not along
                if along:
                    angle += sign * (0.06 + 0.16 * rnd.rand())
                else:
                    ring = min(6, max(0, ring + (1 if rnd.randint(2) == 0 else -1)))
                nxt = Hologram.polar(BASE_R * (0.36 + ring * 0.085), angle, 0.003)
                color = fragment_color(rnd, brightness * (1 - s_ * 0.1))
                out.append((prev, nxt, color, color, phase + 0.13 * s_))
                prev = nxt
        return out

    def build_dust(self, rnd):
        self.dust = []
        for _ in range(220):
            while True:
                p = rnd.rand(3) * 2 - 1
                if p @ p <= 1:
                    break
            p *= 0.38
            p[1] = max(0.01, p[1] * 0.8 + HEIGHT * 0.85)
            size = 0.0008 + 0.0014 * rnd.rand()
            warm = rnd.rand() < 0.25
            self.dust.append((p, size, (AMBER if warm else HOTG) * (0.3 + 0.65 * rnd.rand())))

    def render(self, frame, state, level, now, globe_angle, ring_angles, dust_angle, wave_us=(), base_angle=0.0, sweep_angle=0.0,
               pulse_clock=0.37, dial_angle=40.0, comet_angles=(30, 100, 200, 60, 150, 250)):
        """state: idle / listening / thinking / speaking, as Update() sets them."""
        busy = 3.0 if state == "thinking" else 2.2 if state == "listening" else 1.0
        tint = np.ones(3)
        if state == "listening":
            ring_scale, glow, halo_scale = 0.8, 0.95, 0.88
            lift, wave = 0.006 + 0.012, 0.0
            core_scale, core_int = 1.0, 0.7 + 0.4
            pulse, flare = 0.6, 0.3 + 0.3
            warm = 0.0
        elif state == "thinking":
            ring_scale, glow, halo_scale = 1.08 + 0.06 * math.sin(now * 9), 0.9 + 0.15 * math.sin(now * 23), 1.0
            lift, wave = 0.006, 0.01
            core_scale, core_int = 1 + 0.12 * math.sin(now * 20), 0.7
            pulse, flare = 1.0, 0.3 + 0.25 + 0.2 * math.sin(now * 17)
            warm = 0.75
        elif state == "speaking":
            ring_scale, glow, halo_scale = 1 + 0.08 * level, 0.75 + 0.5 * level, 1 + 0.07 * level
            lift, wave = 0.006 + 0.05 * level, 0.0
            core_scale, core_int = 1 + 0.7 * level + 0.05 * math.sin(now * 2), 0.7 + 0.8 * level
            pulse, flare = 0.5 + 0.5 * level, 0.3 + 0.9 * level
            warm = 0.0
        else:
            ring_scale, glow, halo_scale = 1.0, 0.6 + 0.05 * math.sin(now * 1.3), 1.0
            lift, wave = 0.006, 0.0
            core_scale, core_int = 1 + 0.05 * math.sin(now * 2), 0.7
            pulse, flare = 0.35, 0.3
            warm = 0.0
        white = np.ones(3)
        centre = np.array([0, HEIGHT, 0])
        frame.warm = warm

        # ---- The base, in gold ----
        for radius, alpha, kind in ((1.0, 0.6, "c"),):
            self.circle(frame, np.eye(3), np.zeros(3), BASE_R, BASE_R, premult(GOLD, alpha), glow, tint, plane="xz", y=0.002)
        for i in range(60):
            ang = i * 2 * math.pi / 60
            inner = BASE_R * (0.88 if i % 5 == 0 else 0.93)
            frame.line(self.polar(BASE_R * 0.97, ang), self.polar(inner, ang), premult(GOLD, 0.5), premult(GOLD, 0.5), glow, tint)
        self.circle(frame, np.eye(3), np.zeros(3), BASE_R * 1.08, BASE_R * 1.08, premult(AMBER, 0.55), glow, tint, plane="xz", y=0.002)
        turn_b = base_angle * 18.0
        for k in range(3):
            self.arc(frame, BASE_R * 0.78, k * 120 - turn_b, 80, premult(GOLD, 0.75), glow, tint)
            self.arc(frame, BASE_R * 0.72, k * 120 + 60 - turn_b, 40, premult(GOLD, 0.45), glow, tint)
        self.arc(frame, BASE_R * 0.86, 0, 360, premult(GOLD, 0.18), glow, tint)
        turn_c = -base_angle * 30.0
        for k in range(24):
            self.arc(frame, BASE_R * 0.55, k * 15 - turn_c, 9, premult(GOLD, 0.55), glow, tint)
        self.arc(frame, BASE_R * 0.3, 0, 360, premult(GOLD, 0.35), glow, tint)
        for k in range(8):
            ang = k * math.pi / 4 - math.radians(turn_c)
            frame.line(self.polar(BASE_R * 0.3, ang), self.polar(BASE_R * 0.55, ang), premult(GOLD, 0.25), premult(GOLD, 0.25), glow, tint)

        # The floor of circuit fragments (turns the other way, light running along it)
        rd_floor = ry(-base_angle * 9.0)
        for a, b, ca, cb, phase in self.debris:
            head = ((pulse_clock + phase) % 1.0) * 4.0
            frame.line(rd_floor @ a, rd_floor @ b, ca, cb, glow * (0.85 + 0.5 * level), tint, pulse=(pulse, head),
                       alpha=float(ca.max()))

        center = np.array([0, 0.001, 0.0])
        for i in range(48):
            a0, a1 = i * 2 * math.pi / 48, (i + 1) * 2 * math.pi / 48
            p0 = np.array([math.cos(a0) * BASE_R, 0.001, math.sin(a0) * BASE_R])
            p1 = np.array([math.cos(a1) * BASE_R, 0.001, math.sin(a1) * BASE_R])
            frame.triangle(center, p0, p1, premult(GOLD, 0.16), premult(GOLD, 0.03), premult(GOLD, 0.03), glow * (0.9 + 0.5 * level), tint)
        sweep_glow = glow * (0.7 if state == "idle" else 1.4)
        origin = np.array([0, 0.0025, 0.0])
        for i in range(16):
            a0 = math.radians(-70.0 * i / 16 - sweep_angle)
            a1 = math.radians(-70.0 * (i + 1) / 16 - sweep_angle)
            frame.triangle(origin, self.polar(BASE_R * 0.95, a0, 0.0025), self.polar(BASE_R * 0.95, a1, 0.0025),
                           premult(GOLD, 0.05), premult(GOLD, 0.34 * (1 - i / 16)), premult(GOLD, 0.34 * (1 - (i + 1) / 16)), sweep_glow, tint)
        top = HEIGHT - RAD * 0.8
        for i in range(24):
            a0, a1 = i * 2 * math.pi / 24, (i + 1) * 2 * math.pi / 24
            b0 = np.array([math.cos(a0) * 0.07, 0.003, math.sin(a0) * 0.07])
            t0 = np.array([math.cos(a0) * 0.03, top, math.sin(a0) * 0.03])
            b1 = np.array([math.cos(a1) * 0.07, 0.003, math.sin(a1) * 0.07])
            t1 = np.array([math.cos(a1) * 0.03, top, math.sin(a1) * 0.03])
            cb_, ct_ = premult(GOLD, 0.13), premult(GOLD, 0.0)
            frame.triangle(b0, t0, t1, cb_, ct_, ct_, glow * (0.9 + 0.6 * level), tint)
            frame.triangle(b0, t1, b1, cb_, ct_, cb_, glow * (0.9 + 0.6 * level), tint)
        frame.dot(np.array([0, 0.01, 0]), 0.075, premult(GOLD, 0.25), glow * (0.8 + level), tint)

        # Equalizer ticks (_Warm 0)
        frame.warm = 0.0
        for i in range(TICKS):
            a = i * 2 * math.pi / TICKS
            p = np.array([math.cos(a) * BASE_R * 1.08, 0.002, math.sin(a) * BASE_R * 1.08])
            h = 0.004 + self.tick_noise[i] * (lift + (wave * (0.5 + 0.5 * math.sin(now * 6 - i * 0.5)) if wave else 0))
            frame.line(p, p + np.array([0, h, 0]), premult(GOLD, 0.95), premult(HOTG, 0.25), 0.9 + 0.6 * level, white)
        frame.warm = warm

        # ---- The planet: wire globe and glass ----
        rot = euler(0, 0, 18) @ euler(0, globe_angle, 0)
        scale = 1 + 0.04 * level

        def gp(p):
            return rot @ (np.asarray(p, float) * scale) + centre

        for k in range(6):
            phi = k * math.pi / 6
            pts = [gp([math.cos(a) * math.cos(phi) * RAD, math.sin(a) * RAD, math.cos(a) * math.sin(phi) * RAD]) for a in np.linspace(0, 2 * math.pi, 65)]
            self.polyline(frame, pts, premult(CYAN, 0.5), glow, tint)
        for lat in range(-60, 61, 20):
            rad = math.radians(lat)
            y, ring = math.sin(rad) * RAD, math.cos(rad) * RAD
            pts = [gp([math.cos(a) * ring, y, math.sin(a) * ring]) for a in np.linspace(0, 2 * math.pi, 65)]
            self.polyline(frame, pts, premult(CYAN, 0.85 if lat == 0 else 0.5), glow, tint)
        glass = [rot @ (c * RAD * 1.02 * scale) + centre for c in self.glass_corners]
        for tri in self.glass_faces:
            pa, pb, pc = (glass[i] for i in tri)
            frame.triangle(pa, pb, pc, premult(CYAN, 0.16), premult(CYAN, 0.16), premult(CYAN, 0.16),
                           glow * (1 + 0.8 * level), tint, fresnel=2.2, smooth_from=centre)

        # Rings with dots
        specs = [(1.55, 1.30, (28, 0, 18), ORANGE), (1.40, 1.40, (72, 0, 0), CYAN), (1.85, 1.15, (-18, 0, -28), CYAN)]
        for (a, b, tilt, color), angle in zip(specs, ring_angles):
            rot_r = euler(*tilt) @ euler(0, angle, 0)
            pts = []
            for t in np.linspace(0, 2 * math.pi, 129):
                local = np.array([math.cos(t) * a * RAD, 0, math.sin(t) * b * RAD]) * ring_scale
                pts.append(rot_r @ local + centre)
            self.polyline(frame, pts, premult(color, 0.65), glow, tint)
            orange = color is ORANGE
            for sign, size, alpha in ((1, 0.0065 if orange else 0.0045, 1.0), (-1, 0.0035, 0.8)):
                local = np.array([sign * a * RAD, 0, 0]) * ring_scale
                frame.dot(rot_r @ local + centre, size * ring_scale, premult(color, alpha), glow * 1.2, tint)

        # ---- The golden halo: two shells of fragments, turning against each other ----
        shell_rots = [euler(22, globe_angle * 0.5, 8), euler(-28, -globe_angle * 0.35, -12)]
        for fragments, shell_rot in zip(self.halo, shell_rots):
            for a, b, ca, cb, phase in fragments:
                head = ((pulse_clock + phase) % 1.0) * 4.0
                frame.line(shell_rot @ (a * halo_scale) + centre, shell_rot @ (b * halo_scale) + centre, ca, cb,
                           glow * (0.85 + 0.5 * level), tint, pulse=(pulse, head), alpha=float(ca.max()))

        # ---- Comets: bright arcs with a spark at their head ----
        for (radius_m, span, tilt, _), angle, tail in zip(self.COMETS, comet_angles, self.comet_tail):
            radius = RAD * radius_m
            rot_c = euler(*tilt) @ euler(0, angle, 0)
            for offset in (-0.0012, 0.0, 0.0012):
                prev = self.polar(radius + offset, 0.0, 0.0)
                for i in range(1, 49):
                    t = i / 48
                    nxt = self.polar(radius + offset, math.radians(span * t), 0.0)
                    alpha = t ** 2.2 * (0.95 if offset == 0.0 else 0.55)
                    color = GOLD * (1 - t) + HOTG * t
                    frame.line(rot_c @ prev + centre, rot_c @ nxt + centre, color * alpha * 0.85, color * alpha, glow * (0.9 + 0.5 * level), tint)
                    prev = nxt
            head = rot_c @ self.polar(radius, math.radians(span), 0.0) + centre
            frame.dot(head, 0.0065, premult(WHITE, 1.0), glow * (1 + 0.6 * level), tint)
            frame.dot(head, 0.02, premult(GOLD, 0.45), glow * (1 + 0.6 * level), tint)
            for u in tail:
                frame.dot(rot_c @ self.polar(radius, math.radians(span * (0.35 + 0.6 * u)), 0.0) + centre, 0.0015, premult(HOTG, 0.7),
                          glow * (1 + 0.6 * level), tint)

        # ---- The thin gauge ring round it all ----
        radius = RAD * 2.45
        spin = math.radians(dial_angle)

        def on(r, deg):
            a = math.radians(deg) + spin
            return centre + np.array([math.cos(a) * r, math.sin(a) * r, 0.0])

        for i in range(0, 360, 3):
            frame.line(on(radius, i), on(radius, i + 3), premult(GOLD, 0.4), premult(GOLD, 0.4), glow * 0.9, tint)
        for i in range(120):
            length = 0.034 if i % 10 == 0 else 0.024 if i % 5 == 0 else 0.012
            frame.line(on(radius, i * 3), on(radius - length, i * 3), premult(GOLD, 0.5), premult(GOLD, 0.5), glow * 0.9, tint)
        for layer in range(3):
            r = radius * (0.955 - layer * 0.006)
            for i in range(-30, 95, 3):
                frame.line(on(r, i), on(r, i + 3), premult(AMBER, 0.9), premult(AMBER, 0.9), glow * 0.9, tint)
        for i in range(110, 310, 3):
            frame.line(on(radius * 0.9, i), on(radius * 0.9, i + 3), premult(GOLD, 0.45), premult(GOLD, 0.45), glow * 0.9, tint)

        # Waves of light leaving the planet
        for u in wave_us:
            s_ = 1.05 + (2.6 - 1.05) * u
            inten = (1 - u) ** 2 * 1.3
            for plane, alpha in (("xz", 0.8), ("xy", 0.5), ("yz", 0.5)):
                self.circle(frame, np.eye(3), centre, RAD * s_, RAD * s_, premult(GOLD, alpha), inten, tint, plane=plane, y=0)

        # ---- The planet's glow: soft orange light, a hot middle, a white heart, a cool highlight ----
        frame.warm = 0.0
        for offset, radius_c, color in (((0, 0, 0), 0.09, premult(ORANGE, 0.36)), ((0, 0, 0), 0.052, premult(np.array([1, 0.75, 0.35]), 0.62)),
                                        ((0, 0, 0), 0.022, premult(white, 1.0)), ((-0.026, 0.03, 0.02), 0.035, premult(CYAN, 0.22))):
            frame.dot(centre + np.array(offset) * core_scale, radius_c * core_scale, color, core_int, white)
        for (rx_, ry_), color in (((0.17, 0.0035), premult(HOTG, 0.5)), ((0.0035, 0.07), premult(GOLD, 0.3)), ((0.05, 0.0025), premult(AMBER, 0.6))):
            frame.dot(centre, rx_, color, flare, white, radius_y=ry_)

        # Sparks (drawn in while it thinks)
        frame.warm = 0.75 if state == "thinking" else 0.0
        dust_scale = 0.9 if state == "thinking" else 1.0
        rd = ry(dust_angle)
        for p, size, color in self.dust:
            frame.dot(dust_scale * (rd @ p), size, color, 0.9, tint)

    @staticmethod
    def polar(radius, angle, y=0.002):
        return np.array([math.cos(angle) * radius, y, math.sin(angle) * radius])

    def arc(self, frame, radius, from_deg, span_deg, color, intensity, tint):
        steps = max(2, int(math.ceil(span_deg / 5.0)))
        prev = self.polar(radius, math.radians(from_deg))
        for i in range(1, steps + 1):
            nxt = self.polar(radius, math.radians(from_deg + span_deg * i / steps))
            frame.line(prev, nxt, color, color, intensity, tint)
            prev = nxt

    def circle(self, frame, rot, origin, ra, rb, color, intensity, tint, plane="xz", y=0.0):
        pts = []
        for t in np.linspace(0, 2 * math.pi, 97):
            if plane == "xz":
                p = [math.cos(t) * ra, y, math.sin(t) * rb]
            elif plane == "xy":
                p = [math.cos(t) * ra, math.sin(t) * rb, 0]
            else:
                p = [0, math.cos(t) * ra, math.sin(t) * rb]
            pts.append(rot @ np.array(p) + origin)
        self.polyline(frame, pts, color, intensity, tint)

    def polyline(self, frame, pts, color, intensity, tint):
        for a, b in zip(pts[:-1], pts[1:]):
            frame.line(np.asarray(a, float), np.asarray(b, float), color, color, intensity, tint)


# ---- The room (a stand-in) ----
def background(camera):
    img = np.zeros((H, W, 3), np.float32)
    ys = np.linspace(0, 1, H)[:, None]
    img[:] = (np.array([26, 30, 38]) * (1 - ys) + np.array([17, 19, 24]) * ys)[:, None, :] / 255.0
    pil = Image.fromarray((img * 255).astype(np.uint8))
    draw = ImageDraw.Draw(pil, "RGBA")

    def poly(points3d, fill, outline=None):
        xs, ys, _ = camera.project(np.array(points3d), ss=1)
        draw.polygon(list(zip(xs, ys)), fill=fill, outline=outline)

    # The desk
    poly([(-1.4, 0, -0.9), (1.4, 0, -0.9), (1.4, 0, 1.2), (-1.4, 0, 1.2)], (44, 36, 30, 255))
    poly([(-1.4, 0, 0.9), (1.4, 0, 0.9), (1.4, 0, 1.2), (-1.4, 0, 1.2)], (36, 30, 26, 255))
    # A keyboard in front
    poly([(-0.23, 0.015, -0.5), (0.23, 0.015, -0.5), (0.23, 0.02, -0.34), (-0.23, 0.02, -0.34)], (58, 60, 68, 255), (84, 86, 96, 255))
    for row in range(1, 5):
        z = -0.5 + row * 0.032
        poly([(-0.22, 0.02, z), (0.22, 0.02, z), (0.22, 0.02, z + 0.002), (-0.22, 0.02, z + 0.002)], (38, 40, 46, 255))
    out = np.asarray(pil).astype(np.float32) / 255.0
    # a soft vignette, like a camera's
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
    out *= (1 - 0.35 * np.clip(r - 0.5, 0, 1) ** 1.5)[..., None]
    return out


# ---- Panels (dp, as the M3 kit lays them out) ----
def font(size_px, name="Roboto-Medium.ttf"):
    if name == "Roboto-Bold.ttf":
        name = "Roboto-Medium.ttf"  # the project has no bold weight; TMP fakes it
    try:
        return ImageFont.truetype(FONTS + name, max(6, int(size_px)))
    except OSError:
        return ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", max(6, int(size_px)))


ICONS = {"stop": 0xe047, "close": 0xe5cd, "pinch": 0xeb38, "open_with": 0xe89f, "auto_awesome": 0xe65f, "edit": 0xe3c9}


def draw_controls(overlay, camera):
    d = ImageDraw.Draw(overlay, "RGBA")
    centre = np.array([0, 0.10, -(BASE_R * SCALE + 0.17)])  # HologramAssistant.LateUpdate
    x, y, z = camera.project(centre, ss=SS)
    ppd = F * SS * DP * CONTROLS_SCALE / z[0]
    cx, cy = x[0], y[0]

    def dp(vx, vy):
        return cx + vx * ppd, cy - vy * ppd

    def pill(x_dp, y_dp, w_dp, fill, text=None, icon=None, ink=(56, 30, 114, 255)):
        x0, y0 = dp(x_dp - w_dp / 2, y_dp + 28)
        x1, y1 = dp(x_dp + w_dp / 2, y_dp - 28)
        d.rounded_rectangle([x0, y0, x1, y1], radius=28 * ppd, fill=fill)
        mx, my = dp(x_dp, y_dp)
        fi = font(22 * ppd, "MaterialIconsRound-Regular.otf")
        ft = font(15 * ppd)
        if icon and text:
            tw = d.textlength(text, font=ft)
            gap = 8 * ppd
            iw = 22 * ppd
            left = mx - (iw + gap + tw) / 2
            d.text((left + iw / 2, my), chr(ICONS[icon]), font=fi, fill=ink, anchor="mm")
            d.text((left + iw + gap, my), text, font=ft, fill=ink, anchor="lm")
        elif icon:
            d.text((mx, my), chr(ICONS[icon]), font=fi, fill=ink, anchor="mm")
        else:
            d.text((mx, my), text, font=ft, fill=ink, anchor="mm")

    # the name, letter-spaced (characterSpacing 6: 6% of the font size)
    f = font(22 * ppd)
    name = "V.I.C.T.O.R."
    spacing = 0.06 * 22 * ppd
    widths = [d.textlength(ch, font=f) for ch in name]
    total = sum(widths) + spacing * (len(name) - 1)
    px = cx - total / 2
    for ch, w in zip(name, widths):
        d.text((px, cy - 64 * ppd), ch, font=f, fill=(179, 242, 255, 255), anchor="lm")
        px += w + spacing
    tonal, on_tonal = (74, 68, 88, 255), (232, 222, 248, 255)
    pill(-122, 64, 56, tonal, icon="stop", ink=on_tonal)
    pill(122, 64, 56, tonal, icon="close", ink=on_tonal)
    pill(-80, 4, 150, (208, 188, 255, 255), text="Zapytaj", icon="pinch")
    pill(80, 4, 150, tonal, text="Na blat", icon="open_with", ink=on_tonal)
    pill(-80, -56, 150, tonal, text="Model", icon="auto_awesome", ink=on_tonal)
    pill(80, -56, 150, tonal, text="Pamięć", icon="edit", ink=on_tonal)


def draw_hud(overlay, camera, state, fps=59, memory=0.38, battery=0.82, uptime="00:42:17",
             latencies=(2.1, 3.4, 1.8, 2.6, 4.2, 2.9, 3.1, 2.2, 3.8, 2.4, 2.0)):
    """HologramHud: the status panel to the user's left of the figure (values are samples)."""
    d = ImageDraw.Draw(overlay, "RGBA")
    centre = np.array([-0.40, 0.27, -0.04])
    x, y, z = camera.project(centre, ss=SS)
    ppd = F * SS * DP * CONTROLS_SCALE / z[0]
    cx, cy = x[0], y[0]
    pw, ph = 300.0, 440.0
    cyan, orange = (64, 217, 255), (255, 140, 38)
    dim, bright = (140, 179, 199), (209, 242, 255)

    def P(vx, vy):
        return cx + vx * ppd, cy - vy * ppd

    def rect(xc, yc, w, h, fill, radius=0.0):
        x0, y0 = P(xc - w / 2, yc + h / 2)
        x1, y1 = P(xc + w / 2, yc - h / 2)
        if radius > 0:
            d.rounded_rectangle([x0, y0, x1, y1], radius=radius * ppd, fill=fill)
        else:
            d.rectangle([x0, y0, x1, y1], fill=fill)

    def text(string, edge_x, yc, align, color, bold=False):
        f = font(13 * ppd)
        px, py = P(edge_x, yc)
        anchor = "lm" if align == "left" else "rm"
        d.text((px, py), string, font=f, fill=color + (255,), anchor=anchor,
               stroke_width=int(0.5 * SS) if bold else 0, stroke_fill=color + (255,))

    left, top = -pw / 2, ph / 2
    rect(0, 0, pw, ph, (5, 18, 26, 128), 10)
    for sx in (-1, 1):
        for sy in (-1, 1):
            rect(sx * (pw / 2 - 2 - 13), sy * (ph / 2 - 2), 28, 2, cyan + (230,))
            rect(sx * (pw / 2 - 2), sy * (ph / 2 - 2 - 13), 2, 28, cyan + (230,))

    def section(title, yc, note=None):
        rect(left + 16, yc, 3, 16, orange + (255,))
        text(title, left + 28, yc, "left", bright, bold=True)
        if note:
            text(note, pw / 2 - 16, yc, "right", dim)
        rect(0, yc - 16, pw - 32, 1, cyan + (77,))

    def row(label, yc, ratio, value, warn=False):
        text(label, left + 22, yc, "left", dim)
        rect(-52 + 60, yc, 120, 6, cyan + (46,), 3)
        width = max(2.0, min(1.0, ratio) * 120)
        rect(-52 + width / 2, yc, width, 6, (orange if warn else cyan) + (255,), 3)
        text(value, pw / 2 - 16, yc, "right", bright)

    section("SYSTEMY", top - 28, "NA ŻYWO")
    row("FPS", top - 62, fps / 60.0, str(fps), fps < 40)
    row("RAM", top - 92, memory, f"{memory * 100:.0f}%", memory > 0.5)
    row("BATERIA", top - 122, battery, f"{battery * 100:.0f}%", battery < 0.2)
    section("SIEĆ", top - 160)
    text(f"OSTATNIA ODPOWIEDŹ  {latencies[-1]:.1f} s", left + 28, top - 184, "left", dim)
    count = len(latencies)
    for i in range(30):
        index = count - 30 + i
        seconds = latencies[index] if 0 <= index < count else -1
        h = 3.0 if seconds < 0 else 4 + 36 * min(1.0, seconds / 6.0)
        color = (orange + (255,)) if (i == 29 and seconds >= 0) else (cyan + (64,) if seconds < 0 else cyan + (255,))
        rect(left + 22 + i * 8.7, top - 226 + (h - 3) / 2, 5, h, color, 1)
    section("STATUS", top - 262)
    names = {"idle": "SPOCZYNEK", "listening": "SŁUCHAM", "thinking": "MYŚLĘ", "speaking": "MÓWIĘ"}
    for yc, label, value in ((top - 292, "STAN", names[state]), (top - 318, "MODEL", "CLAUDE OPUS 5.5"),
                             (top - 344, "GŁOS", "PL-PL"), (top - 370, "CZAS PRACY", uptime),
                             (top - 396, "NOTATKI", "412 ZN.")):
        text(label, left + 22, yc, "left", dim)
        text(value, pw / 2 - 16, yc, "right", bright)


def draw_subtitle(overlay, camera, parts):
    """parts: [(text, colour)] drawn as one line, centred, bold 26 dp."""
    if not parts:
        return
    d = ImageDraw.Draw(overlay, "RGBA")
    centre = np.array([0, (HEIGHT + RAD * 2.45) * SCALE + 0.06, 0])  # above the dial
    x, y, z = camera.project(centre, ss=SS)
    ppd = F * SS * DP / z[0]
    f = font(20 * ppd, "Roboto-Bold.ttf")
    widths = [d.textlength(t, font=f) for t, _ in parts]
    px = x[0] - sum(widths) / 2
    for (text, color), w in zip(parts, widths):
        # a soft shadow, so white text reads on a bright room
        d.text((px + 1.5 * SS, y[0] + 1.5 * SS), text, font=f, fill=(0, 0, 0, 150), anchor="lm")
        d.text((px, y[0]), text, font=f, fill=color + (255,), anchor="lm", stroke_width=int(0.8 * SS), stroke_fill=color + (255,))
        px += w


def composite(bg, holo, overlay):
    img = np.clip(bg + holo, 0, 1)
    pil = Image.fromarray((img * 255 + 0.5).astype(np.uint8)).convert("RGBA")
    ov = overlay.resize((W, H), Image.LANCZOS)
    return Image.alpha_composite(pil, ov).convert("RGB")


WORDS = ["Dziś", "w", "Warszawie", "będzie", "dwadzieścia", "stopni", "i", "słonecznie."]


def window(current):
    lo, hi = max(0, current - 1), min(len(WORDS) - 1, current + 2)
    parts = []
    for i in range(lo, hi + 1):
        word = WORDS[i] + " "
        parts.append((word, (51, 224, 255) if i == current else (255, 255, 255)))
    return parts


def scene(camera_eye, camera_target, state, level, now, subtitle, waves=(), globe=40, rings=(10, 200, 100), dust=17, **extra):
    camera = Camera(camera_eye, camera_target)
    frame = Frame(camera)
    holo = Hologram()
    holo.render(frame, state, level, now, globe, rings, dust, waves, **extra)
    overlay = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    draw_hud(overlay, camera, state)
    draw_controls(overlay, camera)
    draw_subtitle(overlay, camera, subtitle)
    return composite(background(camera), frame.result(), overlay), camera


if __name__ == "__main__":
    import os
    os.makedirs(OUT, exist_ok=True)
    eye, target = (0.0, 0.5, -0.85), (0.0, 0.22, 0.0)
    shots = {
        "idle": dict(state="idle", level=0.0, now=3.0, subtitle=[], base_angle=1.3, sweep_angle=35, dial_angle=40, pulse_clock=0.37, comet_angles=(30, 100, 200, 60, 150, 250)),
        "listening": dict(state="listening", level=0.0, now=3.0, subtitle=[("słucham…", (154, 223, 242))],
                          base_angle=2.7, sweep_angle=140, dial_angle=95, pulse_clock=0.12),
        "thinking": dict(state="thinking", level=0.0, now=3.0, subtitle=[("myślę...", (92, 225, 255))], globe=120, rings=(70, 20, 250),
                         base_angle=5.0, sweep_angle=260, dial_angle=160, pulse_clock=0.71),
        "speaking": dict(state="speaking", level=1.0, now=3.0, subtitle=window(4), waves=(0.25, 0.6), globe=75, rings=(55, 140, 20),
                         base_angle=3.3, sweep_angle=200, dial_angle=210, pulse_clock=0.52),
    }
    images = {}
    for name, kw in shots.items():
        img, cam = scene(eye, target, **kw)
        images[name] = img
        print("rendered", name)

    def label(img, text, xy=(14, 12)):
        d = ImageDraw.Draw(img)
        f = font(24)
        w = d.textlength(text, font=f)
        d.rounded_rectangle([xy[0] - 8, xy[1] - 4, xy[0] + w + 8, xy[1] + 32], radius=10, fill=(0, 0, 0, 170))
        d.text(xy, text, font=f, fill=(235, 240, 245))

    # 1. The desk, the hologram speaking: the whole frame of one eye, cropped top and bottom
    hero = images["speaking"].crop((0, 150, W, 1040))
    label(hero, "V.I.C.T.O.R. na biurku, mówi (jedno oko, 1311 x 890 px)")
    hero.save(f"{OUT}/10_victor_na_biurku.png")

    # 2. The four states side by side
    box = (180, 180, 1130, 940)
    names = [("idle", "Spoczynek"), ("listening", "Słucha"), ("thinking", "Myśli"), ("speaking", "Mówi")]
    tiles = []
    for key, title in names:
        tile = images[key].crop(box)
        label(tile, title)
        tiles.append(tile)
    tw, th = tiles[0].size
    sheet = Image.new("RGB", (tw * 2 + 6, th * 2 + 6), (0, 0, 0))
    for i, tile in enumerate(tiles):
        sheet.paste(tile, ((i % 2) * (tw + 6), (i // 2) * (th + 6)))
    sheet.save(f"{OUT}/11_victor_stany.png")

    # 3. A closer look at the globe, enlarged 3x (the pixels are the phone's: this is a zoom, not more detail)
    zoom = images["speaking"].crop((490, 355, 820, 685)).resize((1122, 1122), Image.LANCZOS)
    label(zoom, "Zbliżenie 3x (mówi)")
    zoom.save(f"{OUT}/12_victor_zblizenie.png")
