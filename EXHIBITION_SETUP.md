# Exhibition Setup

The artwork remains a breathing-led ocean experience. The controls below are for the exhibition operator and are hidden from the audience.

## Runtime Controls

Press `F8` in the built application to open or close **Exhibition Audio Controls**.

- Choose any currently available microphone from the device list. The selected device is used immediately and remembered on that computer.
- The input meter shows the real-time RMS/peak mix used by the water system, its instantaneous peak, and the resulting wave level.
- Adjust the noise floor with the room quiet, then adjust **Maximum input** while testing the intended interaction distance. Use **Sensitivity** only for fine adjustment.
- **Rise speed**, **Return speed**, **Wave curve**, and **Pulse threshold** control the sound-to-water response. Use **Save on this computer** after calibration; the build restores these settings when it next starts.

## Input Simulator

Select **Simulator** in the F8 panel when no microphone is connected or when checking the build before installation.

`F9` is a direct shortcut to the Simulator's **Stress test** and is useful while checking the visitor camera.

- **Quiet** supplies a fixed low input, set with Manual level.
- **Short burst** repeats a short, high input and verifies that a brief strong input immediately makes a substantial wave.
- **Sustained** supplies a changing medium input.
- **Stress test** cycles silence, short peaks, sustained input, and a high peak. Leave it running for several cycles while watching the nearby rocks and shoreline.

The simulator runs through exactly the same response and water-safety code as the microphone. It is intended for pre-opening checks and for reproducing response failures without relying on live audio.

## Water Safety

The active large-wave bands and travelling pulse now have explicit caps. **Wave safety cap** in the F8 panel limits broad HDRP swell so that troughs do not uncover shoreline rocks. The supplied natural-water preset uses `0.92`; do not raise it without checking the full stress test from the visitor camera.

The HDRP water-decal atlas is set to `2048` with room for `96` visible decals in every quality asset. At runtime, shoreline decals are capped at `64x64`, which prevents the previous `No more space in the Water Decal Atlas` error from omitting rock foam and water-deformation decals in dense shoreline areas. The larger atlas uses approximately 24 MB more GPU memory than the former `1024` atlas.
