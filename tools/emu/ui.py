# Utilitario de teste do app no emulador: le a tela e toca nos elementos pelo texto.
#   python ui.py dump            lista textos e posicoes
#   python ui.py tap "TEXTO"     toca no elemento com esse texto (ou que o contenha)
#   python ui.py wait "TEXTO" N  espera ate N segundos o texto aparecer
#   python ui.py shot arquivo    salva uma captura de tela
import os, re, subprocess, sys, time
import xml.etree.ElementTree as ET
ADB = os.environ["ADB"]

def adb(*a, binary=False):
    r = subprocess.run([ADB] + list(a), stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return r.stdout if binary else r.stdout.decode("utf-8", "replace")

def nodes(tries=4):
    for _ in range(tries):
        adb("shell", "uiautomator", "dump", "/sdcard/ui.xml")
        x = adb("exec-out", "cat", "/sdcard/ui.xml")
        if x.strip().startswith("<?xml"):
            out = []
            for n in ET.fromstring(x).iter("node"):
                t = n.get("text") or n.get("content-desc") or ""
                m = re.match(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", n.get("bounds", ""))
                if t and m:
                    x1, y1, x2, y2 = map(int, m.groups())
                    out.append((t, (x1 + x2) // 2, (y1 + y2) // 2, n.get("class", "").split(".")[-1], n.get("enabled")))
            return out
        time.sleep(1)
    return []

def find(want):
    want = want.lower()
    ns = nodes()
    return [n for n in ns if n[0].lower() == want] or [n for n in ns if want in n[0].lower()]

cmd = sys.argv[1]
if cmd == "dump":
    for t, x, y, c, e in nodes():
        print("%-110s %4d,%-4d %s%s" % (t[:110].replace("\n", " | "), x, y, c, "" if e == "true" else " (desativado)"))
elif cmd == "tap":
    found = find(sys.argv[2])
    if not found:
        print("NAO ENCONTRADO: " + sys.argv[2]); sys.exit(1)
    t, x, y, c, e = found[0]
    adb("shell", "input", "tap", str(x), str(y))
    print("toque em '%s' (%d,%d)" % (t[:40], x, y))
elif cmd == "wait":
    limit = time.time() + float(sys.argv[3])
    while time.time() < limit:
        if find(sys.argv[2]):
            print("apareceu: " + sys.argv[2]); sys.exit(0)
        time.sleep(2)
    print("NAO APARECEU: " + sys.argv[2]); sys.exit(1)
elif cmd == "shot":
    open(sys.argv[2], "wb").write(adb("exec-out", "screencap", "-p", binary=True))
