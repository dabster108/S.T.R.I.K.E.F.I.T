#!/bin/bash
# encode-demo-video.sh: the landing-page demo clip, from the Unity Recorder capture (CinematicCapture) to the web copies.
#
#   Captures/Demo/level2_demo_raw.mp4  (1920x1080, 30 fps, H.264, from Unity)
#     -> web/frontend/public/video/level2-demo.mp4   H.264 High, 1280x720, 30 fps, no audio, +faststart, under 4 MB
#     -> web/frontend/public/video/level2-demo.webm  VP9, 1280x720, 30 fps, no audio (smaller; browsers that play WebM pick it first)
#     -> web/frontend/public/video/level2-demo.jpg   poster (the double hit), 1280x720, under 150 KB
#
# Needs ffmpeg (brew install ffmpeg). Run from anywhere:  bash web/tools/encode-demo-video.sh [raw.mp4] [poster second]
# Both encodes are two-pass at a bitrate worked out from the clip's length, so the size limit holds whatever the length.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RAW="${1:-$ROOT/Captures/Demo/level2_demo_raw.mp4}"
POSTER_AT="${2:-4.95}"
OUT="$ROOT/web/frontend/public/video"
MAX_MP4=$((4 * 1000 * 1000))
MAX_JPG=$((150 * 1000))

command -v ffmpeg >/dev/null || { echo "ffmpeg is not installed: run  brew install ffmpeg  and try again." >&2; exit 1; }
[ -f "$RAW" ] || { echo "no capture at $RAW (record it with CinematicCapture first)" >&2; exit 1; }
mkdir -p "$OUT"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT

DUR=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$RAW")
VF="scale=1280:720:flags=lanczos,fps=30,format=yuv420p"
size() { stat -f%z "$1" 2>/dev/null || stat -c%s "$1"; }

# H.264: video bitrate = 92 % of (4 MB over the clip) for container overhead; retried 15 % lower if it still lands over.
KBPS=$(python3 -c "print(int(${MAX_MP4} * 8 / ${DUR} / 1000 * 0.92))")
for attempt in 1 2 3; do
  ffmpeg -v error -y -i "$RAW" -an -vf "$VF" -c:v libx264 -profile:v high -preset slow -b:v "${KBPS}k" -pass 1 -passlogfile "$TMP/x264" -f mp4 /dev/null
  ffmpeg -v error -y -i "$RAW" -an -vf "$VF" -c:v libx264 -profile:v high -preset slow -b:v "${KBPS}k" -maxrate "$((KBPS * 2))k" -bufsize "$((KBPS * 4))k" \
         -pass 2 -passlogfile "$TMP/x264" -movflags +faststart "$OUT/level2-demo.mp4"
  [ "$(size "$OUT/level2-demo.mp4")" -lt "$MAX_MP4" ] && break
  KBPS=$((KBPS * 85 / 100))
done

# VP9: same target, two-pass (row-mt for speed).
ffmpeg -v error -y -i "$RAW" -an -vf "$VF" -c:v libvpx-vp9 -b:v "${KBPS}k" -row-mt 1 -deadline good -cpu-used 2 -pass 1 -passlogfile "$TMP/vp9" -f webm /dev/null
ffmpeg -v error -y -i "$RAW" -an -vf "$VF" -c:v libvpx-vp9 -b:v "${KBPS}k" -row-mt 1 -deadline good -cpu-used 1 -pass 2 -passlogfile "$TMP/vp9" "$OUT/level2-demo.webm"

# Poster: one frame, quality stepped down until it is under 150 KB.
for q in 4 6 8 10 12; do
  ffmpeg -v error -y -ss "$POSTER_AT" -i "$RAW" -frames:v 1 -vf "scale=1280:720:flags=lanczos" -q:v "$q" "$OUT/level2-demo.jpg"
  [ "$(size "$OUT/level2-demo.jpg")" -lt "$MAX_JPG" ] && break
done

ok=1
check() { local f="$1" max="$2" s; s=$(size "$f"); printf '%-20s %7.2f MB' "$(basename "$f")" "$(python3 -c "print($s/1e6)")"; if [ -n "$max" ] && [ "$s" -ge "$max" ]; then echo "  OVER LIMIT"; ok=0; else echo; fi; }
check "$OUT/level2-demo.mp4" "$MAX_MP4"
check "$OUT/level2-demo.webm" ""
check "$OUT/level2-demo.jpg" "$MAX_JPG"
for f in level2-demo.mp4 level2-demo.webm; do
  ffprobe -v error -select_streams v:0 -show_entries stream=codec_name,width,height,r_frame_rate -of csv=p=0 "$OUT/$f" | sed "s/^/  $f: /"
  [ -z "$(ffprobe -v error -select_streams a -show_entries stream=index -of csv=p=0 "$OUT/$f")" ] && echo "  $f: no audio track"
done
[ "$ok" = 1 ] || { echo "a file is over its limit" >&2; exit 1; }
