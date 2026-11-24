# Third-party notices

## Google Cardboard SDK

Parts of `Assets/_Project/Scripts/Stereo/` are C# ports of the Cardboard SDK
(https://github.com/googlevr/cardboard), licensed under the Apache License 2.0:

- `PolynomialRadialDistortion.cs` — from `sdk/polynomial_radial_distortion.cc`
- `LensDistortion.cs` — from `sdk/lens_distortion.cc` and `sdk/distortion_mesh.cc`
- `ViewerParams.cs` — field layout from `proto/cardboard_device.proto`, Cardboard v1 values from
  `sdk/qrcode/cardboard_v1/cardboard_v1.cc`

Parts of `Assets/_Project/Scripts/Tracking/` port the SDK's head tracking, under the same license:

- `SensorFusionEkf.cs` — from `sdk/sensors/sensor_fusion_ekf.cc`
- `GyroscopeBiasEstimator.cs` — from `sdk/sensors/gyroscope_bias_estimator.cc`
- `SensorFilters.cs` — from `sdk/sensors/lowpass_filter.cc`, `mean_filter.cc`, `median_filter.cc`
- `Rotation.cs`, `Vector3d.cs`, `Matrix3x3d.cs` — from `sdk/util/rotation.cc`, `vector.h`,
  `vectorutils.cc`, `matrix_3x3.cc`, `matrixutils.cc`

`Assets/Plugins/Android/CardboardXRImu.cpp` follows the SDK's Android sensor readers
(`sdk/sensors/android/device_gyroscope_sensor.cc`, `device_accelerometer_sensor.cc` and
`sensor_event_producer.cc`), rewritten to read both sensors on one thread into a buffer.

Changes from the original: translated from C++ to C#, triangle strips replaced with a triangle list,
an iteration cap added to the inverse distortion solver, the sensor fusion's mutex removed (samples
arrive on one thread), and `acos` clamped when reading a rotation's angle.

Copyright 2019 Google LLC. Licensed under the Apache License, Version 2.0:
http://www.apache.org/licenses/LICENSE-2.0

## ZXing.Net

`Assets/Plugins/ZXing/zxing.dll` is ZXing.Net 0.16.11 (https://github.com/micjahn/ZXing.Net),
the `lib/netstandard2.0` build from the NuGet package `ZXing.Net` 0.16.11, unmodified. Used to
decode viewer QR codes. Licensed under the Apache License 2.0.

- Package SHA-256: `7d39234d668e558b3d374d18116ed57021d7c0df482801f1e1db4f1db9314ec3`
- `zxing.dll` SHA-256: `f3b823b6fd6492525a7547989056883def5d43be1e12c4f63fa54df73e3c5cfc`

## BlazeHand models

`Assets/_Project/Hands/Models/hand_detector.onnx` and `hand_landmarks_detector.onnx` come from
https://huggingface.co/unity/inference-engine-blaze-hand, which converts Google's MediaPipe hand
models to ONNX. Licensed under the Apache License 2.0.

## Headset photos

The photos in `docs/photos/`, used by the headset timeline page, are from Wikimedia Commons. Each
keeps its own license (CC BY, CC BY-SA, CC0 or public domain), and the page credits its author and
license and links to its Commons page beside it.

## Phone door generator libraries

The phone door generator page bundles three libraries, unmodified, in `docs/door-generator/vendor/`. Their
licenses are in `vendor/LICENSES.txt` beside them.

- `manifold.js` and `manifold.wasm`: Manifold 3.5.4 (https://github.com/elalish/manifold), for the
  door's geometry. Licensed under the Apache License 2.0.
- `three.module.js`, `three.core.js` and `OrbitControls.js`: three.js r187 (https://threejs.org), for
  the 3D preview. MIT License.
- `fflate.js`: fflate 0.8.3 (https://github.com/101arrowz/fflate), for writing the 3MF and zip files.
  MIT License.

## Inspirations for the hologram (no code copied)

The look of the assistant's hologram (`HologramAssistant.cs`, `Shaders/Hologram.shader`) follows common
hologram-shader techniques (rim light by Fresnel, scan-line shimmer and flicker, glow added to the
background) and audio-reactive particle orbs with idle / listening / thinking / speaking states. Looked at
for the ideas, written from scratch:
- https://github.com/andydbc/HologramShader (MIT, Andy Duboc): a Unity hologram shader.
- https://github.com/nehasriva/phonon (MIT, Neha Srivastava): an audio-reactive particle orb on a sphere.
The sci-fi look of the reference images supplied by the owner (a voice-interface console, glowing
filament spheres from film) is imitated, not reproduced; no film frames, models or assets are included.

## Inspirations for the assistant's tools (no code copied)

The assistant's tool loop and its MCP client (`AssistantSession.cs`, `AgentTools.cs`) are written from scratch against the
public specifications: the Anthropic Messages API (tool use, server tools) and the Model Context Protocol, Streamable HTTP
transport (https://modelcontextprotocol.io). Looked at for the ideas (agent loop, tool confirmation, MCP servers):
- https://github.com/modelcontextprotocol/servers (MIT, reference MCP servers) and
  https://github.com/modelcontextprotocol/csharp-sdk (Apache-2.0, a C# MCP client; not used, as it doesn't fit Unity's IL2CPP).
- https://github.com/block/goose (Apache-2.0), https://github.com/All-Hands-AI/OpenHands (MIT): agents with tools and MCP.
- Google Drive through MCP, run by the user on their own computer, not included: https://github.com/taylorwilsdon/google_workspace_mcp
  (MIT) and https://github.com/piotr-agier/google-drive-mcp.

## Roboto and Material Icons

`Assets/_Project/UI/Fonts/Roboto-Regular.ttf` and `Roboto-Medium.ttf` are from
https://github.com/googlefonts/roboto (`src/hinted/`), and `MaterialIconsRound-Regular.otf` with its
`.codepoints` list from https://github.com/google/material-design-icons (`font/`), unmodified. The
SDF font assets beside them are generated from these. Copyright Google LLC. Licensed under the
Apache License 2.0.
