# Ambiente isolado do emulador Android: tudo dentro de android/build/emu (nada em ~/.android).
# Uso (Git Bash, a partir de qualquer pasta):  . tools/emu/env.sh
EMU_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/android/build/emu"
mkdir -p "$EMU_ROOT/home" "$EMU_ROOT/avd"
export ANDROID_HOME="${ANDROID_HOME:-$LOCALAPPDATA/Android/Sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export ANDROID_USER_HOME="$(cygpath -w "$EMU_ROOT/home")"
export ANDROID_EMULATOR_HOME="$ANDROID_USER_HOME"
export ANDROID_AVD_HOME="$(cygpath -w "$EMU_ROOT/avd")"
export ANDROID_ADB_SERVER_PORT=5038
export ADB="$ANDROID_HOME/platform-tools/adb.exe"
export PYTHONIOENCODING=utf-8
