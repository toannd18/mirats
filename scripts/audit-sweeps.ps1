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
  Sweep 5 - Transactions: a handler calling SaveChangesAsync MORE THAN ONCE must run inside an
                         explicit transaction -- preferably DbTransaction.ExecuteAsync (shared
                         helper), or a manual BeginTransactionAsync + CommitAsync pair. Otherwise
                         data commits before the ActionLog (the B5 failure class: data without an
                         audit row). Also flags the inverse bug: CommitAsync before the ActionLog
                         write + its SaveChangesAsync (log saved outside the transaction).
  Sweep 6 - Mojibake:    tracked text files containing double-encoded UTF-8 sequences (the damage
                         caused by PowerShell 5.1 Get-Content/Set-Content on UTF-8-without-BOM
                         files). Small explicit exclusion list = documents that deliberately QUOTE
                         the broken sequences (AGENTS.md, .claude/skills/mirats-docs/SKILL.md,
                         docs/HANDOFF_LATEST.md).

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
#   1) _actionLogService.LogAction( ... companyId: ... )            -> checks `companyId:`
#   2) _context.ActionLogs.Add(new ActionLog { ... CompanyId = ... }) -> checks `CompanyId =`
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
    # Company-independent master data (no CompanyId column on the entity) legitimately omit it --
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
    # does NOT need to inspect inside the entry -- treat these as automatically safe (no false
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
# Sweep 5 - Handlers with >1 SaveChangesAsync must use DbTransaction.ExecuteAsync
# ------------------------------------------------------------
# Known-error class (AUDIT-LOG B5): a handler that saves DATA and then saves the ActionLog in a
# SECOND SaveChanges WITHOUT an explicit transaction commits the data first -- if the log step fails
# (or the process dies in between) the change exists with NO audit row. The shared helper
# Application/Common/Persistence/DbTransaction.ExecuteAsync wraps CreateExecutionStrategy +
# BeginTransaction + single commit, so "data + ActionLog" commit together.
# Rule: for each HANDLER class body, count SaveChangesAsync. More than one save requires an EXPLICIT
# transaction: DbTransaction.ExecuteAsync (shared helper) OR a manual BeginTransactionAsync +
# CommitAsync pair (Checkout/RefreshToken/Publish and other concurrency-critical handlers use
# FOR UPDATE with a manual transaction). Two failure shapes are reported:
#   1) >1 SaveChangesAsync with NO explicit transaction -> data commits before the ActionLog (B5).
#   2) CommitAsync FOLLOWED BY both an ActionLog write and a SaveChangesAsync -> the log is saved
#      outside the transaction (the old CreateCampaign shape).
# The "after commit" check only inspects the text AFTER the last CommitAsync and requires BOTH an
# ActionLog write and a save, so handlers whose private helper methods SaveChanges later in the file
# (e.g. RefreshTokenCommandHandler -> RotateAsync, called BEFORE the commit) are not false positives.
$appRoot = Join-Path $RepoRoot 'aspire-react/aspire-react.Application'
$s5Count = 0
if (Test-Path -LiteralPath $appRoot) {
    $appCsFiles = @(Get-ChildItem -LiteralPath $appRoot -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
    $handlerStart = [regex]'public\s+(?:sealed\s+)?class\s+(?<name>\w+Handler)\b'
    foreach ($f in $appCsFiles) {
        $content = Get-Content -LiteralPath $f.FullName -Raw
        if ($content -notmatch 'SaveChangesAsync') { continue }

        $handlerMatches = $handlerStart.Matches($content)
        for ($h = 0; $h -lt $handlerMatches.Count; $h++) {
            $start = $handlerMatches[$h].Index
            $end = if ($h + 1 -lt $handlerMatches.Count) { $handlerMatches[$h + 1].Index } else { $content.Length }
            $body = $content.Substring($start, $end - $start)
            $saveCount = ([regex]::Matches($body, 'SaveChangesAsync')).Count
            $hasExplicitTransaction = ($body -match 'DbTransaction\.ExecuteAsync') -or
                                      ($body -match 'BeginTransactionAsync' -and $body -match 'CommitAsync')

            $lastCommit = $body.LastIndexOf('CommitAsync', [System.StringComparison]::Ordinal)
            $tail = if ($lastCommit -ge 0) { $body.Substring($lastCommit) } else { '' }
            $logAfterCommit = $tail -match '_actionLogService\.Log\(|LogAction\(|ActionLogs\.Add\('
            $saveAfterCommit = $tail -match 'SaveChangesAsync'

            if ($logAfterCommit -and $saveAfterCommit) {
                Add-Violation 'S5' ("{0}: handler {1} calls CommitAsync BEFORE writing the ActionLog + SaveChangesAsync (log saved outside the transaction)" -f (Normalize $f.FullName), $handlerMatches[$h].Groups['name'].Value)
                $s5Count++
            }
            elseif ($saveCount -gt 1 -and -not $hasExplicitTransaction) {
                Add-Violation 'S5' ("{0}: handler {1} calls SaveChangesAsync {2} times WITHOUT an explicit transaction (DbTransaction.ExecuteAsync or BeginTransactionAsync+CommitAsync)" -f (Normalize $f.FullName), $handlerMatches[$h].Groups['name'].Value, $saveCount)
                $s5Count++
            }
        }
    }
    $script:stats['Sweep 5 (Transactions)'] = "$s5Count violation(s) / $($appCsFiles.Count) application files"
} else {
    Add-Violation 'S5' ("Application directory not found: {0}" -f (Normalize $appRoot))
    $s5Count++
    $script:stats['Sweep 5 (Transactions)'] = "$s5Count violation(s) / 0 files"
}

# ------------------------------------------------------------
# Sweep 6 - Mojibake: double-encoded UTF-8 sequences in tracked text files
# ------------------------------------------------------------
# WHY: PowerShell 5.1 reads a UTF-8 file WITHOUT BOM as ANSI, so any Get-Content/Set-Content (or
# -replace) pipeline turns Vietnamese into mojibake ("Khong the" -> "Kh<mojibake>"). AGENTS.md forbids
# that workflow; this sweep catches the damage BEFORE it is committed.
# Detector: a mojibake LEAD char (U+00C3, U+00C4, U+00E1, U+00E2) immediately followed by a mojibake
# TRAIL char (U+00BA/BB/B0/A1..A3, U+20AC, U+2020, U+2018/19, U+201C/1D, U+2013/14). Legitimate
# Vietnamese text does not match - a plain "A-tilde" in uppercase "DA" is not followed by those trails.
# EXCLUSIONS (documents that deliberately QUOTE mojibake examples; keep this list short + explicit):
#   AGENTS.md                             - the hard rule itself shows the bad sequence
#   .claude/skills/mirats-docs/SKILL.md    - doc-writing skill shows the same example
#   docs/HANDOFF_LATEST.md                 - historical incident notes
$s6Count = 0
$mojibakeExcluded = @(
    'AGENTS.md',
    '.claude/skills/mirats-docs/SKILL.md',
    'docs/HANDOFF_LATEST.md'
)
$mojibakeLead = '[' + [char]0x00C3 + [char]0x00C4 + [char]0x00E1 + [char]0x00E2 + ']'
$mojibakeTrail = '[' + [char]0x00BA + [char]0x00BB + [char]0x00B0 + [char]0x00A1 + [char]0x00A2 +
    [char]0x00A3 + [char]0x20AC + [char]0x2020 + [char]0x2018 + [char]0x2019 + [char]0x201C +
    [char]0x201D + [char]0x2013 + [char]0x2014 + ']'
$mojibakeRegex = [regex]($mojibakeLead + $mojibakeTrail)
$trackedFiles = @()
try { $trackedFiles = @(& git -C $RepoRoot ls-files 2>$null) } catch { $trackedFiles = @() }
$mojibakeScanned = 0
foreach ($rel in $trackedFiles) {
    if (-not $rel) { continue }
    $normRel = Normalize $rel
    if ($mojibakeExcluded -contains $normRel) { continue }
    if ($normRel -match '\.(png|jpg|jpeg|gif|ico|woff2?|ttf|eot|zip|gz|dump|bin|dll|pdb|exe|pdf|har|trx|snap|map)$') { continue }
    $full = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $full)) { continue }
    if ((Get-Item -LiteralPath $full).Length -gt 2MB) { continue }
    $mojibakeScanned++
    $lines = [IO.File]::ReadAllLines($full, [Text.Encoding]::UTF8)
    $hitLines = @()
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($mojibakeRegex.IsMatch($lines[$i])) { $hitLines += ($i + 1) }
    }
    if ($hitLines.Count -gt 0) {
        $shown = ($hitLines | Select-Object -First 3) -join ', '
        Add-Violation 'S6' ("{0}: mojibake on line(s) {1} - file corrupted by a non-UTF8 text pipeline" -f $normRel, $shown)
        $s6Count++
    }
}
$script:stats['Sweep 6 (Mojibake)'] = "$s6Count violation(s) / $mojibakeScanned tracked text files"

# ------------------------------------------------------------
# Report + exit code
# ------------------------------------------------------------
Write-Host ''
Write-Host (' {0,-34} {1}' -f 'Sweep 1 (Claims):', $script:stats['Sweep 1 (Claims)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 2 (Enum string):', $script:stats['Sweep 2 (Enum string)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 3 (ActionLog companyId):', $script:stats['Sweep 3 (ActionLog companyId)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 4 (Table scroll):', $script:stats['Sweep 4 (Table scroll)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 5 (Transactions):', $script:stats['Sweep 5 (Transactions)'])
Write-Host (' {0,-34} {1}' -f 'Sweep 6 (Mojibake):', $script:stats['Sweep 6 (Mojibake)'])
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


