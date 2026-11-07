# One-time setup for the webcam sidecar: powershell -File blink_server/setup.ps1
$ErrorActionPreference = "Stop"
$venv = Join-Path $PSScriptRoot ".venv"
if (-not (Test-Path "$venv\Scripts\python.exe")) { py -3.13 -m venv $venv }
& "$venv\Scripts\python.exe" -m pip install -r (Join-Path $PSScriptRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed" }
