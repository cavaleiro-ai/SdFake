#!/usr/bin/env python3
"""Monta o APK do SdFake direto com as ferramentas do Android SDK (aapt2, d8, zipalign, apksigner).

Nao usa Gradle e nao grava nada fora desta pasta. Requer: Android SDK (build-tools + uma
plataforma) e um JDK 17+ (variavel JAVA_HOME).

    python build_apk.py           monta dist/SdFake.apk (assinado)
    python build_apk.py --test    roda antes o autoteste do motor contra cartoes simulados
"""
import os
import re
import secrets
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

APP_ID = "com.sdfake.app"
VERSION_CODE = "2"
VERSION_NAME = "1.0.1"
MIN_SDK = "24"
TARGET_SDK = "35"

HERE = Path(__file__).resolve().parent
BUILD = HERE / "build"
DIST = HERE / "dist"
KEYSTORE_DIR = HERE / "keystore"


def fail(message):
    print("ERRO: " + message)
    sys.exit(1)


def run(args, **kw):
    args = [str(a) for a in args]
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", **kw)
    if result.returncode != 0:
        print(result.stdout)
        fail("comando falhou (%d): %s" % (result.returncode, " ".join(args[:3]) + " ..."))
    return result.stdout


def version_key(path):
    return [int(x) for x in re.findall(r"\d+", path.name)]


def find_tools():
    sdk = os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT")
    if not sdk:
        sdk = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Android", "Sdk")
    sdk = Path(sdk)
    if not sdk.is_dir():
        fail("Android SDK nao encontrado (defina ANDROID_HOME).")
    build_tools = sorted([p for p in (sdk / "build-tools").iterdir() if p.is_dir()], key=version_key)
    if not build_tools:
        fail("build-tools do Android SDK nao encontradas.")
    bt = build_tools[-1]
    platform = sdk / "platforms" / ("android-" + TARGET_SDK) / "android.jar"
    if not platform.is_file():
        platforms = sorted([p for p in (sdk / "platforms").iterdir() if (p / "android.jar").is_file()], key=version_key)
        if not platforms:
            fail("nenhuma plataforma Android instalada no SDK.")
        platform = platforms[-1] / "android.jar"
    clang = None
    ndks = sorted([p for p in (sdk / "ndk").iterdir() if p.is_dir()], key=version_key) if (sdk / "ndk").is_dir() else []
    if ndks:
        hosts = list((ndks[-1] / "toolchains" / "llvm" / "prebuilt").glob("*/bin/clang" + (".exe" if os.name == "nt" else "")))
        if hosts:
            clang = hosts[0]
    java_home = os.environ.get("JAVA_HOME")
    if not java_home or not (Path(java_home) / "bin").is_dir():
        fail("JAVA_HOME nao aponta para um JDK.")
    jbin = Path(java_home) / "bin"
    exe = ".exe" if os.name == "nt" else ""
    return {
        "aapt2": bt / ("aapt2" + exe),
        "zipalign": bt / ("zipalign" + exe),
        "d8": bt / "lib" / "d8.jar",
        "apksigner": bt / "lib" / "apksigner.jar",
        "android_jar": platform,
        "clang": clang,
        "java": jbin / ("java" + exe),
        "javac": jbin / ("javac" + exe),
        "keytool": jbin / ("keytool" + exe),
    }


ABIS = {
    "arm64-v8a": "aarch64-linux-android" + MIN_SDK,
    "armeabi-v7a": "armv7a-linux-androideabi" + MIN_SDK,
    "x86_64": "x86_64-linux-android" + MIN_SDK,
    "x86": "i686-linux-android" + MIN_SDK,
}


def build_native(t):
    """Compila native/sdfake.c para cada arquitetura com o clang do NDK. Retorna {abi: caminho do .so}."""
    libs_dir = HERE / "native" / "libs"
    source = HERE / "native" / "sdfake.c"
    clang = t.get("clang")
    out = {}
    for abi, target in ABIS.items():
        so = libs_dir / abi / "libsdfake.so"
        stale = not so.is_file() or so.stat().st_mtime < source.stat().st_mtime
        if stale and clang:
            so.parent.mkdir(parents=True, exist_ok=True)
            run([clang, "--target=" + target, "-shared", "-fPIC", "-O2", "-Wall", "-Wextra", "-s",
                 "-Wl,--build-id=none", "-Wl,-z,max-page-size=16384", "-o", so, source])
        if so.is_file():
            out[abi] = so
    if not out:
        print("  AVISO: NDK nao encontrado e nao ha bibliotecas prontas em native/libs; o app sai sem a parte nativa.")
    return out


def engine_test(t):
    out = BUILD / "test"
    out.mkdir(parents=True, exist_ok=True)
    sources = sorted((HERE / "src" / "com" / "sdfake" / "app" / "engine").glob("*.java")) + [HERE / "test" / "SimTest.java"]
    run([t["javac"], "-encoding", "UTF-8", "-Xlint:-serial", "-d", out] + sources)
    result = subprocess.run([str(t["java"]), "-Dfile.encoding=UTF-8", "-Dstdout.encoding=UTF-8", "-cp", str(out), "SimTest"])
    if result.returncode != 0:
        fail("autoteste do motor falhou.")


def ensure_keystore(t):
    """Chave de assinatura do app. GUARDE a pasta keystore: atualizacoes precisam da mesma chave."""
    KEYSTORE_DIR.mkdir(exist_ok=True)
    ks = KEYSTORE_DIR / "sdfake.jks"
    pw_file = KEYSTORE_DIR / "senha.txt"
    if ks.is_file() and pw_file.is_file():
        return ks, pw_file.read_text(encoding="utf-8").strip()
    if ks.exists() or pw_file.exists():
        fail("pasta keystore incompleta (falta sdfake.jks ou senha.txt). Nao vou sobrescrever uma chave existente.")
    password = secrets.token_urlsafe(18)
    run([t["keytool"], "-genkeypair", "-keystore", ks, "-storetype", "PKCS12", "-storepass", password, "-keypass", password,
         "-alias", "sdfake", "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000", "-dname", "CN=SdFake, O=SdFake"])
    pw_file.write_text(password + "\n", encoding="utf-8")
    print("  chave de assinatura criada em keystore/ (guarde esta pasta)")
    return ks, password


def main():
    t = find_tools()
    if "--test" in sys.argv:
        engine_test(t)

    for sub in ("gen", "classes", "dex"):
        shutil.rmtree(BUILD / sub, ignore_errors=True)
        (BUILD / sub).mkdir(parents=True)
    DIST.mkdir(exist_ok=True)

    print("== recursos (aapt2) ==")
    res_zip = BUILD / "res.zip"
    run([t["aapt2"], "compile", "--dir", HERE / "res", "-o", res_zip])
    unsigned = BUILD / "unsigned.apk"
    run([t["aapt2"], "link", "-o", unsigned, "-I", t["android_jar"], "--manifest", HERE / "AndroidManifest.xml",
         "--java", BUILD / "gen", "--min-sdk-version", MIN_SDK, "--target-sdk-version", TARGET_SDK,
         "--version-code", VERSION_CODE, "--version-name", VERSION_NAME, res_zip])

    print("== codigo (javac + d8) ==")
    sources = sorted((HERE / "src").rglob("*.java")) + sorted((BUILD / "gen").rglob("*.java"))
    run([t["javac"], "-encoding", "UTF-8", "-source", "8", "-target", "8", "-Xlint:-options", "-Xlint:deprecation",
         "-bootclasspath", t["android_jar"], "-d", BUILD / "classes"] + sources)
    classes = sorted((BUILD / "classes").rglob("*.class"))
    run([t["java"], "-cp", t["d8"], "com.android.tools.r8.D8", "--release", "--lib", t["android_jar"],
         "--min-api", MIN_SDK, "--output", BUILD / "dex"] + classes)

    print("== biblioteca nativa (NDK) ==")
    native = build_native(t)

    print("== pacote (zipalign + apksigner) ==")
    with zipfile.ZipFile(unsigned, "a", zipfile.ZIP_DEFLATED) as z:
        z.write(BUILD / "dex" / "classes.dex", "classes.dex")
        for abi, so in sorted(native.items()):
            z.write(so, "lib/%s/libsdfake.so" % abi, compress_type=zipfile.ZIP_STORED)
    aligned = BUILD / "aligned.apk"
    run([t["zipalign"], "-f", "-P", "16", "4", unsigned, aligned])
    ks, password = ensure_keystore(t)
    apk = DIST / "SdFake.apk"
    run([t["java"], "-jar", t["apksigner"], "sign", "--ks", ks, "--ks-key-alias", "sdfake",
         "--ks-pass", "pass:" + password, "--key-pass", "pass:" + password, "--out", apk, aligned])
    run([t["java"], "-jar", t["apksigner"], "verify", apk])
    idsig = Path(str(apk) + ".idsig")
    if idsig.exists():
        idsig.unlink()
    final = HERE.parent / "Instaladores"
    final.mkdir(exist_ok=True)
    shutil.copy2(apk, final / "SdFake.apk")
    print("OK  %s  (%d KB)  %s v%s" % (apk, apk.stat().st_size // 1024, APP_ID, VERSION_NAME))
    print("Copiado para %s" % (final / "SdFake.apk"))


if __name__ == "__main__":
    main()
