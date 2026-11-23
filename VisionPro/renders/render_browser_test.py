"""Puts real page captures into the Workspace's browser window, as the headset would show them.

Captures come from a desktop Chromium set up like the in-app browser (page 700 x 420 points at 2x, dark
mode, Polish, iPhone Safari user agent, the same trackers blocked; Messenger as a desktop site) - an
approximation: on the iPhone the engine is Safari's WebKit.
Usage: python render_browser_test.py <Unity project> <folder with *_vr.png captures> <output folder>
"""
import sys
from PIL import Image, ImageDraw
import render_workspace as rw

ROOT, SHOTS, OUT = sys.argv[1], sys.argv[2], sys.argv[3]

PAGES = [  # capture, window title, host, encrypted, what the test found
    ("duckduckgo", "DuckDuckGo — Prywatność, uproszczona.", "duckduckgo.com", True, "OK, tryb ciemny"),
    ("wikipedia", "Rzeczywistość wirtualna – Wikipedia", "pl.wikipedia.org", True, "OK, czytelny tekst"),
    ("youtube", "YouTube", "m.youtube.com", True, "OK, tryb ciemny; 2 trackery zablokowane"),
    ("github", "GitHub · Build and ship software", "github.com", True, "OK"),
    ("messenger", "Messenger", "www.messenger.com", True, "OK jako wersja na komputer (logowanie)"),
    ("onet", "Onet – Jesteś na bieżąco", "www.onet.pl", True, "Okno zgody na cookies – kliknij raz"),
    ("wp", "Wirtualna Polska", "www.wp.pl", True, "Okno zgody na cookies – kliknij raz"),
    ("allegro", "Allegro", "allegro.pl", True, "Zablokowane w teście (serwer w chmurze)"),
]

def browser_window(capture, title, host, secure, w=760, h=640):
    # The header shows "Szyfrowane · host — title" (BrowserView.ShowHost); nothing lies over the page
    p = rw.window_frame("", w, h)
    top, left = h / 2, -w / 2
    mark, color = ("Szyfrowane", (90, 200, 120, 255)) if secure else ("NIESZYFROWANE", (230, 90, 70, 255))
    p.text(left + 20, top - 32, mark, 16, color, True, anchor="lm")
    mw = rw.font(16, True).getlength(mark + " · ") / rw.S
    p.text(left + 20 + mw, top - 32, f"{host} — {title}", 16, rw.ON_SURF, True, anchor="lm")
    x0, y0, x1, y1 = p.body
    page = Image.open(f"{SHOTS}/{capture}_vr.png").convert("RGBA")
    box = [int(v) for v in (*p.p(x0, y0), *p.p(x1, y1))]
    page = page.resize((box[2] - box[0], box[3] - box[1]), Image.LANCZOS)
    mask = Image.new("L", page.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, page.size[0] - 1, page.size[1] - 1], radius=4 * rw.S, fill=255)
    p.img.paste(page, (box[0], box[1]), mask)
    fy, left, right = p.footer_y, -w / 2, w / 2
    for x, name in ((55, "chevron_left"), (107, "chevron_right"), (159, "refresh")):
        p.icon_button(left + x, fy, name, "text", 48)
    p.button(left + 215, fy, "Aa", "text", width=48)
    p.button(20, fy, "-", "text", width=56)
    p.button(84, fy, "+", "text", width=56)
    p.button(right - 80, fy, "Adres", "tonal", width=110)
    return p

def contact_sheet():
    panels = [(browser_window(c, t, hst, sec).img, c, note) for c, t, hst, sec, note in PAGES]
    scale = 0.22
    pw, ph = int(panels[0][0].width * scale), int(panels[0][0].height * scale)
    sheet = Image.new("RGB", (2 * pw + 60, 4 * (ph + 56) + 70), rw.BACKGROUND)
    d = ImageDraw.Draw(sheet)
    d.text((20, 14), "Test stron w przeglądarce Workspace (okno 760 × 640 dp, strona 700 × 420)",
           font=rw.ImageFont.truetype(rw.FONTS + "Roboto-Medium.ttf", 26), fill=(255, 255, 255))
    small = rw.ImageFont.truetype(rw.FONTS + "Roboto-Regular.ttf", 20)
    for i, (img, name, note) in enumerate(panels):
        x, y = 20 + (i % 2) * (pw + 20), 60 + (i // 2) * (ph + 56)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, y), img.resize((pw, ph), Image.LANCZOS))
        d.text((x + 6, y + ph + 6), f"{name}: {note}", font=small, fill=(220, 220, 228))
    sheet.save(f"{OUT}/7_test_stron.png", optimize=True)

def in_headset(capture, title, host, out_name, caption):
    window = browser_window(capture, title, host, True)
    def one_window():
        f = rw.direction(0, 3)
        centre = tuple(c * 1.4 for c in f)
        right = rw.norm(rw.cross((0, 1, 0), f)); up = rw.cross(f, right)
        hw, hh = rw.dp_m(window.w, 1.4) / 2, rw.dp_m(window.h, 1.4) / 2
        corners = [tuple(centre[i] + sx * right[i] * hw + sy * up[i] * hh for i in range(3))
                   for sx, sy in ((-1, 1), (1, 1), (1, -1), (-1, -1))]
        return [(1.4, window, corners)]
    rw.scene = one_window
    rw.label(rw.render(1600, 1200, 0, 3), caption).convert("RGB").save(f"{OUT}/{out_name}", optimize=True)

if __name__ == "__main__":
    contact_sheet()
    in_headset("wikipedia", "Rzeczywistość wirtualna – Wikipedia", "pl.wikipedia.org", "8_wikipedia_w_goglach.png",
               "Wikipedia w oknie przeglądarki, patrząc na wprost (domyślne okno przeglądarki)")
    in_headset("messenger", "Messenger", "www.messenger.com", "9_messenger_w_goglach.png",
               "Messenger jako wersja na komputer (automatycznie dla messenger.com)")
    print("ok")
