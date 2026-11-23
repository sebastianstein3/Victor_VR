"""Renders of the Spatial Workspace as seen in the headset, from the app's own layout numbers.

Panels are drawn flat in dp (as the M3 UI kit lays them out), then projected in perspective from the
user's eye with the angles/distances in WorkspaceDemo.cs and the dp scale of M3Theme
(DpToMeters = dp * 0.868e-3 * distance * 1.6). Background: one colour (in the headset it is the room).
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = sys.argv[1]   # the Unity project
OUT = sys.argv[2]    # output folder
FONTS = ROOT + "/Assets/_Project/UI/Fonts/"
S = 3  # px per dp when drawing a panel

def hexc(v, a=255):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255, a)

PRIMARY, ON_PRIMARY = hexc(0xD0BCFF), hexc(0x381E72)
SEC, ON_SEC = hexc(0x4A4458), hexc(0xE8DEF8)
SURF, SURF_HIGH, SURF_HIGHEST = hexc(0x211F26), hexc(0x2B2930), hexc(0x36343B)
ON_SURF, ON_SURF_VAR = hexc(0xE6E0E9), hexc(0xCAC4D0)
OUTLINE, OUTLINE_VAR = hexc(0x938F99), hexc(0x49454F)
BACKGROUND = (28, 40, 54)

ICONS = {"aspect_ratio": 0xe85b, "check": 0xe5ca, "close": 0xe5cd, "open_with": 0xe89f, "chevron_left": 0xe5cb, "chevron_right": 0xe5cc,
         "refresh": 0xe5d5, "bolt": 0xea0b, "auto_awesome": 0xe65f, "play_arrow": 0xe037, "movie": 0xe02c,
         "visibility": 0xe8f4, "edit": 0xe3c9, "home": 0xe88a}

_font_cache = {}
def font(size_dp, medium=False, icon=False):
    name = "MaterialIconsRound-Regular.otf" if icon else ("Roboto-Medium.ttf" if medium else "Roboto-Regular.ttf")
    key = (name, size_dp)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(FONTS + name, int(size_dp * S))
    return _font_cache[key]

class Panel:
    """A panel image in dp coordinates: origin at the centre, y up."""
    def __init__(self, w, h, color, radius):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (int(w * S), int(h * S)), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.rect(0, 0, w, h, color, radius)

    def p(self, x, y):
        return ((x + self.w / 2) * S, (self.h / 2 - y) * S)

    def rect(self, x, y, w, h, fill=None, radius=0, outline=None, width=1):
        x0, y0 = self.p(x - w / 2, y + h / 2)
        x1, y1 = self.p(x + w / 2, y - h / 2)
        self.d.rounded_rectangle([x0, y0, x1, y1], radius=radius * S, fill=fill, outline=outline, width=int(width * S))

    def text(self, x, y, s, size=14, color=ON_SURF, medium=False, anchor="mm"):
        self.d.text(self.p(x, y), s, font=font(size, medium), fill=color, anchor=anchor)

    def icon(self, x, y, name, size=20, color=ON_SURF):
        self.d.text(self.p(x, y), chr(ICONS[name]), font=font(size, icon=True), fill=color, anchor="mm")

    def button(self, x, y, label, variant="filled", icon=None, width=0, height=56):
        if not width:
            width = button_width(label, icon)
        bg, fg, ol = {"filled": (PRIMARY, ON_PRIMARY, None), "tonal": (SEC, ON_SEC, None),
                      "outlined": (None, PRIMARY, OUTLINE), "text": (None, PRIMARY, None)}[variant]
        self.rect(x, y, width, height, bg, height / 2, outline=ol, width=1)
        if icon:
            label_w = font(14, True).getlength(label) / S
            self.icon(x - (label_w + 8) / 2, y, icon, 20, fg)
            self.text(x + 14, y, label, 14, fg, True)
        else:
            self.text(x, y, label, 14, fg, True)

    def icon_button(self, x, y, name, variant="text", size=56):
        bg, fg, ol = {"filled": (PRIMARY, ON_PRIMARY, None), "outlined": (None, ON_SURF, OUTLINE),
                      "tonal": (SEC, ON_SEC, None), "text": (None, ON_SURF, None)}[variant]
        self.rect(x, y, size, size, bg, size / 2, outline=ol, width=1)
        self.icon(x, y, name, size * 0.45, fg)

def button_width(label, icon=None):
    return math.ceil(48 + font(14, True).getlength(label) / S + (28 if icon else 0))

# ---- The panels, as the C# builds them ----

def window_frame(title, w=520, h=440):
    """A VirtualScreen at layout size w x h dp: header follows the top, footer/grab the bottom."""
    p = Panel(w, h, SURF, 28)
    top, bottom, left, right = h / 2, -h / 2, -w / 2, w / 2
    p.text(left + 20, top - 32, title, 16, ON_SURF, True, anchor="lm")
    p.icon_button(right - 98, top - 32, "check", "outlined")
    p.icon_button(right - 34, top - 32, "close", "text")
    p.rect(0, top - 72, w - 40, 2, OUTLINE_VAR)
    p.button(left + 220, bottom + 44, "Chwyć, aby przenieść ekran", "tonal", "open_with", 380)
    p.icon_button(right - 60, bottom + 44, "aspect_ratio", "tonal")
    p.footer_y = bottom + 112
    p.body = (left + 30, top - 80, right - 30, bottom + 140)  # x0, y0 (top), x1, y1 (bottom)
    return p

def notepad():
    p = window_frame("Główny Notatnik")
    for i, line in enumerate(["Witaj w przestrzennym notatniku!", "Pisz na wirtualnej klawiaturze biurkowej.",
                              "Lista na dziś: skalibrować soczewki|"]):
        p.text(-230, 128 - i * 24, line, 16, ON_SURF, anchor="lm")
    p.text(-230, -108, "Znaki: 104 | Słowa: 14", 14, ON_SURF_VAR, anchor="lm")
    p.button(20, -108, "-", "text", width=56)
    p.button(84, -108, "+", "text", width=56)
    p.button(190, -108, "Wyczyść", "text", width=110)
    return p

def browser(w=520, h=440, title="DuckDuckGo — Prywatność, uproszczona."):
    p = window_frame(title, w, h)
    x0, y0, x1, y1 = p.body
    cx, cy, bw, bh = (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y0 - y1
    # dark mode page (the browser asks sites for dark mode), laid out for the window's size
    p.rect(cx, cy, bw, bh, (28, 28, 30, 255), 4)
    p.rect(x0 + bw * 0.3, y0 - 2, bw * 0.6, 4, PRIMARY)  # loading progress, 60%
    p.d.ellipse([*p.p(cx - 36, cy + 90), *p.p(cx + 36, cy + 18)], fill=(222, 88, 51, 255))
    p.text(cx, cy + 54, "D", 40, (255, 255, 255, 255), True)
    p.text(cx, cy - 2, "DuckDuckGo", 22, (240, 240, 240, 255), True)
    p.rect(cx, cy - 46, min(560, bw - 100), 40, (52, 52, 56, 255), 20, outline=(80, 80, 86, 255))
    p.text(cx - min(560, bw - 100) / 2 + 20, cy - 46, "Szukaj w internecie bez śledzenia", 14, (170, 170, 178, 255), anchor="lm")
    if bh > 300:  # more of the page fits in a taller window: two rows of tiles inside the page
        for i, line in enumerate(["Wiadomości", "Pogoda: Poznań 4°C", "Kursy walut", "Mapy"]):
            tx = cx + (-1 if i % 2 == 0 else 1) * bw / 4
            ty = cy - 100 - (i // 2) * 52
            p.rect(tx, ty, bw / 2 - 40, 42, (44, 44, 48, 255), 12)
            p.text(tx, ty, line, 14, (220, 220, 225, 255))
    p.text(cx, y1 + 14, "Szyfrowane · duckduckgo.com", 14, (90, 200, 120, 255))
    fy, left, right = p.footer_y, -w / 2, w / 2
    for x, name in ((55, "chevron_left"), (107, "chevron_right"), (159, "refresh")):
        p.icon_button(left + x, fy, name, "text", 48)
    p.button(left + 215, fy, "Aa", "text", width=48)
    p.button(20, fy, "-", "text", width=56)
    p.button(84, fy, "+", "text", width=56)
    p.button(right - 80, fy, "Adres", "tonal", width=110)
    return p

def messenger_from_mac():
    p = window_frame("Messenger")
    p.rect(0, 30, 400, 220, (255, 255, 255, 255), 2)   # a 16:9-ish app window, letterboxed
    p.rect(-140, 30, 120, 220, (244, 245, 247, 255))
    for i, name in enumerate(["Anna", "Marek", "Piotr", "Krzysztof"]):
        p.d.ellipse([*p.p(-192, 120 - i * 44 + 14), *p.p(-164, 120 - i * 44 - 14)], fill=(0, 132, 255, 255))
        p.text(-156, 120 - i * 44, name, 12, (30, 30, 30, 255), anchor="lm")
    for i, (side, msg) in enumerate([(-1, "Działa ten Workspace?"), (1, "Tak, piszę z gogli!"), (-1, "Kosmos 🚀" if False else "Kosmos")]):
        w = font(13).getlength(msg) / S + 24
        x = (60 - 90 + w / 2) if side < 0 else (190 - w / 2)
        p.rect(x, 110 - i * 40, w, 28, (0, 132, 255, 255) if side > 0 else (233, 233, 235, 255), 14)
        p.text(x, 110 - i * 40, msg, 13, (255, 255, 255, 255) if side > 0 else (30, 30, 30, 255))
    p.text(0, -66, "Messenger | WebRTC", 14, (255, 191, 77, 255))
    p.button(20, -108, "-", "text", width=56)
    p.button(84, -108, "+", "text", width=56)
    return p

def phone():
    p = Panel(280, 560, (20, 23, 31, 255), 36)
    p.text(-120, 258, "21:37", 14, ON_SURF, True, anchor="lm")
    p.rect(0, 258, 76, 18, (0, 0, 0, 255), 9)
    p.icon_button(100, 248, "close", "text", 48)
    p.text(0, 190, "Twoje Aplikacje", 16, ON_SURF, True)
    p.text(0, 166, "Wybierz aplikację do podglądu", 14, ON_SURF_VAR)
    apps = [("Messenger", "bolt"), ("TikTok", "auto_awesome"), ("Shorts", "play_arrow"),
            ("Reels", "movie"), ("Twitch", "visibility"), ("Notatki", "edit")]
    for i, (name, ic) in enumerate(apps):
        x, y = (-64 if i % 2 == 0 else 64), 92 - (i // 2) * 100
        p.icon_button(x, y, ic, "filled", 56)
        p.text(x, y - 40, name, 14, ON_SURF, True)
    p.button(0, -248, "Przenieś smartfon", "text", "open_with", 240)
    return p

DOCK = [("Ekran", "filled"), ("PC", "tonal"), ("Aplikacje", "tonal"), ("Telefon", "filled"), ("Głos", "outlined"),
        ("PL", "text"), ("Kino", "outlined"), ("Na blat", "tonal"), ("Wyśrodkuj", "tonal"), ("?", "text")]

def dock():
    widths = [button_width(label) for label, _ in DOCK]
    total = sum(widths) + 8 * (len(widths) - 1)
    p = Panel(total + 32, 56 + 32, SURF_HIGHEST, 36)
    x = -total / 2
    for (label, variant), w in zip(DOCK, widths):
        p.button(x + w / 2, 0, label, variant, width=w)
        x += w + 8
    return p

def keyboard():
    p = Panel(640, 240, SURF_HIGH, 24)
    step = 54
    start = -((11 * step) - 6) / 2 + 24
    for i, k in enumerate("qwertyuiop"):
        p.button(start + i * step, 90, k, "filled", width=48)
    p.button(start + 10 * step, 90, "Del", "tonal", width=48)
    start2 = -(10 * step + 16) / 2 + 24
    for i, k in enumerate("asdfghjkl"):
        p.button(start2 + i * step, 30, k, "outlined" if k == "k" else "filled", width=48)  # "k": under the fingertip
    p.button(start2 + 9 * step + 8, 30, "Enter", "filled", width=64.8)
    shift_x = -270 + 28.8
    p.button(shift_x, -30, "Shift", "outlined", width=57.6)
    cx = shift_x + 28.8 + 6 + 24
    for i, k in enumerate("zxcvbnm,."):
        p.button(cx + i * step, -30, k, "filled", width=48)
    p.button(0, -90, "SPACJA", "tonal", width=320)
    p.button(230, -90, "Wyczyść", "outlined", width=90)
    return p

def trackpad():
    p = Panel(180, 130, SURF_HIGHEST, 16)
    p.rect(0, 0, 176, 126, SURF, 14)
    p.text(0, -44, "Gładzik biurkowy", 14, ON_SURF_VAR, True)
    return p

# ---- Geometry: where each panel is (WorkspaceDemo.cs) ----

def dp_m(dp, d):
    return dp * 0.868e-3 * d * 1.6

def direction(yaw, pitch):
    y, p = math.radians(yaw), math.radians(pitch)
    return (math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p))

def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])

def norm(a):
    l = math.sqrt(sum(c * c for c in a))
    return tuple(c / l for c in a)

def scene():
    items = [(notepad(), 0, 3, 1.4), (browser(), -46, 3, 1.4), (messenger_from_mac(), 86, 3, 1.4),
             (phone(), 54, 0, 1.25), (dock(), 0, -20, 1.2), (keyboard(), 0, -38, 0.6), (trackpad(), 44, -38, 0.6)]
    placed = []
    for panel, yaw, pitch, d in items:
        f = direction(yaw, pitch)
        centre = tuple(c * d for c in f)
        right = norm(cross((0, 1, 0), f))     # Unity's LookRotation: x = up x forward
        up = cross(f, right)
        hw, hh = dp_m(panel.w, d) / 2, dp_m(panel.h, d) / 2
        corners = [tuple(centre[i] - right[i] * hw + up[i] * hh for i in range(3)),   # top-left
                   tuple(centre[i] + right[i] * hw + up[i] * hh for i in range(3)),   # top-right
                   tuple(centre[i] + right[i] * hw - up[i] * hh for i in range(3)),   # bottom-right
                   tuple(centre[i] - right[i] * hw - up[i] * hh for i in range(3))]   # bottom-left
        placed.append((d, panel, corners))
    return sorted(placed, key=lambda t: -t[0])  # far first

def perspective_coeffs(dst, src):
    # PIL's PERSPECTIVE maps output (dst) points to input (src) points
    import numpy as np
    A, B = [], []
    for (x, y), (u, v) in zip(dst, src):
        A.append([x, y, 1, 0, 0, 0, -u * x, -u * y]); B.append(u)
        A.append([0, 0, 0, x, y, 1, -v * x, -v * y]); B.append(v)
    return np.linalg.solve(np.array(A, float), np.array(B, float)).tolist()

def render(width, height, head_yaw=0, head_pitch=0, eye_offset=0.0, hfov=95):
    img = Image.new("RGBA", (width, height), BACKGROUND + (255,))
    f = (width / 2) / math.tan(math.radians(hfov / 2))
    fwd = direction(head_yaw, head_pitch)
    right = norm(cross((0, 1, 0), fwd))
    up = cross(fwd, right)
    eye = tuple(right[i] * eye_offset for i in range(3))
    for _, panel, corners in scene():
        pts = []
        for c in corners:
            v = tuple(c[i] - eye[i] for i in range(3))
            z = sum(v[i] * fwd[i] for i in range(3))
            if z < 0.05:
                break
            x = sum(v[i] * right[i] for i in range(3))
            y = sum(v[i] * up[i] for i in range(3))
            pts.append((width / 2 + f * x / z, height / 2 - f * y / z))
        if len(pts) < 4:
            continue
        src = [(0, 0), (panel.img.width, 0), (panel.img.width, panel.img.height), (0, panel.img.height)]
        warped = panel.img.transform((width, height), Image.PERSPECTIVE, perspective_coeffs(pts, src), Image.BICUBIC)
        shadow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        shadow.putalpha(warped.getchannel("A").point(lambda a: a * 0.35).filter(ImageFilter.GaussianBlur(10)))
        img.alpha_composite(shadow)
        img.alpha_composite(warped)
    return img

def label(img, text):
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(FONTS + "Roboto-Medium.ttf", 26)
    w = d.textlength(text, font=f)
    d.rounded_rectangle([12, 10, 36 + w, 56], radius=12, fill=(0, 0, 0, 170))
    d.text((24, 18), text, font=f, fill=(255, 255, 255, 235))
    return img

if __name__ == "__main__":
    W, H = 1600, 1200
    label(render(W, H, 0, 0), "Patrząc prosto (jedno oko, pole widzenia 95°)").convert("RGB").save(OUT + "/1_na_wprost.png", optimize=True)
    label(render(W, H, 0, -32), "Patrząc w dół, na klawiaturę i gładzik").convert("RGB").save(OUT + "/2_w_dol_biurko.png", optimize=True)
    label(render(W, H, 40, 0), "Obrót głowy w prawo: telefon i okno aplikacji z Maca").convert("RGB").save(OUT + "/3_w_prawo_telefon.png", optimize=True)
    label(render(W, H, -40, 0), "Obrót głowy w lewo: przeglądarka").convert("RGB").save(OUT + "/4_w_lewo_przegladarka.png", optimize=True)
    # A browser window stretched to 1000 x 640 dp (corner handle), in front of the user
    base_scene = scene
    def stretched_scene():
        items = [(browser(1000, 640), 0, 3, 1.4), (dock(), 0, -27, 1.2)]
        placed = []
        for panel, yaw, pitch, d in items:
            f = direction(yaw, pitch); centre = tuple(c * d for c in f)
            right = norm(cross((0, 1, 0), f)); up = cross(f, right)
            hw, hh = dp_m(panel.w, d) / 2, dp_m(panel.h, d) / 2
            corners = [tuple(centre[i] + sx * right[i] * hw + sy * up[i] * hh for i in range(3))
                       for sx, sy in ((-1, 1), (1, 1), (1, -1), (-1, -1))]
            placed.append((d, panel, corners))
        return sorted(placed, key=lambda t: -t[0])
    scene = stretched_scene
    label(render(W, H, 0, -4), "Okno przeglądarki rozciągnięte za uchwyt w rogu (1000 × 640 dp)").convert("RGB").save(OUT + "/6_rozciagniete_okno.png", optimize=True)
    scene = base_scene
    # What the iPhone's screen shows in the headset: one view per eye (63 mm apart), each seen through a lens
    eye_w, eye_h = 900, 1000
    phone_img = Image.new("RGB", (eye_w * 2, eye_h), (0, 0, 0))
    for i, offset in enumerate((-0.0315, 0.0315)):
        view = render(eye_w, eye_h, 0, 0, offset, hfov=95).convert("RGB")
        mask = Image.new("L", (eye_w, eye_h), 0)
        ImageDraw.Draw(mask).ellipse([20, 20 + (eye_h - eye_w) // 2, eye_w - 20, eye_h - 20 - (eye_h - eye_w) // 2], fill=255)
        phone_img.paste(view, (i * eye_w, 0), mask.filter(ImageFilter.GaussianBlur(14)))
    label(phone_img, "Ekran iPhone'a w goglach: obraz dla każdego oka (bez zniekształcenia soczewek)").save(OUT + "/5_ekran_iphone_stereo.png", optimize=True)
    print("ok")
