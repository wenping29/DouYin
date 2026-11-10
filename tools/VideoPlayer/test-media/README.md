# Synthetic video samples

Run `.\test-media\generate-samples.ps1` in PowerShell to generate two short,
silent, four-second video samples using only built-in PowerShell/.NET APIs:

- `sample-uncompressed.avi` — AVI container with uncompressed 24-bit RGB frames.
- `sample.y4m` — YUV4MPEG2 container with 4:2:0 YUV frames.

Both use animated color bars and a moving block so playback, seeking, and frame
updates can be checked. These are synthetic test patterns, not movie clips.

This is a small baseline set, not every extension supported by VideoPlayer.
Additional compressed formats such as MP4/H.264, WebM/VP9, MKV, MOV, and WMV
need an encoder such as FFmpeg; no such tool was installed for this set.
