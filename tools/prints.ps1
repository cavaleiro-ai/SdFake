# Gera os prints do programa de PC em docs\telas (pc-*.png).
# Compila tools\prints\Prints.cs junto com o codigo do programa e roda as janelas reais contra
# cartoes simulados. Nao le nem grava em nenhum disco e leva cerca de 1 minuto.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$pc = Join-Path $root 'pc'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'Compilador C# do .NET Framework 4 nao encontrado.' }

$obj = Join-Path $pc 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$exe = Join-Path $obj 'SdFakePrints.exe'
$sources = @('src\Core.cs', 'src\FullTest.cs', 'src\QuickTest.cs', 'src\Win.cs', 'src\MainForm.cs', 'src\WelcomeForm.cs', 'tests\Cli.cs') |
    ForEach-Object { Join-Path $pc $_ }
$sources += Join-Path $root 'tools\prints\Prints.cs'

& $csc /nologo /codepage:65001 /optimize+ /unsafe /warn:4 /nowarn:1591 /target:exe /main:SdFake.Prints "/out:$exe" `
    "/win32icon:$pc\assets\sdfake.ico" "/win32manifest:$pc\src\app.manifest" "/resource:$pc\assets\logo.png,logo.png" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw "csc falhou ($LASTEXITCODE)" }

$out = Join-Path $root 'docs\telas'
Write-Host "Gerando prints em $out"
& $exe $out
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar os prints.' }
