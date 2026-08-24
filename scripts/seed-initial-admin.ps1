<#
.SYNOPSIS
  Seed the initial Mirats app admin (INITIAL_ADMIN_*) via Keycloak Admin API (Phuong an A).
  JIT-provisioning se tao local DB User voi IsSuperUser=true khi user dang nhap lan dau.

.DESCRIPTION
  - Auto-loads .env from repo root (if present) into the process environment, so the
    script can be run right after `cp .env.example .env` without manual `export`.
    Process env vars always win over .env values.
  - Reads INITIAL_ADMIN_USERNAME / EMAIL / PASSWORD and KC_BOOTSTRAP_ADMIN_USERNAME /
    PASSWORD (master realm) to obtain an admin-cli token. Fails fast if any missing.
  - Idempotent: if the user already exists in realm aspire-react, it is left as-is.
  - Uses curl.exe (bundled with Windows 10 1803+, Docker Desktop, and Git for Windows)
    for JSON API calls: PS 5.1 Invoke-RestMethod cannot send a JSON array body to
    Keycloak's role-mappings endpoint correctly (400 "Cannot parse the JSON"), while
    `curl.exe --data-binary @file` works reliably (204). Token call uses Invoke-RestMethod.
  - Compatible with Windows PowerShell 5.1 and PowerShell 7+.

.PARAMETER KeycloakUrl
  Keycloak base URL. Defaults to KEYCLOAK_PUBLIC_URL / KEYCLOAK_URL / http://localhost:8080.
  For Aspire dev (HTTPS self-signed) pass -KeycloakUrl https://localhost:<dynamic-port>
  (e.g. https://localhost:63096 — see `aspire ps` or container port mapping).

.PARAMETER SkipCertCheck
  Bypass TLS certificate validation for Invoke-RestMethod and curl.exe.
  Auto-enabled when -KeycloakUrl is https://localhost or https://127.0.0.1 (dev self-signed).
  Docker HTTP (http://keycloak:8080 or http://localhost:8080) is NOT affected.

.EXAMPLE
  powershell -File scripts/seed-initial-admin.ps1
.EXAMPLE
  # Aspire dev (HTTPS self-signed, dynamic port from `docker ps`):
  powershell -File scripts/seed-initial-admin.ps1 -KeycloakUrl https://localhost:63096
  # Explicit bypass for any host:
  powershell -File scripts/seed-initial-admin.ps1 -KeycloakUrl https://myhost:8443 -SkipCertCheck
#>
[CmdletBinding()]
param(
  [string]$KeycloakUrl = "",
  [string]$Realm = "",
  [string]$MasterRealm = "master",
  [switch]$SkipCertCheck
)

$ErrorActionPreference = "Stop"

# Auto-load .env from repo root into the process environment (fresh-user friendly).
$envFile = Join-Path (Split-Path -Parent $PSScriptRoot) ".env"
if (Test-Path $envFile) {
  Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -match '^[A-Za-z_][A-Za-z0-9_]*=') {
      $name = $line.Substring(0, $line.IndexOf('=')).Trim()
      if (-not [Environment]::GetEnvironmentVariable($name)) {
        $value = $line.Substring($line.IndexOf('=') + 1)
        $value = $value.Trim().Trim('"').Trim("'")
        [Environment]::SetEnvironmentVariable($name, $value)
      }
    }
  }
}

if (-not $KeycloakUrl) {
  # Priority: explicit .env public URL > generic KEYCLOAK_URL > legacy SERVER_URL.
  # For Aspire dev (HTTPS self-signed) the caller should pass -KeycloakUrl https://localhost:<port>.
  if ($env:KEYCLOAK_PUBLIC_URL) { $KeycloakUrl = $env:KEYCLOAK_PUBLIC_URL }
  elseif ($env:KEYCLOAK_URL) { $KeycloakUrl = $env:KEYCLOAK_URL }
  elseif ($env:KEYCLOAK_SERVER_URL) { $KeycloakUrl = $env:KEYCLOAK_SERVER_URL }
  else { $KeycloakUrl = "http://localhost:8080" }
}
if (-not $Realm) {
  if ($env:KEYCLOAK_REALM) { $Realm = $env:KEYCLOAK_REALM }
  else { $Realm = "aspire-react" }
}

# --- HTTPS self-signed cert handling (Aspire dev) ---
# PowerShell 5.1: Invoke-RestMethod has no -SkipCertificateCheck; must use ServicePointManager callback.
# PowerShell 7+: Invoke-RestMethod supports -SkipCertificateCheck natively, and curl needs -k.
# Policy:
#   - Default: do NOT bypass (Docker path is HTTP, unaffected).
#   - Bypass ONLY when (a) user passed -SkipCertCheck, OR (b) KeycloakUrl is https + localhost/127.0.0.1
#     (Aspire dev self-signed — safe to auto-enable per task requirement 3).
$skipCert = $false
if ($SkipCertCheck) { $skipCert = $true }
elseif ($KeycloakUrl -match '^https://(localhost|127\.0\.0\.1)([:/]|$)') { $skipCert = $true }

# PS version probe for Invoke-RestMethod -SkipCertificateCheck support (PS 6+ / 7+).
$supportsSkipCertParam = $false
try {
  $supportsSkipCertParam = (Get-Command Invoke-RestMethod).Parameters.ContainsKey('SkipCertificateCheck')
} catch {}

# PS 5.1 fallback: bypass via ServicePointManager for the entire process (Invoke-RestMethod + .NET HttpClient).
if ($skipCert -and -not $supportsSkipCertParam) {
  try { [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true } } catch {}
  try { [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 } catch {}
}

# Common splat for all Invoke-RestMethod calls: only add -SkipCertificateCheck when actually supported.
# Every Invoke-RestMethod site below MUST use @irmSplat instead of bare params so PS 5.1 does not
# error on unknown -SkipCertificateCheck.
$irmSplat = @{}
if ($skipCert -and $supportsSkipCertParam) { $irmSplat['SkipCertificateCheck'] = $true }

# On PS 5.1 (ConstrainedLanguage / no Runspace in callback thread) the ServicePointManager
# callback trick fails with "There is no Runspace available". In that case Invoke-RestMethod
# will never succeed against a self-signed HTTPS endpoint. The reliable fallback is to
# drive ALL HTTPS API calls through curl.exe (Git's OpenSSL curl handles -k correctly).
# We detect this at runtime: if PS 5.1 + https self-signed, use curl for token/search/roles.
$useCurlForApi = ($skipCert -and -not $supportsSkipCertParam)

function Fail([string]$msg) { Write-Error $msg; exit 1 }

# Locate curl.exe. Priority matters for HTTPS self-signed (Aspire dev):
#   Git's curl (OpenSSL) handles self-signed with -k reliably, while Windows
#   System32 curl (Schannel) may fail with SEC_E_NO_CREDENTIALS even with -k
#   unless --ssl-no-revoke is added. We probe both, but prefer the one that
#   actually works for the current $skipCert mode.
function Get-CurlExe {
  $candidates = @(
    "$env:ProgramFiles\Git\mingw64\bin\curl.exe",
    "$env:SystemRoot\System32\curl.exe",
    "$env:ProgramFiles\Docker\Docker\resources\bin\curl.exe"
  )
  # When HTTPS self-signed, prefer Git curl (OpenSSL) if present — more reliable with -k.
  if ($skipCert) {
    foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
  }
  $c = Get-Command curl.exe -ErrorAction SilentlyContinue
  if ($c) { return $c.Source }
  foreach ($p in $candidates) { if (Test-Path $p) { return $p } }
  return $null
}
$curl = Get-CurlExe
if (-not $curl) {
  Fail "curl.exe not found. Install Git for Windows (ships curl) or use the .sh variant with WSL."
}

# curl wrapper: POST/PUT/DELETE with JSON body from a temp file.
# Uses `curl -f` so curl returns exit 0 ONLY on HTTP 2xx (success) and non-zero
# (e.g. 22) on HTTP 4xx/5xx. Body suppressed via -o NUL.
# When $skipCert is true, adds -k (insecure) + --ssl-no-revoke so curl bypasses
# self-signed Aspire dev cert (Schannel on Windows needs --ssl-no-revoke to avoid
# SEC_E_NO_CREDENTIALS / revocation check failures).
# Returns: 0 = success, non-zero = curl/HTTP error.
function Invoke-CurlJson([string]$Method, [string]$Uri, [string]$BodyFile, [string]$AuthToken) {
  $args = @("-sS", "-f", "-X", $Method, "-H", "Authorization: Bearer $AuthToken", "-H", "Content-Type: application/json", "-o", "NUL")
  if ($skipCert) { $args += "-k"; $args += "--ssl-no-revoke" }
  if ($BodyFile) { $args += "--data-binary"; $args += "@$BodyFile" }
  $args += $Uri
  & $curl @args 2>&1 | Out-Null
  return $LASTEXITCODE
}

$curlAvailable = $true

# 1. Required env
$username = $env:INITIAL_ADMIN_USERNAME
$email    = $env:INITIAL_ADMIN_EMAIL
$password = $env:INITIAL_ADMIN_PASSWORD
$kcAdminUser = $env:KC_BOOTSTRAP_ADMIN_USERNAME
$kcAdminPass = $env:KC_BOOTSTRAP_ADMIN_PASSWORD

if (-not $username) { Fail "INITIAL_ADMIN_USERNAME is required - set it in .env (no default, no Admin123! fallback)." }
if (-not $email)    { Fail "INITIAL_ADMIN_EMAIL is required - set it in .env." }
if (-not $password) { Fail "INITIAL_ADMIN_PASSWORD is required - set it in .env (no default, no Admin123! fallback)." }
if (-not $kcAdminUser) { Fail "KC_BOOTSTRAP_ADMIN_USERNAME is required - set it in .env (Keycloak master admin)." }
if (-not $kcAdminPass) { Fail "KC_BOOTSTRAP_ADMIN_PASSWORD is required - set it in .env." }

$KeycloakUrl = $KeycloakUrl.TrimEnd('/')
$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mirats-seed-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpDir | Out-Null

Write-Host "Seeding initial admin '$username' ($email) into realm '$Realm' via $KeycloakUrl ..." -ForegroundColor Cyan

# 2. Obtain master admin token (with retry)
$tokenUrl = "$KeycloakUrl/realms/$MasterRealm/protocol/openid-connect/token"
$maxAttempts = 12
$token = $null

function Invoke-TokenRequest([string]$Url, [string]$FormBody) {
  if ($useCurlForApi) {
    # PS 5.1 + self-signed HTTPS: use Git's curl (OpenSSL) with -k — Windows schannel curl + PS
    # ServicePointManager both fail with SEC_E_NO_CREDENTIALS under DSH's constrained env.
    # Git curl is OpenSSL-based and handles -k correctly for Aspire's dev cert.
    $gitCurl = "$env:ProgramFiles\Git\mingw64\bin\curl.exe"
    $useCurl = if (Test-Path $gitCurl) { $gitCurl } else { $curl }
    $tmpBody = Join-Path ([System.IO.Path]::GetTempPath()) ("mirats-token-" + [Guid]::NewGuid().ToString("N") + ".json")
    $curlArgs = @("-sS", "-f", "-X", "POST", "-H", "Content-Type: application/x-www-form-urlencoded", "--data", $FormBody, "-o", $tmpBody, $Url)
    if ($skipCert) { $curlArgs = @("-k") + $curlArgs }
    & $useCurl @curlArgs 2>&1 | Out-Null
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
      $err = Get-Content -Raw -Path $tmpBody -ErrorAction SilentlyContinue
      Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
      throw "curl token request failed (exit $exit) $err"
    }
    $json = Get-Content -Raw -Path $tmpBody | ConvertFrom-Json
    Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
    return $json
  } else {
    return Invoke-RestMethod -Uri $Url -Method Post -ContentType "application/x-www-form-urlencoded" -Body $FormBody -TimeoutSec 10 @irmSplat
  }
}

function Invoke-ApiGet([string]$Url, [string]$Token) {
  if ($useCurlForApi) {
    $gitCurl = "$env:ProgramFiles\Git\mingw64\bin\curl.exe"
    $useCurl = if (Test-Path $gitCurl) { $gitCurl } else { $curl }
    $tmpBody = Join-Path ([System.IO.Path]::GetTempPath()) ("mirats-get-" + [Guid]::NewGuid().ToString("N") + ".json")
    $curlArgs = @("-sS", "-f", "-H", "Authorization: Bearer $Token", "-o", $tmpBody, $Url)
    if ($skipCert) { $curlArgs = @("-k") + $curlArgs }
    & $useCurl @curlArgs 2>&1 | Out-Null
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
      $err = Get-Content -Raw -Path $tmpBody -ErrorAction SilentlyContinue
      Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
      throw "curl GET $Url failed (exit $exit) $err"
    }
    $text = Get-Content -Raw -Path $tmpBody -ErrorAction SilentlyContinue
    Remove-Item $tmpBody -Force -ErrorAction SilentlyContinue
    if (-not $text -or $text.Trim() -eq "") { return @() }
    return $text | ConvertFrom-Json
  } else {
    return Invoke-RestMethod -Uri $Url -Method Get -Headers @{ Authorization = "Bearer $Token" } -TimeoutSec 10 @irmSplat
  }
}

for ($i = 1; $i -le $maxAttempts; $i++) {
  try {
    $encUser = [Uri]::EscapeDataString($kcAdminUser)
    $encPass = [Uri]::EscapeDataString($kcAdminPass)
    $body = "grant_type=password&client_id=admin-cli&username=$encUser&password=$encPass"
    $resp = Invoke-TokenRequest $tokenUrl $body
    $token = $resp.access_token
    break
  } catch {
    $status = 0
    try { $status = $_.Exception.Response.StatusCode.value__ } catch {}
    Write-Host "  Attempt $i/$maxAttempts : token request failed ($status) - $($_.Exception.Message)" -ForegroundColor Yellow
    if ($i -eq $maxAttempts) { Fail "Failed to obtain Keycloak admin token after $maxAttempts attempts. Is Keycloak ready at $KeycloakUrl? Check KC_BOOTSTRAP_* credentials." }
    Start-Sleep -Seconds 5
  }
}
Write-Host "  Admin token obtained." -ForegroundColor Green

# 2b. Sync the confidential client 'backend-service' secret with KEYCLOAK_BACKEND_CLIENT_SECRET.
# Keycloak realm import does NOT substitute env placeholders inside the realm JSON - a fresh
# import leaves the client secret as the literal string "${KEYCLOAK_BACKEND_CLIENT_SECRET}".
# The backend authenticates to Keycloak with the REAL secret from .env (client_credentials
# grant in KeycloakService.GetAdminTokenAsync) - without this sync every app-side user
# operation (create/update/disable) fails with 401 invalid_client. Placed BEFORE the
# idempotent existing-user exit below so re-running the seed always re-syncs.
$backendSecret = $env:KEYCLOAK_BACKEND_CLIENT_SECRET
if (-not $backendSecret) { Fail "KEYCLOAK_BACKEND_CLIENT_SECRET is required - set it in .env." }
$clientsSearch = @()
try { $clientsSearch = @(Invoke-ApiGet "$KeycloakUrl/admin/realms/$Realm/clients?clientId=backend-service&first=0&max=1" $token | ForEach-Object { $_ }) } catch {}
$backendClient = $null
foreach ($item in $clientsSearch) {
  if ($item -and $item.id) { $backendClient = $item; break }
  if ($item -is [System.Array] -and $item.Count -gt 0 -and $item[0].id) { $backendClient = $item[0]; break }
}
if (-not $backendClient) {
  Write-Host "  Warning: client 'backend-service' not found in realm '$Realm' - cannot sync its secret." -ForegroundColor Yellow
} else {
  # Full client representation (GET by id includes the secret field; search results do not).
  $clientRep = Invoke-ApiGet "$KeycloakUrl/admin/realms/$Realm/clients/$($backendClient.id)" $token
  $currentSecret = $clientRep.secret
  if ($currentSecret -eq $backendSecret) {
    Write-Host "  Client 'backend-service' secret already matches .env - no change." -ForegroundColor Green
  } else {
    if ($null -eq $currentSecret -or -not $clientRep.PSObject.Properties['secret']) {
      $clientRep | Add-Member -NotePropertyName secret -NotePropertyValue $backendSecret -Force
    } else {
      $clientRep.secret = $backendSecret
    }
    $secretBodyFile = Join-Path $tmpDir "client-secret.json"
    [System.IO.File]::WriteAllText($secretBodyFile, ($clientRep | ConvertTo-Json -Depth 12 -Compress), [System.Text.UTF8Encoding]::new($false))
    $syncCode = Invoke-CurlJson "PUT" "$KeycloakUrl/admin/realms/$Realm/clients/$($backendClient.id)" $secretBodyFile $token
    if ($syncCode -eq 0) {
      Write-Host "  Client 'backend-service' secret synced from KEYCLOAK_BACKEND_CLIENT_SECRET (import left a literal placeholder/stale value)." -ForegroundColor Green
    } else {
      Fail "Failed to sync 'backend-service' client secret (curl exit $syncCode). App-side user management would fail until fixed."
    }
  }
}

# 3. Check if user already exists
$encUsername = [Uri]::EscapeDataString($username)
$searchUrl = "$KeycloakUrl/admin/realms/$Realm/users?username=$encUsername&exact=true"
$existing = @()
try {
  $existing = @(Invoke-ApiGet $searchUrl $token)
} catch {
  Fail "Failed to search users in realm '$Realm': $($_.Exception.Message)"
}

# PS 5.1 turns an empty JSON array `[]` into `@(@())` (nested empty array) when wrapped
# with @() — count is 1 but the item has no `.id`. A real user always has `.id`.
# So the "already exists" check must test for an actual user object, not just count.
$existingUser = $null
foreach ($item in $existing) {
  if ($item -and $item.id) { $existingUser = $item; break }
  if ($item -is [System.Array] -and $item.Count -gt 0 -and $item[0].id) { $existingUser = $item[0]; break }
}

if ($existingUser) {
  Write-Host "  User '$username' already exists in realm '$Realm' - skipping creation (idempotent)." -ForegroundColor Yellow
  Write-Host "  Done. The user can log in; JIT will ensure the local DB record on first login." -ForegroundColor Green
  exit 0
}

# 4. Create user (curl.exe with --data-binary @file - reliable in PS 5.1)
$createBodyFile = Join-Path $tmpDir "create.json"
[System.IO.File]::WriteAllText($createBodyFile, (@{
  username = $username; email = $email; enabled = $true; emailVerified = $true;
  firstName = "System"; lastName = "Admin"
} | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))

$code = Invoke-CurlJson "POST" "$KeycloakUrl/admin/realms/$Realm/users" $createBodyFile $token
if ($code -eq 0) {
  Start-Sleep -Milliseconds 500
} else {
  Fail "Failed to create user '$username' in realm '$Realm' (curl exit $code). Is the realm imported and KC_BOOTSTRAP_* correct?"
}

# Re-search for the user id (Keycloak returns Location header; we don't parse it).
$existing = @()
try { $existing = @(Invoke-ApiGet $searchUrl $token) } catch {}
$existingUser = $null
foreach ($item in $existing) {
  if ($item -and $item.id) { $existingUser = $item; break }
  if ($item -is [System.Array] -and $item.Count -gt 0 -and $item[0].id) { $existingUser = $item[0]; break }
}
if (-not $existingUser) {
  Fail "User '$username' was not found after creation - cannot continue."
}
$userId = $existingUser.id
Write-Host "  User '$username' created (id: $userId)." -ForegroundColor Green

# 5. Reset password
$pwdBodyFile = Join-Path $tmpDir "pwd.json"
[System.IO.File]::WriteAllText($pwdBodyFile, (@{ type = "password"; value = $password; temporary = $false } | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
$code = Invoke-CurlJson "PUT" "$KeycloakUrl/admin/realms/$Realm/users/$userId/reset-password" $pwdBodyFile $token
if ($code -eq 0) {
  Write-Host "  Password set for '$username'." -ForegroundColor Green
} else {
  Fail "Failed to set password for '$username' (curl exit $code)."
}

# 6. Assign realm role 'admin' (so JIT sets IsSuperUser=true)
# NOTE: `@(Invoke-RestMethod ...)` on a JSON array double-wraps into @(Object[]),
# so `$_.name -eq "admin"` would match the whole array. Flatten with ForEach-Object.
$roles = @(Invoke-ApiGet "$KeycloakUrl/admin/realms/$Realm/roles" $token | ForEach-Object { $_ })
$adminRole = $roles | Where-Object { $_ -and $_.name -eq "admin" } | Select-Object -First 1
if (-not $adminRole) {
  Write-Host "  Warning: realm role 'admin' not found in '$Realm' - skipping role assignment." -ForegroundColor Yellow
  Write-Host "  The user was still created - assign the role manually in Keycloak Admin Console." -ForegroundColor Yellow
} else {
  $roleBodyFile = Join-Path $tmpDir "role.json"
  [System.IO.File]::WriteAllText($roleBodyFile, ('[{"id":"' + $adminRole.id + '","name":"' + $adminRole.name + '"}]'), [System.Text.UTF8Encoding]::new($false))
  $code = Invoke-CurlJson "POST" "$KeycloakUrl/admin/realms/$Realm/users/$userId/role-mappings/realm" $roleBodyFile $token
  if ($code -eq 0) {
    Write-Host "  Realm role 'admin' assigned to '$username' (IsSuperUser on first login)." -ForegroundColor Green
  } else {
    Write-Host "  Warning: failed to assign realm role 'admin' to '$username' (curl exit $code)." -ForegroundColor Yellow
    Write-Host "  The user was still created - assign the role manually in Keycloak Admin Console." -ForegroundColor Yellow
  }
}

# Cleanup temp files
Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Done. User '$username' is ready - log in at the app to trigger JIT local provisioning." -ForegroundColor Green
