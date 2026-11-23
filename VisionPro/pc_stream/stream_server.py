#!/usr/bin/env python3
"""Shows this computer's screen in the Vision Pro DIY headset (Spatial Workspace, "PC" window).

Serves the screen as JPEG frames on the local network:
    GET  /frame.jpg            the whole main screen (one frame per request)
    GET  /frame.jpg?app=NAME   only that app's window, so each app can be its own screen in VR
    GET  /windows              the visible app windows, as JSON (for the headset's app list)
    POST /input                clicks and typing from the headset, ONLY with --allow-input

A window is captured from the screen where it is, so it must not be covered by another window:
place the apps side by side (on a big monitor or a large virtual display, e.g. BetterDisplay on
a Mac mini without a monitor).

Setup:
    pip install mss pillow            # view only
    pip install pyobjc-framework-Quartz   # macOS: the list of app windows (one app per VR screen)
    pip install pyautogui pyperclip   # also needed for --allow-input
    python stream_server.py                  # view only (safe)
    python stream_server.py --allow-input    # the headset can click and type on this computer

Then put this computer's address (printed below) in the iPhone's Documents/pulpit.txt
(Files app > CardboardXR > pulpit.txt) and open a "PC" window in the Workspace.

Security:
- Every request must carry the access token (header X-CXR-Token). It is made once, saved in
  ~/.cardboardxr_stream_token (only your user can read it) and printed at start; put it after the
  address in pulpit.txt. Without it nobody else on the Wi-Fi can see or control this computer.
  --new-token makes a new one (the old one stops working).
- The token travels unencrypted on the local network (plain HTTP, like the frames): use your own
  home network, not public Wi-Fi. Stop the server (Ctrl+C) when you're done.
- --allow-input only: clicks are limited to the shared window or screen, keys to a short list, and an
  app is brought to the front only if it is one of the visible windows.
macOS: allow Screen Recording (and Accessibility for --allow-input) for your terminal in
System Settings > Privacy & Security.
"""
import argparse
import hmac
import io
import os
import secrets
import json
import socket
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

try:
    import mss
    from PIL import Image
except ImportError:
    sys.exit("Brak bibliotek: pip install mss pillow")

ARGS = None
TOKEN = None
TOKEN_FILE = os.path.join(os.path.expanduser("~"), ".cardboardxr_stream_token")


def load_token(renew):
    """The saved access token, or a new one (saved readable by this user only)."""
    if not renew and os.path.exists(TOKEN_FILE):
        with open(TOKEN_FILE, encoding="utf-8") as f:
            token = f.read().strip()
        if len(token) >= 12:
            return token
    token = secrets.token_urlsafe(12)  # ~96 bits
    descriptor = os.open(TOKEN_FILE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as f:
        f.write(token)
    return token
_capture_lock = threading.Lock()
_local = threading.local()


def list_windows():
    """Visible app windows: [{"app", "title", "left", "top", "width", "height"}], in the screen
    coordinates that mss and pyautogui use (points on macOS, pixels on Windows)."""
    windows = []
    if sys.platform == "darwin":
        try:
            import Quartz  # pip install pyobjc-framework-Quartz
        except ImportError:
            return windows
        options = Quartz.kCGWindowListOptionOnScreenOnly | Quartz.kCGWindowListExcludeDesktopElements
        for info in Quartz.CGWindowListCopyWindowInfo(options, Quartz.kCGNullWindowID) or []:
            if info.get("kCGWindowLayer", 1) != 0:  # menus, the Dock, overlays
                continue
            bounds = info.get("kCGWindowBounds") or {}
            width, height = int(bounds.get("Width", 0)), int(bounds.get("Height", 0))
            if width < 200 or height < 150:
                continue
            windows.append({"app": str(info.get("kCGWindowOwnerName", "")), "title": str(info.get("kCGWindowName", "") or ""),
                            "left": int(bounds.get("X", 0)), "top": int(bounds.get("Y", 0)),
                            "width": width, "height": height})
    elif sys.platform == "win32":
        try:
            import pygetwindow  # installed with pyautogui on Windows
        except ImportError:
            return windows
        for window in pygetwindow.getAllWindows():
            if not window.title or window.isMinimized or window.width < 200 or window.height < 150:
                continue
            windows.append({"app": window.title, "title": window.title, "left": window.left, "top": window.top,
                            "width": window.width, "height": window.height})
    # One entry per app (its largest window), named the way the headset will ask for it
    best = {}
    for window in windows:
        name = window["app"]
        if name and (name not in best or window["width"] * window["height"] > best[name]["width"] * best[name]["height"]):
            best[name] = window
    return sorted(best.values(), key=lambda window: window["app"].lower())


def find_region(app):
    """The screen area to capture: the app's window, or the main screen when app is None."""
    if app:
        for window in list_windows():
            if window["app"] == app:
                return {"left": window["left"], "top": window["top"], "width": window["width"], "height": window["height"]}
        raise LookupError(f"no visible window of {app}")
    left, top, width, height = monitor_size()
    return {"left": left, "top": top, "width": width, "height": height}


def grab_jpeg(app=None):
    """The main screen, or one app's window, as JPEG bytes at most ARGS.width pixels wide."""
    region = find_region(app)
    with _capture_lock:
        if not hasattr(_local, "sct"):
            _local.sct = getattr(mss, "MSS", None)() if hasattr(mss, "MSS") else mss.mss()
        shot = _local.sct.grab(region)
    image = Image.frombytes("RGB", shot.size, shot.rgb)
    if image.width > ARGS.width:
        image = image.resize((ARGS.width, round(image.height * ARGS.width / image.width)), Image.BILINEAR)
    buffer = io.BytesIO()
    image.save(buffer, "JPEG", quality=ARGS.quality)
    return buffer.getvalue()


def monitor_size():
    with (mss.MSS() if hasattr(mss, "MSS") else mss.mss()) as sct:
        monitor = sct.monitors[1]
        return monitor["left"], monitor["top"], monitor["width"], monitor["height"]


def activate(app):
    """Brings the app to the front, so the keys typed in the headset reach it."""
    if app not in {window["app"] for window in list_windows()}:
        raise LookupError(f"no visible window of {app}")
    if sys.platform == "darwin":
        import subprocess
        # The name goes in as an argument (argv), never into the script's text: no AppleScript injection
        subprocess.run(["osascript", "-e", "on run argv", "-e", "tell application (item 1 of argv) to activate",
                        "-e", "end run", app], check=False, capture_output=True, timeout=3)
    elif sys.platform == "win32":
        import pygetwindow
        for window in pygetwindow.getWindowsWithTitle(app):
            try:
                window.activate()
            except Exception:
                pass
            break


def handle_input(event):
    import pyautogui  # imported only when input is allowed
    pyautogui.FAILSAFE = True  # moving the real mouse to a screen corner stops it
    kind = event.get("type")
    app = event.get("app") or None
    if app:
        activate(app)  # clicks and typing go to that app
    if kind in ("click", "rightclick"):
        region = find_region(app)
        u = min(max(float(event["x"]), 0.0), 1.0)  # never outside the shared window/screen
        v = min(max(float(event["y"]), 0.0), 1.0)
        x = region["left"] + u * region["width"]
        y = region["top"] + v * region["height"]
        pyautogui.click(x, y, button="right" if kind == "rightclick" else "left")
    elif kind == "text":
        text = str(event.get("text", ""))[:2000]
        if text.isascii():
            pyautogui.write(text)
        else:  # Polish letters etc.: paste through the clipboard
            import pyperclip
            pyperclip.copy(text)
            pyautogui.hotkey("command" if sys.platform == "darwin" else "ctrl", "v")
    elif kind == "key":
        key = str(event.get("key", ""))
        if key in ("enter", "backspace", "tab", "esc", "space", "left", "right", "up", "down", "delete"):
            pyautogui.press(key)


class Handler(BaseHTTPRequestHandler):
    def authorized(self):
        given = self.headers.get("X-CXR-Token", "")
        if hmac.compare_digest(given.encode("utf-8"), TOKEN.encode("utf-8")):
            return True
        self.send_error(401, "missing or wrong token (see the server's output)")
        return False

    def do_GET(self):
        if not self.authorized():
            return
        from urllib.parse import parse_qs, urlparse
        url = urlparse(self.path)
        if url.path == "/windows":
            body = json.dumps({"windows": list_windows()}, ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if url.path != "/frame.jpg":
            self.send_error(404)
            return
        app = (parse_qs(url.query).get("app") or [None])[0]
        try:
            data = grab_jpeg(app)
        except LookupError as error:  # the app's window is closed, minimised or on another Space
            self.send_error(404, str(error))
            return
        except Exception as error:  # e.g. no screen-recording permission on macOS
            print(f"capture error: {error}", file=sys.stderr)
            self.send_error(500, "screen capture failed (macOS: allow Screen Recording for the terminal)")
            return
        self.send_response(200)
        self.send_header("Content-Type", "image/jpeg")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        if not self.authorized():
            return
        if self.path != "/input":
            self.send_error(404)
            return
        length = min(int(self.headers.get("Content-Length", 0)), 64 * 1024)
        body = self.rfile.read(length)
        if not ARGS.allow_input:
            self.send_error(403, "view only: start with --allow-input")
            return
        try:
            handle_input(json.loads(body.decode("utf-8")))
        except Exception as error:  # a bad event must not stop the server
            print(f"input error: {error}", file=sys.stderr)
            self.send_error(400)
            return
        self.send_response(204)
        self.end_headers()

    def log_message(self, *args):  # quiet: one line per frame would flood the terminal
        pass


def local_address():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        try:
            probe.connect(("10.255.255.255", 1))  # no packet is sent; picks the LAN interface
            return probe.getsockname()[0]
        except OSError:
            return "127.0.0.1"


def main():
    global ARGS
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--width", type=int, default=1280, help="max frame width in pixels (default 1280)")
    parser.add_argument("--quality", type=int, default=60, help="JPEG quality 1-95 (default 60)")
    parser.add_argument("--allow-input", action="store_true", help="let the headset click and type here")
    parser.add_argument("--new-token", action="store_true", help="make a new access token (the old one stops working)")
    ARGS = parser.parse_args()
    global TOKEN
    TOKEN = load_token(ARGS.new_token)

    address = local_address()
    server = ThreadingHTTPServer(("0.0.0.0", ARGS.port), Handler)
    print(f"Ekran tego komputera: http://{address}:{ARGS.port}/frame.jpg")
    print(f"Wpisz do iPhone'a, plik Documents/pulpit.txt (jedna linia):  {address}:{ARGS.port} {TOKEN}")
    print("Sterowanie z gogli: " + ("WŁĄCZONE (każdy w tej sieci Wi-Fi może klikać!)" if ARGS.allow_input else "wyłączone (tylko podgląd)"))
    print("Zatrzymanie: Ctrl+C")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
