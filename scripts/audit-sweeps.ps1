<#
.SYNOPSIS
Automated audit sweep over the known-error classes in Appendix A of docs/DEVELOPMENT_WORKFLOW.md.
Run before every commit / build / release to catch regressions early (ST9 / Appendix C).

.DESCRIPTION
Static, dependency-free checks implemented in PowerShell Core (cross-platform: Windows/Linux/macOS):

  Sweep 1 - Claims:      *.cs reading sub / preferred_username / NameIdentifier WITHOUT preferring
                         (or also having) the "local_user_id" claim stamped by JIT provisioning.
  Sweep 2 - Enum:        frontend *.ts/*.tsx comparing enum fields to numbers instead of the
                         string values the API returns (JsonStringEnumConverter).
  Sweep 3 - ActionLog:   LogAction( call sites that do not pass companyId: (mandatory per the
                         project convention "ActionLog bat buoc kem CompanyId").
  Sweep 4 - Table scroll: frontend <Table>/<ProTable> without a scroll= prop (responsive tables).

Exit code: 0 = CLEAN, 1 = violations found (file:line details are printed).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/audit-sweeps.ps1
  pwsh ./scripts/audit-sweeps.ps1 -RepoRoot /path/to/repo
#>
[CmdletBinding()]
param(
    # Repository root. Defaults to the parent of the scripts/ folder.
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = Split-Path -Parent $scriptDir
}

$script:violations = [System.Collections.Generic.List[string]]::new()
$script:stats = [ordered]@{}

function Add-Violation([string]$sweep, [string]$detail) {
    $script:violations.Add("[$sweep] $detail")
}

# Normalize path separators so path checks work identically on Windows and Unix.
function Normalize([string]$path) { return $path.Replace('\', '/') }

$serverRoot = Join-Path $RepoRoot 'aspire-react/aspire-react.Server'
$frontendSrc = Join-Path $RepoRoot 'aspire-react/frontend/src'

if (-not (Test-Path -LiteralPath $serverRoot)) { throw "Backend directory not found: $serverRoot" }
if (-not (Test-Path -LiteralPath $frontendSrc)) { throw "Frontend src directory not found: $frontendSrc" }

$csFiles = @(Get-ChildItem -LiteralPath $serverRoot -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$frontFiles = @(Get-ChildItem -LiteralPath $frontendSrc -Recurse -File |
    Where-Object { $_.Extension -in '.ts', '.tsx' -and $_.FullName -notmatch '\\(node_modules|dist)\\' })

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Audit Sweep - Appendix A known-error classes' -ForegroundColor Cyan
Write-Host (' Repo root: {0}' -f $RepoRoot) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ------------------------------------------------------------
# Sweep 1 - Claims: sub/preferred_username without local_user_id
# ------------------------------------------------------------
$s1Count = 0
foreach ($f in $csFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Raw
    $usesClaim = $content -match 'preferred_username' -or $content -match '"sub"' -or $content -match 'ClaimTypes\.NameIdentifier'
    if ($usesClaim -and $content -notmatch 'local_user_id') {
        Add-Violation 'S1' ("{0}: reads sub/preferred_username/NameIdentifier without local_user_id priority" -f (Normalize $f.FullName))
        $s1Count++
    }
}
$script:stats['Sweep 1 (Claims)'] = "$s1Count violation(s) / $($csFiles.Count) .cs files"

# ------------------------------------------------------------
# Sweep 2 - Enum numeric comparisons in the frontend
# ------------------------------------------------------------
# The API serializes enums as strings (JsonStringEnumConverter), so comparing e.g. status === 2
# is the known-error class from Appendix A row 2. Legitimate exceptions:
#   - HTTP status checks (response?.status === 401) - not our enum fields.
#   - frontend/src/types/asset.ts - the canonical legacy-number -> string normalize helper.
$enumPattern = '\b(?:status|categoryType|checkoutType|targetType|actionType|actionSource|itemType|type)\s*(?:===|!==|==|!=)\s*[0-9]'
$s2Count = 0
foreach ($f in $frontFiles) {
    $normPath = Normalize $f.FullName
    if ($normPath -match '/types/asset\.ts$') { continue }   # sanctioned normalize helper
    $lines = Get-Content -LiteralPath $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'response\??\.status') { continue } # HTTP status, not an enum field
        if ($line -match $enumPattern) {
            Add-Violation 'S2' ("{0}:{1}: enum compared to a number - {2}" -f $normPath, ($i + 1), $line.Trim())
            $s2Count++
        }
    }
}
$script:stats['Sweep 2 (Enum string)'] = "$s2Count violation(s) / $($frontFiles.Count) frontend files"

# ------------------------------------------------------------
# Sweep 3 - ActionLog call sites missing companyId
# ------------------------------------------------------------
# Matches BOTH log-writing patterns so neither can be a blind spot:
#   1) _actionLogService.LogAction( ... companyId: ... )            → checks `companyId:`
#   2) _context.ActionLogs.Add(new ActionLog { ... CompanyId = ... }) → checks `CompanyId =`
function Get-ParenBlocks([string]$content, [string]$needle) {
    $blocks = [System.Collections.Generic.List[string]]::new()
    $idx = 0
    while (($start = $content.IndexOf($needle, $idx)) -ge 0) {
        $open = $content.IndexOf('(', $start)
        if ($open -lt 0) { break }
        $depth = 0
        $i = $open
        while ($i -lt $content.Length) {
            $ch = $content[$i]
            if ($ch -eq '(') { $depth++ }
            elseif ($ch -eq ')') { $depth--; if ($depth -eq 0) { break } }
            $i++
        }
        if ($i -ge $content.Length) { break }   # unbalanced - stop scanning this file
        $blocks.Add($content.Substring($start, $i - $start + 1))
        $idx = $i + 1
    }
    return $blocks
}

$s3Count = 0
foreach ($f in $csFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Raw

    # Pattern 1: LogAction( call sites (skip the interface/implementation declaration only).
    if ($content -match 'LogAction\(' -and $content -notmatch 'void LogAction\(') {
        foreach ($block in (Get-ParenBlocks $content 'LogAction(')) {
            if ($block -notmatch 'companyId:') {
                $snippet = $block.Substring(0, [Math]::Min(90, $block.Length)).Replace("`r", '').Replace("`n", ' ')
                Add-Violation 'S3' ("{0}: LogAction call without companyId: - {1}..." -f (Normalize $f.FullName), $snippet)
                $s3Count++
            }
        }
    }

    # Pattern 2: _context.ActionLogs.Add(new ActionLog { ... }) call sites.
    # Company-independent master data (no CompanyId column on the entity) legitimately omit it —
    # these are NOT flagged (avoids false positives).
    $companyIndependent = 'ItemType\s*=\s*ItemType\.(Category|Model|Manufacturer|Supplier|Company|CustomField)\b'
    if ($content -match 'ActionLogs\.Add\(') {
        foreach ($block in (Get-ParenBlocks $content 'ActionLogs.Add(')) {
            if ($block -match 'new\s+ActionLog' -and $block -notmatch 'CompanyId\s*=' -and $block -notmatch $companyIndependent) {
                $snippet = $block.Substring(0, [Math]::Min(90, $block.Length)).Replace("`r", '').Replace("`n", ' ')
                Add-Violation 'S3' ("{0}: ActionLogs.Add(new ActionLog{{...}}) without CompanyId = - {1}..." -f (Normalize $f.FullName), $snippet)
                $s3Count++
            }
        }
    }

    # Pattern 3 (Task S2a): typed helper _actionLogService.Log(new ActionLogEntry { ... }).
    # This is compiler-ENFORCED safe: ActionLogEntry declares ItemType/ItemId/ActionType/CreatedBy/
    # CompanyId as `required` (C# 11), so a call site that omits CompanyId fails to compile. Sweep 3
    # does NOT need to inspect inside the entry — treat these as automatically safe (no false
    # positive, no blind spot). Only flag if a `Log(` call is NOT followed by a `new ActionLogEntry`
    # (i.e. someone passed an ActionLog directly, bypassing the typed guard).
    if ($content -match '\bLog\(new\s+ActionLog\b') {
        foreach ($block in (Get-ParenBlocks $content 'Log(new ')) {
            if ($block -match 'Log\(new\s+ActionLog\b' -and $block -notmatch 'new\s+ActionLogEntry') {
                $snippet = $block.Substring(0, [Math]::Min(90, $block.Length)).Replace("`r", '').Replace("`n", ' ')
                Add-Violation 'S3' ("{0}: Log( bypasses the typed ActionLogEntry builder (compiler-safe CompanyId lost) - {1}..." -f (Normalize $f.FullName), $snippet)
                $s3Count++
            }
        }
    }
}
$script:stats['Sweep 3 (ActionLog companyId)'] = "$s3Count violation(s) / $($csFiles.Count) .cs files"


# ------------------------------------------------------------
# Sweep 4 - <Table>/<ProTable> without a scroll prop
# ------------------------------------------------------------
$s4Count = 0
foreach ($f in $frontFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Raw
    $hasTable = $content -match '<ProTable\b' -or $content -match '<Table\b'
    if ($hasTable -and $content -notmatch 'scroll=') {
        Add-Violation 'S4' ("{0}: <Table>/<ProTable> without scroll={{ x: 'max-content' }}" -f (Normalize $f.FullName))
        $s4Count++
    }
}
$script:stats['Sweep 4 (Table scroll)'] = "$s4Count violation(s) / $($frontFiles.Count) frontend files"

# ------------------------------------------------------------
# Report + exit code
# ------------------------------------------------------------
Write-Host ''
Write-Host (' {0,-34} {1}' -f 'Sweep 1 (Claims):', $script:stats['Sweep 1 (Claims)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 2 (Enum string):', $script:stats['Sweep 2 (Enum string)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 3 (ActionLog companyId):', $script:stats['Sweep 3 (ActionLog companyId)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 4 (Table scroll):', $script:stats['Sweep 4 (Table scroll)'])
Write-Host '------------------------------------------------------------'
Write-Host (' {0,-34} {1}' -f 'TOTAL:', "$($script:violations.Count) violation(s)")
Write-Host ''

if ($script:violations.Count -gt 0) {
    Write-Host ' FAIL - violations found:' -ForegroundColor Red
    foreach ($v in $script:violations) { Write-Host ('   ' + $v) -ForegroundColor Red }
    exit 1
}

Write-Host ' PASS - repository is CLEAN (0 violations).' -ForegroundColor Green
exit 0

