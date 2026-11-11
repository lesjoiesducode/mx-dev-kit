#!/bin/sh
# Builds what a release ships, in dist/release/. The release workflow runs exactly this script,
# so a release can be checked locally before any tag is pushed.
#
#   tools/build-release.sh                 tests, build, package, checks
#   tools/build-release.sh --tag v1.2.0    the same, and fails unless the tag matches the version
#   tools/build-release.sh --skip-tests    no unit or shell tests (CI already ran them)
#
# dist/release/
#   MxDevKit.lplug4          the plugin, one package for macOS and Windows: double-click it
#   mx-dev-kit.zip   installers, hooks, shell integration, scripts and the plugin
#   install.sh, install.ps1    the installers alone, for curl | sh and irm | iex
#   SHA256SUMS                 checksums of the files above
# dist/release-notes.md        the CHANGELOG section of this version
#
# Names carry no version, so that .../releases/latest/download/<name> always works.
# Needs the .NET 10 SDK and LogiPluginTool (installed with dotnet tool if missing).

set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

tag="" tests=1
while [ $# -gt 0 ]; do
  case $1 in
    --tag) tag=${2:?--tag needs a value}; shift ;;
    --skip-tests) tests=0 ;;
    -h | --help) sed -n '2,17s/^# \{0,1\}//p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done

step() { printf '\n== %s\n' "$*"; }
fail() { echo "build-release: $*" >&2; exit 1; }

project=src/MxDevKitPlugin
yaml=$project/package/metadata/LoupedeckPackage.yaml
version=$(sed -n 's/^version: *//p' "$yaml" | tr -d '\r')
csproj_version=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$project/MxDevKitPlugin.csproj")
echo "MX Dev Kit $version"
case $version in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) fail "version '$version' in $yaml is not X.Y.Z" ;;
esac
[ "$csproj_version" = "$version" ] || fail "$project/MxDevKitPlugin.csproj says $csproj_version, $yaml says $version"
[ -z "$tag" ] || [ "$tag" = "v$version" ] || fail "tag $tag does not match the version v$version of $yaml"

# The notes of this version: its CHANGELOG section, without the heading.
notes=$(awk -v v="$version" '
  /^## / { if (found) exit; if ($2 == v) { found = 1; next } }
  found { print }
' CHANGELOG.md)
if [ -z "$(printf '%s' "$notes" | tr -d '[:space:]')" ]; then
  [ -z "$tag" ] || fail "CHANGELOG.md has no '## $version' section"
  echo "Note: CHANGELOG.md has no '## $version' section yet (needed for a tagged release)."
fi

# Homebrew's .NET needs DOTNET_ROOT for global tools such as LogiPluginTool.
if [ -z "${DOTNET_ROOT:-}" ] && command -v brew >/dev/null 2>&1 && [ -d "$(brew --prefix)/opt/dotnet/libexec" ]; then
  export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
fi
PATH="$PATH:$HOME/.dotnet/tools"
command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.' || fail "the .NET 10 SDK is needed (brew install dotnet)"
if ! command -v logiplugintool >/dev/null 2>&1; then
  step "LogiPluginTool"
  dotnet tool install --global LogiPluginTool --version "${LOGI_PLUGIN_TOOL_VERSION:-6.4.2.3599}"
fi

if [ "$tests" = 1 ]; then
  step "Unit tests"
  dotnet test tests/MxDevKit.Core.Tests -c Release -nologo -v q
  step "Shell tests"
  sh tests/shell/run.sh
fi

step "Plugin"
rm -rf "$project/bin/Release" "$project/obj/Release"
dotnet build "$project" -c Release -nologo -v q -p:LinkPlugin=false

out=dist/release
rm -rf "$out" dist/release-notes.md
mkdir -p "$out"
package="$out/MxDevKit.lplug4"
logiplugintool pack "$project/bin/Release" "$package"
logiplugintool verify "$package"

step "Package checks"
listing=$(unzip -Z1 "$package" | tr '\\' '/')
for f in metadata/LoupedeckPackage.yaml metadata/Icon256x256.png bin/MxDevKitPlugin.dll \
  bin/mx-notify bin/mx-notify.ps1 events/DefaultEventSource.yaml events/extra/eventMapping.yaml; do
  printf '%s\n' "$listing" | grep -qx "$f" || fail "$f is missing from $package"
done
# Logi Plugin Service brings its own PluginApi: a copy in the package would clash with it.
if printf '%s\n' "$listing" | grep -qiE '(^|/)(PluginApi|LogiPluginApi)\.dll$'; then
  fail "$package carries a copy of PluginApi.dll"
fi
packaged_version=$(unzip -p "$package" metadata/LoupedeckPackage.yaml | sed -n 's/^version: *//p' | tr -d '\r')
[ "$packaged_version" = "$version" ] || fail "the package says version $packaged_version, expected $version"
# One package for both systems: the managed DLL runs on both, from the same folder.
grep -q '^pluginFolderWin: *bin' "$yaml" && grep -q '^pluginFolderMac: *bin' "$yaml" || fail "$yaml must list bin for Windows and macOS"
echo "$package: $(printf '%s\n' "$listing" | grep -c .) files, version $packaged_version, macOS and Windows"

step "Scripts"
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
kit="$stage/mx-dev-kit"
mkdir -p "$kit"
# Tracked files only: no backups, no build output.
git ls-files -z -- install.sh install.ps1 uninstall.sh uninstall.ps1 config.example.json LICENSE README.md README.fr.md CHANGELOG.md hooks shell bin \
  | COPYFILE_DISABLE=1 xargs -0 tar cf - | (cd "$kit" && tar xf -)
[ -f "$kit/install.sh" ] && [ -f "$kit/hooks/install.sh" ] || fail "git ls-files found no scripts (not a git checkout?)"
for f in install.sh uninstall.sh bin/haptic bin/mx-notify bin/uptime-watch bin/docker-watch hooks/install.sh hooks/uninstall.sh hooks/agent-haptics.sh; do
  chmod +x "$kit/$f"
done
printf '%s\n' "$version" > "$kit/VERSION"
cp "$package" "$kit/MxDevKit.lplug4"
(cd "$stage" && zip -qrX "$root/$out/mx-dev-kit.zip" mx-dev-kit)
cp install.sh install.ps1 "$out/"
chmod +x "$out/install.sh"

# The archive installs as it is: the installer finds its files and the plugin.
check="$stage/check"
mkdir -p "$check"
(cd "$check" && unzip -q "$root/$out/mx-dev-kit.zip")
for f in install.sh uninstall.sh hooks/install.sh hooks/uninstall.sh bin/haptic bin/mx-notify bin/docker-watch; do
  sh -n "$check/mx-dev-kit/$f" || fail "$f does not parse"
done
[ -x "$check/mx-dev-kit/install.sh" ] || fail "install.sh lost its executable bit in the archive"
cmp -s "$check/mx-dev-kit/MxDevKit.lplug4" "$package" || fail "the archive does not carry the same MxDevKit.lplug4"
sh "$check/mx-dev-kit/install.sh" --help >/dev/null || fail "install.sh --help fails from the archive"

(cd "$out" && shasum -a 256 MxDevKit.lplug4 mx-dev-kit.zip install.sh install.ps1 > SHA256SUMS)
printf "%s\n" "$notes" | sed "/./,\$!d" > dist/release-notes.md

step "Done"
(cd "$out" && ls -l)
echo "Release notes: dist/release-notes.md"
