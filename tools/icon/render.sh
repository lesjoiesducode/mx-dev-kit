#!/bin/sh
# Renders the plugin icon (Logi Options+ wants a 256x256 PNG) from icon.svg.
# Needs rsvg-convert: brew install librsvg. `</>` is Fira Code Medium and SemiBold (OFL), converted to paths.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
rsvg-convert -w 256 -h 256 "$here/icon.svg" -o "$here/../../src/MxDevKitPlugin/package/metadata/Icon256x256.png"
