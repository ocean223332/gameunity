<#
Opens two game windows side by side on this machine, for a 1v1 duel against yourself. See docs/DUEL_LOCAL.md.

  play-duel-local.ps1           launch from the existing build in Builds/Windows
  play-duel-local.ps1 -Build    rebuild the Windows player first (about 10-20 min; close the Unity Editor first)
  play-duel-local.ps1 -Lighting Misty      both windows in another preset than the duel's own (Overcast)
#>
param([switch]$Build, [string]$Lighting = '')
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$exe = Join-Path $repo 'Builds\Windows\TienTuyen.exe'
$logs = Join-Path $env:TEMP 'TienTuyenDuel'
New-Item -ItemType Directory -Force $logs | Out-Null

if ($Build) {
    if (Get-Process -Name Unity -ErrorAction SilentlyContinue) { throw 'Unity is running. Close the Editor, then build again.' }
    $unity = 'D:\Unity\Editors\6000.3.24f1\Editor\Unity.exe'
    $buildLog = Join-Path $logs 'build.log'
    Write-Host "Building the Windows player (log: $buildLog)..."
    # Two job workers: more and the shader compilers push free memory under the 1 GB floor.
    $p = Start-Process $unity -PassThru -Wait -ArgumentList @('-batchmode', '-quit', '-job-worker-count', '2',
        '-projectPath', "`"$(Join-Path $repo 'TienTuyen')`"", '-executeMethod', 'TienTuyen.Editor.CombatSceneSetup.BuildWindows',
        '-logFile', "`"$buildLog`"")
    if ($p.ExitCode -ne 0) { throw "Build failed (exit $($p.ExitCode)). See $buildLog" }
}
if (-not (Test-Path $exe)) { throw "No build at $exe. Run with -Build first." }

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class DuelWindow {
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
}
'@
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$width = [int]($area.Width / 2)
$height = [int]($width * 9 / 16)

function Start-Duel([string]$name, [int]$x) {
    $log = Join-Path $logs "$name.log"
    $argv = @('-screen-fullscreen', '0', '-screen-width', "$width", '-screen-height', "$height", '-logFile', "`"$log`"")
    if ($Lighting) { $argv += @('-lighting', $Lighting) }
    $p = Start-Process $exe -PassThru -ArgumentList $argv
    for ($i = 0; $i -lt 100 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    if ($p.MainWindowHandle -ne 0) { [DuelWindow]::MoveWindow($p.MainWindowHandle, $x, $area.Top, $width, $height, $true) | Out-Null }
    Write-Host "$name window started (log: $log)"
}

Start-Duel 'left' $area.Left
Start-Duel 'right' ($area.Left + $width)
