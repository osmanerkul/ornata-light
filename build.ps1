$ErrorActionPreference = 'Stop'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc"
}

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$iconScript = Join-Path $dir 'make_icon.ps1'
$ico = Join-Path $dir 'OrnataLight.ico'
$src = Join-Path $dir 'Program.cs'
$out = Join-Path $dir 'OrnataLight.exe'

& powershell -NoProfile -ExecutionPolicy Bypass -File $iconScript

& $csc /nologo /target:winexe /optimize+ /platform:x64 `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/win32icon:$ico" "/out:$out" "$src"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$info = Get-Item $out
Write-Output ("Built {0} ({1} bytes)" -f $info.FullName, $info.Length)
