#requires -Version 7.0
param(
    [Parameter(Mandatory)]
    [string] $Executable
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'This smoke test isolates Windows user data through APPDATA.'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$artifacts = Join-Path ([IO.Path]::GetTempPath()) "tempora-startup-smoke-$([Guid]::NewGuid().ToString('N'))"
[void][IO.Directory]::CreateDirectory($artifacts)
Write-Host "Startup screenshots and logs: $artifacts"

function New-StartupProfile([string] $scenario) {
    $directory = Join-Path $artifacts $scenario
    $userData = Join-Path $directory 'profile/Godot/app_userdata/Tempora'
    [void][IO.Directory]::CreateDirectory($userData)
    return $directory
}

function Restore-ExampleProject([string] $directory) {
    $userData = Join-Path $directory 'profile/Godot/app_userdata/Tempora'
    $audio = Join-Path $directory 'Loop.mp3'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Audio/Loop.mp3') -Destination $audio
    [IO.File]::WriteAllText((Join-Path $userData 'settings.cfg'), "[settings]`nNumberOfRows=3`n")
    [IO.File]::WriteAllText((Join-Path $userData 'autosave.tmpr'), "[AudioPath]`n$audio`n[TimeSignaturePoints]`n[TimingPoints]`n0;0;4;4;0.5`n")
}

function Invoke-Export([string] $directory) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = 'Hidden'
    $start.Environment['APPDATA'] = Join-Path $directory 'profile'
    foreach ($argument in @('--write-movie', (Join-Path $directory 'frame.png'), '--fixed-fps', '2',
            '--quit-after', '8', '--resolution', '1280x720', '--log-file', (Join-Path $directory 'run.log'))) {
        $start.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(60000)) {
            $process.Kill($true)
            $process.WaitForExit()
            Write-Host 'Startup exceeded 60 seconds.'
            return -1
        }
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

function Test-Startup([string] $directory) {
    $scenario = Split-Path -Leaf $directory
    $exitCode = Invoke-Export $directory
    $logPath = Join-Path $directory 'run.log'
    $frames = @(Get-ChildItem -LiteralPath $directory -Filter 'frame*.png')
    $log = if (Test-Path -LiteralPath $logPath) { [IO.File]::ReadAllText($logPath) } else { '' }
    $errors = @($log -split '\r?\n' | Where-Object {
        $_ -match '^(ERROR:|SCRIPT ERROR:|Unhandled exception)' -or
        $_ -match 'WARNING:.*(leaked|still in use|orphan)'
    })
    $passed = $exitCode -eq 0 -and $frames.Count -eq 8 -and
        @($frames | Where-Object Length -eq 0).Count -eq 0 -and $log.Length -gt 0 -and $errors.Count -eq 0
    if (-not $passed) {
        Write-Host "FAIL ${scenario}: exit=$exitCode, frames=$($frames.Count), errors=$($errors.Count)."
        $errors | ForEach-Object { Write-Host $_ }
    }
    else {
        Write-Host "PASS ${scenario}: startup and capture completed without runtime errors."
    }
    return $passed
}

$fresh = New-StartupProfile 'fresh'
$freshPassed = Test-Startup $fresh
$restored = New-StartupProfile 'restored'
Restore-ExampleProject $restored
$restoredPassed = Test-Startup $restored
Write-Host 'Inspect frame00000000.png and frame00000007.png in each scenario for visual correctness; images are not compared automatically.'
if (-not ($freshPassed -and $restoredPassed)) {
    exit 1
}
