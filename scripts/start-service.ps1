param(
    [string]$ServiceName = "lhm-exporter",
    [string]$DistDir = "dist",
    [string]$ExeName = "lhm-exporter.exe"
)

# Ensure the script is running as Administrator; if not, relaunch elevated
$currId = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currId)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[start-service] Restarting with administrator privileges for service '$ServiceName'" -ForegroundColor Yellow
    $argList = @(
        '-NoProfile'
        '-ExecutionPolicy','Bypass'
        '-File',"`"$PSCommandPath`""
        '-ServiceName',"`"$ServiceName`""
        '-DistDir',"`"$DistDir`""
        '-ExeName',"`"$ExeName`""
    )

    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs
    exit 0
}

Write-Host "[start-service] Trying to start service or binary '$ServiceName'." -ForegroundColor Cyan

# Try to start service if it exists
$svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Running') {
        Write-Host "[start-service] Service '$ServiceName' found with status '$($svc.Status)'. Starting..." -ForegroundColor Cyan
        Start-Service $svc -ErrorAction SilentlyContinue
        try {
            $svc.WaitForStatus('Running','00:00:15')
            Write-Host "[start-service] Service '$ServiceName' is running." -ForegroundColor Green
        } catch {
            Write-Host "[start-service] Failed to wait for service '$ServiceName' to start (it may still have started)." -ForegroundColor Yellow
        }
    } else {
        Write-Host "[start-service] Service '$ServiceName' is already running." -ForegroundColor DarkGray
    }
    exit 0
}

Write-Host "[start-service] Service '$ServiceName' not found. Trying to start binary directly." -ForegroundColor DarkGray

# If there is no service, start the exe directly (e.g. dev scenario)
$exePath = Join-Path $DistDir $ExeName
Write-Host "[start-service] Expected binary path: '$exePath'" -ForegroundColor Cyan
if (Test-Path $exePath) {
    Write-Host "[start-service] Starting '$exePath'." -ForegroundColor Green
    Start-Process -FilePath $exePath -WorkingDirectory $DistDir
} else {
    Write-Host "[start-service] Binary '$exePath' not found - nothing to start." -ForegroundColor Red
}

