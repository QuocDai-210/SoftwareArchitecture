param(
    [ValidateSet('setup', 'build', 'build-demo', 'up', 'demo', 'stop', 'status', 'logs', 'trip', 'matching', 'payment')]
    [string]$Command = 'status'
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Python {
    param([string]$Script)
    foreach ($candidate in @('py', 'python', 'python3')) {
        $executable = Get-Command $candidate -CommandType Application -ErrorAction SilentlyContinue
        if (-not $executable) {
            continue
        }
        $prefix = @()
        if ($candidate -eq 'py') {
            $prefix = @('-3')
        }
        try {
            $version = & $executable.Source @prefix -c 'import sys; print(sys.version_info.major)' 2>$null
            $available = $LASTEXITCODE -eq 0 -and $version -eq '3'
        } catch {
            $available = $false
        }
        if ($available) {
            Invoke-Checked $executable.Source ($prefix + @('-X', 'utf8', '-B', $Script))
            return
        }
    }
    throw ('Python 3 is not available. Install it with: ' +
        'winget install --exact --id Python.Python.3.12 --scope user. ' +
        'Then close and reopen your terminal (restart VS Code if using its terminal).')
}

Push-Location (Split-Path $PSScriptRoot -Parent)
$previousMaps = $env:MAPS_PROVIDER
$previousOtel = $env:OTEL_SDK_DISABLED
try {
    switch ($Command) {
        'setup' {
            Invoke-Python 'scripts/setup.py'
        }
        { $_ -in @('build', 'build-demo') } {
            Invoke-Python 'scripts/check-architecture.py'
            Invoke-Python 'scripts/check-resources.py'
            Invoke-Checked 'docker' @('compose', '--profile', '*', 'stop')
            $services = @('rabbitmq', 'trip', 'matching', 'payment', 'gateway')
            if ($Command -eq 'build-demo') {
                $services += 'web'
            }
            foreach ($service in $services) {
                Invoke-Checked 'docker' @('compose', 'build', $service)
            }
        }
        'stop' {
            Invoke-Checked 'docker' @('compose', '--profile', '*', 'stop')
        }
        'status' {
            Invoke-Checked 'docker' @('compose', '--profile', '*', 'ps', '--all')
        }
        'logs' {
            Invoke-Checked 'docker' @('compose', '--profile', '*', 'logs', '--tail=100', '--follow')
        }
        default {
            Invoke-Python 'scripts/check-resources.py'
            $target = $Command
            $env:OTEL_SDK_DISABLED = 'true'
            if ($Command -eq 'up') {
                Invoke-Checked 'docker' @('compose', '--profile', '*', 'stop', 'web', 'observability')
                $env:MAPS_PROVIDER = 'Test'
                $target = 'gateway'
            } elseif ($Command -eq 'demo') {
                Invoke-Checked 'docker' @('compose', 'stop', 'observability')
                $target = 'web'
            } else {
                Invoke-Checked 'docker' @('compose', '--profile', '*', 'stop')
            }
            $composeArgs = @(
                'compose', 'up', '--no-build', '-d', '--wait', '--wait-timeout', '300', $target
            )
            Invoke-Checked 'docker' $composeArgs
        }
    }
} finally {
    $env:MAPS_PROVIDER = $previousMaps
    $env:OTEL_SDK_DISABLED = $previousOtel
    Pop-Location
}
