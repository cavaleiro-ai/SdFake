# Teste ponta a ponta da interface do SdFake para Windows, gravando numa pasta de rascunho
# dentro do projeto (pc\obj\gui-test). Clica em "Teste Completo", confirma e espera o resultado.
param([int]$LimitMiB = 6000)
$ErrorActionPreference = 'Stop'
$p = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'pc'
$shot = "$p\obj\shots"; $dir = "$p\obj\gui-test"
New-Item -ItemType Directory -Force $dir, $shot | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class W2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
  public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  public static IntPtr Child(IntPtr parent, string text) { IntPtr found = IntPtr.Zero; EnumChildWindows(parent, (h, l) => { if (Text(h) == text) { found = h; return false; } return true; }, IntPtr.Zero); return found; }
  public static IntPtr Dialog(uint pid) { IntPtr found = IntPtr.Zero; EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h) && Class(h) == "#32770") { found = h; return false; } return true; }, IntPtr.Zero); return found; }
}
'@
function Shot($h, $file) {
  $r = New-Object W2+RECT; [W2]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T)); $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); [W2]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$app = Start-Process "$p\dist\SdFake.exe" -ArgumentList '--pasta', "`"$dir`"", '--limite', "$LimitMiB" -PassThru
Start-Sleep -Seconds 3; $app.Refresh()
$main = $app.MainWindowHandle
try {
  Shot $main "$shot\main.png"
  $btn = [W2]::Child($main, 'Teste Completo')
  if ($btn -eq [IntPtr]::Zero) { throw 'botao Teste Completo nao encontrado' }
  [W2]::PostMessage($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 1200
  $dlg = [W2]::Dialog([uint32]$app.Id)
  if ($dlg -eq [IntPtr]::Zero) { throw 'dialogo de confirmacao nao apareceu' }
  "dialogo: '$([W2]::Text($dlg))'"
  Shot $dlg "$shot\confirm.png"
  [W2]::PostMessage($dlg, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 2500
  Shot $main "$shot\running.png"
  'arquivos durante o teste:'
  Get-ChildItem "$dir\SdFake_Teste" -ErrorAction SilentlyContinue | ForEach-Object { '  {0}  {1:N0} MB' -f $_.Name, ($_.Length / 1MB) }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 240) { Start-Sleep -Milliseconds 1000; $app.Refresh(); if ($app.MainWindowTitle -eq 'SdFake') { break } }
  Start-Sleep -Seconds 3
  "terminou em ~$([int]$sw.Elapsed.TotalSeconds + 3)s; titulo='$($app.MainWindowTitle)'"
  Shot $main "$shot\done.png"
  'itens que restaram na pasta de teste: ' + (@(Get-ChildItem $dir -Recurse).Count)
} finally {
  $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800
  if (-not $app.HasExited) { $app.Kill() }
}
