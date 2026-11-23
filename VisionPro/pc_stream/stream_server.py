#!/usr/bin/env python3
"""Shows this computer's screen in the Vision Pro DIY headset (Spatial Workspace, "PC" window).

Serves the screen as JPEG frames on the local network:
    GET  /frame.jpg            the whole main screen (one frame per request)
    GET  /frame.jpg?app=NAME   only that app's window, so each app can be its own screen in VR
    GET  /windows              the visible app windows, as JSON (for the headset's app list)
    POST /input                clicks and typing from the headset, ONLY with --allow-input
    POST /offer                WebRTC: smooth, encrypted video instead of JPEG frames (optional:
                               pip install aiortc numpy; the headset falls back to JPEG without it)

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
import math
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
            os.chmod(TOKEN_FILE, 0o600)
            return token
    token = secrets.token_urlsafe(12)  # ~96 bits
    descriptor = os.open(TOKEN_FILE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as f:
        f.write(token)
    return token
_capture_lock = threading.Lock()


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
    with _capture_lock, new_mss() as sct:  # one per grab: mss handles belong to one thread on Windows/X11
        shot = sct.grab(region)
    image = Image.frombytes("RGB", shot.size, shot.rgb)
    if image.width > ARGS.width:
        image = image.resize((ARGS.width, round(image.height * ARGS.width / image.width)), Image.BILINEAR)
    buffer = io.BytesIO()
    image.save(buffer, "JPEG", quality=ARGS.quality)
    return buffer.getvalue()


def new_mss():
    return mss.MSS() if hasattr(mss, "MSS") else mss.mss()


def monitor_size():
    with new_mss() as sct:
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
        u, v = float(event["x"]), float(event["y"])
        if not (math.isfinite(u) and math.isfinite(v)):
            return
        u = min(max(u, 0.0), 1.0)  # never outside the shared window/screen
        v = min(max(v, 0.0), 1.0)
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


# ---- WebRTC (optional): the screen or one app's window as a live video track ----
# aiortc runs on its own asyncio loop in a background thread; the HTTP handler hands it the headset's
# offer and returns the answer (all ICE candidates included: no trickle, no STUN, local network only).
# Media is encrypted (DTLS-SRTP), and /offer needs the token like everything else.
MAX_PEERS = 4
_rtc = {"loop": None, "peers": set()}

try:
    import asyncio
    import fractions  # noqa: F401  (used by aiortc's timestamps)
    import numpy
    from aiortc import RTCPeerConnection, RTCSessionDescription, VideoStreamTrack
    from av import VideoFrame
    HAVE_RTC = True
except ImportError:
    HAVE_RTC = False

if HAVE_RTC:
    class ScreenTrack(VideoStreamTrack):
        """~30 fps of the main screen or one app's window, at most ARGS.width wide."""

        def __init__(self, app):
            super().__init__()
            self.app = app
            self.sct = None
            # Always the same thread for this track's mss handle (required on Windows/X11)
            from concurrent.futures import ThreadPoolExecutor
            self.executor = ThreadPoolExecutor(max_workers=1)

        def stop(self):
            super().stop()
            self.executor.shutdown(wait=False)

        def grab(self):
            if self.sct is None:
                self.sct = new_mss()
            shot = self.sct.grab(find_region(self.app))
            image = numpy.frombuffer(shot.rgb, dtype=numpy.uint8).reshape(shot.height, shot.width, 3)
            frame = VideoFrame.from_ndarray(image, format="rgb24")
            if frame.width > ARGS.width:
                height = round(frame.height * ARGS.width / frame.width) // 2 * 2
                frame = frame.reformat(width=ARGS.width, height=height)
            return frame

        async def recv(self):
            pts, time_base = await self.next_timestamp()
            loop = asyncio.get_running_loop()
            try:
                frame = await loop.run_in_executor(self.executor, self.grab)
            except Exception as error:  # window closed meanwhile: a black frame, not a dead stream
                print(f"capture error: {error}", file=sys.stderr)
                frame = VideoFrame.from_ndarray(numpy.zeros((360, 640, 3), dtype=numpy.uint8), format="rgb24")
            frame.pts, frame.time_base = pts, time_base
            return frame

    async def answer_offer(sdp, kind, app):
        if len(_rtc["peers"]) >= MAX_PEERS:
            raise RuntimeError("too many viewers")
        peer = RTCPeerConnection()
        _rtc["peers"].add(peer)

        @peer.on("connectionstatechange")
        async def on_state():
            if peer.connectionState in ("failed", "closed", "disconnected"):
                _rtc["peers"].discard(peer)
                await peer.close()

        try:
            await peer.setRemoteDescription(RTCSessionDescription(sdp=sdp, type=kind))
            for transceiver in peer.getTransceivers():
                if transceiver.kind == "video":
                    transceiver.direction = "sendonly"
                    peer.addTrack(ScreenTrack(app))
                    break
            else:
                peer.addTrack(ScreenTrack(app))
            await peer.setLocalDescription(await peer.createAnswer())  # waits for ICE gathering
            return {"sdp": peer.localDescription.sdp, "type": peer.localDescription.type}
        except Exception:
            _rtc["peers"].discard(peer)  # a bad offer must not use up a viewer slot
            await peer.close()
            raise

    def start_rtc_loop():
        loop = asyncio.new_event_loop()
        threading.Thread(target=loop.run_forever, daemon=True).start()
        _rtc["loop"] = loop


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
        except LookupError:  # the app's window is closed, minimised or on another Space
            self.send_error(404, "no visible window of that app")
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
        if self.path not in ("/input", "/offer"):
            self.send_error(404)
            return
        try:
            length = max(0, min(int(self.headers.get("Content-Length", 0)), 64 * 1024))
        except ValueError:
            length = 0
        body = self.rfile.read(length)
        if self.path == "/offer":
            self.handle_offer(body)
            return
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

    def handle_offer(self, body):
        if not HAVE_RTC:
            self.send_error(501, "WebRTC not installed on the computer: pip install aiortc numpy")
            return
        try:
            offer = json.loads(body.decode("utf-8"))
            app = offer.get("app") or None
            if app is not None and app not in {window["app"] for window in list_windows()}:
                self.send_error(404, "no such window")
                return
            future = asyncio.run_coroutine_threadsafe(
                answer_offer(str(offer["sdp"]), str(offer.get("type", "offer")), app), _rtc["loop"])
            answer = future.result(timeout=15)
        except Exception as error:
            print(f"offer error: {error}", file=sys.stderr)
            self.send_error(400)
            return
        data = json.dumps(answer).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

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

    if HAVE_RTC:
        start_rtc_loop()
    address = local_address()
    server = ThreadingHTTPServer(("0.0.0.0", ARGS.port), Handler)
    print(f"Ekran tego komputera: http://{address}:{ARGS.port}/frame.jpg")
    print(f"Wpisz do iPhone'a, plik Documents/pulpit.txt (jedna linia):  {address}:{ARGS.port} {TOKEN}")
    print("Sterowanie z gogli: " + ("WŁĄCZONE (każdy w tej sieci Wi-Fi może klikać!)" if ARGS.allow_input else "wyłączone (tylko podgląd)"))
    print("WebRTC (płynny obraz): " + ("dostępny" if HAVE_RTC else "brak (pip install aiortc numpy), działa tryb JPEG"))
    print("Zatrzymanie: Ctrl+C")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
