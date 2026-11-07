# One-time setup for the webcam sidecar: powershell -ExecutionPolicy Bypass -File blink_server/setup.ps1
$ErrorActionPreference = "Stop"
$venv = Join-Path $PSScriptRoot ".venv"
if (-not (Test-Path "$venv\Scripts\python.exe")) {
    py -3.13 -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw "Python 3.13 not found: install it from python.org (py -3.13)" }
}
& "$venv\Scripts\python.exe" -m pip install -r (Join-Path $PSScriptRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed" }
