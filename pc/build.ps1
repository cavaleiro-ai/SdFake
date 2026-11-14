# Compila o SdFake para Windows usando o compilador C# que ja vem no Windows (.NET Framework 4).
# Nao instala nem baixa nada. Saida em pc\dist\.
#   .\build.ps1            compila tudo e roda o autoteste
#   .\build.ps1 -NoTest    compila sem rodar o autoteste
param([switch]$NoTest)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'Compilador C# do .NET Framework 4 nao encontrado.' }

$dist = Join-Path $here 'dist'
$obj = Join-Path $here 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null

$icon = Join-Path $here 'assets\sdfake.ico'
if (-not (Test-Path $icon)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here '..\tools\make-icons.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar os icones.' }
}

function Invoke-Csc([string[]]$arguments) {
    & $csc /nologo /codepage:65001 /optimize+ /unsafe /warn:4 /nowarn:1591 @arguments
    if ($LASTEXITCODE -ne 0) { throw "csc falhou ($LASTEXITCODE)" }
}

$core = @('src\Core.cs', 'src\FullTest.cs', 'src\QuickTest.cs', 'src\Win.cs') | ForEach-Object { Join-Path $here $_ }
$refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

Write-Host '== SdFakeCli.exe (linha de comando + autoteste) =='
Invoke-Csc (@('/target:exe', "/out:$obj\SdFakeCli.exe", '/r:System.dll', '/r:System.Core.dll') + $core + (Join-Path $here 'tests\Cli.cs'))

if (-not $NoTest) {
    Write-Host '== Autoteste =='
    & "$obj\SdFakeCli.exe" selftest (Join-Path $obj 'selftest-tmp')
    if ($LASTEXITCODE -ne 0) { throw 'Autoteste falhou.' }
}

Write-Host '== SdFake.exe (programa) =='
Invoke-Csc (@('/target:winexe', "/out:$dist\SdFake.exe", "/win32icon:$icon", "/win32manifest:$here\src\app.manifest",
    "/resource:$here\assets\logo.png,logo.png") + $refs + $core +
    @((Join-Path $here 'src\MainForm.cs'), (Join-Path $here 'src\WelcomeForm.cs'), (Join-Path $here 'src\Program.cs'), (Join-Path $here 'src\AssemblyInfo.cs')))

Write-Host '== SdFake-Instalador.exe =='
Invoke-Csc (@('/target:winexe', "/out:$dist\SdFake-Instalador.exe", "/win32icon:$icon", "/win32manifest:$here\installer\setup.manifest",
    "/resource:$dist\SdFake.exe,SdFake.exe") + $refs + @((Join-Path $here 'installer\Setup.cs'), (Join-Path $here 'installer\AssemblyInfo.cs')))

Copy-Item "$obj\SdFakeCli.exe" "$dist\SdFakeCli.exe" -Force
Write-Host ''
Get-ChildItem $dist -Filter *.exe | ForEach-Object { '{0,-24} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }

# Copia o que interessa ao usuario final para a pasta Instaladores, na raiz do projeto.
$out = Join-Path $here '..\Instaladores'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$dist\SdFake-Instalador.exe" (Join-Path $out 'SdFake-Instalador.exe') -Force
Copy-Item "$dist\SdFake.exe" (Join-Path $out 'SdFake-Portatil.exe') -Force
Write-Host "Copiado para $((Resolve-Path $out).Path)"
