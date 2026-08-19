<#
.SYNOPSIS
  Reset the Mirats Docker Compose production stack data (remove containers + volumes).

.DESCRIPTION
  - Runs `docker compose down -v` from the repo root.
  - Removes ONLY the Docker Compose production volumes, i.e. those named `mirats-*`
    (mirats-postgres-data, mirats-redis-data, mirats-keycloak-data).
  - NEVER touches the Aspire dev volumes (`postgres-data`, `keycloak-data` - no
    `mirats-` prefix). Those belong to the dev stack and are left untouched.
  - Asks for confirmation first - data deletion is IRREVERSIBLE.
  - PS 5.1 compatible: no `??`, no null-conditional, plain ASCII (no BOM).

.EXAMPLE
  powershell -File scripts/docker-reset.ps1
#>
# Native command stderr must NOT terminate the script in PS 5.1 when redirected.
# We rely on $LASTEXITCODE checks instead of $ErrorActionPreference="Stop".
$ErrorActionPreference = "Continue"

# Resolve repo root = parent of the scripts/ directory.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
Set-Location $repoRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  Write-Host "ERROR: docker not found on PATH." -ForegroundColor Red
  exit 1
}

Write-Host "== Mirats Docker Compose stack reset ==" -ForegroundColor Cyan
Write-Host "Repo root: $repoRoot"
Write-Host ""

$composePresent = Test-Path (Join-Path $repoRoot "docker-compose.yml")
if ($composePresent) {
  Write-Host "Compose file: present (docker-compose.yml)"
} else {
  Write-Host "Compose file: MISSING (docker-compose.yml) - volumes will still be cleaned"
}
Write-Host ""

# Collect the production volumes we are allowed to remove (mirats-* prefix).
$before = @(docker volume ls -q --filter "name=^mirats-" 2>$null)
if ($before.Count -eq 0) {
  Write-Host "No existing 'mirats-*' volumes found right now."
  Write-Host "  -> docker compose down -v will still be run (removes any compose-managed volumes)."
} else {
  Write-Host "The following volumes will be PERMANENTLY DELETED:"
  foreach ($v in $before) { Write-Host "  - $v" }
}

# Informational guard: show the Aspire dev volumes we will NOT touch.
$devVolumes = @()
$devVolumes += @(docker volume ls -q --filter "name=^postgres-data$" 2>$null)
$devVolumes += @(docker volume ls -q --filter "name=^keycloak-data$" 2>$null)
$devVolumes = @($devVolumes | Where-Object { $_ })
if ($devVolumes.Count -gt 0) {
  Write-Host "Aspire dev volumes (NOT touched): $($devVolumes -join ', ')"
} else {
  Write-Host "Aspire dev volumes (NOT touched): none"
}
Write-Host ""

# Confirmation - never delete silently.
$answer = Read-Host "WARNING: This deletes ALL data of the Docker Compose production stack. This CANNOT be undone. Type 'yes' to continue"
if ($answer -ne "yes") {
  Write-Host "Aborted. No volumes were removed."
  exit 1
}

Write-Host ""
Write-Host "Running: docker compose down -v ..."
docker compose down -v 2>&1 | Out-Host
$downOk = ($LASTEXITCODE -eq 0)

if (-not $downOk) {
  Write-Host "WARN: 'docker compose down -v' failed (usually missing/empty required vars in .env)." -ForegroundColor Yellow
  Write-Host "  Falling back to manual cleanup of the compose project's containers." -ForegroundColor Yellow

  # Fallback: remove the compose project's containers directly via its label.
  $project = (Get-Item $repoRoot).Name.ToLowerInvariant() -replace '[^a-z0-9_.-]', ''
  $containers = @(docker ps -aq --filter "label=com.docker.compose.project=$project" 2>$null)
  if ($containers.Count -gt 0) {
    Write-Host "Removing compose containers for project '$project':"
    foreach ($c in $containers) { Write-Host "  - $c" }
    docker rm -f $containers 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
      Write-Host "ERROR: Failed to remove containers (exit $LASTEXITCODE)." -ForegroundColor Red
      exit 1
    }
  } else {
    Write-Host "No running containers found for project '$project'."
  }
}

# Safety net: remove any leftover mirats-* volumes not removed by compose.
$left = @(docker volume ls -q --filter "name=^mirats-" 2>$null)
if ($left.Count -gt 0) {
  Write-Host "Removing leftover mirats-* volumes:"
  foreach ($v in $left) { Write-Host "  - $v" }
  docker volume rm $left 2>&1 | Out-Host
  if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to remove leftover volumes (exit $LASTEXITCODE)." -ForegroundColor Red
    exit 1
  }
} else {
  Write-Host "No leftover mirats-* volumes."
}

Write-Host ""
Write-Host "=== DONE. Production stack data has been reset. ===" -ForegroundColor Green
Write-Host ""
Write-Host "To rebuild from scratch:"
Write-Host "  1) cp .env.example .env   (fill in all REQUIRED vars)"
Write-Host "  2) docker compose up -d --build"
Write-Host "  3) scripts/seed-initial-admin.ps1   (or: bash scripts/seed-initial-admin.sh on Linux/Mac)"
Write-Host ""
Write-Host "Aspire dev volumes (postgres-data, keycloak-data) are untouched."
