import struct
import base64
import os
import qrcode

def varint(n):
    res = []
    while n > 0x7f:
        res.append((n & 0x7f) | 0x80)
        n >>= 7
    res.append(n)
    return bytes(res)

def f_string(field, s):
    b = s.encode('utf-8')
    return varint((field << 3) | 2) + varint(len(b)) + b

def f_float(field, val):
    return varint((field << 3) | 5) + struct.pack('<f', val)

def f_packed_floats(field, floats):
    payload = b''.join(struct.pack('<f', x) for x in floats)
    return varint((field << 3) | 2) + varint(len(payload)) + payload

def f_varint(field, val):
    return varint((field << 3) | 0) + varint(val)

# Shinecon G10 metrics:
# Screen to lens: ~44mm (0.044m)
# Inter-lens IPD: ~63mm (0.063m)
# FOV: ~90 deg total -> 45 deg each side [45.0, 45.0, 45.0, 45.0]
# Tray to lens: ~35mm (0.035m)
# Distortion: k1=0.34, k2=0.12 (aspherical lens)
# Vertical alignment: Center (1)
# Primary button: None (0)

buf = (
    f_string(1, 'VR Shinecon') +
    f_string(2, 'G10') +
    f_float(3, 0.044) +
    f_float(4, 0.063) +
    f_packed_floats(5, [45.0, 45.0, 45.0, 45.0]) +
    f_float(6, 0.035) +
    f_packed_floats(7, [0.34, 0.12]) +
    f_varint(11, 1) +
    f_varint(12, 0)
)

b64 = base64.urlsafe_b64encode(buf).decode('ascii').rstrip('=')
url = f"https://google.com/cardboard/cfg?p={b64}"

out_dir = os.path.dirname(os.path.abspath(__file__))
png_path = os.path.join(out_dir, "Shinecon_G10_QR.png")
html_path = os.path.join(out_dir, "Shinecon_G10_Profile.html")

# Generate QR Code image
qr = qrcode.QRCode(
    version=1,
    error_correction=qrcode.constants.ERROR_CORRECT_M,
    box_size=10,
    border=4,
)
qr.add_data(url)
qr.make(fit=True)
img = qr.make_image(fill_color="black", back_color="white")
img.save(png_path)

html_content = f"""<!DOCTYPE html>
<html lang="pl">
<head>
    <meta charset="UTF-8">
    <title>Profil Optyczny VR Shinecon G10 - CardboardXR</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            background: #111418;
            color: #f0f2f5;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            margin: 0;
            padding: 20px;
            box-sizing: border-box;
        }}
        .card {{
            background: #1e222b;
            border: 1px solid #2d3340;
            border-radius: 16px;
            padding: 32px;
            text-align: center;
            max-width: 480px;
            box-shadow: 0 12px 36px rgba(0,0,0,0.4);
        }}
        h1 {{
            font-size: 24px;
            margin-top: 0;
            color: #ffffff;
        }}
        p {{
            color: #9aa0a6;
            font-size: 15px;
            line-height: 1.5;
        }}
        .qr-container {{
            background: white;
            padding: 16px;
            border-radius: 12px;
            display: inline-block;
            margin: 20px 0;
        }}
        img {{
            display: block;
            width: 260px;
            height: 260px;
        }}
        .param-list {{
            text-align: left;
            background: #15181f;
            border-radius: 8px;
            padding: 14px 20px;
            font-family: monospace;
            font-size: 13px;
            color: #79c0ff;
            margin-top: 20px;
        }}
        .param-list div {{
            margin-bottom: 6px;
        }}
        .badge {{
            display: inline-block;
            background: #238636;
            color: white;
            font-size: 12px;
            font-weight: 600;
            padding: 4px 10px;
            border-radius: 20px;
            margin-bottom: 12px;
        }}
    </style>
</head>
<body>
    <div class="card">
        <div class="badge">PROFIL SKONFIGUROWANY</div>
        <h1>VR Shinecon G10</h1>
        <p>Zeskanuj poniższy kod QR aparatem w aplikacji <b>CardboardXR</b> na telefonie iPhone 16 Pro (przycisk zębatki w prawym górnym rogu &rarr; <i>Set viewer</i>).</p>
        
        <div class="qr-container">
            <img src="Shinecon_G10_QR.png" alt="Kod QR dla VR Shinecon G10">
        </div>

        <div class="param-list">
            <div><b>Model:</b> VR Shinecon G10</div>
            <div><b>Ogniskowa (soczewka-ekran):</b> 44 mm</div>
            <div><b>Rozstaw soczewek (IPD):</b> 63 mm</div>
            <div><b>Pole widzenia (FOV):</b> 90° (symetryczne)</div>
            <div><b>Dystorsja beczkowa (k1, k2):</b> 0.34, 0.12</div>
            <div><b>Wyrównanie:</b> Środek ekranu (Center)</div>
        </div>
    </div>
</body>
</html>
"""

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html_content)

print("Pomyślnie wygenerowano:")
print("1. Obraz QR:", png_path)
print("2. Podgląd HTML:", html_path)
print("3. Cardboard URL:", url)
