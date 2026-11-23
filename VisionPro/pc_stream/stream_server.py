#!/usr/bin/env python3
"""Shows this computer's screen in the Vision Pro DIY headset (Spatial Workspace, "PC" window).

Serves the screen as JPEG frames on the local network:
    GET  /frame.jpg   the current screen (one frame per request)
    POST /input       clicks and typing from the headset, ONLY with --allow-input

Setup:
    pip install mss pillow            # view only
    pip install pyautogui pyperclip   # also needed for --allow-input
    python stream_server.py                  # view only (safe)
    python stream_server.py --allow-input    # the headset can click and type on this computer

Then put this computer's address (printed below) in the iPhone's Documents/pulpit.txt
(Files app > CardboardXR > pulpit.txt) and open a "PC" window in the Workspace.

Security: anyone on the same Wi-Fi can see the screen, and with --allow-input also control it.
Use it on your own home network only, and stop it (Ctrl+C) when you're done.
macOS: allow Screen Recording (and Accessibility for --allow-input) for your terminal in
System Settings > Privacy & Security.
"""
import argparse
import io
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
_capture_lock = threading.Lock()
_local = threading.local()


def grab_jpeg():
    """The primary monitor as JPEG bytes, at most ARGS.width pixels wide."""
    with _capture_lock:
        if not hasattr(_local, "sct"):
            _local.sct = getattr(mss, "MSS", None)() if hasattr(mss, "MSS") else mss.mss()
        monitor = _local.sct.monitors[1]
        shot = _local.sct.grab(monitor)
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


def handle_input(event):
    import pyautogui  # imported only when input is allowed
    pyautogui.FAILSAFE = True  # moving the real mouse to a screen corner stops it
    kind = event.get("type")
    if kind in ("click", "rightclick"):
        left, top, width, height = monitor_size()
        x = left + float(event["x"]) * width
        y = top + float(event["y"]) * height
        pyautogui.click(x, y, button="right" if kind == "rightclick" else "left")
    elif kind == "text":
        text = str(event.get("text", ""))
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
    def do_GET(self):
        if self.path.split("?")[0] != "/frame.jpg":
            self.send_error(404)
            return
        try:
            data = grab_jpeg()
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
    ARGS = parser.parse_args()

    address = local_address()
    server = ThreadingHTTPServer(("0.0.0.0", ARGS.port), Handler)
    print(f"Ekran tego komputera: http://{address}:{ARGS.port}/frame.jpg")
    print(f"Wpisz do iPhone'a, plik Documents/pulpit.txt:  {address}:{ARGS.port}")
    print("Sterowanie z gogli: " + ("WŁĄCZONE (każdy w tej sieci Wi-Fi może klikać!)" if ARGS.allow_input else "wyłączone (tylko podgląd)"))
    print("Zatrzymanie: Ctrl+C")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
