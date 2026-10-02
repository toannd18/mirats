# Mirats / AspireReact -- pre-commit gates (DUNG BANG CI GATES).
# Quy tac: docs/DEVELOPMENT_WORKFLOW.md §1.9 -- commit nao fail gate thi KHONG duoc commit.
# Chay tu repo root:  pwsh -File scripts/precommit-gates.ps1
# ASCII-only: executor mac dinh la Windows PowerShell 5.1 (file non-ASCII khong BOM se vo parser).
$ErrorActionPreference = 'Continue'

$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$be = Join-Path $repo 'aspire-react'
$fe = Join-Path $be 'frontend'
$results = @()

function Report($name, $code) {
  if ($code -eq 0) {
    Write-Host ("  OK   {0}" -f $name) -ForegroundColor Green
    $script:results += [pscustomobject]@{ Gate = $name; Exit = 0; Ok = $true }
  } else {
    Write-Host ("  FAIL {0} (exit {1})" -f $name, $code) -ForegroundColor Red
    $script:results += [pscustomobject]@{ Gate = $name; Exit = $code; Ok = $false }
  }
}

Write-Host "=== PRE-COMMIT GATES (== CI) ===" -ForegroundColor Cyan

Write-Host "1/6 dotnet format --verify-no-changes"
Push-Location $be
dotnet format aspire-react.sln --verify-no-changes --no-restore *> $null
Report "dotnet format" $LASTEXITCODE
Pop-Location

Write-Host "2/6 dotnet build -c Release"
Push-Location $be
dotnet build aspire-react.sln --configuration Release --no-restore *> $null
Report "dotnet build Release" $LASTEXITCODE
Pop-Location

Write-Host "3/6 dotnet test (Category!=Concurrency)"
Push-Location $be
dotnet test aspire-react.sln --configuration Release --no-build --filter "Category!=Concurrency" *> $null
Report "dotnet test" $LASTEXITCODE
Pop-Location

Write-Host "4/6 npm run lint"
Push-Location $fe
npm run lint *> $null
Report "npm run lint" $LASTEXITCODE
Pop-Location

Write-Host "5/6 npm run build"
Push-Location $fe
npm run build *> $null
Report "npm run build" $LASTEXITCODE
Pop-Location

Write-Host "6/6 audit-sweeps.ps1"
Push-Location $repo
& (Join-Path $repo 'scripts/audit-sweeps.ps1') *> $null
Report "audit-sweeps" $LASTEXITCODE
Pop-Location

Write-Host ""
$failed = @($results | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
  Write-Host ("RESULT: FAIL -- {0}/6 gate do: {1}" -f $failed.Count, (($failed | ForEach-Object { $_.Gate }) -join ', ')) -ForegroundColor Red
  Write-Host "KHONG DUOC COMMIT khi con gate do." -ForegroundColor Red
  exit 1
}
Write-Host "RESULT: PASS -- 6/6 gate xanh, duoc phep commit." -ForegroundColor Green
exit 0
