# BEN D3D11 GPU Compositor — Experimental Prototype

Status: Experimental. Not integrated into the production BEN Trade Recorder.

## Technology

- .NET 8
- Vortice.Direct3D11 3.8.3
- Vortice.DXGI 3.8.3
- Vortice.D3DCompiler 3.8.3
- Lennox.NvEncSharp 2.2.0
- NVIDIA D3D11 / NVENC

## Verified on the test machine

- NVIDIA RTX 5060, D3D11 Feature Level 11.1.
- Three DXGI Desktop Duplication outputs created.
- Three desktop frames copied to persistent D3D11 textures.
- Pointer-only DXGI updates are skipped.
- GPU pixel shader composes three 1920x1080 inputs into a
  1920x1080 2+1 layout.
- Composite D3D11 texture registered with NVENC.
- H.264 NVENC produced 30 frames from the composite texture.
- H.264 elementary stream was produced.

## Important limitations

This is a diagnostic single-frame capture/composition prototype.
The same composite texture is submitted repeatedly for the 30-frame
NVENC test. It is NOT yet a continuous three-monitor recording engine.

Still required:

- Continuous capture loop for all three outputs.
- Correct handling of unchanged desktop frames and pointer-only events.
- Common QPC-based presentation timestamps.
- Per-frame shader composition.
- Continuous NVENC submission and bitstream draining.
- MP4 muxing with explicit, validated timestamps.
- 30 FPS and 60-second stability tests.
- Device-loss, monitor reconfiguration and shutdown handling.

Diagnostic BMP readback is present for inspection only and must not be
used in the production video path.

Do not merge into the production recorder until the live pipeline
passes its performance and recovery tests.
