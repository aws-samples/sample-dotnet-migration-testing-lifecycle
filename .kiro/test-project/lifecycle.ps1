#Requires -Version 5.1
<#
.SYNOPSIS
    Deterministic migration-testing runner. The ONLY process that checks the environment,
    starts apps, builds, runs tests and generates reports, and the single writer of
    .kiro/app-testing/<Page>/status.json, timings.json and PROGRESS.md.
.DESCRIPTION
    Agents call it with one constant prefix: .\.kiro\test-project\lifecycle.ps1 -Action ...
    Runs on Windows PowerShell 5.1 and PowerShell 7. All paths resolve from the script
    location (never from the current directory), except doctor's workspace check.

    Exit codes (all actions):
      0 = ran (findings allowed)
      2 = Reference baseline failed
      3 = environment / preflight failure (nothing ran)
      1 = harness error only (bad args, build failure, missing test, lock held,
          report generator failure, status.json write failure)
.PARAMETER Action
    doctor | build-module | run-page | rerun-target | fix-round | gate | progress | report |
    render | wait | reset | status | preflight | build | run-module | stop
.EXAMPLE
    .\.kiro\test-project\lifecycle.ps1 -Action doctor
    .\.kiro\test-project\lifecycle.ps1 -Action build-module -Page PayorList -Module A
    .\.kiro\test-project\lifecycle.ps1 -Action run-page -Page PayorList -Modules A,B -MaxParallel 4
    .\.kiro\test-project\lifecycle.ps1 -Action run-page -Page PayorList -Detach
    .\.kiro\test-project\lifecycle.ps1 -Action wait -Page PayorList -TimeoutSec 90
    .\.kiro\test-project\lifecycle.ps1 -Action render -Page PayorList -View module-result -Module A
    .\.kiro\test-project\lifecycle.ps1 -Action status -Page PayorList
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("preflight", "status", "build", "run-module", "stop", "doctor", "run-page", "rerun-target", "fix-round", "gate", "progress", "report", "reset", "build-module", "render", "wait")]
    [string]$Action,
    [string]$Page = "",
    [string]$Module = "",
    [string[]]$Modules = @(),
    [ValidateSet("Reference", "Target", "")]
    [string]$System = "",
    [string]$TestFilter = "",
    [ValidateSet("plan_signoff", "final_signoff", "baseline_failed", "")]
    [string]$Gate = "",
    [ValidateSet("pending", "confirmed", "")]
    [string]$State = "",
    [string]$ResponseFile = "",
    [string]$Message = "",
    [string]$ReportPath = "",
    [ValidateSet("loop", "final-gate")]
    [string]$Source = "loop",
    [ValidateSet("plan", "tests-authored", "live-status", "module-result", "run-summary", "fix-round", "final", "")]
    [string]$View = "",
    [ValidateRange(1, 8)]
    [int]$MaxParallel = 4,
    # P8: per-step screenshots in run-page / rerun-target (all | failed = failed + first + last step | none).
    [ValidateSet("all", "failed", "none", "")]
    [string]$Screenshots = "",
    [switch]$Detach,
    [ValidateRange(-1, 4)]
    [int]$Phase = -1,
    [ValidateRange(30, 1800)]
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell 5.1 serializes arrays wrapped in PSObject as {value,Count}; drop the ETS member.
if ($PSVersionTable.PSVersion.Major -lt 6) { Remove-TypeData -TypeName System.Array -ErrorAction SilentlyContinue }

# ============================================================================
# Paths and constants
# ============================================================================
$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$TestProjectDir = $PSScriptRoot
$ScriptDir = $PSScriptRoot
$HarnessDir = Join-Path $TestProjectDir 'Harness'
$HarnessCsproj = Join-Path $HarnessDir 'MigrationTests.Harness.csproj'
$ReportsDir = Join-Path $TestProjectDir 'Reports'
$ReportsCsproj = Join-Path $ReportsDir 'MigrationTests.Reports.csproj'
$ReportsDll = Join-Path $ReportsDir 'bin\Debug\net10.0\MigrationTests.Reports.dll'
$HarnessTestsCsproj = Join-Path $TestProjectDir 'Harness.Tests\MigrationTests.Harness.Tests.csproj'
$ModulesDir = Join-Path $TestProjectDir 'Modules'
$SettingsFile = Join-Path $TestProjectDir 'appsettings.test.json'
$ResultFile = Join-Path $ScriptDir 'lifecycle-result.json'
$DoctorResultFile = Join-Path $TestProjectDir 'doctor-result.json'
$AppTestingRoot = Join-Path $RepoRoot '.kiro\app-testing'
$PermissionsTemplate = Join-Path $RepoRoot '.kiro\settings\permissions.yaml'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$ZeroPromptMarker = '# testing-lifecycle zero-prompt rules v1'
$TestTimeoutSec = 1200
$ReportTimeoutSec = 120
$BuildTimeoutSec = 900
$AppStartTimeoutSec = 120
$RestartDefaultMs = 20000
$DefaultStepMs = 3500
$PhaseNames = @('Plan', 'Author tests', 'Run Reference + Target', 'Fix', 'Report')
$PhaseBudgetMs = @(240000, 240000, 240000, 180000, 60000)
$TotalBudgetMs = 900000
$NoLifecycleLine = 'Phase timing not measured (no lifecycle recorded)'
# P8: default per-step screenshot policy when -Screenshots is not passed (see README "Screenshots").
$DefaultScreenshots = 'all'

$script:ExitCode = 0
$script:ExitMessage = ''
$script:LockPath = $null
$script:RunLog = $null
$script:PageDir = $null
$script:LogDir = Join-Path $TestProjectDir 'logs'
$script:S = $null
$script:Ctx = $null
$script:Settings = $null
$script:DoctorChecks = $null
$script:RunAction = $null
$script:PlaywrightBinDir = $null
$script:BaselineFailed = [ordered]@{}
$script:HarnessFailed = [ordered]@{}
$script:QuietExit = $false

# ============================================================================
# Exit / error helpers
# ============================================================================
function Stop-Lifecycle([int]$Code, [string]$Msg) {
    $script:ExitCode = $Code
    $script:ExitMessage = $Msg
    throw 'LIFECYCLE_EXIT'
}

function Test-IsLifecycleExit($ErrRecord) {
    return ($null -ne $ErrRecord -and $ErrRecord.Exception -and $ErrRecord.Exception.Message -eq 'LIFECYCLE_EXIT')
}

# ============================================================================
# Time / formatting helpers
# ============================================================================
function Get-NowIso { return (Get-Date).ToString('o') }

function Get-IsoString($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.ToString('o') }
    return [string]$Value
}

function ConvertTo-DateSafe($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value }
    $str = [string]$Value
    if ($str -eq '') { return $null }
    $d = [datetime]::MinValue
    if ([datetime]::TryParse($str, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$d)) { return $d }
    return $null
}

function Format-Hms([double]$Ms) {
    if ($Ms -lt 0) { $Ms = 0 }
    $ts = [TimeSpan]::FromMilliseconds($Ms)
    return ('{0:00}:{1:00}:{2:00}' -f [Math]::Floor($ts.TotalHours), $ts.Minutes, $ts.Seconds)
}

function Format-MinSec([double]$Ms) {
    if ($Ms -lt 0) { $Ms = 0 }
    $ts = [TimeSpan]::FromMilliseconds($Ms)
    return ('{0:00}:{1:00}' -f [Math]::Floor($ts.TotalMinutes), $ts.Seconds)
}

function Limit-Text([string]$Text, [int]$Max) {
    if ($null -eq $Text) { return '' }
    $t = ($Text -replace '[\r\n]+', ' ').Trim()
    if ($t.Length -gt $Max) { return $t.Substring(0, $Max - 3) + '...' }
    return $t
}

function New-List { return , ([System.Collections.ArrayList]::new()) }

# ============================================================================
# JSON helpers (all loaded JSON becomes ordered dictionaries + ArrayLists)
# ============================================================================
function ConvertTo-PlainData($Obj) {
    if ($null -eq $Obj) { return $null }
    if ($Obj -is [string]) { return $Obj }
    if ($Obj -is [datetime]) { return $Obj.ToString('o') }
    if ($Obj -is [System.Management.Automation.PSCustomObject]) {
        $h = [ordered]@{}
        foreach ($p in $Obj.PSObject.Properties) { $h[$p.Name] = ConvertTo-PlainData $p.Value }
        return $h
    }
    if ($Obj -is [System.Collections.IDictionary]) {
        $h = [ordered]@{}
        foreach ($k in @($Obj.Keys)) { $h[[string]$k] = ConvertTo-PlainData $Obj[$k] }
        return $h
    }
    if ($Obj -is [System.Collections.IEnumerable]) {
        $l = [System.Collections.ArrayList]::new()
        foreach ($i in $Obj) { [void]$l.Add((ConvertTo-PlainData $i)) }
        return , $l
    }
    return $Obj
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = [IO.File]::ReadAllText($Path, $Utf8NoBom)
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $obj = ConvertFrom-Json -InputObject $text -ErrorAction Stop
    return (ConvertTo-PlainData $obj)
}

function ConvertTo-JsonText($Obj) { return (ConvertTo-Json -InputObject $Obj -Depth 20) }

function Get-List($Dict, [string]$Key) {
    $v = $Dict[$Key]
    if ($v -is [System.Collections.ArrayList]) { return , $v }
    $l = [System.Collections.ArrayList]::new()
    if ($null -ne $v) {
        if (($v -is [System.Collections.IEnumerable]) -and -not ($v -is [string]) -and -not ($v -is [System.Collections.IDictionary])) {
            foreach ($i in $v) { [void]$l.Add($i) }
        } else { [void]$l.Add($v) }
    }
    $Dict[$Key] = $l
    return , $l
}

function Get-Items($Dict, [string]$Key) {
    # Emits the ELEMENTS of a list value (use as @(Get-Items $d 'k')). Get-List returns the list
    # object itself (for .Add); wrapping that in @() would give a one-element array.
    if ($null -eq $Dict) { return }
    $v = $Dict[$Key]
    if ($null -eq $v) { return }
    if (($v -is [string]) -or ($v -is [System.Collections.IDictionary])) { return $v }
    if ($v -is [System.Collections.IEnumerable]) { foreach ($i in $v) { , $i }; return }
    return $v
}

function Get-Dict($Dict, [string]$Key) {
    $v = $Dict[$Key]
    if ($v -is [System.Collections.IDictionary]) { return $v }
    $n = [ordered]@{}
    $Dict[$Key] = $n
    return $n
}

function Get-Nested($Obj, [string[]]$Keys) {
    $cur = $Obj
    foreach ($k in $Keys) {
        if ($null -eq $cur) { return $null }
        if ($cur -is [System.Collections.IDictionary]) {
            if (-not $cur.Contains($k)) { return $null }
            $cur = $cur[$k]
        } else { return $null }
    }
    return $cur
}

function Get-Int($Value) {
    if ($null -eq $Value) { return 0 }
    $n = 0
    if ([int]::TryParse([string]$Value, [ref]$n)) { return $n }
    $d = 0.0
    if ([double]::TryParse([string]$Value, [Globalization.NumberStyles]::Any, [Globalization.CultureInfo]::InvariantCulture, [ref]$d)) { return [int]$d }
    return 0
}

# ============================================================================
# File helpers
# ============================================================================
function Write-AtomicFile([string]$Path, [string]$Text) {
    # Returns $true on success. Callers decide whether a failure is fatal.
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = "$Path.tmp"
    try { [IO.File]::WriteAllText($tmp, $Text, $Utf8NoBom) }
    catch { Write-Host "WARN: could not write $tmp : $($_.Exception.Message)" -ForegroundColor Yellow; return $false }
    for ($i = 0; $i -lt 4; $i++) {
        try {
            # [NullString]::Value: a plain $null would be marshalled as "" (invalid backup path).
            if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($tmp, $Path, [NullString]::Value) }
            else { [IO.File]::Move($tmp, $Path) }
            return $true
        } catch {
            if ($i -lt 3) { Start-Sleep -Milliseconds 200 }
        }
    }
    try {
        Copy-Item -LiteralPath $tmp -Destination $Path -Force -ErrorAction Stop
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        Write-Host "WARN: atomic replace failed for $Path; used Copy-Item fallback" -ForegroundColor Yellow
        return $true
    } catch {
        Write-Host "WARN: write failed for $Path : $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
}

function Get-SharedText([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return '' }
    $fs = $null
    try {
        $fs = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
        $sr = New-Object IO.StreamReader($fs, $Utf8NoBom, $true)
        return $sr.ReadToEnd()
    } catch { return '' }
    finally { if ($fs) { $fs.Dispose() } }
}

function Get-FileTail([string]$Path, [int]$Count) {
    $text = Get-SharedText $Path
    if (-not $text) { return @() }
    $lines = @($text -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($lines.Count -le $Count) { return $lines }
    return $lines[($lines.Count - $Count)..($lines.Count - 1)]
}

function Get-RelPath([string]$Path) {
    # Path relative to the repo root, forward slashes (for console/PROGRESS display).
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($RepoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $full = $full.Substring($RepoRoot.Length).TrimStart('\')
    }
    return ($full -replace '\\', '/')
}

function Resolve-RepoPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

# ============================================================================
# Console + run log
# ============================================================================
function Write-Line([string]$Text, [string]$Color = '') {
    if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text }
    if ($script:RunLog) {
        try { [IO.File]::AppendAllText($script:RunLog, ((Get-Date).ToString('HH:mm:ss') + ' ' + $Text + "`r`n"), $Utf8NoBom) } catch { Write-Verbose ("run log append failed: {0}" -f $_.Exception.Message) }
    }
}

# ============================================================================
# Page / lock
# ============================================================================
function Assert-Page([switch]$Create) {
    if (-not $Page) { Stop-Lifecycle 1 "-Page is required for -Action $Action" }
    if ($Page -notmatch '^[A-Za-z0-9_.-]+$') { Stop-Lifecycle 1 "Invalid -Page '$Page' (letters, digits, _ . - only)" }
    $script:PageDir = Join-Path $AppTestingRoot $Page
    if ($Create -and -not (Test-Path -LiteralPath $script:PageDir)) { New-Item -ItemType Directory -Path $script:PageDir -Force | Out-Null }
    $script:LogDir = Join-Path $script:PageDir 'logs'
}

function Get-LockState {
    # @{ Info = lock json (or $null); Alive = owner pid is a live process other than this one }
    $lockPath = Join-Path $script:PageDir '.lock'
    $info = $null
    if (Test-Path -LiteralPath $lockPath) { try { $info = Read-JsonFile $lockPath } catch { $info = $null } }
    $alive = $false
    if ($info) {
        $lockPid = Get-Int $info.pid
        if ($lockPid -gt 0 -and $lockPid -ne $PID) { $alive = ($null -ne (Get-Process -Id $lockPid -ErrorAction SilentlyContinue)) }
    }
    return @{ Info = $info; Alive = $alive }
}

function Enter-Lock([string]$LockAction, [switch]$Wait, [string]$RunId = '') {
    $lockPath = Join-Path $script:PageDir '.lock'
    $deadline = (Get-Date).AddSeconds(5)
    while ($true) {
        if (Test-Path -LiteralPath $lockPath) {
            $info = $null
            try { $info = Read-JsonFile $lockPath } catch { $info = $null }
            $lockPid = 0
            if ($info) { $lockPid = Get-Int $info.pid }
            $alive = $false
            if ($lockPid -gt 0 -and $lockPid -ne $PID) {
                $alive = ($null -ne (Get-Process -Id $lockPid -ErrorAction SilentlyContinue))
            }
            if ($alive) {
                if ($Wait -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500; continue }
                Stop-Lifecycle 1 ("Lock held by PID {0} (action {1} since {2}): {3}" -f $lockPid, $info.action, $info.startedAt, (Get-RelPath $lockPath))
            }
            if ($lockPid -gt 0 -and $lockPid -ne $PID) {
                Write-Line ("WARN: taking over stale lock from dead PID {0} (action {1})" -f $lockPid, $info.action) 'Yellow'
            }
        }
        $lock = [ordered]@{ pid = $PID; action = $LockAction; startedAt = (Get-NowIso) }
        if ($RunId) { $lock.runId = $RunId }
        if (-not (Write-AtomicFile $lockPath (ConvertTo-JsonText $lock))) { Stop-Lifecycle 1 "Could not write lock file $lockPath" }
        $script:LockPath = $lockPath
        return
    }
}

function Exit-Lock {
    if ($script:LockPath -and (Test-Path -LiteralPath $script:LockPath)) {
        try {
            $info = Read-JsonFile $script:LockPath
            if ($info -and (Get-Int $info.pid) -eq $PID) { Remove-Item -LiteralPath $script:LockPath -Force -ErrorAction SilentlyContinue }
        } catch { Remove-Item -LiteralPath $script:LockPath -Force -ErrorAction SilentlyContinue }
    }
    $script:LockPath = $null
}

# ============================================================================
# Process launch (cmd /c redirection through ShellExecute; no inherited handles)
# ============================================================================
function Start-Logged {
    param(
        [string]$ArgString,
        [string]$Log,
        [string]$ErrLog,
        [string]$WorkingDirectory = $TestProjectDir,
        [string]$FilePath = 'dotnet',
        [int]$TimeoutSec = 0,
        [scriptblock]$OnTick = $null,
        [switch]$NoWait
    )
    foreach ($f in @($Log, $ErrLog)) {
        $d = Split-Path -Parent $f
        if ($d -and -not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    }
    $cmdLine = "/c $FilePath $ArgString 1>`"$Log`" 2>`"$ErrLog`""
    $p = Start-Process -FilePath $env:ComSpec -ArgumentList $cmdLine -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru
    $null = $p.Handle   # cache the handle so ExitCode is populated on Windows PowerShell 5.1
    if ($NoWait) { return $p }
    $timedOut = $false
    $deadline = $null
    if ($TimeoutSec -gt 0) { $deadline = (Get-Date).AddSeconds($TimeoutSec) }
    $completed = $false
    try {
        while (-not $p.HasExited) {
            if ($OnTick) { & $OnTick }
            if ($deadline -and (Get-Date) -gt $deadline) {
                & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
                $timedOut = $true
                break
            }
            Start-Sleep -Seconds 1
        }
        $p.WaitForExit()
        if ($OnTick) { & $OnTick }
        $completed = $true
    } finally {
        if (-not $completed -and -not $p.HasExited) { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null }
    }
    return @{ ExitCode = $p.ExitCode; TimedOut = $timedOut; Pid = $p.Id }
}

$BuildArgs = '-nologo -nodeReuse:false -p:UseSharedCompilation=false'
$ModuleBuildArgs = '-nologo -nodeReuse:false -p:UseSharedCompilation=false -p:BuildProjectReferences=false -p:RestoreRecursive=false'

function Get-BuildOutcome($ExitCode, [bool]$TimedOut, [string]$Log, [string]$ErrLog) {
    # Same success rule everywhere: exit 0 and 'Build succeeded' in the log, no timeout.
    $text = Get-SharedText $Log
    $codeText = if ($null -eq $ExitCode) { 'null' } else { [string]$ExitCode }
    $ok = ($null -ne $ExitCode) -and ($ExitCode -eq 0) -and ($text -match 'Build succeeded') -and (-not $TimedOut)
    $errs = @()
    if (-not $ok) {
        $all = @(($text + "`n" + (Get-SharedText $ErrLog)) -split "`r?`n")
        $errs = @($all | Where-Object { $_ -match 'error CS\d+' } | ForEach-Object { $_.Trim() } | Select-Object -Unique | Select-Object -First 5)
        if ($errs.Count -eq 0) { $errs = @($all | Where-Object { $_ -match ':\s*error\s' } | ForEach-Object { $_.Trim() } | Select-Object -Unique | Select-Object -First 5) }
    }
    $why = if ($ok) { '' } elseif ($TimedOut) { 'timeout' } elseif ($ExitCode -eq 0) { "exit 0 but no 'Build succeeded' in log" } else { "exit=$codeText" }
    return @{ Ok = $ok; Why = $why; Errors = $errs; CodeText = $codeText }
}

function Invoke-DotnetBuild([string]$Project, [string]$LogName, [string]$Label, [string]$ExtraArgs = '') {
    $log = Join-Path $script:LogDir $LogName
    $errLog = [IO.Path]::ChangeExtension($log, '.err.log')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $wd = Split-Path -Parent $Project
    $argString = "build `"$Project`" $BuildArgs"
    if ($ExtraArgs) { $argString += ' ' + $ExtraArgs }
    $r = Start-Logged -ArgString $argString -Log $log -ErrLog $errLog -WorkingDirectory $wd -TimeoutSec $BuildTimeoutSec
    $sw.Stop()
    $o = Get-BuildOutcome $r.ExitCode ([bool]$r.TimedOut) $log $errLog
    if ($o.Ok) {
        Write-Line ("[build] {0} PASS exit={1} 'Build succeeded' {2}ms log={3}" -f $Label, $o.CodeText, $sw.ElapsedMilliseconds, (Get-RelPath $log)) 'Green'
        return $sw.ElapsedMilliseconds
    }
    Write-Line ("[build] {0} FAIL {1} log={2}" -f $Label, $o.Why, (Get-RelPath $log)) 'Red'
    foreach ($e in $o.Errors) { Write-Line ("  " + $e) 'Red' }
    Stop-Lifecycle 1 ("Build failed ({0}): {1}; see {2}" -f $Label, $o.Why, (Get-RelPath $log))
}

# ============================================================================
# Named mutexes, Harness/Reports build (once, never concurrently), module builds (parallel)
# ============================================================================
function Enter-NamedMutex($Mtx, [int]$TimeoutMs, [string]$Label) {
    # Returns $true when acquired. An abandoned mutex (previous holder died) counts as acquired.
    try { return [bool]$Mtx.WaitOne($TimeoutMs) }
    catch {
        $ex = $_.Exception
        while ($ex) {
            if ($ex -is [System.Threading.AbandonedMutexException]) {
                Write-Line ("WARN: previous holder of the {0} lock died; lock taken over" -f $Label) 'Yellow'
                return $true
            }
            $ex = $ex.InnerException
        }
        throw
    }
}

function Get-NewestSourceTime([string]$Dir) {
    # Newest *.cs / *.csproj / *.json under $Dir, excluding bin/ and obj/.
    $newest = [datetime]::MinValue
    if (-not (Test-Path -LiteralPath $Dir)) { return $newest }
    $stack = New-Object System.Collections.Stack
    $stack.Push((Get-Item -LiteralPath $Dir))
    while ($stack.Count -gt 0) {
        $d = $stack.Pop()
        foreach ($i in @(Get-ChildItem -LiteralPath $d.FullName -Force -ErrorAction SilentlyContinue)) {
            if ($i.PSIsContainer) {
                if ($i.Name -ne 'bin' -and $i.Name -ne 'obj') { $stack.Push($i) }
                continue
            }
            if ($i.Extension -ne '.cs' -and $i.Extension -ne '.csproj' -and $i.Extension -ne '.json') { continue }
            if ($i.LastWriteTime -gt $newest) { $newest = $i.LastWriteTime }
        }
    }
    return $newest
}

function Test-StampFresh([string]$Stamp, [datetime]$Newest, [string]$Output) {
    if (-not (Test-Path -LiteralPath $Stamp) -or -not (Test-Path -LiteralPath $Output)) { return $false }
    return ((Get-Item -LiteralPath $Stamp).LastWriteTime -ge $Newest)
}

function Write-Stamp([string]$Stamp) {
    $d = Split-Path -Parent $Stamp
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    [IO.File]::WriteAllText($Stamp, (Get-NowIso), $Utf8NoBom)
}

function Invoke-HarnessBuild([switch]$WithReports) {
    # Builds Harness (and the Reports tool with -WithReports) inside the machine-wide build mutex.
    # Up to date (stamp newer than every source, output dll present) -> 0 ms, no dotnet call.
    $harnessDll = Join-Path $HarnessDir 'bin\Debug\net10.0\MigrationTests.Harness.dll'
    $hStamp = Join-Path $HarnessDir 'obj\lifecycle-build.stamp'
    $rStamp = Join-Path $ReportsDir 'obj\lifecycle-build.stamp'
    $mtx = New-Object System.Threading.Mutex($false, 'Local\MigrationTests.HarnessBuild')
    $acquired = $false
    try {
        $acquired = Enter-NamedMutex $mtx ($BuildTimeoutSec * 1000) 'Harness build'
        if (-not $acquired) { Stop-Lifecycle 1 'Harness build lock timeout' }
        $hNewest = Get-NewestSourceTime $HarnessDir
        $hOk = Test-StampFresh $hStamp $hNewest $harnessDll
        $rOk = $true
        if ($WithReports) {
            $rNewest = Get-NewestSourceTime $ReportsDir
            if ($hNewest -gt $rNewest) { $rNewest = $hNewest }
            $rOk = Test-StampFresh $rStamp $rNewest $ReportsDll
        }
        if ($hOk -and $rOk) { return [long]0 }
        if ($WithReports) {
            $ms = Invoke-DotnetBuild $ReportsCsproj 'build-reports.log' 'Reports tool (+ Harness)'
            Write-Stamp $hStamp
            Write-Stamp $rStamp
        } else {
            $ms = Invoke-DotnetBuild $HarnessCsproj 'build-harness.log' 'Harness'
            Write-Stamp $hStamp
        }
        return [long]$ms
    } finally {
        if ($acquired) { try { $mtx.ReleaseMutex() } catch { Write-Verbose ("mutex release failed: {0}" -f $_.Exception.Message) } }
        $mtx.Dispose()
    }
}

function Get-ModuleDir([string]$X) { return (Join-Path $ModulesDir ("{0}.Mod{1}" -f $Page, $X)) }
function Get-ModuleProject([string]$X) { return (Join-Path (Get-ModuleDir $X) ("{0}.Mod{1}.csproj" -f $Page, $X)) }
function Get-ModuleBinDir([string]$X) { return (Join-Path (Get-ModuleDir $X) ("bin\Debug\" + (Get-TestTfm))) }
function Get-TestFilePath([string]$X, [string]$Sys) {
    if ($Sys -eq 'Reference') { return (Join-Path (Get-ModuleDir $X) ("Reference{0}Module{1}_v2.cs" -f $Page, $X)) }
    return (Join-Path (Get-ModuleDir $X) ("{0}Module{1}_v2.cs" -f $Page, $X))
}

function Get-ModuleCsprojText([string]$X) {
    $lines = @(
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <!-- Generated by lifecycle.ps1 -Action build-module. One project per module (parallel-safe). -->',
        '  <PropertyGroup>',
        '    <TargetFramework>net10.0</TargetFramework>',
        '    <Nullable>enable</Nullable>',
        '    <ImplicitUsings>enable</ImplicitUsings>',
        '    <IsPackable>false</IsPackable>',
        '    <IsTestProject>true</IsTestProject>',
        '    <RootNamespace>MigrationTests.Playwright</RootNamespace>',
        ('    <AssemblyName>{0}.Mod{1}</AssemblyName>' -f $Page, $X),
        '  </PropertyGroup>',
        '  <ItemGroup>',
        '    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />',
        '    <PackageReference Include="NUnit" Version="4.3.2" />',
        '    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />',
        '    <PackageReference Include="Microsoft.Playwright" Version="1.52.0" />',
        '    <PackageReference Include="Microsoft.Playwright.NUnit" Version="1.52.0" />',
        '  </ItemGroup>',
        '  <ItemGroup>',
        '    <ProjectReference Include="..\..\Harness\MigrationTests.Harness.csproj" />',
        '  </ItemGroup>',
        '  <ItemGroup>',
        '    <Compile Include="..\..\Harness\Shared\ModuleSetUpFixture.cs" Link="ModuleSetUpFixture.cs" />',
        '    <Content Include="..\..\appsettings.test.json" Link="appsettings.test.json" CopyToOutputDirectory="Always" />',
        '    <Content Include="..\..\suppressions.json" Link="suppressions.json" Condition="Exists(''..\..\suppressions.json'')" CopyToOutputDirectory="Always" />',
        '  </ItemGroup>',
        '</Project>'
    )
    return (($lines -join "`r`n") + "`r`n")
}

function Invoke-ProjectBuilds($Items) {
    # $Items: @(@{ Key; Project; Log }). Parallel 'dotnet build' (<= $MaxParallel at once) with
    # module-build flags (Harness must already be built). Never calls Stop-Lifecycle.
    # Returns [ordered] Key -> @{ ok; ms; errors; why; log }.
    $results = [ordered]@{}
    $pending = [System.Collections.ArrayList]::new()
    foreach ($it in @($Items)) { [void]$pending.Add($it) }
    $running = [ordered]@{}
    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $MaxParallel) {
            $it = $pending[0]
            $pending.RemoveAt(0)
            $errLog = [IO.Path]::ChangeExtension($it.Log, '.err.log')
            if (-not (Test-Path -LiteralPath $it.Project)) {
                $results[$it.Key] = @{ ok = $false; ms = [long]0; errors = @("project not found: " + (Get-RelPath $it.Project)); why = 'project not found'; log = $it.Log }
                continue
            }
            $p = Start-Logged -NoWait -ArgString ("build `"" + $it.Project + "`" " + $ModuleBuildArgs) -Log $it.Log -ErrLog $errLog -WorkingDirectory (Split-Path -Parent $it.Project)
            $running[$it.Key] = @{ Item = $it; Proc = $p; Sw = [Diagnostics.Stopwatch]::StartNew(); Deadline = (Get-Date).AddSeconds($BuildTimeoutSec); ErrLog = $errLog }
        }
        if ($running.Count -eq 0) { continue }
        Start-Sleep -Seconds 1
        foreach ($k in @($running.Keys)) {
            $j = $running[$k]
            $timedOut = $false
            if (-not $j.Proc.HasExited) {
                if ((Get-Date) -lt $j.Deadline) { continue }
                & taskkill.exe /PID $j.Proc.Id /T /F 2>&1 | Out-Null
                $timedOut = $true
            }
            $j.Proc.WaitForExit()
            $j.Sw.Stop()
            $o = Get-BuildOutcome $j.Proc.ExitCode $timedOut $j.Item.Log $j.ErrLog
            $results[$k] = @{ ok = $o.Ok; ms = [long]$j.Sw.ElapsedMilliseconds; errors = $o.Errors; why = $o.Why; log = $j.Item.Log }
            $running.Remove($k)
        }
    }
    return $results
}

function Get-StepNames([string]$Text) {
    # Distinct step names in source order (ExecuteStep first, then manual StepPass/StepFail/StepNotTested).
    $names = [ordered]@{}
    foreach ($m in [regex]::Matches($Text, 'ExecuteStep\(\s*"([^"]+)"')) { $names[$m.Groups[1].Value] = $true }
    foreach ($m in [regex]::Matches($Text, 'Step(Pass|Fail|NotTested)\(\s*"([^"]+)"')) { $names[$m.Groups[2].Value] = $true }
    # Callers wrap the result in @(...): one name unrolls to a scalar, none to $null.
    return [string[]]@($names.Keys)
}

function Get-StepNameCount([string]$Text) { return @(Get-StepNames $Text).Count }

function Test-TestMethodExists([string]$X, [string]$Name) {
    # Static check (replaces dotnet test --list-tests): 'public async Task <Name>(' in Modules/{Page}.Mod{X}/*.cs.
    $dir = Get-ModuleDir $X
    if (-not $Name -or -not (Test-Path -LiteralPath $dir)) { return $false }
    $rx = 'public\s+async\s+Task\s+' + [regex]::Escape($Name) + '\s*\('
    foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
        if ([regex]::IsMatch((Get-SharedText $f.FullName), $rx)) { return $true }
    }
    return $false
}

function Get-TestFileInfo([string]$X, [string]$Sys, [string]$Test) {
    $f = Get-TestFilePath $X $Sys
    $info = [ordered]@{ file = (Get-RelPath $f) -replace '^\.kiro/test-project/', ''; test = $Test; exists = $false; methodFound = $false; revision = $null; steps = 0; stepNames = @(); assertionCalls = 0; lastWrite = $null }
    $info.methodFound = (Test-TestMethodExists $X $Test)
    if (-not (Test-Path -LiteralPath $f)) { return $info }
    $text = Get-SharedText $f
    $info.exists = $true
    $first = ($text -split "`r?`n", 2)[0]
    if ($first -match '^\s*//\s*plan-revision:\s*(\d+)') { $info.revision = [int]$Matches[1] }
    $stepNames = [System.Collections.ArrayList]::new()
    foreach ($n in @(Get-StepNames $text)) { [void]$stepNames.Add([string]$n) }
    $info.stepNames = $stepNames
    $info.steps = $stepNames.Count
    $info.assertionCalls = [regex]::Matches($text, '\.(AssertEquals|AssertChanged|AssertUnchanged|AssertContains|Assert)\(').Count
    $info.lastWrite = (Get-Item -LiteralPath $f).LastWriteTime.ToString('o')
    return $info
}

function Get-CurrentPhaseSegment([int]$K) {
    # Last segment of phase K in the latest lifecycle of timings.json (read-only): @{ Start; End } or $null.
    $t = $null
    try { $t = Read-JsonFile (Get-TimingsPath) } catch { $t = $null }
    if ($null -eq $t) { return $null }
    $lcs = @(Get-Items $t 'lifecycles')
    if ($lcs.Count -eq 0) { return $null }
    $ph = @(@(Get-Items $lcs[$lcs.Count - 1] 'phases') | Where-Object { (Get-Int $_.id) -eq $K })
    if ($ph.Count -eq 0) { return $null }
    $segs = @(Get-Items $ph[0] 'segments')
    if ($segs.Count -eq 0) { return $null }
    $last = $segs[$segs.Count - 1]
    return @{ Start = (ConvertTo-DateSafe $last.start); End = (ConvertTo-DateSafe $last.end) }
}

function Get-ModuleTestNames([string]$X) {
    $ref = $null
    $tgt = $null
    $pm = Get-ModulePlan $X
    if ($pm) { $ref = [string]$pm.referenceTest; $tgt = [string]$pm.targetTest }
    if (-not $ref) { $ref = Get-FallbackTest $X 'Reference' }
    if (-not $tgt) { $tgt = Get-FallbackTest $X 'Target' }
    return @{ Reference = $ref; Target = $tgt }
}

function Write-BuildJson([string]$X, $Result) {
    $dir = Join-Path $script:PageDir ("runs\Module$X")
    $path = Join-Path $dir 'build.json'
    $prev = $null
    try { $prev = Read-JsonFile $path } catch { $prev = $null }
    $attempts = 1
    if ($prev) {
        $seg = Get-CurrentPhaseSegment 1
        $prevAt = ConvertTo-DateSafe $prev.at
        if ($seg -and $seg.Start -and $prevAt -and $prevAt -ge $seg.Start -and ($null -eq $seg.End -or $prevAt -le $seg.End)) { $attempts = (Get-Int $prev.attempts) + 1 }
    }
    $names = Get-ModuleTestNames $X
    $doc = [ordered]@{
        schemaVersion = 1; module = $X
        project = ((Get-RelPath (Get-ModuleProject $X)) -replace '^\.kiro/test-project/', '')
        result = $(if ($Result.ok) { 'PASS' } else { 'FAIL' })
        ms = [long]$Result.ms; at = (Get-NowIso)
        log = ('logs/' + (Split-Path -Leaf $Result.log))
        errors = @($Result.errors)
        attempts = $attempts
        reference = (Get-TestFileInfo $X 'Reference' $names.Reference)
        target = (Get-TestFileInfo $X 'Target' $names.Target)
    }
    [void](Write-AtomicFile $path (ConvertTo-JsonText $doc))
    return $doc
}

function Invoke-ModuleBuilds([string[]]$Ids) {
    # Parallel build of Modules/{Page}.Mod{X}; writes runs/Module{X}/build.json per module.
    # Returns [ordered] X -> @{ ok; ms; errors; why; log; build = build.json doc }.
    $items = @()
    foreach ($X in $Ids) { $items += , @{ Key = $X; Project = (Get-ModuleProject $X); Log = (Join-Path $script:LogDir "build-$X.log") } }
    $res = Invoke-ProjectBuilds $items
    foreach ($X in $Ids) {
        $r = $res[$X]
        $r.build = Write-BuildJson $X $r
        if ($r.ok) {
            Write-Line ("[build] Module {0} PASS {1}ms log={2}" -f $X, $r.ms, (Get-RelPath $r.log)) 'Green'
        } else {
            Write-Line ("[build] FAIL Module {0} {1} log={2}" -f $X, $r.why, (Get-RelPath $r.log)) 'Red'
            foreach ($e in @($r.errors)) { Write-Line ("  " + $e) 'Red' }
        }
    }
    return $res
}

# ============================================================================
# Settings
# ============================================================================
function Read-Settings {
    if (-not (Test-Path -LiteralPath $SettingsFile)) { Write-Error "Settings not found: $SettingsFile"; exit 1 }
    $script:Settings = Get-Content $SettingsFile -Raw | ConvertFrom-Json
}

function Get-SettingsData {
    # Plain-data settings (dictionary). Throws on parse errors.
    if (-not (Test-Path -LiteralPath $SettingsFile)) { return $null }
    return (Read-JsonFile $SettingsFile)
}

function Get-Apps($SettingsData) {
    $result = @()
    if ($null -eq $SettingsData) { return $result }
    foreach ($a in @(Get-Items $SettingsData 'Apps')) {
        if ($null -eq $a) { continue }
        $proj = ''
        if ($a.project) { $proj = [IO.Path]::GetFullPath((Join-Path $TestProjectDir ([string]$a.project))) }
        $accept = 'status200'
        if ($a.healthAccept) { $accept = [string]$a.healthAccept }
        $port = 0
        $uri = $null
        if ($a.healthUrl -and [Uri]::TryCreate([string]$a.healthUrl, [UriKind]::Absolute, [ref]$uri)) { $port = $uri.Port }
        $isTarget = $false
        if ($a.isTarget -eq $true) { $isTarget = $true }
        $result += , @{
            name = [string]$a.name; project = $proj; projectSetting = [string]$a.project
            baseName = [IO.Path]::GetFileNameWithoutExtension($proj); healthUrl = [string]$a.healthUrl
            healthAccept = $accept; port = $port; isTarget = $isTarget; resetData = ($a.resetData -eq $true)
        }
    }
    return $result
}

function Get-SourcePaths($SettingsData) {
    # Repo-root-relative source folders of the Reference and Target apps (appsettings ReferenceSourcePath /
    # TargetSourcePath), forward slashes, $null when unset. Agents read them from status.json paths.*.
    $r = [ordered]@{ reference = $null; target = $null }
    if ($null -eq $SettingsData) { return $r }
    foreach ($pair in @(@('reference', 'ReferenceSourcePath'), @('target', 'TargetSourcePath'))) {
        $v = [string]$SettingsData[$pair[1]]
        if ($v.Trim()) { $r[$pair[0]] = $v.Trim().Replace('\', '/').TrimEnd('/') }
    }
    return $r
}

function Get-FreshShellExecutionPolicy {
    # The policy a NEW PowerShell session gets (the Process scope of this run is ignored: it may be a
    # -ExecutionPolicy Bypass child). Precedence: MachinePolicy > UserPolicy > CurrentUser > LocalMachine.
    $list = @(Get-ExecutionPolicy -List)
    $byScope = @{}
    foreach ($e in $list) { $byScope[[string]$e.Scope] = [string]$e.ExecutionPolicy }
    $effective = 'Undefined'
    $from = ''
    foreach ($scope in @('MachinePolicy', 'UserPolicy', 'CurrentUser', 'LocalMachine')) {
        $v = $byScope[$scope]
        if ($v -and $v -ne 'Undefined') { $effective = $v; $from = $scope; break }
    }
    if ($effective -eq 'Undefined') {
        # Every scope Undefined: the host default applies. PowerShell 7 (Core) on Windows defaults to
        # RemoteSigned (its policy lives in powershell.config.json); Windows PowerShell 5.1 (Desktop) to Restricted.
        if ($PSVersionTable.PSEdition -eq 'Core') { $effective = 'RemoteSigned'; $from = 'PowerShell 7 default (all scopes Undefined)' }
        else { $effective = 'Restricted'; $from = 'Windows PowerShell default (all scopes Undefined)' }
    }
    $summary = (@($list | ForEach-Object { "$($_.Scope)=$($_.ExecutionPolicy)" }) -join ', ')
    return @{ Policy = $effective; Scope = $from; Summary = $summary }
}

# ============================================================================
# Ports / processes
# ============================================================================
function Get-PortOwner([int]$Port) {
    if ($Port -le 0) { return $null }
    $conns = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($conns.Count -eq 0) { return $null }
    $ownerPid = [int]($conns[0].OwningProcess)
    $name = ''
    $cmd = ''
    $start = $null
    try {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$ownerPid" -ErrorAction Stop
        if ($cim) { $name = [string]$cim.Name; $cmd = [string]$cim.CommandLine }
    } catch { Write-Verbose ("CIM process lookup failed: {0}" -f $_.Exception.Message) }
    try {
        $gp = Get-Process -Id $ownerPid -ErrorAction Stop
        if (-not $name) { $name = $gp.ProcessName }
        $start = $gp.StartTime
    } catch { Write-Verbose ("Get-Process lookup failed: {0}" -f $_.Exception.Message) }
    return @{ Pid = $ownerPid; Name = $name; CommandLine = $cmd; StartTime = $start }
}

function Test-OwnerMatches($Owner, [string]$BaseName) {
    if ($null -eq $Owner -or -not $BaseName) { return $false }
    foreach ($v in @($Owner.Name, $Owner.CommandLine)) {
        if ($v -and $v.IndexOf($BaseName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Wait-PortFree([int]$Port, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ($null -eq (Get-PortOwner $Port)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return ($null -eq (Get-PortOwner $Port))
}

# ============================================================================
# csproj helpers
# ============================================================================
function Get-CsprojTfms([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $text = [IO.File]::ReadAllText($Path)
    $tfms = @()
    foreach ($m in [regex]::Matches($text, '<TargetFrameworks?>([^<]+)</TargetFrameworks?>')) {
        foreach ($t in ($m.Groups[1].Value -split ';')) { if ($t.Trim()) { $tfms += $t.Trim() } }
    }
    return $tfms
}

function Get-TestTfm {
    $t = @(Get-CsprojTfms $HarnessCsproj)
    if ($t.Count -gt 0) { return $t[0] }
    return 'net10.0'
}

function Get-TestBinDir {
    # run-page / rerun-target point this at the first built module bin (holds playwright.ps1 + .playwright\).
    if ($script:PlaywrightBinDir) { return $script:PlaywrightBinDir }
    return (Join-Path $HarnessDir ("bin\Debug\" + (Get-TestTfm)))
}

function Get-ModuleCsprojFiles {
    $files = @()
    if (-not (Test-Path -LiteralPath $ModulesDir)) { return $files }
    foreach ($d in @(Get-ChildItem -LiteralPath $ModulesDir -Directory -ErrorAction SilentlyContinue)) {
        foreach ($f in @(Get-ChildItem -LiteralPath $d.FullName -Filter '*.csproj' -File -ErrorAction SilentlyContinue)) { $files += $f.FullName }
    }
    return $files
}

# ============================================================================
# Playwright browsers
# ============================================================================
function Get-PlaywrightInfo {
    $info = @{ Version = $null; BrowsersJson = $null; Root = $null; Required = @(); Missing = @(); Error = $null }
    $text = ''
    if (Test-Path -LiteralPath $HarnessCsproj) { $text = [IO.File]::ReadAllText($HarnessCsproj) }
    $m = [regex]::Match($text, '<PackageReference\s+Include="Microsoft\.Playwright"\s+Version="([^"]+)"')
    if (-not $m.Success) { $info.Error = 'Microsoft.Playwright PackageReference not found in the test csproj'; return $info }
    $ver = $m.Groups[1].Value
    $info.Version = $ver
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages'
    if ($env:NUGET_PACKAGES) { $nugetRoot = $env:NUGET_PACKAGES }
    $candidates = @(
        (Join-Path $nugetRoot ("microsoft.playwright\" + $ver + "\.playwright\package\browsers.json")),
        (Join-Path (Get-TestBinDir) '.playwright\package\browsers.json')
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { $info.BrowsersJson = $c; break } }
    if (-not $info.BrowsersJson) { return $info }
    try { $bj = Read-JsonFile $info.BrowsersJson } catch { $info.Error = "browsers.json unreadable: $($_.Exception.Message)"; return $info }
    $revChromium = $null
    $revShell = $null
    foreach ($b in @(Get-Items $bj 'browsers')) {
        if ($b.name -eq 'chromium') { $revChromium = [string]$b.revision }
        if ($b.name -eq 'chromium-headless-shell') { $revShell = [string]$b.revision }
    }
    if (-not $revChromium) { $info.Error = 'chromium entry not found in browsers.json'; return $info }
    if (-not $revShell) { $revShell = $revChromium }
    $root = Join-Path $env:LOCALAPPDATA 'ms-playwright'
    if ($env:PLAYWRIGHT_BROWSERS_PATH -and $env:PLAYWRIGHT_BROWSERS_PATH -ne '0') { $root = $env:PLAYWRIGHT_BROWSERS_PATH }
    $info.Root = $root
    $info.Required = @("chromium-$revChromium", "chromium_headless_shell-$revShell")
    $info.Missing = @($info.Required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_)) })
    return $info
}

function Get-PlaywrightInstallCommand {
    $ps1 = Join-Path (Get-TestBinDir) 'playwright.ps1'
    $rel = Get-RelPath $ps1
    return ("powershell -ExecutionPolicy Bypass -File " + ($rel -replace '/', '\') + " install chromium")
}

function Confirm-PlaywrightBrowsers {
    # Post-build: verify the browsers, auto-install when missing, then re-check. Exit 3 on failure.
    $info = Get-PlaywrightInfo
    if ($info.Error -or -not $info.BrowsersJson) {
        $why = $info.Error
        if (-not $why) { $why = 'browsers.json not found after build' }
        Write-Line "[playwright] FAIL $why. Fix: $(Get-PlaywrightInstallCommand)" 'Red'
        Stop-Lifecycle 3 "Playwright browsers: $why"
    }
    if ($info.Missing.Count -eq 0) {
        Write-Line ("[playwright] PASS {0} in {1}" -f ($info.Required -join ' + '), $info.Root) 'Green'
        return
    }
    $ps1 = Join-Path (Get-TestBinDir) 'playwright.ps1'
    if (-not (Test-Path -LiteralPath $ps1)) { Stop-Lifecycle 3 "Playwright browsers missing ($($info.Missing -join ', ')) and $((Get-RelPath $ps1)) not found" }
    Write-Line ("[playwright] missing {0}; installing chromium..." -f ($info.Missing -join ', ')) 'Yellow'
    $log = Join-Path $script:LogDir 'playwright-install.log'
    $errLog = Join-Path $script:LogDir 'playwright-install.err.log'
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) { $r = Start-Logged -FilePath 'pwsh' -ArgString "-NoProfile -File `"$ps1`" install chromium" -Log $log -ErrLog $errLog -TimeoutSec $BuildTimeoutSec }
    else { $r = Start-Logged -FilePath 'powershell' -ArgString "-NoProfile -ExecutionPolicy Bypass -File `"$ps1`" install chromium" -Log $log -ErrLog $errLog -TimeoutSec $BuildTimeoutSec }
    $info = Get-PlaywrightInfo
    if ($info.Missing.Count -gt 0) {
        Write-Line ("[playwright] FAIL still missing {0} after install (exit {1}); see {2}. Fix: {3}" -f ($info.Missing -join ', '), $r.ExitCode, (Get-RelPath $log), (Get-PlaywrightInstallCommand)) 'Red'
        Stop-Lifecycle 3 "Playwright browsers missing after auto-install"
    }
    Write-Line ("[playwright] PASS installed {0} in {1}" -f ($info.Required -join ' + '), $info.Root) 'Green'
}

# ============================================================================
# Doctor (environment checklist)
# ============================================================================
function New-Check([string]$Status, [string]$Name, [string]$Detail, [string]$Fix, [bool]$Required) {
    return [ordered]@{ status = $Status; name = $Name; detail = $Detail; fix = $Fix; required = $Required }
}

function Format-CheckLine($C) {
    $line = "[{0}] {1} - {2}" -f $C.status, $C.name, $C.detail
    if ($C.status -ne 'PASS' -and $C.fix) { $line = $line.TrimEnd('.') + ". Fix: " + $C.fix }
    return $line
}

function Get-TemplateInstallSteps {
    $steps = @()
    if (Test-Path -LiteralPath $PermissionsTemplate) {
        foreach ($l in @(Get-Content -LiteralPath $PermissionsTemplate -TotalCount 30)) {
            if ($l -match '^#\s+([1-9]\..*)$') { $steps += $Matches[1].Trim() }
        }
    }
    if ($steps.Count -eq 0) { return 'Follow the install steps in the header of .kiro/settings/permissions.yaml' }
    return (($steps -join ' ') + ' (template: .kiro/settings/permissions.yaml)')
}

function Invoke-Doctor([switch]$BuildPending) {
    $checks = New-List
    $settings = $null
    $settingsError = $null
    try { $settings = Get-SettingsData } catch { $settingsError = $_.Exception.Message }
    $apps = @(Get-Apps $settings)

    # 1. dotnet + SDK majors
    $majors = @()
    $projFiles = @($HarnessCsproj) + @(Get-ModuleCsprojFiles) + @($apps | Where-Object { $_.project } | ForEach-Object { $_.project })
    foreach ($pf in $projFiles) {
        foreach ($tfm in @(Get-CsprojTfms $pf)) {
            foreach ($mm in [regex]::Matches($tfm, 'net(\d+)\.')) { $majors += [int]$mm.Groups[1].Value }
        }
    }
    $majors = @($majors | Sort-Object -Unique)
    $fixSdk = (@($majors | ForEach-Object { "winget install Microsoft.DotNet.SDK.$_ (or https://dotnet.microsoft.com/download/dotnet/$_.0)" }) -join '; ')
    if (-not $fixSdk) { $fixSdk = 'Install the .NET SDK: https://dotnet.microsoft.com/download' }
    $dn = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dn) {
        [void]$checks.Add((New-Check 'FAIL' 'dotnet SDK' 'dotnet is not on PATH' $fixSdk $true))
    } else {
        $sdkLines = @(& dotnet --list-sdks 2>$null)
        $installed = @($sdkLines | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } } | Sort-Object -Unique)
        $missing = @($majors | Where-Object { $installed -notcontains $_ })
        $sdkVersions = (@($sdkLines | ForEach-Object { ($_ -split ' ')[0] }) -join ', ')
        if ($missing.Count -gt 0) {
            $fixMissing = (@($missing | ForEach-Object { "winget install Microsoft.DotNet.SDK.$_ (or https://dotnet.microsoft.com/download/dotnet/$_.0)" }) -join '; ')
            [void]$checks.Add((New-Check 'FAIL' 'dotnet SDK' ("required SDK major(s) {0} from {1} csproj(s); installed: {2}" -f ($majors -join ','), $projFiles.Count, $sdkVersions) $fixMissing $true))
        } else {
            [void]$checks.Add((New-Check 'PASS' 'dotnet SDK' ("required major(s) {0} (from {1} csproj TargetFramework); installed: {2}" -f ($majors -join ','), $projFiles.Count, $sdkVersions) '' $true))
        }
    }

    # 1b. ExecutionPolicy: agents call .\.kiro\test-project\lifecycle.ps1 directly from new shells.
    #     Restricted (the Windows PowerShell 5.1 default when every scope is Undefined) and AllSigned block it.
    $epFix = 'Set-ExecutionPolicy -Scope CurrentUser RemoteSigned'
    try {
        $ep = Get-FreshShellExecutionPolicy
        $epName = $ep.Policy
        if ($ep.Scope -like '*default*') { $epName = "$($ep.Policy) ($($ep.Scope))" }
        if (@('Restricted', 'AllSigned', 'Undefined') -contains $ep.Policy) {
            $fix = $epFix
            if (@('MachinePolicy', 'UserPolicy') -contains $ep.Scope) { $fix = "$epFix does not override a Group Policy ($($ep.Scope)); ask your admin to allow RemoteSigned" }
            [void]$checks.Add((New-Check 'FAIL' 'ExecutionPolicy' ("effective policy for new shells is {0}: local scripts such as lifecycle.ps1 cannot run ({1})" -f $epName, $ep.Summary) $fix $true))
        } else {
            $src = ''
            if ($ep.Scope) { $src = " from $($ep.Scope)" }
            [void]$checks.Add((New-Check 'PASS' 'ExecutionPolicy' ("effective policy for new shells is {0}{1} ({2})" -f $ep.Policy, $src, $ep.Summary) '' $true))
        }
    } catch {
        [void]$checks.Add((New-Check 'WARN' 'ExecutionPolicy' ("could not read the execution policy: " + $_.Exception.Message) $epFix $false))
    }

    # 2. Playwright browsers
    $pw = Get-PlaywrightInfo
    $pwFix = Get-PlaywrightInstallCommand
    if ($pw.Error) {
        [void]$checks.Add((New-Check 'FAIL' 'Playwright browsers' $pw.Error $pwFix $true))
    } elseif (-not $pw.BrowsersJson) {
        [void]$checks.Add((New-Check 'WARN' 'Playwright browsers' ("Microsoft.Playwright {0}: browsers.json not available yet; will verify after build" -f $pw.Version) '' $true))
    } elseif ($pw.Missing.Count -gt 0) {
        if ($BuildPending) {
            [void]$checks.Add((New-Check 'WARN' 'Playwright browsers' ("Microsoft.Playwright {0}: missing {1} in {2}; will auto-install after build" -f $pw.Version, ($pw.Missing -join ', '), $pw.Root) '' $true))
        } else {
            [void]$checks.Add((New-Check 'FAIL' 'Playwright browsers' ("Microsoft.Playwright {0}: missing {1} in {2}" -f $pw.Version, ($pw.Missing -join ', '), $pw.Root) $pwFix $true))
        }
    } else {
        [void]$checks.Add((New-Check 'PASS' 'Playwright browsers' ("Microsoft.Playwright {0}: {1} in {2}" -f $pw.Version, ($pw.Required -join ' + '), $pw.Root) '' $true))
    }

    # 3. Settings
    $settingsFix = 'Edit .kiro/test-project/appsettings.test.json'
    if (-not (Test-Path -LiteralPath $SettingsFile)) {
        [void]$checks.Add((New-Check 'FAIL' 'Settings' 'appsettings.test.json not found' 'Restore it from git: git checkout -- .kiro/test-project/appsettings.test.json (then edit it for your app)' $true))
    } elseif ($settingsError) {
        [void]$checks.Add((New-Check 'FAIL' 'Settings' "appsettings.test.json does not parse: $settingsError" $settingsFix $true))
    } else {
        $issues = @()
        foreach ($key in @('BaseUrl', 'ReferenceBaseUrl')) {
            $u = $null
            $v = [string]$settings[$key]
            if (-not ([Uri]::TryCreate($v, [UriKind]::Absolute, [ref]$u)) -or ($u.Scheme -ne 'http' -and $u.Scheme -ne 'https')) { $issues += "$key '$v' is not an absolute http(s) URL" }
        }
        if ($apps.Count -eq 0) { $issues += 'Apps array missing or empty (the runner cannot start the apps)' }
        foreach ($a in $apps) {
            $u = $null
            if (-not $a.name) { $issues += 'Apps[] entry without name' }
            if (-not ([Uri]::TryCreate($a.healthUrl, [UriKind]::Absolute, [ref]$u)) -or ($u.Scheme -ne 'http' -and $u.Scheme -ne 'https')) { $issues += "Apps[$($a.name)].healthUrl '$($a.healthUrl)' is not an absolute http(s) URL" }
            if (-not $a.project -or -not (Test-Path -LiteralPath $a.project)) { $issues += "Apps[$($a.name)].project '$($a.projectSetting)' not found (resolved $($a.project))" }
            if (@('status200', 'anyResponse') -notcontains $a.healthAccept) { $issues += "Apps[$($a.name)].healthAccept '$($a.healthAccept)' must be status200 or anyResponse" }
        }
        if ($issues.Count -gt 0) {
            [void]$checks.Add((New-Check 'FAIL' 'Settings' ($issues -join '; ') ($settingsFix + ' (' + $issues[0] + ')') $true))
        } else {
            [void]$checks.Add((New-Check 'PASS' 'Settings' ("appsettings.test.json OK: BaseUrl {0}, ReferenceBaseUrl {1}, {2} app(s): {3}" -f $settings.BaseUrl, $settings.ReferenceBaseUrl, $apps.Count, (@($apps | ForEach-Object { "$($_.name):$($_.port)/$($_.healthAccept)" }) -join ', ')) '' $true))
        }
    }

    # 3b. Source paths (advisory): the agents edit only the Target source and never the Reference source.
    if ($settings -and -not $settingsError) {
        $sp = Get-SourcePaths $settings
        $spIssues = @()
        foreach ($pair in @(@('reference', 'ReferenceSourcePath'), @('target', 'TargetSourcePath'))) {
            $v = $sp[$pair[0]]
            if (-not $v) { $spIssues += "$($pair[1]) not set" }
            elseif (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $v) -PathType Container)) { $spIssues += "$($pair[1]) '$v' not found under the repo root" }
        }
        $spDetail = "reference {0}, target {1} (repo-relative; written to status.json paths.*)" -f $(if ($sp.reference) { $sp.reference } else { '(unset)' }), $(if ($sp.target) { $sp.target } else { '(unset)' })
        if ($spIssues.Count -gt 0) {
            [void]$checks.Add((New-Check 'WARN' 'Source paths' ($spDetail + '; ' + ($spIssues -join '; ')) 'Set ReferenceSourcePath and TargetSourcePath (folders relative to the repo root) in .kiro/test-project/appsettings.test.json' $false))
        } else {
            [void]$checks.Add((New-Check 'PASS' 'Source paths' $spDetail '' $false))
        }
    }

    # 3c. Stack profile: profiles/<StackProfile>.json and its 'extends' chain (generic last) must exist and parse.
    if ($settings -and -not $settingsError) {
        $profDir = Join-Path $TestProjectDir 'profiles'
        $profName = [string]$settings['StackProfile']
        $profFix = 'Fix or add .kiro/test-project/profiles/<name>.json (see docs/RUN-TESTING-LIFECYCLE.md "Adding a stack profile"), or clear StackProfile in appsettings.test.json'
        $chain = @()
        $profErr = $null
        $profMissing = @()
        $next = $(if ($profName) { $profName } else { 'generic' })
        $seen = @{}
        while ($next -and -not $seen.ContainsKey($next.ToLowerInvariant())) {
            $seen[$next.ToLowerInvariant()] = $true
            $pf = Join-Path $profDir ($next + '.json')
            if (-not (Test-Path -LiteralPath $pf)) {
                $profMissing += $next
                $next = $(if ($next -ieq 'generic') { $null } else { 'generic' })
                continue
            }
            try { $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json -ErrorAction Stop } catch { $profErr = "$next.json does not parse: $($_.Exception.Message)"; break }
            $chain += $next
            $ext = [string]$pj.extends
            $next = $(if ($ext) { $ext } elseif ($next -ieq 'generic') { $null } else { 'generic' })
        }
        if ($profErr) {
            [void]$checks.Add((New-Check 'FAIL' 'Stack profile' $profErr $profFix $true))
        } elseif ($profMissing.Count -gt 0) {
            [void]$checks.Add((New-Check 'FAIL' 'Stack profile' ("profile file(s) not found in {0}: {1}" -f (Get-RelPath $profDir), (($profMissing | ForEach-Object { "$_.json" }) -join ', ')) $profFix $true))
        } elseif (-not $profName) {
            [void]$checks.Add((New-Check 'WARN' 'Stack profile' ("StackProfile not set in appsettings.test.json: generic.json only ({0})" -f ($chain -join ' -> ')) 'Set "StackProfile" to the profile of your Reference/Target stack pair (optional)' $false))
        } else {
            [void]$checks.Add((New-Check 'PASS' 'Stack profile' ("{0} ({1})" -f $profName, ($chain -join ' -> ')) '' $false))
        }
    }

    # 4. Ports
    if ($apps.Count -gt 0) {
        $parts = @()
        $bad = @()
        $fixes = @()
        foreach ($a in $apps) {
            if ($a.port -le 0) { continue }
            $owner = Get-PortOwner $a.port
            if ($null -eq $owner) { $parts += ("{0} free - runner will start {1}" -f $a.port, $a.name); continue }
            if (Test-OwnerMatches $owner $a.baseName) { $parts += ("{0} served by {1} ({2} pid {3})" -f $a.port, $a.name, $owner.Name, $owner.Pid) }
            else {
                $bad += ("port {0} is held by {1} (pid {2}), expected {3}" -f $a.port, $owner.Name, $owner.Pid, $a.baseName)
                $fixes += ("Stop-Process -Id {0}   # {1}, or change the {2} port in appsettings.test.json" -f $owner.Pid, $owner.Name, $a.name)
            }
        }
        if ($bad.Count -gt 0) { [void]$checks.Add((New-Check 'FAIL' 'Ports' ($bad -join '; ') ($fixes -join '; ') $true)) }
        else { [void]$checks.Add((New-Check 'PASS' 'Ports' ($parts -join '; ') '' $true)) }
    }

    # 5. Repo is workspace (the one CWD-based check)
    $cwd = (Get-Location -PSProvider FileSystem).Path
    $permOk = Test-Path -LiteralPath (Join-Path $cwd '.kiro\settings\permissions.yaml')
    $wfOk = Test-Path -LiteralPath (Join-Path $cwd '.kiro\workflows\test-page.workflow.json')
    if ($permOk -and $wfOk) {
        [void]$checks.Add((New-Check 'PASS' 'Repo is workspace' "cwd $cwd holds .kiro\settings\permissions.yaml and .kiro\workflows\test-page.workflow.json" '' $true))
    } else {
        $miss = @()
        if (-not $permOk) { $miss += '.kiro\settings\permissions.yaml' }
        if (-not $wfOk) { $miss += '.kiro\workflows\test-page.workflow.json' }
        [void]$checks.Add((New-Check 'FAIL' 'Repo is workspace' ("cwd {0} does not contain {1}" -f $cwd, ($miss -join ' / ')) "Open the repo folder itself as the Kiro workspace: $RepoRoot" $true))
    }

    # 6. Zero-prompt rules (READ ONLY)
    $files = @()
    $userPerm = Join-Path $env:USERPROFILE '.kiro\settings\permissions.yaml'
    if (Test-Path -LiteralPath $userPerm) { $files += $userPerm }
    $wsRoots = Join-Path $env:USERPROFILE '.kiro\workspace-roots'
    if (Test-Path -LiteralPath $wsRoots) {
        foreach ($d in @(Get-ChildItem -LiteralPath $wsRoots -Directory -ErrorAction SilentlyContinue)) {
            $f = Join-Path $d.FullName 'permissions.yaml'
            if (Test-Path -LiteralPath $f) { $files += $f }
        }
    }
    $found = @()
    foreach ($f in $files) {
        try {
            $head = @(Get-Content -LiteralPath $f -TotalCount 5 -ErrorAction Stop)
            if ($head | Select-String -SimpleMatch -Pattern $ZeroPromptMarker -Quiet) { $found += $f }
        } catch { Write-Verbose ("could not read rule file: {0}" -f $_.Exception.Message) }
    }
    if ($found.Count -gt 0) {
        [void]$checks.Add((New-Check 'PASS' 'Zero-prompt rules' ("marker found in " + ($found -join ', ')) '' $true))
    } else {
        [void]$checks.Add((New-Check 'FAIL' 'Zero-prompt rules' ("marker '{0}' not found in {1} permissions file(s) under {2}\.kiro" -f $ZeroPromptMarker, $files.Count, $env:USERPROFILE) (Get-TemplateInstallSteps) $true))
    }

    # 7. git (advisory)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        [void]$checks.Add((New-Check 'PASS' 'git' ("on PATH: " + $git.Source) '' $false))
    } else {
        $cands = @()
        if ($env:LOCALAPPDATA) { $cands += (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe') }
        if ($env:ProgramFiles) { $cands += (Join-Path $env:ProgramFiles 'Git\cmd\git.exe') }
        if (${env:ProgramFiles(x86)}) { $cands += (Join-Path ${env:ProgramFiles(x86)} 'Git\cmd\git.exe') }
        $hit = @($cands | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
        if ($hit.Count -gt 0) { [void]$checks.Add((New-Check 'WARN' 'git' ("not on PATH; found at " + $hit[0]) ("add " + (Split-Path -Parent $hit[0]) + " to PATH (optional)") $false)) }
        else { [void]$checks.Add((New-Check 'WARN' 'git' 'not found on PATH or in the common install folders' 'Install Git from https://git-scm.com/download/win (optional)' $false)) }
    }

    # 8. Kiro workflows enabled (advisory)
    $wfFix = 'enable the Kiro user setting "Workflows" (kiroAgent.workflows.enabled) in Kiro Settings, then start a new chat'
    $kiroSettings = ''
    if ($env:APPDATA) { $kiroSettings = Join-Path $env:APPDATA 'Kiro\User\settings.json' }
    $wfText = $null
    if ($kiroSettings -and (Test-Path -LiteralPath $kiroSettings)) { try { $wfText = [IO.File]::ReadAllText($kiroSettings) } catch { $wfText = $null } }
    if ($null -eq $wfText) {
        [void]$checks.Add((New-Check 'WARN' 'Kiro workflows' "settings not readable: $kiroSettings" $wfFix $false))
    } elseif ($wfText -match '"kiroAgent\.workflows\.enabled"\s*:\s*true') {
        [void]$checks.Add((New-Check 'PASS' 'Kiro workflows' "kiroAgent.workflows.enabled=true in $kiroSettings" '' $false))
    } else {
        [void]$checks.Add((New-Check 'WARN' 'Kiro workflows' "kiroAgent.workflows.enabled is not true in $kiroSettings" $wfFix $false))
    }

    $failed = @($checks | Where-Object { $_.required -and $_.status -eq 'FAIL' })
    $result = [ordered]@{
        schemaVersion = 1
        generatedAt = (Get-NowIso)
        cwd = $cwd
        repoRoot = $RepoRoot
        buildPending = [bool]$BuildPending
        result = $(if ($failed.Count -gt 0) { 'FAIL' } else { 'PASS' })
        exitCode = $(if ($failed.Count -gt 0) { 3 } else { 0 })
        checks = $checks
    }
    [void](Write-AtomicFile $DoctorResultFile (ConvertTo-JsonText $result))
    $script:DoctorChecks = $result
    return $result
}

function Write-DoctorOutput($Result) {
    foreach ($c in @($Result.checks)) {
        $color = 'Green'
        if ($c.status -eq 'FAIL') { $color = 'Red' } elseif ($c.status -eq 'WARN') { $color = 'Yellow' }
        Write-Line (Format-CheckLine $c) $color
    }
    $fails = @($Result.checks | Where-Object { $_.status -eq 'FAIL' }).Count
    $warns = @($Result.checks | Where-Object { $_.status -eq 'WARN' }).Count
    Write-Line ("[doctor] {0}: {1} check(s), {2} FAIL, {3} WARN; written {4}" -f $Result.result, @($Result.checks).Count, $fails, $warns, (Get-RelPath $DoctorResultFile))
}

function Get-FirstDoctorFailure($Result) {
    $f = @($Result.checks | Where-Object { $_.required -and $_.status -eq 'FAIL' } | Select-Object -First 1)
    if ($f.Count -eq 0) { return '' }
    return ("doctor: {0} - {1}. Fix: {2}" -f $f[0].name, $f[0].detail, $f[0].fix)
}

# ============================================================================
# App lifecycle
# ============================================================================
function Test-AppHealth($App) {
    $code = $null
    try {
        $r = Invoke-WebRequest -Uri $App.healthUrl -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        $code = [int]$r.StatusCode
    } catch {
        $resp = $null
        try { $resp = $_.Exception.Response } catch { $resp = $null }
        if ($null -eq $resp) { return $false }
        try { $code = [int]$resp.StatusCode } catch { $code = -1 }
    }
    if ($App.healthAccept -eq 'anyResponse') {
        return (Test-OwnerMatches (Get-PortOwner $App.port) $App.baseName)
    }
    return ($code -eq 200)
}

function Show-AppLogTail($App) {
    $log = Join-Path $script:LogDir ($App.name + '.log')
    $err = Join-Path $script:LogDir ($App.name + '.err.log')
    foreach ($f in @($log, $err)) {
        Write-Line ("--- last 20 lines of {0} ---" -f (Get-RelPath $f)) 'Yellow'
        foreach ($l in @(Get-FileTail $f 20)) { Write-Line ("  " + $l) }
    }
}

function Start-App($App) {
    $log = Join-Path $script:LogDir ($App.name + '.log')
    $err = Join-Path $script:LogDir ($App.name + '.err.log')
    $wd = Split-Path -Parent $App.project
    Write-Line ("[apps] {0} down; starting dotnet run --project {1}" -f $App.name, (Get-RelPath $App.project))
    $p = Start-Logged -ArgString ("run --project `"" + $App.project + "`"") -Log $log -ErrLog $err -WorkingDirectory $wd -NoWait
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $deadline = (Get-Date).AddSeconds($AppStartTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) {
            Write-Line ("[apps] FAIL {0} exited early (exit {1}) before becoming healthy at {2}" -f $App.name, $p.ExitCode, $App.healthUrl) 'Red'
            Show-AppLogTail $App
            Stop-Lifecycle 3 ("App '{0}' did not start (exited early); see {1}" -f $App.name, (Get-RelPath $err))
        }
        if (Test-AppHealth $App) {
            $owner = Get-PortOwner $App.port
            $ownerPid = 0
            if ($owner) { $ownerPid = $owner.Pid }
            Write-Line ("[apps] {0} up after {1}s ({2}, pid {3}) log={4}" -f $App.name, [int]$sw.Elapsed.TotalSeconds, $App.healthAccept, $ownerPid, (Get-RelPath $log)) 'Green'
            return $ownerPid
        }
        Start-Sleep -Seconds 2
    }
    & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
    Write-Line ("[apps] FAIL {0} not healthy at {1} after {2}s" -f $App.name, $App.healthUrl, $AppStartTimeoutSec) 'Red'
    Show-AppLogTail $App
    Stop-Lifecycle 3 ("App '{0}' did not become healthy within {1}s; see {2}" -f $App.name, $AppStartTimeoutSec, (Get-RelPath $err))
}

function Get-StaleSourceFile($App, $Owner) {
    if ($null -eq $Owner -or $null -eq $Owner.StartTime) { return $null }
    $projDir = Split-Path -Parent $App.project
    $start = $Owner.StartTime
    foreach ($f in @(Get-ChildItem -LiteralPath $projDir -Recurse -File -ErrorAction SilentlyContinue)) {
        $rel = $f.FullName.Substring($projDir.Length).TrimStart('\')
        if ($rel -match '(^|\\)(bin|obj)(\\|$)') { continue }
        if ($f.LastWriteTime -gt $start) { return $f.FullName }
    }
    return $null
}

function Restart-TargetApp($App, $Owner, [string]$Reason) {
    if ($Owner) {
        if (-not (Test-OwnerMatches $Owner $App.baseName)) {
            Stop-Lifecycle 3 ("Port {0} is held by {1} (pid {2}), not {3}. Fix: Stop-Process -Id {2}" -f $App.port, $Owner.Name, $Owner.Pid, $App.baseName)
        }
        Write-Line ("[apps] restarting {0} ({1}); stopping pid {2} ({3})" -f $App.name, $Reason, $Owner.Pid, $Owner.Name) 'Yellow'
        Stop-Process -Id $Owner.Pid -Force -ErrorAction SilentlyContinue
        if (-not (Wait-PortFree $App.port 20)) { Stop-Lifecycle 3 ("Port {0} still busy after stopping pid {1}" -f $App.port, $Owner.Pid) }
    }
    [void](Invoke-DotnetBuild $App.project 'target-build.log' ($App.name + ' app'))
    return (Start-App $App)
}

function Invoke-EnsureApps([string[]]$Only = @(), [switch]$SkipTarget) {
    # Ensures apps (array order) are healthy; starts/restarts as needed. Returns $true when the
    # Target app was (re)started by this call.
    $settings = $null
    try { $settings = Get-SettingsData } catch { Stop-Lifecycle 3 "appsettings.test.json does not parse: $($_.Exception.Message)" }
    $apps = @(Get-Apps $settings)
    $targetRestarted = $false
    $records = New-List
    foreach ($a in $apps) {
        if ($Only.Count -gt 0 -and $Only -notcontains $a.name) { continue }
        if ($SkipTarget -and $a.isTarget) { continue }
        $started = $false
        $ownerPid = 0
        if (Test-AppHealth $a) {
            $owner = Get-PortOwner $a.port
            if ($owner) { $ownerPid = $owner.Pid }
            $stale = $null
            if ($a.isTarget) { $stale = Get-StaleSourceFile $a $owner }
            if ($stale) {
                $ownerPid = Restart-TargetApp $a $owner ("stale: " + (Get-RelPath $stale) + " is newer than the running process")
                $started = $true
                $targetRestarted = $true
            } else {
                Write-Line ("[apps] {0} up ({1} {2}, pid {3})" -f $a.name, $a.healthAccept, $a.healthUrl, $ownerPid) 'Green'
            }
        } else {
            $owner = Get-PortOwner $a.port
            if ($owner -and -not (Test-OwnerMatches $owner $a.baseName)) {
                Stop-Lifecycle 3 ("Port {0} for {1} is held by {2} (pid {3}). Fix: Stop-Process -Id {3}, or change the port in appsettings.test.json" -f $a.port, $a.name, $owner.Name, $owner.Pid)
            }
            if ($owner -and $a.isTarget) {
                $ownerPid = Restart-TargetApp $a $owner 'unhealthy'
                $targetRestarted = $true
            } elseif ($owner) {
                Stop-Lifecycle 3 ("App '{0}' owns port {1} (pid {2}) but is not healthy at {3}. Fix: Stop-Process -Id {2} and re-run" -f $a.name, $a.port, $owner.Pid, $a.healthUrl)
            } else {
                $ownerPid = Start-App $a
                if ($a.isTarget) { $targetRestarted = $true }
            }
            $started = $true
        }
        [void]$records.Add([ordered]@{ name = $a.name; pid = $ownerPid; startedByRunner = $started; url = $a.healthUrl; healthAccept = $a.healthAccept })
    }
    if ($script:S) {
        $list = Get-List $script:S 'apps'
        foreach ($r in $records) {
            $existing = @($list | Where-Object { $_.name -eq $r.name })
            foreach ($e in $existing) {
                if ($r.startedByRunner -eq $false -and $e.startedByRunner -eq $true -and (Get-Int $e.pid) -eq $r.pid) { $r.startedByRunner = $true }
                $list.Remove($e)
            }
            [void]$list.Add($r)
        }
    }
    return $targetRestarted
}

function Restart-DataApps([string]$Reason) {
    # Restarts every Apps[] entry with resetData=true (fresh in-memory data), then health-checks the
    # other apps (which also re-opens their channels to the new service). Returns elapsed ms.
    $settings = $null
    try { $settings = Get-SettingsData } catch { Stop-Lifecycle 3 "appsettings.test.json does not parse: $($_.Exception.Message)" }
    $apps = @(Get-Apps $settings)
    $data = @($apps | Where-Object { $_.resetData })
    if ($data.Count -eq 0) {
        Write-Line 'WARN: no Apps[] entry has resetData=true; mutating modules run serialized without a data reset' 'Yellow'
        return [long]0
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    foreach ($a in $data) {
        $owner = Get-PortOwner $a.port
        if ($owner) {
            if (-not (Test-OwnerMatches $owner $a.baseName)) {
                Stop-Lifecycle 3 ("Port {0} is held by {1} (pid {2}), not {3}. Fix: Stop-Process -Id {2}" -f $a.port, $owner.Name, $owner.Pid, $a.baseName)
            }
            Stop-Process -Id $owner.Pid -Force -ErrorAction SilentlyContinue
            if (-not (Wait-PortFree $a.port 20)) { Stop-Lifecycle 3 ("Port {0} still busy after stopping pid {1}" -f $a.port, $owner.Pid) }
        }
        [void](Start-App $a)
    }
    $others = @($apps | Where-Object { -not $_.resetData } | ForEach-Object { $_.name })
    if ($others.Count -gt 0) { [void](Invoke-EnsureApps -Only $others) }
    $sw.Stop()
    $ms = [long]$sw.ElapsedMilliseconds
    $names = (@($data | ForEach-Object { $_.name }) -join ', ')
    Write-Line ("[apps] restart {0} (fresh data): {1} - {2}s" -f $names, $Reason, [Math]::Round($ms / 1000.0, 1).ToString([Globalization.CultureInfo]::InvariantCulture)) 'Cyan'
    if ($script:S) { Add-StatusEvent $script:S ("Restarted {0} (fresh data): {1} ({2} ms)" -f $names, $Reason, $ms) }
    if ($script:Ctx) {
        $list = Get-List $script:Ctx.RunEntry 'restarts'
        foreach ($a in $data) { [void]$list.Add([ordered]@{ app = $a.name; reason = $Reason; ms = $ms; at = (Get-NowIso) }) }
    }
    return $ms
}

function Test-AuthState([string]$FilePath) {
    if (-not $FilePath) { return $false }
    if (-not [IO.Path]::IsPathRooted($FilePath)) { $FilePath = Join-Path $TestProjectDir $FilePath }
    if (-not (Test-Path -LiteralPath $FilePath)) { return $false }
    $file = Get-Item -LiteralPath $FilePath
    $age = (Get-Date) - $file.LastWriteTime
    return ($age.TotalHours -lt 2 -and $file.Length -gt 100)
}

function Test-AuthRequired {
    try { $s = Get-SettingsData } catch { return $false }
    $login = Get-Nested $s @('Auth', 'LoginUrl')
    return ([bool]$login)
}

# ============================================================================
# status.json v2 model (single writer: this script)
# ============================================================================
function Get-StatusPath { return (Join-Path $script:PageDir 'status.json') }
function Get-TimingsPath { return (Join-Path $script:PageDir 'timings.json') }
function Get-PlanPath { return (Join-Path $script:PageDir 'state-map\plan.json') }

function New-SystemEntry([string]$X, [string]$Sys) {
    return [ordered]@{
        status = 'NOT_RUN'; stepsPass = 0; stepsFail = 0; stepsNotTested = 0
        assertionsTotal = 0; assertionsPassed = 0; assertionsFailed = 0
        executedAt = $null; durationMs = 0; abortReason = $null
        results = "runs/Module$X/$Sys/results.json"
    }
}

function New-ModuleEntry([string]$X, [string]$Name, [int]$PlannedSteps, $CssStates, [string]$RefTest, [string]$TgtTest, [string]$DataEffect = 'mutating') {
    $css = [System.Collections.ArrayList]::new()
    foreach ($c in @($CssStates)) { if ($null -ne $c -and [string]$c -ne '') { [void]$css.Add([string]$c) } }
    return [ordered]@{
        name = $Name
        plannedSteps = $PlannedSteps
        dataEffect = $DataEffect
        cssStates = $css
        tests = [ordered]@{ reference = $RefTest; target = $TgtTest }
        reference = (New-SystemEntry $X 'Reference')
        target = (New-SystemEntry $X 'Target')
        openFindings = 0; actionableCssDiffs = 0; actionableCssRows = 0; cssDiffsTotal = 0; notTested = 0; referenceDefects = 0
        harnessError = $null
        reports = [ordered]@{
            parity = "reports/Module${X}_Parity.md"; cssDiff = "reports/Module${X}_CSS_Diff.md"
            performance = "reports/Module${X}_Performance.md"; manualTests = "reports/Module${X}_ManualTests.md"
        }
        complete = $false
    }
}

function New-GateCleared($LastResponse = $null) {
    return [ordered]@{ pending = $false; name = $null; since = $null; question = $null; reportPath = $null; lastResponse = $LastResponse }
}

function New-StatusV2([string]$PageName) {
    return [ordered]@{
        schemaVersion = 2
        page = $PageName
        lifecycle = 'testing'
        state = 'planning'
        updatedAt = (Get-NowIso)
        session = $null
        paths = $null
        plan = [ordered]@{ signedOff = $false; signedOffAt = $null; signedOffRevision = $null; signedOffFingerprint = $null; fingerprint = $null; fingerprintRevision = $null; response = $null; planFile = 'state-map/plan.json'; modules = [System.Collections.ArrayList]::new() }
        gate = (New-GateCleared)
        currentRun = $null
        apps = [System.Collections.ArrayList]::new()
        modules = [ordered]@{}
        totals = [ordered]@{ openFindings = 0; actionableCssDiffs = 0 }
        fixLoop = [ordered]@{ round = 0; maxRounds = 2; clean = $false; history = [System.Collections.ArrayList]::new() }
        finalReport = [ordered]@{ path = 'reports/FinalReport.md'; signedOff = $false; signedOffAt = $null; response = $null }
        allModulesComplete = $false
        previousRun = $null
        progress = [ordered]@{ status = 'COMPLETE'; now = ''; next = ''; eta = ''; recentEvents = [System.Collections.ArrayList]::new() }
        events = [System.Collections.ArrayList]::new()
    }
}

function Get-DataEffectValue($Pm) {
    # 'read-only' | 'mutating' from a plan.json module entry; $null when missing or invalid.
    if ($null -eq $Pm) { return $null }
    $v = ([string]$Pm.dataEffect).Trim().ToLowerInvariant()
    if ($v -eq 'read-only' -or $v -eq 'mutating') { return $v }
    return $null
}

function Get-ModulePlan([string]$X) {
    # plan.json module entry for X (dictionary) or $null.
    $plan = Read-Plan
    if (-not $plan) { return $null }
    $pm = @(@(Get-Items $plan 'modules') | Where-Object { $_ -and ([string]$_.id).Trim().ToUpperInvariant() -eq $X } | Select-Object -First 1)
    if ($pm.Count -eq 0) { return $null }
    return $pm[0]
}

function Add-StructuredEvent($S, $Event) {
    # status.json 'events' array (read by the run-summary render, e.g. type 'render-failed').
    if (-not $Event.Contains('at')) { $Event.at = Get-NowIso }
    $list = Get-List $S 'events'
    [void]$list.Add($Event)
    while ($list.Count -gt 50) { $list.RemoveAt(0) }
}

function Read-Plan {
    $p = Get-PlanPath
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    try { return (Read-JsonFile $p) } catch { Stop-Lifecycle 1 "state-map/plan.json does not parse: $($_.Exception.Message)" }
}

function Get-FallbackTest([string]$X, [string]$Sys) {
    if ($Sys -eq 'Reference') { return ("Reference{0}_Mod{1}" -f $Page, $X) }
    return ("{0}_Mod{1}" -f $Page, $X)
}

function New-ModuleFromPlan($Pm) {
    $id = ([string]$Pm.id).Trim().ToUpperInvariant()
    $ref = [string]$Pm.referenceTest
    if (-not $ref) { $ref = Get-FallbackTest $id 'Reference' }
    $tgt = [string]$Pm.targetTest
    if (-not $tgt) { $tgt = Get-FallbackTest $id 'Target' }
    $effect = Get-DataEffectValue $Pm
    if (-not $effect) { $effect = 'mutating' }
    return (New-ModuleEntry $id ([string]$Pm.name) (Get-Int $Pm.plannedSteps) (Get-List $Pm 'cssStates') $ref $tgt $effect)
}

function Test-ModuleUnchanged($Old, $New) {
    if ($null -eq $Old) { return $false }
    if ((Get-Int $Old.plannedSteps) -ne (Get-Int $New.plannedSteps)) { return $false }
    if ([string](Get-Nested $Old @('tests', 'reference')) -ne [string]$New.tests.reference) { return $false }
    if ([string](Get-Nested $Old @('tests', 'target')) -ne [string]$New.tests.target) { return $false }
    if ((@(Get-Items $Old 'cssStates') -join '|') -ne (@($New.cssStates) -join '|')) { return $false }
    return $true
}

function Import-PlanModules($S, $Plan) {
    # Gate "unchanged module" rule: unchanged modules keep run data; changed ones reset; dropped ones go.
    $old = Get-Dict $S 'modules'
    $new = [ordered]@{}
    $ids = [System.Collections.ArrayList]::new()
    foreach ($pm in @(Get-Items $Plan 'modules')) {
        if ($null -eq $pm -or -not $pm.id) { continue }
        $entry = New-ModuleFromPlan $pm
        $id = ([string]$pm.id).Trim().ToUpperInvariant()
        if ($old.Contains($id) -and (Test-ModuleUnchanged $old[$id] $entry)) {
            $keep = $old[$id]
            $keep.name = $entry.name
            $keep.dataEffect = $entry.dataEffect
            $entry = $keep
        }
        $new[$id] = $entry
        [void]$ids.Add($id)
    }
    $S.modules = $new
    $S.plan.modules = $ids
}

function Get-FingerprintHash($Rows) {
    # SHA256 (hex) over the canonical module rows, sorted by id (ordinal).
    $lines = @($Rows | Sort-Object -Property @{ Expression = { $_.id } } -CaseSensitive | ForEach-Object {
        ('{0}|{1}|{2}|{3}|{4}|{5}' -f $_.id, $_.plannedSteps, $_.referenceTest, $_.targetTest, $_.cssStates, $_.stepIds)
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash($Utf8NoBom.GetBytes(($lines -join "`n"))) } finally { $sha.Dispose() }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-PlanFingerprint($Plan, [switch]$NoStepIds) {
    # Structural fingerprint of plan.json: module ids, plannedSteps, test names, cssStates, step ids.
    # Wording (names, checksText, notTested) is not part of it. -NoStepIds: comparable with status.json modules.
    $rows = [System.Collections.ArrayList]::new()
    foreach ($pm in @(Get-Items $Plan 'modules')) {
        if ($null -eq $pm -or -not $pm.id) { continue }
        $e = New-ModuleFromPlan $pm
        $stepIds = ''
        if (-not $NoStepIds) { $stepIds = (@(@(Get-Items $pm 'steps') | Where-Object { $_ } | ForEach-Object { [string]$_.id }) -join ',') }
        [void]$rows.Add(@{ id = ([string]$pm.id).Trim().ToUpperInvariant(); plannedSteps = $e.plannedSteps; referenceTest = $e.tests.reference; targetTest = $e.tests.target; cssStates = (@($e.cssStates) -join ','); stepIds = $stepIds })
    }
    return (Get-FingerprintHash $rows)
}

function Get-StatusModulesFingerprint($S) {
    # Fingerprint (without step ids) of the modules recorded in status.json; $null when there are none.
    $mods = Get-Dict $S 'modules'
    if ($mods.Count -eq 0) { return $null }
    $rows = [System.Collections.ArrayList]::new()
    foreach ($id in @($mods.Keys)) {
        $m = $mods[$id]
        [void]$rows.Add(@{ id = ([string]$id).Trim().ToUpperInvariant(); plannedSteps = (Get-Int $m.plannedSteps); referenceTest = [string](Get-Nested $m @('tests', 'reference')); targetTest = [string](Get-Nested $m @('tests', 'target')); cssStates = (@(Get-Items $m 'cssStates') -join ','); stepIds = '' })
    }
    return (Get-FingerprintHash $rows)
}

function Update-PlanRevision($S, $Plan) {
    # Deterministic plan revision: when the structure changed but plan.json kept its revision, the runner
    # bumps it (recorded revision + 1) and rewrites plan.json. Records plan.fingerprint/fingerprintRevision.
    $fp = Get-PlanFingerprint $Plan
    $changed = $false
    $prevFp = [string]$S.plan.fingerprint
    if ($prevFp) { $changed = ($prevFp -ne $fp) }
    else {
        $statusFp = Get-StatusModulesFingerprint $S
        if ($statusFp) { $changed = ($statusFp -ne (Get-PlanFingerprint $Plan -NoStepIds)) }
    }
    $prevRev = $S.plan.fingerprintRevision
    if ($null -eq $prevRev) { $prevRev = $S.plan.signedOffRevision }
    $prevRev = Get-Int $prevRev
    $rev = Get-Int $Plan.revision
    if ($changed -and $rev -le $prevRev) {
        $rev = $prevRev + 1
        $Plan.revision = $rev
        if (-not (Write-AtomicFile (Get-PlanPath) (ConvertTo-Json -InputObject $Plan -Depth 30))) { Stop-Lifecycle 1 'could not rewrite state-map/plan.json with the bumped revision' }
        Add-StatusEvent $S "plan revision bumped to $rev by runner (fingerprint changed)"
    }
    $S.plan.fingerprint = $fp
    $S.plan.fingerprintRevision = $rev
    return $rev
}

function Repair-StatusV2($S) {
    $def = New-StatusV2 ([string]$S.page)
    foreach ($k in @($def.Keys)) { if (-not $S.Contains($k)) { $S[$k] = $def[$k] } }
    foreach ($k in @('plan', 'gate', 'totals', 'fixLoop', 'finalReport', 'progress')) {
        if (-not ($S[$k] -is [System.Collections.IDictionary])) { $S[$k] = $def[$k] }
        foreach ($kk in @($def[$k].Keys)) { if (-not $S[$k].Contains($kk)) { $S[$k][$kk] = $def[$k][$kk] } }
    }
    [void](Get-List $S.plan 'modules')
    [void](Get-List $S.fixLoop 'history')
    [void](Get-List $S.progress 'recentEvents')
    [void](Get-List $S 'apps')
    [void](Get-List $S 'events')
    $mods = Get-Dict $S 'modules'
    foreach ($id in @($mods.Keys)) {
        $m = $mods[$id]
        if (-not ($m -is [System.Collections.IDictionary])) { $m = [ordered]@{}; $mods[$id] = $m }
        $d = New-ModuleEntry $id '' 0 @() (Get-FallbackTest $id 'Reference') (Get-FallbackTest $id 'Target')
        foreach ($k in @($d.Keys)) {
            if (-not $m.Contains($k)) { $m[$k] = $d[$k] }
            elseif ($null -eq $m[$k] -and $k -ne 'harnessError') { $m[$k] = $d[$k] }
        }
        foreach ($sys in @('reference', 'target')) {
            foreach ($k in @($d[$sys].Keys)) { if (-not $m[$sys].Contains($k)) { $m[$sys][$k] = $d[$sys][$k] } }
        }
        foreach ($k in @('reference', 'target')) { if (-not $m.tests[$k]) { $m.tests[$k] = $d.tests[$k] } }
        [void](Get-List $m 'cssStates')
    }
}

function Convert-StatusV1($Old) {
    $new = New-StatusV2 $Page
    $new.previousRun = [ordered]@{ kind = 'v1-migration'; archivedAt = (Get-NowIso); path = $null; data = $Old }
    $plan = Read-Plan
    if ($plan) { Import-PlanModules $new $plan }
    else {
        $oldMods = $Old['modules']
        if ($oldMods -is [System.Collections.IDictionary]) {
            foreach ($id in @($oldMods.Keys)) {
                $om = $oldMods[$id]
                $X = ([string]$id).ToUpperInvariant()
                $new.modules[$X] = New-ModuleEntry $X ([string]$om.name) (Get-Int $om.steps) @() (Get-FallbackTest $X 'Reference') (Get-FallbackTest $X 'Target')
                [void]$new.plan.modules.Add($X)
            }
        }
    }
    Add-StatusEvent $new 'status.json migrated from schema v1 (old content kept under previousRun.data)'
    return $new
}

function Read-Status([switch]$Create) {
    $path = Get-StatusPath
    $s = $null
    if (Test-Path -LiteralPath $path) {
        try { $s = Read-JsonFile $path } catch { Stop-Lifecycle 1 "status.json does not parse: $($_.Exception.Message)" }
    }
    if ($null -eq $s) {
        if (-not $Create) { return $null }
        $s = New-StatusV2 $Page
        $plan = Read-Plan
        if ($plan) { Import-PlanModules $s $plan }
        return $s
    }
    if ((Get-Int $s.schemaVersion) -ne 2) {
        if (-not $Create) { return $s }
        return (Convert-StatusV1 $s)
    }
    Repair-StatusV2 $s
    # Fix budget is 2 rounds (sessions created earlier carry 3).
    if ((Get-Int $s.fixLoop.maxRounds) -gt 2) { $s.fixLoop.maxRounds = 2 }
    if ($Create) { Sync-PlanDataEffects $s }
    return $s
}

function Sync-PlanDataEffects($S) {
    # dataEffect is not part of the 'unchanged module' rule: refresh it in place from plan.json.
    $plan = $null
    try { $plan = Read-JsonFile (Get-PlanPath) } catch { return }
    if (-not $plan) { return }
    foreach ($pm in @(Get-Items $plan 'modules')) {
        if ($null -eq $pm -or -not $pm.id) { continue }
        $id = ([string]$pm.id).Trim().ToUpperInvariant()
        if (-not $S.modules.Contains($id)) { continue }
        $eff = Get-DataEffectValue $pm
        if (-not $eff) { $eff = 'mutating' }
        $S.modules[$id].dataEffect = $eff
    }
}

function Get-PlannedModuleIds($S) {
    $ids = @(Get-Items $S.plan 'modules' | Where-Object { $_ })
    if ($ids.Count -eq 0) { $ids = @($S.modules.Keys) }
    return $ids
}

function Get-FixLoopClean($S) {
    # Pure: true when no open findings / actionable CSS diffs on any planned module AND every planned
    # module's Target has run AND no planned module carries a harness error.
    $ids = @(Get-PlannedModuleIds $S)
    if ($ids.Count -eq 0) { return $false }
    $of = 0
    $ac = 0
    foreach ($id in $ids) {
        $m = $S.modules[$id]
        if ($null -eq $m) { return $false }
        $of += Get-Int $m.openFindings
        # Owned CSS groups plus raw rows: rows that belong to another module's group still keep the page unclean.
        $ac += (Get-Int $m.actionableCssDiffs) + (Get-Int $m.actionableCssRows)
        if ((Get-Nested $m @('target', 'status')) -eq 'NOT_RUN' -or $null -eq (Get-Nested $m @('target', 'status'))) { return $false }
        if ($m.harnessError) { return $false }
    }
    return ($of -eq 0 -and $ac -eq 0)
}

function Update-Derived($S) {
    $ids = @(Get-PlannedModuleIds $S)
    $of = 0
    $ac = 0
    foreach ($id in @($S.modules.Keys)) {
        $m = $S.modules[$id]
        # CSS: owned groups AND raw rows (a module whose rows sit in another module's group is not complete).
        $m.complete = (($m.reference.status -eq 'PASS') -and ($m.target.status -ne 'NOT_RUN') -and ((Get-Int $m.openFindings) -eq 0) -and ((Get-Int $m.actionableCssDiffs) -eq 0) -and ((Get-Int $m.actionableCssRows) -eq 0))
    }
    $allDone = ($ids.Count -gt 0)
    foreach ($id in $ids) {
        $m = $S.modules[$id]
        if ($null -eq $m) { $allDone = $false; continue }
        $of += Get-Int $m.openFindings
        $ac += Get-Int $m.actionableCssDiffs
        if (-not $m.complete) { $allDone = $false }
    }
    $S.totals.openFindings = $of
    $S.totals.actionableCssDiffs = $ac
    $S.allModulesComplete = $allDone
    $S.fixLoop.clean = (Get-FixLoopClean $S)
}

function Save-Status([switch]$NonFatal) {
    $s = $script:S
    if ($null -eq $s) { return }
    $s.updatedAt = Get-NowIso
    Update-Derived $s
    $ok = Write-AtomicFile (Get-StatusPath) (ConvertTo-JsonText $s)
    if (-not $ok -and -not $NonFatal) { Stop-Lifecycle 1 'status.json write failed' }
}

function Add-StatusEvent($S, [string]$Text) {
    $list = Get-List (Get-Dict $S 'progress') 'recentEvents'
    [void]$list.Add(((Get-Date).ToString('HH:mm:ss') + ' ' + (Limit-Text $Text 220)))
    while ($list.Count -gt 50) { $list.RemoveAt(0) }
}

function Set-ProgressState([string]$Status, [string]$Now, [string]$Next = $null, [string]$Eta = $null) {
    $p = Get-Dict $script:S 'progress'
    if ($Status) { $p.status = $Status }
    if ($null -ne $Now) { $p.now = $Now }
    if ($null -ne $Next) { $p.next = $Next }
    if ($null -ne $Eta) { $p.eta = $Eta }
}

# ============================================================================
# timings.json
# ============================================================================
function Read-Timings([switch]$Quiet) {
    $t = $null
    try { $t = Read-JsonFile (Get-TimingsPath) } catch {
        if (-not $Quiet) { Write-Line "WARN: timings.json unreadable ($($_.Exception.Message)); starting a new one" 'Yellow' }
        $t = $null
    }
    if ($null -eq $t) { $t = [ordered]@{ schemaVersion = 2; page = $Page; latest = [ordered]@{}; runs = [System.Collections.ArrayList]::new(); lifecycles = [System.Collections.ArrayList]::new() } }
    $t.schemaVersion = 2
    [void](Get-Dict $t 'latest')
    [void](Get-List $t 'runs')
    [void](Get-List $t 'lifecycles')
    return $t
}

function Write-TimingsFile($T) {
    $runs = Get-List $T 'runs'
    while ($runs.Count -gt 10) { $runs.RemoveAt(0) }
    $lcs = Get-List $T 'lifecycles'
    while ($lcs.Count -gt 10) { $lcs.RemoveAt(0) }
    foreach ($lc in $lcs) { Update-LifecycleActiveMs $lc }
    return (Write-AtomicFile (Get-TimingsPath) (ConvertTo-JsonText $T))
}

function Save-Timings {
    # Run-context writer (the page lock is held). Same schema as Update-TimingsLifecycle.
    if (-not $script:Ctx -or -not $script:Ctx.Timings) { return }
    [void](Write-TimingsFile $script:Ctx.Timings)
}

function Update-TimingsLifecycle([scriptblock]$Mutate) {
    # Timings writer for actions without a run context (gate, progress, fix-round). Caller holds the page lock.
    $t = Read-Timings
    & $Mutate $t
    [void](Write-TimingsFile $t)
}

# ---- lifecycles[] (design section 4) ----
function New-Lifecycle($T) {
    $phases = [System.Collections.ArrayList]::new()
    for ($k = 0; $k -lt $PhaseNames.Count; $k++) {
        [void]$phases.Add([ordered]@{ id = $k; name = $PhaseNames[$k]; budgetMs = $PhaseBudgetMs[$k]; status = 'pending'; segments = [System.Collections.ArrayList]::new(); activeMs = 0 })
    }
    $lc = [ordered]@{
        id = (Get-Date -Format 'yyyyMMdd-HHmmss'); page = $Page
        startedAt = (Get-NowIso); finishedAt = $null; totalBudgetMs = $TotalBudgetMs
        phases = $phases
        gates = [System.Collections.ArrayList]::new()
        postGate = [System.Collections.ArrayList]::new()
    }
    $lcs = Get-List $T 'lifecycles'
    [void]$lcs.Add($lc)
    while ($lcs.Count -gt 10) { $lcs.RemoveAt(0) }
    return $lc
}

function Get-LatestLifecycle($T) {
    if ($null -eq $T) { return $null }
    $lcs = @(Get-Items $T 'lifecycles')
    if ($lcs.Count -eq 0) { return $null }
    return $lcs[$lcs.Count - 1]
}

function Get-LifecyclePhase($Lc, [int]$K) {
    foreach ($p in @(Get-Items $Lc 'phases')) { if ((Get-Int $p.id) -eq $K) { return $p } }
    $p = [ordered]@{ id = $K; name = $PhaseNames[$K]; budgetMs = $PhaseBudgetMs[$K]; status = 'pending'; segments = [System.Collections.ArrayList]::new(); activeMs = 0 }
    [void](Get-List $Lc 'phases').Add($p)
    return $p
}

function Get-OpenSegment($Entry) {
    foreach ($s in @(Get-Items $Entry 'segments')) { if ($s -and -not $s.end) { return $s } }
    return $null
}

function Get-SegmentsMs($Segments, [datetime]$OpenEnd) {
    $ms = 0.0
    foreach ($s in @($Segments)) {
        if ($null -eq $s) { continue }
        $a = ConvertTo-DateSafe $s.start
        if ($null -eq $a) { continue }
        $b = ConvertTo-DateSafe $s.end
        if ($null -eq $b) { $b = $OpenEnd }
        if ($b -gt $a) { $ms += ($b - $a).TotalMilliseconds }
    }
    return [long]$ms
}

function Update-LifecycleActiveMs($Lc) {
    $end = ConvertTo-DateSafe $Lc.finishedAt
    if ($null -eq $end) { $end = Get-Date }
    foreach ($p in @(Get-Items $Lc 'phases')) { $p.activeMs = Get-SegmentsMs @(Get-Items $p 'segments') $end }
}

function Close-Phase($Lc, [int]$K) {
    if ($null -eq $Lc) { return }
    $p = Get-LifecyclePhase $Lc $K
    $seg = Get-OpenSegment $p
    if ($seg) { $seg.end = Get-NowIso; $p.status = 'done' }
    Update-LifecycleActiveMs $Lc
}

function Open-Phase($T, [int]$K, [string]$PostGateAction = '') {
    # Idempotent for a running phase. Closes every other open segment; earlier pending phases become
    # 'skipped', phases with segments 'done'. No lifecycle -> one is created (earlier phases
    # 'not-measured'). After finishedAt the time goes to postGate[] instead.
    $lc = Get-LatestLifecycle $T
    if ($null -eq $lc) {
        $lc = New-Lifecycle $T
        for ($j = 0; $j -lt $K; $j++) { (Get-LifecyclePhase $lc $j).status = 'not-measured' }
    }
    if ($lc.finishedAt) {
        $action = $PostGateAction
        if (-not $action) { $action = "phase $($K + 1) ($($PhaseNames[$K]))" }
        [void](Get-List $lc 'postGate').Add([ordered]@{ action = $action; start = (Get-NowIso); end = $null })
        return $lc
    }
    $p = Get-LifecyclePhase $lc $K
    if (Get-OpenSegment $p) { return $lc }
    $now = Get-NowIso
    foreach ($o in @(Get-Items $lc 'phases')) {
        $j = Get-Int $o.id
        if ($j -eq $K) { continue }
        $seg = Get-OpenSegment $o
        if ($seg) { $seg.end = $now }
        $hasSegs = (@(Get-Items $o 'segments').Count -gt 0)
        if ($hasSegs) { $o.status = 'done' }
        elseif ($j -lt $K -and $o.status -eq 'pending') { $o.status = 'skipped' }
    }
    [void](Get-List $p 'segments').Add([ordered]@{ start = $now; end = $null })
    $p.status = 'running'
    Update-LifecycleActiveMs $lc
    return $lc
}

function Close-GateEntry($Lc, $G, [string]$At) {
    $G.confirmedAt = $At
    $start = ConvertTo-DateSafe $Lc.startedAt
    $end = ConvertTo-DateSafe $Lc.finishedAt
    $now = ConvertTo-DateSafe $At
    if ($null -eq $end) { $end = $now }
    $G.waitMs = Get-GateMs (ConvertTo-DateSafe $G.pendingAt) $now $start $end $now
}

function Get-GateMs($PendingAt, $ConfirmedAt, $StartedAt, $End, $Now) {
    # max(0, min(confirmedAt ?? now, end) - max(pendingAt, startedAt)) - same formula as LifecycleTimings.GateMs.
    if ($null -eq $PendingAt) { return [long]0 }
    $to = $ConfirmedAt
    if ($null -eq $to) { $to = $Now }
    if ($to -gt $End) { $to = $End }
    $from = $PendingAt
    if ($StartedAt -and $StartedAt -gt $from) { $from = $StartedAt }
    return [long][Math]::Max(0, ($to - $from).TotalMilliseconds)
}

function Close-OpenGates($Lc, [string]$Name) {
    $closed = 0
    $now = Get-NowIso
    foreach ($g in @(Get-Items $Lc 'gates')) {
        if ($g.name -eq $Name -and -not $g.confirmedAt) { Close-GateEntry $Lc $g $now; $closed++ }
    }
    return $closed
}

function Close-PostGate($Lc) {
    foreach ($e in @(Get-Items $Lc 'postGate')) { if ($e -and -not $e.end) { $e.end = Get-NowIso } }
}

function Invoke-GatePhaseHooks($T, [string]$GateName, [string]$GateState) {
    # Gate hooks on lifecycles[] (design 3.7 / 4 + review N2). Without a lifecycle only the
    # phase-opening hooks (which create one) record anything.
    $lc = Get-LatestLifecycle $T
    $now = Get-NowIso
    if ($GateState -eq 'pending') {
        if ($null -eq $lc) { return }
        [void](Close-OpenGates $lc $GateName)
        [void](Get-List $lc 'gates').Add([ordered]@{ name = $GateName; pendingAt = $now; confirmedAt = $null; waitMs = 0 })
        switch ($GateName) {
            'plan_signoff' { Close-Phase $lc 0 }
            'baseline_failed' { Close-Phase $lc 2 }
            'final_signoff' {
                Close-Phase $lc 4
                if (-not $lc.finishedAt) { $lc.finishedAt = $now }
                Close-PostGate $lc
            }
        }
        return
    }
    # confirmed
    if ($GateName -eq 'plan_signoff') { $lc = Open-Phase $T 1 }
    elseif ($GateName -eq 'baseline_failed') { $lc = Open-Phase $T 2 }
    if ($null -eq $lc) { return }
    if ((Close-OpenGates $lc $GateName) -eq 0) {
        [void](Get-List $lc 'gates').Add([ordered]@{ name = $GateName; pendingAt = $now; confirmedAt = $now; waitMs = 0 })
    }
    if ($GateName -eq 'final_signoff') { Close-PostGate $lc }
}

function Get-PhaseEstimateMs($T, $Latest, [int]$K, $BudgetMs = $null) {
    # Same formula as LifecycleTimings.Estimate: only FINISHED previous lifecycles count (an unfinished
    # one may hold a stale multi-hour segment). Most recent finished one where phase K is done:
    # min(measured, budget); skipped: 0; otherwise the budget.
    $budget = if ($null -ne $BudgetMs -and [long]$BudgetMs -gt 0) { [long]$BudgetMs } else { [long]$PhaseBudgetMs[$K] }
    $lcs = @(Get-Items $T 'lifecycles')
    for ($i = $lcs.Count - 2; $i -ge 0; $i--) {
        $lc = $lcs[$i]
        $end = ConvertTo-DateSafe $lc.finishedAt
        if ($null -eq $end) { continue }
        foreach ($p in @(Get-Items $lc 'phases')) {
            if ((Get-Int $p.id) -ne $K) { continue }
            if ($p.status -eq 'skipped') { return [long]0 }
            if ($p.status -eq 'done') {
                $segs = @(Get-Items $p 'segments')
                $measured = if ($segs.Count -eq 0) { [long](Get-Int $p.activeMs) } else { [long](Get-SegmentsMs $segs $end) }
                return [long][Math]::Min($measured, $budget)
            }
        }
    }
    return $budget
}

function Get-LifecycleStats($T) {
    # Pure: the values behind the phase line and the phase table (same formulas as LifecycleTimings.cs).
    $lc = Get-LatestLifecycle $T
    if ($null -eq $lc) { return @{ Has = $false } }
    $now = Get-Date
    $finished = ConvertTo-DateSafe $lc.finishedAt
    $end = $finished
    if ($null -eq $end) { $end = $now }
    $start = ConvertTo-DateSafe $lc.startedAt
    if ($null -eq $start) { $start = $end }
    $phases = @()
    for ($k = 0; $k -lt $PhaseNames.Count; $k++) {
        $p = $null
        foreach ($x in @(Get-Items $lc 'phases')) { if ((Get-Int $x.id) -eq $k) { $p = $x } }
        $status = 'pending'
        $budget = [long]$PhaseBudgetMs[$k]
        $active = [long]0
        if ($p) {
            if ($p.status) { $status = [string]$p.status }
            if ((Get-Int $p.budgetMs) -gt 0) { $budget = [long](Get-Int $p.budgetMs) }
            $active = Get-SegmentsMs @(Get-Items $p 'segments') $end
        }
        $phases += , @{ Id = $k; Name = $PhaseNames[$k]; Status = $status; BudgetMs = $budget; ActiveMs = $active; EstimateMs = (Get-PhaseEstimateMs $T $lc $k $budget) }
    }
    $gate = [long]0
    $pending = $false
    foreach ($g in @(Get-Items $lc 'gates')) {
        $pa = ConvertTo-DateSafe $g.pendingAt
        if ($null -eq $pa) { continue }
        $ca = ConvertTo-DateSafe $g.confirmedAt
        if ($null -eq $ca) { $pending = $true }
        $gate += Get-GateMs $pa $ca $start $end $now
    }
    $elapsed = [long][Math]::Max(0, ($end - $start).TotalMilliseconds - $gate)
    $sumActive = [long]0
    foreach ($p in $phases) { $sumActive += $p.ActiveMs }
    $cur = -1
    foreach ($p in $phases) { if ($p.Status -eq 'running' -and $p.Id -gt $cur) { $cur = $p.Id } }
    if ($cur -lt 0) { foreach ($p in $phases) { if ($p.Status -eq 'done' -and $p.Id -gt $cur) { $cur = $p.Id } } }
    $budgetTotal = [long](Get-Int $lc.totalBudgetMs)
    if ($budgetTotal -le 0) { $budgetTotal = [long]$TotalBudgetMs }
    return @{
        Has = $true; Lifecycle = $lc; Phases = $phases; GateMs = $gate; GatePending = $pending
        ElapsedMs = $elapsed; OrchestrationMs = [long][Math]::Max(0, $elapsed - $sumActive)
        Current = $cur; Finished = ($null -ne $finished); TotalBudgetMs = $budgetTotal
    }
}

function Get-PhaseLine($T, $Phase2RemainingMs = $null) {
    # 'Phase {k+1} of 5 ({name}) - elapsed mm:ss - estimated remaining mm:ss[ - waiting for you]'
    $st = Get-LifecycleStats $T
    if (-not $st.Has) { return $NoLifecycleLine }
    $k = [Math]::Max(0, $st.Current)
    $rem = [long]0
    if (-not $st.Finished) {
        $p = $st.Phases[$k]
        if ($k -eq 2 -and $null -ne $Phase2RemainingMs) { $rem = [long]$Phase2RemainingMs }
        else { $rem = [long][Math]::Max(0, $p.EstimateMs - $p.ActiveMs) }
        for ($j = $k + 1; $j -lt $st.Phases.Count; $j++) {
            $s = $st.Phases[$j].Status
            if ($s -ne 'done' -and $s -ne 'skipped') { $rem += $st.Phases[$j].EstimateMs }
        }
        # Cap: what is left of the total budget, or the total budget once it is exceeded.
        $cap = [long]$st.TotalBudgetMs - [long]$st.ElapsedMs
        if ($cap -le 0) { $cap = [long]$st.TotalBudgetMs }
        $rem = [long][Math]::Min($rem, $cap)
    }
    $line = ("Phase {0} of {1} ({2}) - elapsed {3} - estimated remaining {4}" -f ($k + 1), $PhaseNames.Count, $st.Phases[$k].Name, (Format-MinSec $st.ElapsedMs), (Format-MinSec $rem))
    if ($st.GatePending) { $line += ' - waiting for you' }
    return $line
}

function Get-PhaseTableLines($T) {
    # Same rows as the 'final' view phase table (FinalReportBuilder).
    $lines = @('| Phase | Target | Measured | Status |', '|---|---|---|---|')
    $st = Get-LifecycleStats $T
    for ($k = 0; $k -lt $PhaseNames.Count; $k++) {
        $label = "Phase {0} ({1})" -f ($k + 1), $PhaseNames[$k]
        if (-not $st.Has) { $lines += ("| {0} | {1} | not-measured | not-measured |" -f $label, (Format-MinSec $PhaseBudgetMs[$k])); continue }
        $p = $st.Phases[$k]
        $measured = if ($p.Status -eq 'not-measured') { 'not-measured' } else { Format-MinSec $p.ActiveMs }
        $status = $p.Status
        if ($p.ActiveMs -gt $p.BudgetMs) { $status += ', OVER' }
        $lines += ("| {0} | {1} | {2} | {3} |" -f $label, (Format-MinSec $p.BudgetMs), $measured, $status)
    }
    if ($st.Has) {
        $lines += ("| Orchestration (between phases) | - | {0} | - |" -f (Format-MinSec $st.OrchestrationMs))
        $within = if ($st.ElapsedMs -le $st.TotalBudgetMs) { 'WITHIN TARGET' } else { 'OVER TARGET' }
        $lines += ("| Total | {0} | {1} | {2} |" -f (Format-MinSec $st.TotalBudgetMs), (Format-MinSec $st.ElapsedMs), $within)
    } else {
        $lines += '| Orchestration (between phases) | - | not measured | not-measured |'
        $lines += ("| Total | {0} | not measured | not-measured |" -f (Format-MinSec $TotalBudgetMs))
    }
    return $lines
}

function Get-DisplayTimings {
    # In-memory timings during a run (fresh), else timings.json from disk.
    if ($script:Ctx -and $script:Ctx.Timings) { return $script:Ctx.Timings }
    if (-not $script:PageDir) { return $null }
    return (Read-Timings -Quiet)
}

function Get-CurrentPhaseLine {
    $t = Get-DisplayTimings
    if ($null -eq $t) { return $NoLifecycleLine }
    $eta = $null
    if ($script:Ctx -and $script:Ctx.Jobs -and $script:Ctx.Jobs.Count -gt 0 -and $script:Ctx.Action -eq 'run-page') { $eta = Get-ParallelEta }
    return (Get-PhaseLine $t $eta)
}

function Get-HistSteps($Timings, [string]$X, [string]$Sys) {
    $steps = Get-Nested $Timings @('latest', $X, $Sys, 'steps')
    if ($null -eq $steps) { return @() }
    return @($steps)
}

function Get-ExpectedStepCount($Timings, $ModulesDict, [string]$X, [string]$Sys) {
    if ($Sys -eq 'Target') {
        $ref = @(Get-HistSteps $Timings $X 'Reference')
        if ($ref.Count -gt 0) { return $ref.Count }
    }
    $m = $null
    if ($ModulesDict) { $m = $ModulesDict[$X] }
    if ($m) { return (Get-Int $m.plannedSteps) }
    return 0
}


# ============================================================================
# Run context, live progress, event tail
# ============================================================================
function New-RunContext([string]$RunAction, [string]$RunId) {
    $runsDir = Join-Path $script:PageDir 'runs'
    foreach ($d in @($runsDir, $script:LogDir)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null } }
    $script:RunLog = Join-Path $script:LogDir ("run-$RunId.log")
    $timings = Read-Timings
    $sessionId = $null
    if ($script:S -and $script:S.session) { $sessionId = $script:S.session.id }
    $runEntry = [ordered]@{
        runId = $RunId; sessionId = $sessionId; action = $RunAction; modules = [System.Collections.ArrayList]::new()
        startedAt = (Get-NowIso); finishedAt = $null; exitCode = $null
        preflightMs = 0; buildMs = 0; reportMs = 0; maxParallel = $MaxParallel
        tests = [System.Collections.ArrayList]::new(); restarts = [System.Collections.ArrayList]::new()
    }
    [void](Get-List $timings 'runs').Add($runEntry)
    $script:Ctx = @{
        RunId = $RunId; Action = $RunAction
        Timings = $timings; RunEntry = $runEntry
        Jobs = [ordered]@{}; Notified = @{}; Measured = [System.Collections.ArrayList]::new()
        RunModules = @(); StartedAt = (Get-Date)
    }
    $script:S.currentRun = [ordered]@{
        runId = $RunId; action = $RunAction; module = $null; system = $null; step = $null
        stepIndex = 0; stepTotal = 0; stepTotalEstimated = $false; startedAt = (Get-NowIso)
        jobs = [System.Collections.ArrayList]::new()
    }
    return $RunId
}

# ---- job model (design 3.2): one job = one 'dotnet test' process for (module, system) ----
function New-TestJob([string]$X, [string]$Sys, [string]$Batch) {
    $c = $script:Ctx
    $s = $script:S
    $m = $s.modules[$X]
    $test = [string]$m.tests[$Sys.ToLowerInvariant()]
    $h = @(Get-HistSteps $c.Timings $X $Sys)
    $total = $h.Count
    $est = $false
    if ($total -eq 0) { $total = Get-ExpectedStepCount $c.Timings $s.modules $X $Sys; $est = $true }
    $evPath = Join-Path $script:PageDir ("runs\Module$X\$Sys\events-" + $c.RunId + ".jsonl")
    $job = @{
        key = "$X/$Sys"; module = $X; system = $Sys; test = $test; batch = $Batch; state = 'pending'
        proc = $null; startedAt = $null; deadline = $null; sw = $null
        events = @{ Path = $evPath; Offset = [long]0; Leftover = (New-Object byte[] 0) }
        done = 0; index = 0; total = $total; estimated = $est; title = ''; titles = @{}
        steps = [System.Collections.ArrayList]::new(); wallMs = [long]0; result = $null
    }
    $c.Jobs[$job.key] = $job
    return $job
}

function Get-ScreenshotPolicy {
    # -Screenshots, else env LIFECYCLE_SCREENSHOTS (already set by the caller), else the default.
    if ($Screenshots) { return $Screenshots }
    $envPolicy = [string]$env:LIFECYCLE_SCREENSHOTS
    if ($envPolicy -in @('all', 'failed', 'none')) { return $envPolicy }
    return $DefaultScreenshots
}

function Get-ModuleJobs([string]$X) {
    return @($script:Ctx.Jobs.Values | Where-Object { $_.module -eq $X })
}

function Test-ModuleJobsDone([string]$X) {
    foreach ($j in @(Get-ModuleJobs $X)) { if ($j.state -ne 'done' -and $j.state -ne 'skipped') { return $false } }
    return $true
}

function Start-TestJob($Job) {
    $c = $script:Ctx
    $s = $script:S
    $X = $Job.module
    $Sys = $Job.system
    $sysDir = Join-Path $script:PageDir ("runs\Module$X\$Sys")
    $resFile = Join-Path $sysDir 'results.json'
    if (-not (Test-Path -LiteralPath $sysDir)) { New-Item -ItemType Directory -Path $sysDir -Force | Out-Null }
    if (Test-Path -LiteralPath $resFile) { Remove-Item -LiteralPath $resFile -Force }
    foreach ($sub in @('css', 'screenshots')) {
        $subDir = Join-Path $sysDir $sub
        if (Test-Path -LiteralPath $subDir) { Remove-Item -LiteralPath $subDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    # P8: last planned step id (always captured under the 'failed' screenshot policy).
    $lastStep = ''
    $pm = Get-ModulePlan $X
    if ($pm) { $pmSteps = @(Get-Items $pm 'steps'); if ($pmSteps.Count -gt 0) { $lastStep = [string]$pmSteps[$pmSteps.Count - 1].id } }
    if ($Sys -eq 'Reference') {
        # A new Reference baseline starts a new comparison: fix-round snapshots of the old one are stale.
        Get-ChildItem -LiteralPath (Join-Path $script:PageDir "runs\Module$X") -Filter 'summary.before-round*.json' -File -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    }
    Write-Line ("[{0}/{1}] start {2} (expected steps: {3}{4}; {5})" -f $X, $Sys, $Job.test, $(if ($Job.estimated) { '~' } else { '' }), $Job.total, $Job.batch)
    Add-StatusEvent $s ("Module {0} {1} started ({2}, {3})" -f $X, $Sys, $Job.test, $Job.batch)
    $log = Join-Path $script:LogDir ("test-$X-$Sys.log")
    $err = Join-Path $script:LogDir ("test-$X-$Sys.err.log")
    $vars = @{
        APP_TEST_MODE = 'validate'; LIFECYCLE_PAGE_DIR = $script:PageDir; LIFECYCLE_RUN_ID = $c.RunId
        LIFECYCLE_EVENTS_FILE = $Job.events.Path; LIFECYCLE_TEST_ROOT = $TestProjectDir; LIFECYCLE_SKIP_BROWSER_INSTALL = '1'
        LIFECYCLE_SCREENSHOTS = (Get-ScreenshotPolicy); LIFECYCLE_LAST_STEP = $lastStep
    }
    $saved = Set-TestEnv $vars
    try {
        $argString = "test `"" + (Get-ModuleProject $X) + "`" --no-build --filter `"Name=" + $Job.test + "`" -- NUnit.NumberOfTestWorkers=1"
        $p = Start-Logged -NoWait -WorkingDirectory (Get-ModuleDir $X) -ArgString $argString -Log $log -ErrLog $err
    } finally {
        Restore-TestEnv $saved
    }
    $Job.proc = $p
    $Job.state = 'running'
    $Job.startedAt = Get-NowIso
    $Job.deadline = (Get-Date).AddSeconds($TestTimeoutSec)
    $Job.sw = [Diagnostics.Stopwatch]::StartNew()
}

function Complete-TestJob($Job, [bool]$TimedOut) {
    $c = $script:Ctx
    $s = $script:S
    $X = $Job.module
    $Sys = $Job.system
    $m = $s.modules[$X]
    if ($Job.sw) { $Job.sw.Stop() }
    $wall = [long]0
    if ($Job.sw) { $wall = [long]$Job.sw.ElapsedMilliseconds }
    Read-Events $Job
    $resFile = Join-Path $script:PageDir ("runs\Module$X\$Sys\results.json")
    $res = $null
    $reason = $null
    if (Test-Path -LiteralPath $resFile) {
        try { $res = Read-JsonFile $resFile } catch { $res = $null; $reason = "unreadable results.json: $($_.Exception.Message)" }
    } else { $reason = 'no results.json' }
    if ($TimedOut) { $reason = 'timeout' }
    $e = Set-SystemResult $m $X $Sys $res $TimedOut $reason $wall

    $steps = Get-ResultSteps $res $Job
    $sum = 0
    foreach ($st in $steps) { $sum += Get-Int $st.stepMs }
    $lat = [ordered]@{ testOverheadMs = [Math]::Max(0, $wall - $sum); wallMs = $wall; steps = $steps }
    $lx = Get-Dict (Get-Dict $c.Timings 'latest') $X
    $lx[$Sys] = $lat
    [void](Get-List $c.RunEntry 'tests').Add([ordered]@{
        module = $X; system = $Sys; startedAt = $Job.startedAt; finishedAt = (Get-NowIso); wallMs = $wall
        testOverheadMs = $lat.testOverheadMs; steps = $steps; batch = $Job.batch
    })
    Save-Timings

    $exitText = 'null'
    if ($Job.proc -and $null -ne $Job.proc.ExitCode) { $exitText = [string]$Job.proc.ExitCode }
    $tot = $e.stepsPass + $e.stepsFail + $e.stepsNotTested
    $line = ("[{0}/{1}] RESULT {2} steps {3}/{4}, assertions {5}/{6}, {7}ms (dotnet exit {8}, ignored for the verdict)" -f $X, $Sys, $e.status, $e.stepsPass, $tot, $e.assertionsPassed, $e.assertionsTotal, $wall, $exitText)
    if ($e.abortReason) { $line += " abortReason=" + (Limit-Text $e.abortReason 120) }
    $color = 'Green'
    if ($e.status -ne 'PASS') { $color = 'Yellow' }
    Write-Line $line $color
    Add-StatusEvent $s ("Module {0} {1} finished: {2} {3}/{4} steps" -f $X, $Sys, $e.status, $e.stepsPass, $tot)
    $Job.state = 'done'
    $Job.wallMs = $wall
    $Job.result = @{ Results = $res; TimedOut = $TimedOut; Reason = $reason; Entry = $e }
}

function Skip-TestJob($Job, [string]$Reason) {
    $m = $script:S.modules[$Job.module]
    $e = New-SystemEntry $Job.module $Job.system
    $e.abortReason = $Reason
    $m[$Job.system.ToLowerInvariant()] = $e
    $Job.state = 'skipped'
    $Job.result = @{ Results = $null; TimedOut = $false; Reason = $Reason; Entry = $e }
    Write-Line ("[{0}/{1}] {2}" -f $Job.module, $Job.system, $Reason) 'Yellow'
    Add-StatusEvent $script:S ("Module {0} {1} {2}" -f $Job.module, $Job.system, $Reason)
}

function Step-Job($Job) {
    # One scheduler tick for a running job: tail events, detect exit / timeout.
    if ($Job.state -ne 'running') { return }
    Read-Events $Job
    if ($Job.proc.HasExited) {
        $Job.proc.WaitForExit()
        Complete-TestJob $Job $false
        return
    }
    if ((Get-Date) -gt $Job.deadline) {
        & taskkill.exe /PID $Job.proc.Id /T /F 2>&1 | Out-Null
        try { [void]$Job.proc.WaitForExit(10000) } catch { Write-Verbose ("WaitForExit after taskkill failed: {0}" -f $_.Exception.Message) }
        Complete-TestJob $Job $true
    }
}

function Stop-RunningJobs {
    if (-not $script:Ctx -or -not $script:Ctx.Jobs) { return }
    foreach ($j in @($script:Ctx.Jobs.Values)) {
        if ($j.state -eq 'running' -and $j.proc -and -not $j.proc.HasExited) {
            & taskkill.exe /PID $j.proc.Id /T /F 2>&1 | Out-Null
            Write-Line ("WARN: stopped running test {0} (run aborted)" -f $j.key) 'Yellow'
        }
    }
}

function Wait-Jobs([string[]]$Keys, [scriptblock]$OnModuleDone = $null) {
    # Ticks every 1 s until every job in $Keys is done/skipped. $OnModuleDone X runs once per
    # module whose jobs are all finished.
    $c = $script:Ctx
    while ($true) {
        foreach ($k in $Keys) { Step-Job $c.Jobs[$k] }
        $all = $true
        foreach ($k in $Keys) { $st = $c.Jobs[$k].state; if ($st -ne 'done' -and $st -ne 'skipped') { $all = $false } }
        if ($OnModuleDone) {
            foreach ($X in @($Keys | ForEach-Object { $c.Jobs[$_].module } | Select-Object -Unique)) {
                if (-not $c.Notified[$X] -and (Test-ModuleJobsDone $X)) { $c.Notified[$X] = $true; & $OnModuleDone $X }
            }
        }
        Update-LiveProgress
        Save-Status
        Update-ProgressFile
        if ($all) { return }
        Start-Sleep -Seconds 1
    }
}

function Invoke-ParallelBatch([string[]]$ModuleIds, [scriptblock]$OnModuleDone) {
    # Read-only batch: up to $MaxParallel modules at once; both systems of a module start together;
    # the next module starts as soon as one finishes.
    $c = $script:Ctx
    $queue = [System.Collections.ArrayList]::new()
    foreach ($X in $ModuleIds) { [void]$queue.Add($X) }
    $active = [System.Collections.ArrayList]::new()
    while ($queue.Count -gt 0 -or $active.Count -gt 0) {
        $started = $false
        while ($queue.Count -gt 0 -and $active.Count -lt $MaxParallel) {
            $X = [string]$queue[0]
            $queue.RemoveAt(0)
            foreach ($j in @(Get-ModuleJobs $X)) { if ($j.state -eq 'pending') { Start-TestJob $j } }
            [void]$active.Add($X)
            $started = $true
        }
        if ($started) { Update-LiveProgress; Save-Status; Update-ProgressFile }
        Start-Sleep -Seconds 1
        foreach ($X in @($active)) { foreach ($j in @(Get-ModuleJobs $X)) { Step-Job $j } }
        foreach ($X in @($active)) {
            if (Test-ModuleJobsDone $X) {
                $active.Remove($X)
                if (-not $c.Notified[$X]) { $c.Notified[$X] = $true; if ($OnModuleDone) { & $OnModuleDone $X } }
            }
        }
        Update-LiveProgress
        Save-Status
        Update-ProgressFile
    }
}

function Get-JobEstimateMs($Job, [double]$AvgStepMs, [double]$MeanOverheadMs) {
    $w = Get-Int (Get-Nested $script:Ctx.Timings @('latest', $Job.module, $Job.system, 'wallMs'))
    if ($w -gt 0) { return [double]$w }
    return ([double](Get-Int $Job.total) * $AvgStepMs + $MeanOverheadMs)
}

function Get-RestartEstimateMs {
    $c = $script:Ctx
    $runs = @(Get-Items $c.Timings 'runs')
    for ($i = $runs.Count - 1; $i -ge 0; $i--) {
        $ms = @(@(Get-Items $runs[$i] 'restarts') | ForEach-Object { Get-Int $_.ms } | Where-Object { $_ -gt 0 })
        if ($ms.Count -gt 0) { return [double](($ms | Measure-Object -Average).Average) }
    }
    return [double]$RestartDefaultMs
}

function Get-ParallelEta {
    # Pure estimate (ms) of the remaining run time over Ctx.Jobs (design 3.2).
    $c = $script:Ctx
    if (-not $c -or -not $c.Jobs) { return [long]0 }
    $measured = @($c.Measured | Where-Object { $null -ne $_ })
    $avg = [double]$DefaultStepMs
    if ($measured.Count -gt 0) { $avg = [double](($measured | Measure-Object -Average).Average) }
    $overheads = @()
    $latest = Get-Nested $c.Timings @('latest')
    if ($latest -is [System.Collections.IDictionary]) {
        foreach ($mk in @($latest.Keys)) {
            if (-not ($latest[$mk] -is [System.Collections.IDictionary])) { continue }
            foreach ($sk in @($latest[$mk].Keys)) {
                $o = $latest[$mk][$sk].testOverheadMs
                if ($null -ne $o) { $overheads += [double]$o }
            }
        }
    }
    $meanOverhead = 8000.0
    if ($overheads.Count -gt 0) { $meanOverhead = [double](($overheads | Measure-Object -Average).Average) }
    $restartMs = Get-RestartEstimateMs
    $roRems = @()
    $mu = 0.0
    foreach ($X in @($c.Jobs.Values | ForEach-Object { $_.module } | Select-Object -Unique)) {
        if ($c.Notified[$X]) { continue }
        $jobs = @(Get-ModuleJobs $X)
        $rems = @()
        $left = 0
        foreach ($j in $jobs) {
            if ($j.state -eq 'done' -or $j.state -eq 'skipped') { $rems += 0.0; continue }
            $left++
            $est = Get-JobEstimateMs $j $avg $meanOverhead
            if ($j.state -eq 'running' -and $j.sw) { $rems += [Math]::Max(0.0, $est - $j.sw.ElapsedMilliseconds) } else { $rems += $est }
        }
        $sum = 0.0
        $max = 0.0
        foreach ($r in $rems) { $sum += $r; if ($r -gt $max) { $max = $r } }
        if ($jobs.Count -gt 0 -and $jobs[0].batch -eq 'read-only') { $roRems += $max }
        else { $mu += $sum + ($left * $restartMs) + 2000 }
    }
    $ro = 0.0
    if ($roRems.Count -gt 0) {
        $rmax = 0.0
        $rsum = 0.0
        foreach ($r in $roRems) { $rsum += $r; if ($r -gt $rmax) { $rmax = $r } }
        $ro = [Math]::Max($rmax, $rsum / $MaxParallel)
    }
    return [long]($ro + $mu + 2000)
}

function Get-Schedule([string[]]$Ids) {
    # @{ readOnly = @(ids); mutating = @(ids) } in plan order. Missing/invalid dataEffect -> mutating + WARN.
    $plan = Read-Plan
    $order = @()
    if ($plan) { $order = @(@(Get-Items $plan 'modules') | ForEach-Object { ([string]$_.id).Trim().ToUpperInvariant() }) }
    $sorted = @($Ids | Sort-Object { $i = [Array]::IndexOf([object[]]$order, $_); if ($i -lt 0) { 1000 + [Array]::IndexOf([object[]]$Ids, $_) } else { $i } })
    $ro = @()
    $mu = @()
    foreach ($X in $sorted) {
        $eff = $null
        if ($plan) { $eff = Get-DataEffectValue (Get-ModulePlan $X) }
        if (-not $eff) {
            Write-Line ("WARN: Module {0} has no valid dataEffect in plan.json; scheduling it as mutating (serialized)" -f $X) 'Yellow'
            $eff = 'mutating'
        }
        if ($script:S -and $script:S.modules.Contains($X)) { $script:S.modules[$X].dataEffect = $eff }
        if ($eff -eq 'read-only') { $ro += $X } else { $mu += $X }
    }
    return @{ readOnly = $ro; mutating = $mu }
}

function Update-LiveProgress {
    $c = $script:Ctx
    $s = $script:S
    if (-not $c -or -not $s) { return }
    $running = @($c.Jobs.Values | Where-Object { $_.state -eq 'running' })
    $parts = @()
    $jobsList = [System.Collections.ArrayList]::new()
    foreach ($j in $running) {
        $tot = [string]$j.total
        if ($j.estimated) { $tot = '~' + $tot }
        if ($j.index -gt 0) { $parts += ("{0} {1} Step {2} of {3}" -f $j.module, $j.system, $j.index, $tot) }
        else { $parts += ("{0} {1} starting" -f $j.module, $j.system) }
        $stepName = $null
        if ($j.steps.Count -gt 0) { $stepName = $j.steps[$j.steps.Count - 1].step }
        [void]$jobsList.Add([ordered]@{ module = $j.module; system = $j.system; step = $stepName; stepIndex = $j.index; stepTotal = $j.total })
    }
    $now = $s.progress.now
    if ($parts.Count -gt 0) { $now = Limit-Text ('Running: ' + ($parts -join ', ')) 300 }
    $pend = @($c.Jobs.Values | Where-Object { $_.state -eq 'pending' } | Select-Object -First 1)
    if ($pend.Count -gt 0) {
        $kind = if ($pend[0].batch -eq 'mutating') { 'mutating, serialized' } else { 'read-only' }
        $next = ("Module {0} {1} ({2})" -f $pend[0].module, $pend[0].system, $kind)
    } elseif ($c.Action -eq 'run-page') { $next = 'Run summary' }
    else { $next = 'Reports + fix-round bookkeeping' }
    $cr = $s.currentRun
    if ($cr) {
        $cr.jobs = $jobsList
        if ($jobsList.Count -gt 0) {
            $f = $jobsList[0]
            $cr.module = $f.module; $cr.system = $f.system; $cr.step = $f.step; $cr.stepIndex = $f.stepIndex; $cr.stepTotal = $f.stepTotal
            $cr.stepTotalEstimated = [bool]$running[0].estimated
        }
    }
    $eta = '~' + (Format-Hms (Get-ParallelEta))
    $status = 'RUNNING'
    if ($s.gate.pending) { $status = 'WAITING FOR YOU' }
    Set-ProgressState $status $now $next $eta
}

function Invoke-EventHandler($Job, $Ev) {
    $c = $script:Ctx
    if ([string]$Ev.module -ne $Job.module -or [string]$Ev.system -ne $Job.system) { return }
    if ($Ev.runId -and [string]$Ev.runId -ne $c.RunId) { return }
    $s = $script:S
    if ($Ev.event -eq 'step-start') {
        $Job.index = $Job.done + 1
        $Job.title = [string]$Ev.description
        $Job.titles[[string]$Ev.step] = [string]$Ev.description
        return
    } elseif ($Ev.event -eq 'step-end') {
        $Job.done = $Job.done + 1
        if ($Job.index -lt $Job.done) { $Job.index = $Job.done }
        $title = $Job.titles[[string]$Ev.step]
        if (-not $title) { $title = Limit-Text ([string]$Ev.description) 100; $Job.title = $title }
        if ($Job.done -gt $Job.total) { $Job.total = $Job.done }
        $stepMs = Get-Int $Ev.stepMs
        [void]$Job.steps.Add([ordered]@{ step = [string]$Ev.step; description = $title; actionMs = (Get-Int $Ev.actionMs); stepMs = $stepMs; status = [string]$Ev.status })
        [void]$c.Measured.Add($stepMs)
        $detail = ''
        $det = @(Get-Items $Ev 'detail')
        if ($det.Count -gt 0) { $detail = Limit-Text ([string]$det[0]) 140 }
        $color = 'Green'
        if ($Ev.status -eq 'FAIL') { $color = 'Red' } elseif ($Ev.status -ne 'PASS') { $color = 'Yellow' }
        $consoleLine = ("[{0}/{1}] {2} {3} {4}ms {5}" -f $Job.module, $Job.system, $Ev.step, $Ev.status, $stepMs, $detail)
        Write-Line $consoleLine.TrimEnd() $color
        $evText = ("Module {0} {1} {2} {3}: {4}" -f $Job.module, $Job.system, $Ev.step, $Ev.status, (Limit-Text $title 80))
        if ($detail) { $evText += ' -> ' + (Limit-Text $detail 100) }
        Add-StatusEvent $s $evText
    } else { return }
    Update-LiveProgress
    Save-Status
    Update-ProgressFile
}

function Read-Events($Job) {
    $ev = $Job.events
    if (-not $ev -or -not $ev.Path -or -not (Test-Path -LiteralPath $ev.Path)) { return }
    $buf = $null
    $read = 0
    $fs = $null
    try {
        $fs = [IO.FileStream]::new($ev.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        if ($fs.Length -le $ev.Offset) { return }
        [void]$fs.Seek($ev.Offset, [IO.SeekOrigin]::Begin)
        $count = [int]($fs.Length - $ev.Offset)
        $buf = New-Object byte[] $count
        while ($read -lt $count) {
            $n = $fs.Read($buf, $read, $count - $read)
            if ($n -le 0) { break }
            $read += $n
        }
        $ev.Offset = $ev.Offset + $read
    } catch {
        return
    } finally {
        if ($fs) { $fs.Dispose() }
    }
    if ($read -le 0) { return }
    $old = $ev.Leftover
    $all = New-Object byte[] ($old.Length + $read)
    [Array]::Copy($old, 0, $all, 0, $old.Length)
    [Array]::Copy($buf, 0, $all, $old.Length, $read)
    $lastNl = -1
    for ($i = $all.Length - 1; $i -ge 0; $i--) { if ($all[$i] -eq 10) { $lastNl = $i; break } }
    if ($lastNl -lt 0) { $ev.Leftover = $all; return }
    $text = $Utf8NoBom.GetString($all, 0, $lastNl + 1)
    $restLen = $all.Length - $lastNl - 1
    $rest = New-Object byte[] $restLen
    if ($restLen -gt 0) { [Array]::Copy($all, $lastNl + 1, $rest, 0, $restLen) }
    $ev.Leftover = $rest
    foreach ($line in ($text -split "`n")) {
        $t = $line.Trim()
        if (-not $t) { continue }
        $e = $null
        try { $e = ConvertTo-PlainData (ConvertFrom-Json -InputObject $t -ErrorAction Stop) } catch { $e = $null }
        if ($e -is [System.Collections.IDictionary]) { Invoke-EventHandler $Job $e }
    }
}

# ============================================================================
# PROGRESS.md
# ============================================================================
function Format-SystemCell($E) {
    if ($null -eq $E -or $E.status -eq 'NOT_RUN' -or -not $E.status) { return 'NOT_RUN' }
    $tot = (Get-Int $E.stepsPass) + (Get-Int $E.stepsFail) + (Get-Int $E.stepsNotTested)
    $cell = ("{0}/{1} {2}" -f (Get-Int $E.stepsPass), $tot, $E.status)
    if ($E.abortReason) { $cell += ' (' + (Limit-Text ([string]$E.abortReason) 40) + ')' }
    return $cell
}

function Get-ElapsedText($S) {
    $start = $null
    if ($S.session) { $start = ConvertTo-DateSafe $S.session.startedAt }
    if (-not $start -and $S.currentRun) { $start = ConvertTo-DateSafe $S.currentRun.startedAt }
    if (-not $start) { return '--:--:--' }
    $end = Get-Date
    if ($S.progress.status -ne 'RUNNING') {
        $u = ConvertTo-DateSafe $S.updatedAt
        if ($u) { $end = $u }
    }
    return (Format-Hms (($end - $start).TotalMilliseconds))
}

function Format-ProgressText($S) {
    $sb = New-Object System.Text.StringBuilder
    $p = $S.progress
    $status = [string]$p.status
    if ($S.gate.pending) { $status = 'WAITING FOR YOU' }
    if (-not $status) { $status = 'COMPLETE' }
    $now = [string]$p.now
    if (-not $now) { $now = '-' }
    $next = [string]$p.next
    if (-not $next) { $next = '-' }
    $eta = '-'
    if ($status -eq 'RUNNING' -and $p.eta) { $eta = [string]$p.eta }
    [void]$sb.AppendLine("# $($S.page) - Testing Progress")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("Status: $status")
    [void]$sb.AppendLine("Now:    $now")
    [void]$sb.AppendLine("Next:   $next")
    if ($S.gate.pending) {
        $needs = [string]$S.gate.question
        if ($S.gate.reportPath) { $needs += " Report: .kiro/app-testing/$($S.page)/$($S.gate.reportPath)" }
        [void]$sb.AppendLine("Needs you: [$($S.gate.name)] $needs")
    }
    [void]$sb.AppendLine((Get-CurrentPhaseLine))
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Module | Data effect | Reference | Target | Findings | CSS groups | Duration |')
    [void]$sb.AppendLine('|--------|-------------|-----------|--------|----------|-----------|----------|')
    $harness = @()
    foreach ($id in @($S.modules.Keys)) {
        $m = $S.modules[$id]
        $findings = '-'
        $css = '-'
        if ($m.target.status -and $m.target.status -ne 'NOT_RUN') {
            $findings = [string](Get-Int $m.openFindings)
            $css = [string](Get-Int $m.actionableCssDiffs)
            if ((Get-Int $m.cssDiffsTotal) -gt 0) { $css += (" ({0} raw rows actionable, {1} total)" -f (Get-Int $m.actionableCssRows), (Get-Int $m.cssDiffsTotal)) }
        }
        $dur = (Get-Int $m.reference.durationMs) + (Get-Int $m.target.durationMs)
        $durText = '-'
        if ($dur -gt 0) { $durText = Format-MinSec $dur }
        $name = [string]$id
        if ($m.name) { $name = "$id " + (Limit-Text ([string]$m.name) 40) }
        $effect = [string]$m.dataEffect
        if (-not $effect) { $effect = 'mutating' }
        [void]$sb.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $name, $effect, (Format-SystemCell $m.reference), (Format-SystemCell $m.target), $findings, $css, $durText))
        if ($m.harnessError) { $harness += ("- Module {0} HARNESS-ERROR: {1}" -f $id, $m.harnessError) }
    }
    if ($harness.Count -gt 0) { [void]$sb.AppendLine(''); foreach ($h in $harness) { [void]$sb.AppendLine($h) } }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Phases')
    [void]$sb.AppendLine('')
    foreach ($l in @(Get-PhaseTableLines (Get-DisplayTimings))) { [void]$sb.AppendLine($l) }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Recent events (last 10)')
    [void]$sb.AppendLine('')
    $events = @(Get-Items $p 'recentEvents')
    if ($events.Count -eq 0) { [void]$sb.AppendLine('- (none yet)') }
    else {
        $from = [Math]::Max(0, $events.Count - 10)
        for ($i = $events.Count - 1; $i -ge $from; $i--) { [void]$sb.AppendLine('- ' + $events[$i]) }
    }
    $round = Get-Int $S.fixLoop.round
    if ($round -gt 0) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('## Fix loop')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine(("Round {0} of {1} (clean: {2})" -f $round, (Get-Int $S.fixLoop.maxRounds), ([string]$S.fixLoop.clean).ToLowerInvariant()))
        foreach ($h in @(Get-Items $S.fixLoop 'history')) {
            $parts = @()
            $hm = $h.modules
            if ($hm -is [System.Collections.IDictionary]) {
                foreach ($mk in @($hm.Keys)) {
                    $e = $hm[$mk]
                    $fa = '?'
                    if ($null -ne $e.openFindingsAfter) { $fa = [string]$e.openFindingsAfter }
                    $ca = '?'
                    if ($null -ne $e.actionableCssAfter) { $ca = [string]$e.actionableCssAfter }
                    $parts += ("{0} findings {1} -> {2}, CSS {3} -> {4}" -f $mk, $e.openFindingsBefore, $fa, $e.actionableCssBefore, $ca)
                }
            }
            if ($parts.Count -eq 0) { $parts = @('no modules with open items') }
            [void]$sb.AppendLine(("- Round {0} ({1}, {2}): {3}" -f $h.round, $h.source, (Get-IsoString $h.at), ($parts -join '; ')))
        }
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Environment')
    [void]$sb.AppendLine('')
    $doc = $null
    try { $doc = Read-JsonFile $DoctorResultFile } catch { $doc = $null }
    if ($script:DoctorChecks) { $doc = $script:DoctorChecks }
    if ($null -eq $doc) { [void]$sb.AppendLine('- doctor has not run yet (.\.kiro\test-project\lifecycle.ps1 -Action doctor)') }
    else {
        $fails = @($doc.checks | Where-Object { $_.status -eq 'FAIL' }).Count
        $warns = @($doc.checks | Where-Object { $_.status -eq 'WARN' }).Count
        [void]$sb.AppendLine(("Last doctor run {0}: {1} ({2} FAIL, {3} WARN)" -f (Get-IsoString $doc.generatedAt), $doc.result, $fails, $warns))
        [void]$sb.AppendLine('')
        foreach ($c in @($doc.checks)) { [void]$sb.AppendLine('- ' + (Format-CheckLine $c)) }
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Files')
    [void]$sb.AppendLine('')
    $reports = @()
    foreach ($id in @($S.modules.Keys)) {
        foreach ($rk in @('parity', 'cssDiff', 'performance', 'manualTests')) {
            $rp = Get-Nested $S.modules[$id] @('reports', $rk)
            if ($rp -and $script:PageDir -and (Test-Path -LiteralPath (Join-Path $script:PageDir $rp))) { $reports += $rp }
        }
    }
    if ($script:PageDir -and (Test-Path -LiteralPath (Join-Path $script:PageDir 'reports/FinalReport.md'))) { $reports += 'reports/FinalReport.md' }
    if ($reports.Count -eq 0) { [void]$sb.AppendLine('- Reports: (none yet)') } else { [void]$sb.AppendLine('- Reports: ' + ($reports -join ', ')) }
    $chatLine = '- Chat renders: chat/ (none yet)'
    if ($script:PageDir) {
        $chatDir = Join-Path $script:PageDir 'chat'
        if (Test-Path -LiteralPath $chatDir) {
            $latest = @(Get-ChildItem -LiteralPath $chatDir -Filter '*.md' -File -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 3 | ForEach-Object { 'chat/' + $_.Name })
            if ($latest.Count -gt 0) { $chatLine = '- Chat renders (latest 3): ' + ($latest -join ', ') }
        }
    }
    [void]$sb.AppendLine($chatLine)
    [void]$sb.AppendLine('- State: status.json, timings.json (single writer: .kiro/test-project/lifecycle.ps1)')
    $logLine = '- Logs: logs/'
    if ($script:RunLog) { $logLine += ' (this run: logs/' + (Split-Path -Leaf $script:RunLog) + ')' }
    [void]$sb.AppendLine($logLine)
    [void]$sb.AppendLine(("- Folder: .kiro/app-testing/{0}/  (updated {1})" -f $S.page, (Get-IsoString $S.updatedAt)))
    return $sb.ToString()
}

function Update-ProgressFile {
    if (-not $script:S -or -not $script:PageDir) { return }
    try {
        $text = Format-ProgressText $script:S
        [void](Write-AtomicFile (Join-Path $script:PageDir 'PROGRESS.md') $text)
    } catch {
        if (Test-IsLifecycleExit $_) { throw }
        Write-Host "WARN: PROGRESS.md not written: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ============================================================================
# Test execution helpers
# ============================================================================
function Set-TestEnv([hashtable]$Vars) {
    $saved = @{}
    foreach ($k in @($Vars.Keys)) {
        $saved[$k] = [Environment]::GetEnvironmentVariable($k)
        [Environment]::SetEnvironmentVariable($k, [string]$Vars[$k])
    }
    return $saved
}

function Restore-TestEnv([hashtable]$Saved) {
    foreach ($k in @($Saved.Keys)) { [Environment]::SetEnvironmentVariable($k, $Saved[$k]) }
}

function Resolve-RunModules([string[]]$Requested) {
    $s = $script:S
    $req = @()
    foreach ($m in @($Requested)) {
        foreach ($p in ([string]$m -split '[,;\s]+')) { if ($p) { $req += $p.Trim().ToUpperInvariant() } }
    }
    $plan = Read-Plan
    if ($req.Count -eq 0 -and $plan) {
        $req = @(@(Get-Items $plan 'modules') | ForEach-Object { ([string]$_.id).Trim().ToUpperInvariant() } | Where-Object { $_ })
    }
    if ($req.Count -eq 0) { $req = @(Get-PlannedModuleIds $s) }
    if ($req.Count -eq 0) { Stop-Lifecycle 1 'No modules to run: pass -Modules A,B or create state-map/plan.json' }
    $out = @()
    foreach ($X in $req) {
        if ($X -notmatch '^[A-Z0-9]+$') { Stop-Lifecycle 1 "Invalid module id '$X'" }
        if ($out -contains $X) { continue }
        $out += $X
        $pm = @()
        if ($plan) { $pm = @(@(Get-Items $plan 'modules') | Where-Object { ([string]$_.id).Trim().ToUpperInvariant() -eq $X } | Select-Object -First 1) }
        if ($pm.Count -gt 0) {
            # plan.json is the source of test names / steps / CSS states; unchanged modules keep run data.
            $fromPlan = New-ModuleFromPlan $pm[0]
            if (-not $s.modules.Contains($X) -or -not (Test-ModuleUnchanged $s.modules[$X] $fromPlan)) {
                if ($s.modules.Contains($X)) { Write-Line ("[run-page] Module {0} differs from state-map/plan.json; using the plan entry (run data reset)" -f $X) 'Yellow' }
                $s.modules[$X] = $fromPlan
            } else {
                $s.modules[$X].dataEffect = $fromPlan.dataEffect
            }
        } elseif (-not $s.modules.Contains($X)) {
            $s.modules[$X] = New-ModuleEntry $X '' 0 @() (Get-FallbackTest $X 'Reference') (Get-FallbackTest $X 'Target')
        }
    }
    return $out
}

function Set-SystemResult($M, [string]$X, [string]$Sys, $Res, [bool]$TimedOut, [string]$Reason, [long]$WallMs) {
    $e = New-SystemEntry $X $Sys
    $e.durationMs = $WallMs
    $e.executedAt = Get-NowIso
    if ($Res) {
        $t = $Res.totals
        if ($t) {
            foreach ($k in @('stepsPass', 'stepsFail', 'stepsNotTested', 'assertionsTotal', 'assertionsPassed', 'assertionsFailed')) { $e[$k] = Get-Int $t[$k] }
        }
        if ($Res.finishedAt) { $e.executedAt = Get-IsoString $Res.finishedAt }
    }
    if ($TimedOut) { $e.status = 'FAIL'; $e.abortReason = 'timeout' }
    elseif (-not $Res) { $e.status = 'FAIL'; $e.abortReason = $Reason }
    elseif ($Res.aborted -eq $true) {
        $e.status = 'FAIL'
        $e.abortReason = [string]$Res.abortReason
        if (-not $e.abortReason) { $e.abortReason = 'aborted' }
    } elseif ($e.stepsFail -eq 0 -and $e.assertionsFailed -eq 0 -and $e.stepsPass -gt 0) { $e.status = 'PASS' }
    else { $e.status = 'FAIL' }
    $M[$Sys.ToLowerInvariant()] = $e
    return $e
}

function Get-ResultSteps($Res, $Cur) {
    $steps = [System.Collections.ArrayList]::new()
    $src = @()
    if ($Res) { $src = @(Get-Items $Res 'steps') }
    if ($src.Count -gt 0) {
        foreach ($st in $src) {
            $desc = [string]$st.title
            if (-not $desc) { $desc = Limit-Text ([string]$st.description) 100 }
            [void]$steps.Add([ordered]@{ step = [string]$st.step; description = $desc; actionMs = (Get-Int $st.actionMs); stepMs = (Get-Int $st.stepMs); status = [string]$st.status })
        }
    } elseif ($Cur) {
        foreach ($st in @($Cur.steps)) { [void]$steps.Add($st) }
    }
    return , $steps
}

function Get-BaselineFailure($M, $Run) {
    if ($Run.TimedOut) { return 'timeout (20 min hard limit)' }
    $res = $Run.Results
    if (-not $res) { return [string]$Run.Reason }
    if ($res.aborted -eq $true) { return ('aborted: ' + [string]$res.abortReason) }
    $t = $res.totals
    if ((Get-Int $t.stepsFail) -gt 0) { return ("{0} step(s) failed" -f (Get-Int $t.stepsFail)) }
    if ((Get-Int $t.assertionsFailed) -gt 0) { return ("{0} assertion(s) failed" -f (Get-Int $t.assertionsFailed)) }
    if ((Get-Int $t.stepsPass) -eq 0) { return 'no steps passed' }
    $captured = @(Get-Items $res 'cssStatesCaptured')
    $miss = @(@(Get-Items $M 'cssStates') | Where-Object { $captured -notcontains $_ })
    if ($miss.Count -gt 0) { return ("planned CSS state(s) not captured: {0}" -f ($miss -join ', ')) }
    return $null
}

function Invoke-Reports([string]$Which, [switch]$BestEffort, [switch]$Documents) {
    # Deterministic reports via the Reports tool: 'module' -> runs/ModuleX/summary.json + 4 reports,
    # 'all' -> every module + reports/FinalReport.md (-Documents: + FinalReport.pdf/.docx and
    # TestCaseResults.pdf/.docx). Success = exit 0 and the output newer than the start.
    [void](Invoke-HarnessBuild -WithReports)
    $c = $script:Ctx
    $label = $Which
    $log = Join-Path $script:LogDir ("report-$label.log")
    $err = Join-Path $script:LogDir ("report-$label.err.log")
    $t0 = (Get-Date).AddSeconds(-2)
    $argString = "`"$ReportsDll`" all --page-dir `"$($script:PageDir)`""
    if ($Which -ne 'ALL') { $argString = "`"$ReportsDll`" module --page-dir `"$($script:PageDir)`" --module $Which" }
    elseif ($Documents) { $argString += ' --documents' }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Start-Logged -FilePath 'dotnet' -ArgString $argString -Log $log -ErrLog $err -TimeoutSec $ReportTimeoutSec
    $sw.Stop()
    if ($c) { $c.RunEntry.reportMs = (Get-Int $c.RunEntry.reportMs) + [long]$sw.ElapsedMilliseconds }
    $ok = (-not $r.TimedOut) -and ($r.ExitCode -eq 0)
    $out = Join-Path $script:PageDir ("runs\Module$Which\summary.json")
    if ($Which -eq 'ALL') { $out = Join-Path $script:PageDir 'reports\FinalReport.md' }
    if ($ok) { $ok = (Test-Path -LiteralPath $out) -and ((Get-Item -LiteralPath $out).LastWriteTime -ge $t0) }
    if (-not $ok) {
        $first = @(Get-FileTail $err 50 | Where-Object { $_.Trim() } | Select-Object -First 1)
        $why = ''
        if ($first.Count -gt 0) { $why = ': ' + (Limit-Text $first[0] 160) }
        $msg = ("Report generator failed for {0} (exit {1}{2}){3}; see {4}" -f $label, $r.ExitCode, $(if ($r.TimedOut) { ', timeout' } else { '' }), $why, (Get-RelPath $log))
        if ($BestEffort) { Write-Line ("WARN: " + $msg) 'Yellow'; return $false }
        Write-Line ("[reports] FAIL " + $msg) 'Red'
        Stop-Lifecycle 1 $msg
    }
    Write-Line ("[reports] {0} PASS {1}ms" -f $label, $sw.ElapsedMilliseconds) 'Green'
    if ($Documents) {
        foreach ($l in @((Get-SharedText $log) -split "`r?`n")) {
            if ($l -match '^Document:\s*(.+?)\s+(\d+)$') { Write-Line ("[report] {0} ({1} bytes)" -f (Get-RelPath $Matches[1]), $Matches[2]) }
            elseif ($l -match '^DocumentWarning:\s*(.+)$') { Write-Line ("WARN: " + $Matches[1]) 'Yellow' }
        }
    }
    return $true
}

function Merge-Summary([string]$X, [switch]$ClearHarnessError) {
    $s = $script:S
    $path = Join-Path $script:PageDir ("runs\Module$X\summary.json")
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $sum = $null
    try { $sum = Read-JsonFile $path } catch { Stop-Lifecycle 1 "runs/Module$X/summary.json does not parse: $($_.Exception.Message)" }
    if (-not $s.modules.Contains($X)) { return $sum }
    $m = $s.modules[$X]
    $m.openFindings = Get-Int $sum.openFindings
    # actionableCssDiffs = page-level CSS groups owned by this module; actionableCssRows = raw rows (informational).
    $m.actionableCssDiffs = Get-Int $sum.actionableCssDiffs
    $m.actionableCssRows = Get-Int $sum.actionableCssRows
    $m.cssDiffsTotal = Get-Int $sum.cssDiffsTotal
    $m.notTested = Get-Int $sum.notTested
    # Reference defects (content checks on the baseline): listed in the reports, never part of the Target counts.
    $m.referenceDefects = Get-Int $sum.referenceDefectCount
    $m.reports = [ordered]@{
        parity = "reports/Module${X}_Parity.md"; cssDiff = "reports/Module${X}_CSS_Diff.md"
        performance = "reports/Module${X}_Performance.md"; manualTests = "reports/Module${X}_ManualTests.md"
    }
    if ($ClearHarnessError) { $m.harnessError = $null }
    Write-Line ("[{0}/reports] openFindings={1} cssGroups={2} actionableCssRows={3} cssDiffsTotal={4} notTested={5} referenceDefects={6} -> reports/Module{0}_Parity.md, _CSS_Diff.md, _Performance.md, _ManualTests.md" -f $X, $m.openFindings, $m.actionableCssDiffs, $m.actionableCssRows, $m.cssDiffsTotal, $m.notTested, $m.referenceDefects)
    Add-StatusEvent $s ("Module {0} reports: {1} finding(s), {2} CSS group(s) ({3} raw CSS rows)" -f $X, $m.openFindings, $m.actionableCssDiffs, $m.actionableCssRows)
    return $sum
}

function Sync-CssGroupCounts {
    # The report generator groups CSS rows per PAGE and rewrites every module's summary.json: group ownership
    # (first module in plan order) can move when another module finishes. Re-read the CSS counts of every
    # module with a summary.json (no status events), then print the page total from runs/css_groups.json.
    $s = $script:S
    foreach ($X in @($s.modules.Keys)) {
        $path = Join-Path $script:PageDir ("runs\Module$X\summary.json")
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $sum = $null
        try { $sum = Read-JsonFile $path } catch { continue }
        $m = $s.modules[$X]
        $m.actionableCssDiffs = Get-Int $sum.actionableCssDiffs
        $m.actionableCssRows = Get-Int $sum.actionableCssRows
    }
    $gf = Join-Path $script:PageDir 'runs\css_groups.json'
    if (Test-Path -LiteralPath $gf) {
        try {
            $g = Read-JsonFile $gf
            Write-Line ("[reports] CSS groups: {0} actionable ({1} raw rows) -> reports/CSS_Groups.md" -f (Get-Int $g.actionableGroups), (Get-Int $g.actionableRows))
        } catch { Write-Line ("WARN: runs/css_groups.json does not parse: {0}" -f $_.Exception.Message) 'Yellow' }
    }
}

function Update-Session([string]$RunId, [switch]$ForceNew) {
    # A session is one versioned run (history/<session.id>/). -ForceNew: the previous run is finished
    # or this is another full manual run-page, so results are never reused across runs.
    $s = $script:S
    $rev = $s.plan.signedOffRevision
    $need = $ForceNew -or ($null -eq $s.session) -or ([string]$s.session.planRevision -ne [string]$rev)
    if ($need -and -not $ForceNew -and $s.session -and (Get-Int $s.session.runPages) -eq 0 -and $null -ne $rev) {
        # Run created by progress -Phase 0 whose plan was revised and signed off before any run-page: same run.
        $s.session.planRevision = $rev
        $need = $false
    }
    if ($need) {
        $s.session = [ordered]@{ id = ('run-' + $RunId); startedAt = (Get-NowIso); planRevision = $rev; lastExitCode = $null; runPages = 0 }
        Reset-RunScopedState $s
        $revText = 'none (manual run)'
        if ($null -ne $rev) { $revText = [string]$rev }
        Write-Line ("[session] new run {0} (plan revision {1}); fix loop reset" -f $s.session.id, $revText)
        Add-StatusEvent $s ("New run {0} (plan revision {1})" -f $s.session.id, $revText)
    } else {
        Write-Line ("[session] continuing run {0} (plan revision {1})" -f $s.session.id, $s.session.planRevision)
    }
    $s.session.runPages = (Get-Int $s.session.runPages) + 1
}

function Test-SessionResumable($s, [int]$MaxIdleHours = 2) {
    # Only a genuinely interrupted run may resume: a session exists, the run has not reached the
    # final report (state report_pending/complete or final sign-off) and was active recently.
    if ($null -eq $s.session) { return $false }
    if (-not ([string]$s.session.id).StartsWith('run-')) { return $false } # legacy session from before versioned runs
    if ($s.finalReport -and $s.finalReport.signedOff -eq $true) { return $false }
    if (@('report_pending', 'complete') -contains [string]$s.state) { return $false }
    $last = ConvertTo-DateSafe $s.updatedAt
    if ($null -eq $last) { return $false }
    return (((Get-Date) - $last).TotalHours -lt $MaxIdleHours)
}

function Start-NewSessionForRun($s) {
    # Phase 0 of a new workflow run: a fresh session so the run step's resume-skip rule
    # (reference.executedAt >= session.startedAt) never matches results of earlier runs.
    $rev = $null
    try { $p = Read-JsonFile (Get-PlanPath); if ($p -and $null -ne $p.revision) { $rev = Get-Int $p.revision } } catch { $rev = $null }
    $id = 'run-' + (Get-Date).ToString('yyyyMMdd-HHmmss')
    $s.session = [ordered]@{ id = $id; startedAt = (Get-NowIso); planRevision = $rev; lastExitCode = $null; runPages = 0 }
    Reset-RunScopedState $s
    $revText = 'none'
    if ($null -ne $rev) { $revText = [string]$rev }
    Write-Host ("[session] new run {0} (plan revision {1}) for a new workflow run" -f $id, $revText)
    Add-StatusEvent $s ("New run {0} (plan revision {1})" -f $id, $revText)
}

function Reset-RunScopedState($s) {
    # Per-run state never carries over to a new run: fix loop, final sign-off and a stale final/baseline gate.
    $s.fixLoop = [ordered]@{ round = 0; maxRounds = 2; clean = $false; history = [System.Collections.ArrayList]::new() }
    $s.finalReport = [ordered]@{ path = 'reports/FinalReport.md'; signedOff = $false; signedOffAt = $null; response = $null }
    if ($s.gate -and $s.gate.pending -eq $true -and @('final_signoff', 'baseline_failed') -contains [string]$s.gate.name) { $s.gate = New-GateCleared $null }
}

function Copy-NewerFiles([string]$Src, [string]$Dst, [datetime]$Since) {
    # Copies files under $Src written at/after $Since (this run's outputs only), skipping unchanged copies.
    if (-not (Test-Path -LiteralPath $Src)) { return }
    $root = (Get-Item -LiteralPath $Src).FullName.TrimEnd('\')
    foreach ($f in @(Get-ChildItem -LiteralPath $Src -Recurse -File -ErrorAction SilentlyContinue)) {
        if ($f.LastWriteTime -lt $Since -or $f.Name -like '*.tmp') { continue }
        $rel = $f.FullName.Substring($root.Length).TrimStart('\')
        $to = Join-Path $Dst $rel
        $ex = Get-Item -LiteralPath $to -ErrorAction SilentlyContinue
        if ($ex -and $ex.Length -eq $f.Length -and $ex.LastWriteTime -eq $f.LastWriteTime) { continue }
        $dir = Split-Path -Parent $to
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        try { Copy-Item -LiteralPath $f.FullName -Destination $to -Force } catch { Write-Host ("WARN: history copy failed for {0}: {1}" -f $rel, $_.Exception.Message) -ForegroundColor Yellow }
    }
}

function Save-RunHistory {
    # history/<runId>/ = this run's reports, chat renders, module results/screenshots, timings, fixes and a
    # status snapshot; top-level reports/ and chat/ stay the 'latest' copy. Also history/index.json + README.md.
    # Never fatal: history is a copy of outputs that already exist at the top level.
    try {
        $st = Read-JsonFile (Get-StatusPath)
        $sid = [string](Get-Nested $st @('session', 'id'))
        $since = ConvertTo-DateSafe (Get-Nested $st @('session', 'startedAt'))
        if (-not $sid -or -not $since) { return }
        $since = $since.ToLocalTime().AddSeconds(-1)
        $histRoot = Join-Path $script:PageDir 'history'
        $dst = Join-Path $histRoot $sid
        if (-not (Test-Path -LiteralPath $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
        foreach ($sub in @('reports', 'chat', 'runs')) { Copy-NewerFiles (Join-Path $script:PageDir $sub) (Join-Path $dst $sub) $since }
        foreach ($fname in @('timings.json', 'fixes.json')) {
            $fp = Join-Path $script:PageDir $fname
            if ((Test-Path -LiteralPath $fp) -and (Get-Item -LiteralPath $fp).LastWriteTime -ge $since) { Copy-Item -LiteralPath $fp -Destination (Join-Path $dst $fname) -Force }
        }
        Copy-Item -LiteralPath (Get-StatusPath) -Destination (Join-Path $dst 'status.json') -Force

        $mods = [ordered]@{}
        $findings = 0
        foreach ($X in @(Get-Items (Get-Nested $st @('plan')) 'modules')) {
            $m = Get-Nested $st @('modules', [string]$X)
            if ($null -eq $m) { continue }
            $mods[[string]$X] = [ordered]@{ reference = (Format-SystemCell $m['reference']); target = (Format-SystemCell $m['target']) }
            $findings += Get-Int $m['openFindings']
        }
        $fixes = @(Get-Items (Get-Nested $st @('fixLoop')) 'history').Count
        $entry = [ordered]@{
            runId = $sid; startedAt = (Get-Nested $st @('session', 'startedAt')); finishedAt = (Get-Nested $st @('currentRun', 'finishedAt'))
            planRevision = (Get-Nested $st @('session', 'planRevision')); modules = $mods; findings = $findings; fixRounds = $fixes
            finalSignOff = (Get-Nested $st @('finalReport', 'signedOff')) -eq $true; folder = "history/$sid"
        }
        $idxPath = Join-Path $histRoot 'index.json'
        $idx = $null
        try { $idx = Read-JsonFile $idxPath } catch { $idx = $null }
        $runs = [System.Collections.ArrayList]::new()
        foreach ($r in @(Get-Items $idx 'runs')) { if ($r -and [string]$r['runId'] -ne $sid) { [void]$runs.Add($r) } }
        [void]$runs.Add($entry)
        $sorted = @($runs | Sort-Object { [string]$_['runId'] })
        [void](Write-AtomicFile $idxPath (ConvertTo-JsonText ([ordered]@{ page = $Page; runs = $sorted })))

        $md = [System.Collections.Generic.List[string]]::new()
        $md.Add("# $Page - run history")
        $md.Add('')
        $md.Add('Every test-page run is versioned: its outputs are in `history/<run id>/`. Top-level `reports/` and `chat/` hold the latest run.')
        $md.Add('')
        $md.Add('One row per run, newest first. Per module: steps passed on the old page (Reference) and on the new page (Target). Open gaps = functional gaps still open at the end of the run; Final sign-off = the final report was confirmed at the last gate.')
        $md.Add('')
        $md.Add('| Run id | Started | Finished | Plan revision | Steps passed per module (old page / new page) | Open gaps | Fix rounds | Final sign-off |')
        $md.Add('|---|---|---|---|---|---|---|---|')
        foreach ($r in ($sorted | Sort-Object { [string]$_['runId'] } -Descending)) {
            $cells = @()
            $rm = $r['modules']
            if ($rm -is [System.Collections.IDictionary]) { foreach ($k in $rm.Keys) { $cells += ("{0}: old {1}, new {2}" -f $k, $rm[$k]['reference'], $rm[$k]['target']) } }
            $fmt = { param($v) $d = ConvertTo-DateSafe $v; if ($d) { $d.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss') } else { '-' } }
            $sign = 'no'
            if ($r['finalSignOff'] -eq $true) { $sign = 'yes' }
            $md.Add(("| ``{0}`` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f $r['runId'], (& $fmt $r['startedAt']), (& $fmt $r['finishedAt']), $r['planRevision'], ($cells -join '; '), $r['findings'], $r['fixRounds'], $sign))
        }
        [void](Write-AtomicFile (Join-Path $histRoot 'README.md') (($md -join "`r`n") + "`r`n"))
    } catch {
        Write-Host ("WARN: run history not saved: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
}

function Complete-RunAction {
    $s = $script:S
    if (-not $s -or -not $script:RunAction) { return }
    $code = $script:ExitCode
    $msg = $script:ExitMessage
    $c = $script:Ctx
    Stop-RunningJobs
    if ($s.session -and $script:RunAction -ne 'report') { $s.session.lastExitCode = $code }
    if ($s.currentRun) { $s.currentRun.finishedAt = Get-NowIso; $s.currentRun.exitCode = $code }
    $mods = @()
    if ($c) { $mods = @($c.RunModules) }
    $label = 'Run'
    if ($mods.Count -eq 1) { $label = "Module $($mods[0]) complete" }
    elseif ($mods.Count -gt 1) { $label = "Modules $($mods -join ', ') complete" }
    $signed = ($s.plan.signedOff -eq $true)
    switch ($code) {
        0 {
            if ($script:RunAction -eq 'report') {
                $st = 'COMPLETE'
                if ($signed -and $s.finalReport.signedOff -ne $true) { $st = 'RUNNING' }
                Set-ProgressState $st 'Reports regenerated (all modules + reports/FinalReport.md)' '-' ''
            } elseif ($signed) {
                Set-ProgressState 'RUNNING' "$label - workflow continues" 'Auto-fix / final report (workflow)' ''
            } else {
                Set-ProgressState 'COMPLETE' "$label (manual run)" '-' ''
            }
        }
        2 {
            $s.state = 'baseline_failed'
            Set-ProgressState 'FAILED' 'Reference baseline failed - waiting for you' ("Fix the Reference baseline (" + (Limit-Text $msg 150) + ")") ''
        }
        default {
            $s.state = 'failed'
            Set-ProgressState 'FAILED' (Limit-Text $msg 300) '-' ''
        }
    }
    $ev = ("{0} exit {1}" -f $script:RunAction, $code)
    if ($msg) { $ev += ': ' + $msg } elseif ($code -eq 0) { $ev += ': ' + $s.progress.now }
    Add-StatusEvent $s $ev
    try { Save-Status -NonFatal } catch { Write-Host "WARN: status.json not saved: $($_.Exception.Message)" -ForegroundColor Yellow }
    if ($c) {
        $c.RunEntry.finishedAt = Get-NowIso
        $c.RunEntry.exitCode = $code
        Save-Timings
    }
    Update-ProgressFile
    Save-RunHistory
    if ($c) {
        # Written before Exit-Lock: a removed lock always implies last-run.json for a finished run ('wait').
        $lr = [ordered]@{ runId = $c.RunId; action = $script:RunAction; pid = $PID; exitCode = $code; message = $msg; finishedAt = (Get-NowIso) }
        [void](Write-AtomicFile (Join-Path $script:PageDir 'runs\last-run.json') (ConvertTo-JsonText $lr))
    }
}

# ============================================================================
# Actions: doctor / run-page / rerun-target / fix-round / gate / progress / report / reset / status
# ============================================================================
function Invoke-DoctorAction {
    if ($Page) { Assert-Page }
    $doc = Invoke-Doctor
    Write-DoctorOutput $doc
    if ($Page -and (Test-Path -LiteralPath $script:PageDir)) {
        $s = $null
        try { $s = Read-Status } catch { $s = $null }
        if ($null -eq $s -or (Get-Int $s.schemaVersion) -ne 2) { $s = New-StatusV2 $Page; $s.progress.now = 'Environment check (doctor)' }
        $script:S = $s
        Update-ProgressFile
        $script:S = $null
        Write-Line ("[doctor] Environment section written to .kiro/app-testing/{0}/PROGRESS.md" -f $Page)
    }
    if ($doc.exitCode -ne 0) { Stop-Lifecycle 3 (Get-FirstDoctorFailure $doc) }
}

function Start-PageRun([string]$RunAction) {
    Assert-Page -Create
    # The runId is generated BEFORE the lock so the lock, status.currentRun and timings agree.
    $runId = Get-Date -Format 'yyyyMMdd-HHmmss'
    Enter-Lock $RunAction -RunId $runId
    $script:S = Read-Status -Create
    $script:RunAction = $RunAction
    [void](New-RunContext $RunAction $runId)
    Write-Line ("[{0}] page={1} runId={2} progress=.kiro/app-testing/{1}/PROGRESS.md" -f $RunAction, $Page, $runId)
    return $runId
}

function Format-IdList($Ids) {
    $a = @($Ids | Where-Object { $_ })
    if ($a.Count -eq 0) { return '-' }
    return ($a -join ', ')
}

function Complete-ScheduledRun([string]$BaselineVerb) {
    # Exit precedence 2 (baseline) > 1 (build / missing test) > 0.
    if ($script:BaselineFailed.Count -gt 0) {
        $ids = @($script:BaselineFailed.Keys)
        $why = (@($ids | ForEach-Object { "{0}: {1}" -f $_, $script:BaselineFailed[$_] }) -join '; ')
        Stop-Lifecycle 2 ("Reference baseline {0} for Module(s) {1}: {2}" -f $BaselineVerb, ($ids -join ', '), $why)
    }
    if ($script:HarnessFailed.Count -gt 0) {
        $ids = @($script:HarnessFailed.Keys)
        $why = (@($ids | ForEach-Object { "{0}: {1}" -f $_, $script:HarnessFailed[$_] }) -join '; ')
        Stop-Lifecycle 1 ("Module(s) {0}" -f $why)
    }
}

function Initialize-ModuleRun([string[]]$Ids, [string[]]$Systems, [string]$BaselineVerb) {
    # Harness/Reports build, parallel module builds, Playwright check, static test-method check.
    # Failed modules get harnessError and are dropped. Returns the runnable module ids.
    $c = $script:Ctx
    $s = $script:S
    Set-ProgressState 'RUNNING' ("Building Harness + module projects ({0})" -f ($Ids -join ', ')) 'Run tests'
    Save-Status
    Update-ProgressFile
    $sw = [Diagnostics.Stopwatch]::StartNew()
    [void](Invoke-HarnessBuild -WithReports)
    $builds = Invoke-ModuleBuilds $Ids
    $sw.Stop()
    $c.RunEntry.buildMs = [long]$sw.ElapsedMilliseconds
    $ok = @()
    foreach ($X in $Ids) {
        $b = $builds[$X]
        if ($b.ok) { $ok += $X; continue }
        $first = $b.why
        if (@($b.errors).Count -gt 0) { $first = @($b.errors)[0] }
        $s.modules[$X].harnessError = ('build failed: ' + (Limit-Text ([string]$first) 200))
        $script:HarnessFailed[$X] = ("build failed (see logs/build-{0}.log)" -f $X)
        Add-StatusEvent $s ("Module {0} build failed: {1}" -f $X, (Limit-Text ([string]$first) 150))
    }
    if ($ok.Count -eq 0) {
        Save-Status
        Complete-ScheduledRun $BaselineVerb
        Stop-Lifecycle 1 'No module project built'
    }
    $script:PlaywrightBinDir = Get-ModuleBinDir $ok[0]
    Confirm-PlaywrightBrowsers
    $runnable = @()
    foreach ($X in $ok) {
        $m = $s.modules[$X]
        $missing = @()
        foreach ($sys in $Systems) {
            $name = [string]$m.tests[$sys.ToLowerInvariant()]
            if (-not (Test-TestMethodExists $X $name)) { $missing += $name }
        }
        if ($missing.Count -gt 0) {
            $m.harnessError = ('test not found: ' + ($missing -join ', '))
            $script:HarnessFailed[$X] = $m.harnessError
            Write-Line ("[tests] Module {0} dropped: test not found: {1} (no 'public async Task <name>(' in Modules/{2}.Mod{0}/*.cs)" -f $X, ($missing -join ', '), $Page) 'Red'
            Add-StatusEvent $s ("Module {0} dropped: {1}" -f $X, $m.harnessError)
            continue
        }
        $runnable += $X
    }
    if ($runnable.Count -gt 0) {
        $names = @()
        foreach ($X in $runnable) { foreach ($sys in $Systems) { $names += [string]$s.modules[$X].tests[$sys.ToLowerInvariant()] } }
        Write-Line ("[tests] found {0}" -f ($names -join ', ')) 'Green'
    } else {
        Save-Status
        Complete-ScheduledRun $BaselineVerb
        Stop-Lifecycle 1 'No runnable module'
    }
    Save-Status
    Update-ProgressFile
    return $runnable
}

function Invoke-DirtyDataGuard {
    # Read-only modules read the shared store too: restart the data apps once when the previous
    # run/rerun ran mutating tests without ending on a closing restart (or crashed).
    $c = $script:Ctx
    $runs = @(Get-Items $c.Timings 'runs')
    $prev = $null
    for ($i = $runs.Count - 1; $i -ge 0; $i--) {
        $r = $runs[$i]
        if ($r.runId -eq $c.RunId) { continue }
        if ($r.action -eq 'run-page' -or $r.action -eq 'rerun-target') { $prev = $r; break }
    }
    if ($null -eq $prev) { return }
    $mut = @(@(Get-Items $prev 'tests') | Where-Object { $_.batch -eq 'mutating' })
    $started = @(Get-Items $prev 'mutating')
    if ($mut.Count -eq 0 -and $started.Count -eq 0) { return }
    $rs = @(Get-Items $prev 'restarts')
    $last = ''
    if ($rs.Count -gt 0) { $last = [string]$rs[$rs.Count - 1].reason }
    if ((-not $prev.finishedAt) -or ($last -notmatch '^after Module \S+ Target( rerun)?$')) {
        Write-Line ("[apps] previous run {0} ran mutating tests without a closing data reset; restarting the data app(s) first" -f $prev.runId) 'Yellow'
        [void](Restart-DataApps 'fresh data (previous run ended without a closing restart)')
    }
}

function Complete-ModuleRun([string]$X) {
    # run-page: baseline check, reports, summary merge and the module-result chat render for one module.
    $c = $script:Ctx
    $s = $script:S
    $c.Notified[$X] = $true
    $m = $s.modules[$X]
    $ref = $c.Jobs["$X/Reference"]
    $why = $null
    if ($ref -and $ref.result) { $why = Get-BaselineFailure $m $ref.result }
    if ($why) {
        $script:BaselineFailed[$X] = $why
        Write-Line ("[{0}/Reference] BASELINE FAILED: {1}" -f $X, $why) 'Red'
        Add-StatusEvent $s ("Module {0} Reference baseline failed: {1}" -f $X, $why)
        if (Invoke-Reports $X -BestEffort) { [void](Merge-Summary $X) }
    } else {
        [void](Invoke-Reports $X)
        [void](Merge-Summary $X -ClearHarnessError)
    }
    Sync-CssGroupCounts
    Save-Status
    Update-ProgressFile
    Write-Line ("[done] Module {0}: Reference {1}, Target {2}, findings {3}, CSS groups {4}" -f $X, (Format-SystemCell $m.reference), (Format-SystemCell $m.target), $m.openFindings, $m.actionableCssDiffs)
    # P6: during the run only the compact live status table; full module results follow in plan order.
    Update-LiveProgress
    [void](Render-View 'live-status' '' -Runner)
}

function Publish-ModuleResults([string[]]$Ids) {
    # P6: one module-result render per run module, in plan order (not finish order).
    $order = @(Get-PlannedModuleIds $script:S)
    $sorted = @($Ids | Sort-Object { $i = [array]::IndexOf($order, [string]$_); if ($i -lt 0) { 1000 } else { $i } }, { [string]$_ })
    foreach ($X in $sorted) {
        if (Test-Path -LiteralPath (Join-Path $script:PageDir "runs\Module$X")) { [void](Render-View 'module-result' $X -Runner) }
    }
}

function Invoke-RunPageDetach {
    # Launches the run as a detached Windows PowerShell 5.1 child and returns once it holds the page lock.
    Assert-Page -Create
    $ls = Get-LockState
    if ($ls.Alive) { Stop-Lifecycle 1 ("run already in progress (pid {0})" -f (Get-Int $ls.Info.pid)) }
    if (-not (Test-Path -LiteralPath $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null }
    $ts = Get-Date -Format 'yyyyMMdd-HHmmss'
    $console = Join-Path $script:LogDir ("run-page-$ts.console.log")
    $consoleErr = Join-Path $script:LogDir ("run-page-$ts.console.err.log")
    $mods = @()
    foreach ($m in @($Modules)) { foreach ($p in ([string]$m -split '[,;\s]+')) { if ($p) { $mods += $p.Trim().ToUpperInvariant() } } }
    $argString = "-NoProfile -ExecutionPolicy Bypass -File `"" + (Join-Path $ScriptDir 'lifecycle.ps1') + "`" -Action run-page -Page $Page"
    # [review N3] a bare -Modules would fail parameter binding in the child.
    if ($mods.Count -gt 0) { $argString += ' -Modules ' + ($mods -join ',') }
    $argString += " -MaxParallel $MaxParallel"
    if ($Screenshots) { $argString += " -Screenshots $Screenshots" }
    $launch = (Get-Date).AddSeconds(-1)
    $p = Start-Logged -NoWait -FilePath 'powershell.exe' -ArgString $argString -Log $console -ErrLog $consoleErr -WorkingDirectory $RepoRoot
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $ls = Get-LockState
        $info = $ls.Info
        if ($info -and $info.action -eq 'run-page' -and $info.runId) {
            $at = ConvertTo-DateSafe $info.startedAt
            if ($at -and $at -ge $launch) {
                Write-Host ("[run-page] detached runner pid={0} runId={1}; console log {2}" -f (Get-Int $info.pid), $info.runId, (Get-RelPath $console))
                Write-Host ("[run-page] next: .\.kiro\test-project\lifecycle.ps1 -Action wait -Page {0} -TimeoutSec 90" -f $Page)
                return
            }
        }
        if ($p.HasExited) { break }
    }
    Write-Host ("[run-page] FAIL detached runner did not take the page lock within 20 s (child exited: {0}); last lines of {1}:" -f ([string]$p.HasExited).ToLowerInvariant(), (Get-RelPath $console)) -ForegroundColor Red
    foreach ($l in @(Get-FileTail $console 20)) { Write-Host ("  " + $l) }
    foreach ($l in @(Get-FileTail $consoleErr 5)) { Write-Host ("  " + $l) }
    Stop-Lifecycle 1 ("detached runner did not start; see " + (Get-RelPath $console))
}

function Invoke-RunPage {
    if ($Detach) { Invoke-RunPageDetach; return }
    $runId = Start-PageRun 'run-page'
    $s = $script:S
    $c = $script:Ctx
    # Versioned runs: continue the current run only when it is unfinished and recent (workflow run, retry,
    # interruption); another full manual run-page (no -Modules) after a run-page always starts a new run.
    $newRun = (-not (Test-SessionResumable $s)) -or (@($Modules).Count -eq 0 -and $s.session -and (Get-Int $s.session.runPages) -ge 1)
    [void](Open-Phase $c.Timings 2 'run-page')
    Save-Timings
    $s.state = 'running'
    Set-ProgressState 'RUNNING' 'Doctor (environment check)' 'Start apps (service, reference, target)' 'estimating...'
    Add-StatusEvent $s "run-page $runId started"
    Save-Status
    Update-ProgressFile
    $pre = [Diagnostics.Stopwatch]::StartNew()

    # (0) doctor
    $doc = Invoke-Doctor -BuildPending
    Write-DoctorOutput $doc
    if ($doc.exitCode -ne 0) { Stop-Lifecycle 3 (Get-FirstDoctorFailure $doc) }

    # (1) session rule, then app preflight
    Update-Session $runId -ForceNew:$newRun
    $c.RunEntry.sessionId = $s.session.id
    Set-ProgressState 'RUNNING' 'Checking apps (service, reference, target)' 'Build the test projects'
    Save-Status
    Update-ProgressFile
    [void](Invoke-EnsureApps)
    if ((Test-AuthRequired) -and -not (Test-AuthState ([string](Get-Nested (Get-SettingsData) @('AuthStateFilePath'))))) {
        Stop-Lifecycle 3 'Auth state missing or expired. Run .\.kiro\test-project\lifecycle.ps1 -Action preflight once to sign in'
    }
    $pre.Stop()
    $c.RunEntry.preflightMs = [long]$pre.ElapsedMilliseconds

    # (2) modules, builds (parallel), browsers, test methods
    $ids = @(Resolve-RunModules $Modules)
    $c.RunModules = $ids
    foreach ($X in $ids) { [void]$c.RunEntry.modules.Add($X) }
    $runnable = @(Initialize-ModuleRun $ids @('Reference', 'Target') 'failed')

    # (3) schedule: read-only modules in parallel (Ref + Tgt together), mutating ones serialized
    $sched = Get-Schedule $runnable
    Write-Line ("[schedule] parallel (read-only): {0} | serialized (mutating): {1} | max parallel modules: {2}" -f (Format-IdList $sched.readOnly), (Format-IdList $sched.mutating), $MaxParallel) 'Cyan'
    Add-StatusEvent $s ("Schedule: parallel (read-only) {0}; serialized (mutating) {1}; max parallel {2}" -f (Format-IdList $sched.readOnly), (Format-IdList $sched.mutating), $MaxParallel)
    foreach ($X in @($sched.readOnly)) { [void](New-TestJob $X 'Reference' 'read-only'); [void](New-TestJob $X 'Target' 'read-only') }
    foreach ($X in @($sched.mutating)) { [void](New-TestJob $X 'Reference' 'mutating'); [void](New-TestJob $X 'Target' 'mutating') }
    Invoke-DirtyDataGuard

    if (@($sched.readOnly).Count -gt 0) { Invoke-ParallelBatch @($sched.readOnly) { param($X) Complete-ModuleRun $X } }

    foreach ($X in @($sched.mutating)) {
        [void](Get-List $c.RunEntry 'mutating').Add($X)
        Save-Timings
        $rj = $c.Jobs["$X/Reference"]
        Start-TestJob $rj
        Wait-Jobs @($rj.key)
        [void](Restart-DataApps "after Module $X Reference")
        $why = Get-BaselineFailure $s.modules[$X] $rj.result
        $tj = $c.Jobs["$X/Target"]
        if ($why) { Skip-TestJob $tj 'skipped: Reference baseline failed' }
        else {
            Start-TestJob $tj
            Wait-Jobs @($tj.key)
            [void](Restart-DataApps "after Module $X Target")
        }
        Complete-ModuleRun $X
    }

    # (4) module results in plan order, run summary (runner renders; never abort the run), then exit precedence
    Set-ProgressState 'RUNNING' 'Run summary' '-' ''
    Publish-ModuleResults @($runnable)
    [void](Render-View 'run-summary' '' -Runner)
    Complete-ScheduledRun 'failed'
}

function Complete-ModuleRerun([string]$X) {
    $c = $script:Ctx
    $s = $script:S
    $c.Notified[$X] = $true
    [void](Invoke-Reports $X)
    [void](Merge-Summary $X -ClearHarnessError)
    Sync-CssGroupCounts
    $round = Get-Int $s.fixLoop.round
    if ($round -gt 0) {
        $entry = $null
        foreach ($h in @(Get-Items $s.fixLoop 'history')) { if ((Get-Int $h.round) -eq $round) { $entry = $h } }
        if ($entry) {
            $me = Get-Dict (Get-Dict $entry 'modules') $X
            $me.openFindingsAfter = Get-Int $s.modules[$X].openFindings
            $me.actionableCssAfter = Get-Int $s.modules[$X].actionableCssDiffs
            Write-Line ("[fix-round] round {0} Module {1}: findings {2} -> {3}, CSS {4} -> {5}" -f $round, $X, $me.openFindingsBefore, $me.openFindingsAfter, $me.actionableCssBefore, $me.actionableCssAfter)
        }
    }
    Save-Status
    Update-ProgressFile
    $m = $s.modules[$X]
    Write-Line ("[done] Module {0}: Target {1}, findings {2}, CSS groups {3}, round {4}" -f $X, (Format-SystemCell $m.target), $m.openFindings, $m.actionableCssDiffs, $round)
}

function Invoke-RerunTarget {
    $req = @()
    foreach ($m in (@($Modules) + @($Module))) { if ($m) { $req += [string]$m } }
    if ($req.Count -eq 0) { Stop-Lifecycle 1 '-Modules A,B (or -Module X) is required for rerun-target' }
    $runId = Start-PageRun 'rerun-target'
    $s = $script:S
    $c = $script:Ctx
    $s.state = 'fixing'
    Set-ProgressState 'RUNNING' 'Doctor (environment check)' ("Target rerun: " + ($req -join ', ')) 'estimating...'
    Add-StatusEvent $s ("rerun-target {0} ({1}) started" -f ($req -join ', '), $runId)
    Save-Status
    Update-ProgressFile
    $pre = [Diagnostics.Stopwatch]::StartNew()

    $doc = Invoke-Doctor -BuildPending
    Write-DoctorOutput $doc
    if ($doc.exitCode -ne 0) { Stop-Lifecycle 3 (Get-FirstDoctorFailure $doc) }
    $ids = @(Resolve-RunModules $req)
    $c.RunModules = $ids
    foreach ($X in $ids) { [void]$c.RunEntry.modules.Add($X) }

    # service + reference only, then the Reference baseline requirement per module
    [void](Invoke-EnsureApps -SkipTarget)
    $todo = @()
    foreach ($X in $ids) {
        $refFile = Join-Path $script:PageDir ("runs\Module$X\Reference\results.json")
        $refRes = $null
        $refReason = 'no Reference results.json (run run-page for this module first)'
        if (Test-Path -LiteralPath $refFile) { try { $refRes = Read-JsonFile $refFile } catch { $refReason = "unreadable Reference results.json: $($_.Exception.Message)" } }
        $why = Get-BaselineFailure $s.modules[$X] @{ Results = $refRes; TimedOut = $false; Reason = $refReason }
        if ($why) {
            $script:BaselineFailed[$X] = $why
            Write-Line ("[{0}/Reference] baseline unavailable: {1} (Target not rerun)" -f $X, $why) 'Red'
        } else { $todo += $X }
    }

    # Target: restart only when down or stale
    $targetNames = @(@(Get-Apps (Get-SettingsData)) | Where-Object { $_.isTarget } | ForEach-Object { $_.name })
    $restarted = $false
    if ($targetNames.Count -gt 0) { $restarted = [bool](Invoke-EnsureApps -Only $targetNames) }
    Write-Line ("target-restarted=" + ([string]$restarted).ToLowerInvariant())
    $pre.Stop()
    $c.RunEntry.preflightMs = [long]$pre.ElapsedMilliseconds

    if ($todo.Count -gt 0) {
        $runnable = @(Initialize-ModuleRun $todo @('Target') 'unavailable')
        foreach ($X in $runnable) {
            $tgtFile = Join-Path $script:PageDir ("runs\Module$X\Target\results.json")
            if (Test-Path -LiteralPath $tgtFile) {
                Copy-Item -LiteralPath $tgtFile -Destination (Join-Path $script:PageDir ("runs\Module$X\Target\results.prev.json")) -Force
                Write-Line ("[{0}/Target] previous results saved as runs/Module{0}/Target/results.prev.json" -f $X)
            }
            # Findings as they were before this fix round (first rerun of the round wins): the final
            # report shows severity and values of findings the round resolved.
            $sumFile = Join-Path $script:PageDir ("runs\Module$X\summary.json")
            $snapName = ("summary.before-round{0}.json" -f (Get-Int $s.fixLoop.round))
            $snap = Join-Path $script:PageDir ("runs\Module$X\$snapName")
            if ((Test-Path -LiteralPath $sumFile) -and -not (Test-Path -LiteralPath $snap)) {
                Copy-Item -LiteralPath $sumFile -Destination $snap -Force
                Write-Line ("[{0}/Target] findings before the rerun saved as runs/Module{0}/{1}" -f $X, $snapName)
            }
        }
        $sched = Get-Schedule $runnable
        Write-Line ("[schedule] parallel (read-only): {0} | serialized (mutating): {1} | max parallel modules: {2}" -f (Format-IdList $sched.readOnly), (Format-IdList $sched.mutating), $MaxParallel) 'Cyan'
        foreach ($X in @($sched.readOnly)) { [void](New-TestJob $X 'Target' 'read-only') }
        foreach ($X in @($sched.mutating)) { [void](New-TestJob $X 'Target' 'mutating') }
        Invoke-DirtyDataGuard
        if (@($sched.readOnly).Count -gt 0) { Invoke-ParallelBatch @($sched.readOnly) { param($X) Complete-ModuleRerun $X } }
        foreach ($X in @($sched.mutating)) {
            [void](Get-List $c.RunEntry 'mutating').Add($X)
            Save-Timings
            $tj = $c.Jobs["$X/Target"]
            Start-TestJob $tj
            Wait-Jobs @($tj.key)
            [void](Restart-DataApps "after Module $X Target rerun")
            Complete-ModuleRerun $X
        }
    }
    Save-Status
    Complete-ScheduledRun 'unavailable'
}

function Invoke-FixRound {
    Assert-Page -Create
    Enter-Lock 'fix-round'
    $script:S = Read-Status -Create
    $s = $script:S
    $fl = $s.fixLoop
    $round = Get-Int $fl.round
    $max = Get-Int $fl.maxRounds
    if ($max -le 0) { $max = 2; $fl.maxRounds = 2 }
    # Phase 3 (Fix) for the loop; after the final gate opened, a 'fix <what>' round is post-gate time.
    Update-TimingsLifecycle { param($T) [void](Open-Phase $T 3 ("fix-round " + $Source)) }
    if ($Source -eq 'loop' -and $round -ge $max) {
        Add-StatusEvent $s ("fix-round: budget exhausted (round {0} of {1}); remaining findings go to the final report" -f $round, $max)
        Save-Status
        Update-ProgressFile
        Write-Host ("round={0} budget-exhausted" -f $round)
        return
    }
    $round++
    $fl.round = $round
    $mods = [ordered]@{}
    foreach ($id in @($s.modules.Keys)) {
        $m = $s.modules[$id]
        if ((Get-Int $m.openFindings) -gt 0 -or (Get-Int $m.actionableCssDiffs) -gt 0) {
            $mods[$id] = [ordered]@{ openFindingsBefore = (Get-Int $m.openFindings); actionableCssBefore = (Get-Int $m.actionableCssDiffs) }
        }
    }
    [void](Get-List $fl 'history').Add([ordered]@{ round = $round; source = $Source; at = (Get-NowIso); modules = $mods })
    $s.state = 'fixing'
    $modText = (@($mods.Keys) -join ', ')
    if (-not $modText) { $modText = 'none' }
    Set-ProgressState 'RUNNING' ("Fix round {0} of {1} ({2}): modules with open items: {3}" -f $round, $max, $Source, $modText) 'Re-run Target for the fixed modules'
    Add-StatusEvent $s ("Fix round {0} ({1}) started; modules: {2}" -f $round, $Source, $modText)
    Save-Status
    Update-ProgressFile
    Write-Host ("round={0}" -f $round)
}

function Read-ResponseText {
    if (-not $ResponseFile) { return $null }
    $rp = Resolve-RepoPath $ResponseFile
    if (-not (Test-Path -LiteralPath $rp)) { Stop-Lifecycle 1 "ResponseFile not found: $ResponseFile" }
    $text = [IO.File]::ReadAllText($rp, $Utf8NoBom)
    if ($text.Length -gt 2000) { $text = $text.Substring(0, 2000) }
    return $text.Trim()
}

function Invoke-Gate {
    Assert-Page -Create
    if (-not $Gate -or -not $State) { Stop-Lifecycle 1 'gate requires -Gate plan_signoff|final_signoff|baseline_failed and -State pending|confirmed' }
    if ($State -eq 'pending' -and ($Gate -eq 'final_signoff' -or $Gate -eq 'baseline_failed') -and -not $ReportPath) { Stop-Lifecycle 1 "-ReportPath is required for -Gate $Gate -State pending" }
    $response = Read-ResponseText
    Enter-Lock 'gate' -Wait
    $script:S = Read-Status -Create
    $s = $script:S
    if ($State -eq 'confirmed' -and $s.gate.pending -eq $true -and $s.gate.name -and $s.gate.name -ne $Gate) {
        Stop-Lifecycle 1 ("Gate {0} is pending; cannot confirm {1}" -f $s.gate.name, $Gate)
    }
    $plan = $null
    if ($Gate -eq 'plan_signoff') {
        $plan = Read-Plan
        if (-not $plan) { Stop-Lifecycle 1 'state-map/plan.json not found (required for plan_signoff)' }
    }
    $lastResponse = $s.gate.lastResponse
    if ($null -ne $response) { $lastResponse = $response }
    $now = Get-NowIso
    $key = "$Gate/$State"
    switch ($key) {
        'plan_signoff/pending' {
            # Before Import-PlanModules: compares the new plan with the modules recorded so far.
            $rev = Update-PlanRevision $s $plan
            Import-PlanModules $s $plan
            $q = "Review the test plan (state-map/plan.json revision $rev, modules $(@($s.plan.modules) -join ', ')) and reply confirm, or describe changes."
            if ($Message) { $q = $Message }
            $rpath = 'state-map/plan.json'
            if ($ReportPath) { $rpath = $ReportPath }
            $s.gate = [ordered]@{ pending = $true; name = $Gate; since = $now; question = $q; reportPath = $rpath; lastResponse = $lastResponse }
            $s.state = 'plan_pending'
            Set-ProgressState 'WAITING FOR YOU' "Plan revision $rev ready for sign-off" 'Author tests after sign-off' ''
            Add-StatusEvent $s "Gate plan_signoff pending (plan revision $rev)"
        }
        'plan_signoff/confirmed' {
            $rev = Update-PlanRevision $s $plan
            $fp = [string]$s.plan.fingerprint
            if ($s.plan.signedOff -eq $true -and [string]$s.plan.signedOffRevision -eq [string]$rev -and [string]$s.plan.signedOffFingerprint -eq $fp) {
                Add-StatusEvent $s "plan revision $rev re-confirmed"
            } else {
                Import-PlanModules $s $plan
                $s.plan.signedOff = $true
                $s.plan.signedOffAt = $now
                $s.plan.signedOffRevision = $rev
                $s.plan.signedOffFingerprint = $fp
                $s.plan.response = $response
                Add-StatusEvent $s "plan revision $rev signed off"
            }
            $s.gate = New-GateCleared $lastResponse
            $s.state = 'authoring'
            Set-ProgressState 'RUNNING' "Plan revision $rev signed off - authoring tests" 'Run modules (run-page)' ''
        }
        'final_signoff/pending' {
            $q = "Review $ReportPath and reply approve, or fix <what>."
            if ($Message) { $q = $Message }
            $s.gate = [ordered]@{ pending = $true; name = $Gate; since = $now; question = $q; reportPath = $ReportPath; lastResponse = $lastResponse }
            $s.finalReport.path = $ReportPath
            $s.state = 'report_pending'
            Set-ProgressState 'WAITING FOR YOU' 'Final report ready for sign-off' '-' ''
            Add-StatusEvent $s "Gate final_signoff pending ($ReportPath)"
        }
        'final_signoff/confirmed' {
            $s.finalReport.signedOff = $true
            $s.finalReport.signedOffAt = $now
            $s.finalReport.response = $response
            $s.gate = New-GateCleared $lastResponse
            $s.state = 'complete'
            Set-ProgressState 'COMPLETE' 'Final report signed off' '-' ''
            Add-StatusEvent $s 'Final report signed off'
        }
        'baseline_failed/pending' {
            $q = "Reference baseline failed. Review $ReportPath and reply retry, a correction (e.g. 'correct the expected value to X'), or stop."
            if ($Message) { $q = $Message }
            $s.gate = [ordered]@{ pending = $true; name = $Gate; since = $now; question = $q; reportPath = $ReportPath; lastResponse = $lastResponse }
            $s.state = 'baseline_failed'
            Set-ProgressState 'WAITING FOR YOU' 'Reference baseline failed - waiting for you' '-' ''
            Add-StatusEvent $s "Gate baseline_failed pending ($ReportPath)"
        }
        'baseline_failed/confirmed' {
            $s.gate = New-GateCleared $lastResponse
            $s.state = 'running'
            $short = '(no response file)'
            if ($response) { $short = Limit-Text $response 120 }
            Set-ProgressState 'RUNNING' ("Baseline gate answered: " + $short) 'Re-run the module' ''
            Add-StatusEvent $s ("Gate baseline_failed confirmed: " + $short)
        }
        default { Stop-Lifecycle 1 "Invalid gate/state combination: $key" }
    }
    Update-TimingsLifecycle { param($T) Invoke-GatePhaseHooks $T $Gate $State }
    Save-Status
    Update-ProgressFile
    if ($ResponseFile) { Remove-Item -LiteralPath (Resolve-RepoPath $ResponseFile) -Force -ErrorAction SilentlyContinue }
    Save-RunHistory
    Write-Host ("gate={0} state={1} recorded (status.state={2})" -f $Gate, $State, $s.state)
}

function Invoke-ProgressAction {
    Assert-Page -Create
    if (-not $Message) { Stop-Lifecycle 1 'progress requires -Message' }
    Enter-Lock 'progress' -Wait
    $script:S = Read-Status -Create
    $s = $script:S
    if ($Phase -eq 0) {
        if (Test-SessionResumable $s) { Write-Host ("[session] resuming interrupted session {0}" -f $s.session.id) }
        else { Start-NewSessionForRun $s }
        # Source folders for the agents (never edit paths.reference; Target fixes only under paths.target).
        $sp = Get-SourcePaths (Get-SettingsData)
        $s.paths = $sp
        Write-Host ("[paths] reference {0}, target {1}" -f $(if ($sp.reference) { $sp.reference } else { '(unset)' }), $(if ($sp.target) { $sp.target } else { '(unset)' }))
    }
    if ($Phase -ge 0) {
        Update-TimingsLifecycle {
            param($T)
            if ($Phase -eq 0) {
                # New lifecycle unless the latest one is still in the plan phase (plan revision loop).
                $lc = Get-LatestLifecycle $T
                $reuse = ($null -ne $lc) -and (-not $lc.finishedAt)
                if ($reuse) {
                    foreach ($p in @(Get-Items $lc 'phases')) { if ((Get-Int $p.id) -ge 1 -and @(Get-Items $p 'segments').Count -gt 0) { $reuse = $false } }
                }
                if ($reuse) { [void](Close-OpenGates $lc 'plan_signoff') } else { [void](New-Lifecycle $T) }
            }
            [void](Open-Phase $T $Phase)
        }
        Write-Host ("phase {0} ({1}) marked" -f ($Phase + 1), $PhaseNames[$Phase])
    }
    Set-ProgressState 'RUNNING' $Message $null $null
    if ($Message -match '^HARNESS-ERROR\s+([A-Za-z0-9]+)\s*:\s*(.+)$') {
        $X = $Matches[1].ToUpperInvariant()
        $reason = $Matches[2].Trim()
        if ($s.modules.Contains($X)) { $s.modules[$X].harnessError = $reason; Write-Host ("harnessError recorded for Module {0}" -f $X) }
        else { Write-Host ("WARN: module {0} not in status.json; harnessError not recorded" -f $X) -ForegroundColor Yellow }
    }
    Add-StatusEvent $s $Message
    Save-Status
    Update-ProgressFile
    Write-Host 'progress recorded'
}

function Invoke-ReportAction {
    [void](Start-PageRun 'report')
    $s = $script:S
    $c = $script:Ctx
    [void](Open-Phase $c.Timings 4 'report')
    Save-Timings
    Set-ProgressState 'RUNNING' 'Generating reports for every module + reports/FinalReport.md, .pdf, .docx and TestCaseResults' '-' ''
    Save-Status
    Update-ProgressFile
    [void](Invoke-Reports 'ALL' -Documents)
    $n = 0
    foreach ($X in @($s.modules.Keys)) { if ($null -ne (Merge-Summary $X)) { $n++ } }
    Sync-CssGroupCounts
    $stale = Join-Path $script:PageDir 'reports\FinalReport.data.md'
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force -ErrorAction SilentlyContinue; Write-Line '[report] removed stale reports/FinalReport.data.md' }
    Write-Line ("[report] reports/FinalReport.md written ({0} modules)" -f $n)
}

function Move-ToArchive([string]$Src, [string]$DestDir) {
    $name = Split-Path -Leaf $Src
    $dest = Join-Path $DestDir $name
    try { Move-Item -LiteralPath $Src -Destination $dest -Force -ErrorAction Stop; return $true } catch { Write-Verbose ("Move-Item failed, falling back to per-file move: {0}" -f $_.Exception.Message) }
    if (Test-Path -LiteralPath $Src -PathType Container) {
        # A file inside is in use (e.g. an app log held open): move what we can, copy the rest.
        foreach ($f in @(Get-ChildItem -LiteralPath $Src -Recurse -File -ErrorAction SilentlyContinue)) {
            $rel = $f.FullName.Substring($Src.Length).TrimStart('\')
            $target = Join-Path $dest $rel
            $td = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $td)) { New-Item -ItemType Directory -Path $td -Force | Out-Null }
            try { Move-Item -LiteralPath $f.FullName -Destination $target -Force -ErrorAction Stop }
            catch {
                try { Copy-Item -LiteralPath $f.FullName -Destination $target -Force -ErrorAction Stop; Write-Host ("WARN: {0} is in use; copied (original left in place)" -f (Get-RelPath $f.FullName)) -ForegroundColor Yellow } catch { Write-Verbose ("copy of in-use file failed: {0}" -f $_.Exception.Message) }
            }
        }
        Get-ChildItem -LiteralPath $Src -Recurse -Directory -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { if (@(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue } }
        if (@(Get-ChildItem -LiteralPath $Src -Force -ErrorAction SilentlyContinue).Count -eq 0) { Remove-Item -LiteralPath $Src -Force -ErrorAction SilentlyContinue }
        return $true
    }
    try { Copy-Item -LiteralPath $Src -Destination $dest -Force -ErrorAction Stop; Write-Host ("WARN: {0} is in use; copied" -f (Get-RelPath $Src)) -ForegroundColor Yellow; return $true } catch { return $false }
}

function Invoke-Reset {
    Assert-Page -Create
    Enter-Lock 'reset'
    $old = $null
    try { $old = Read-Status } catch { $old = $null }
    $ts = Get-Date -Format 'yyyyMMdd-HHmmss'
    $archRel = "previous-run/$ts"
    $arch = Join-Path $script:PageDir ("previous-run\$ts")
    $n = 1
    while (Test-Path -LiteralPath $arch) { $archRel = "previous-run/$ts-$n"; $arch = Join-Path $script:PageDir ("previous-run\$ts-$n"); $n++ }
    New-Item -ItemType Directory -Path $arch -Force | Out-Null
    $moved = @()
    foreach ($item in @('status.json', 'fixes.json', 'PROGRESS.md', 'runs', 'reports', 'logs')) {
        $src = Join-Path $script:PageDir $item
        if (Test-Path -LiteralPath $src) { if (Move-ToArchive $src $arch) { $moved += $item } }
    }
    $timings = Get-TimingsPath
    if (Test-Path -LiteralPath $timings) { Copy-Item -LiteralPath $timings -Destination (Join-Path $arch 'timings.json') -Force; $moved += 'timings.json (copy; kept in place)' }

    $s = New-StatusV2 $Page
    $plan = Read-Plan
    if ($plan) { Import-PlanModules $s $plan }
    elseif ($old -and $old.modules -is [System.Collections.IDictionary]) {
        foreach ($id in @($old.modules.Keys)) {
            $om = $old.modules[$id]
            $X = ([string]$id).ToUpperInvariant()
            $steps = Get-Int $om.plannedSteps
            if ($steps -eq 0) { $steps = Get-Int $om.steps }
            $ref = [string](Get-Nested $om @('tests', 'reference'))
            if (-not $ref) { $ref = Get-FallbackTest $X 'Reference' }
            $tgt = [string](Get-Nested $om @('tests', 'target'))
            if (-not $tgt) { $tgt = Get-FallbackTest $X 'Target' }
            $s.modules[$X] = New-ModuleEntry $X ([string]$om.name) $steps (Get-List $om 'cssStates') $ref $tgt
            [void]$s.plan.modules.Add($X)
        }
    }
    $s.previousRun = [ordered]@{ kind = 'reset'; archivedAt = (Get-NowIso); path = $archRel; data = $null }
    $script:S = $s
    Set-ProgressState 'COMPLETE' ("Page reset; previous run archived to {0}" -f $archRel) ("Run test-page for page {0}" -f $Page) ''
    Add-StatusEvent $s ("reset: archived {0} to {1}" -f ($moved -join ', '), $archRel)
    Save-Status
    Update-ProgressFile
    Write-Host ("reset: archived {0} item(s) to .kiro/app-testing/{1}/{2}; timings.json kept; fresh status.json (schema v2, session=null)" -f $moved.Count, $Page, $archRel)
}

function Invoke-PageStatus {
    Assert-Page
    $path = Get-StatusPath
    if (-not (Test-Path -LiteralPath $path)) { Stop-Lifecycle 1 ("status.json not found: .kiro/app-testing/{0}/status.json" -f $Page) }
    $s = Read-Status
    if ((Get-Int $s.schemaVersion) -ne 2) {
        Write-Host ("{0}: status.json is schema v1 (legacy). run-page or reset migrates it to v2." -f $Page)
        Write-Host ("Modules: {0}" -f (@($s.modules.Keys) -join ', '))
        return
    }
    Update-Derived $s
    $st = [string]$s.progress.status
    if ($s.gate.pending) { $st = 'WAITING FOR YOU' }
    $eta = '-'
    if ($st -eq 'RUNNING' -and $s.progress.eta) { $eta = $s.progress.eta }
    Write-Host ("{0}: Status {1} (state {2}, updated {3})" -f $Page, $st, $s.state, (Get-IsoString $s.updatedAt))
    Write-Host ("Now:  " + $s.progress.now)
    Write-Host ("Next: " + $s.progress.next)
    Write-Host (Get-CurrentPhaseLine)
    if ($st -eq 'RUNNING' -and $eta -ne '-') { Write-Host ("Run estimate: {0} remaining" -f $eta) }
    foreach ($id in @($s.modules.Keys)) {
        $m = $s.modules[$id]
        $line = ("Module {0}: Reference {1} | Target {2} | findings {3} | CSS groups {4} | complete {5}" -f $id, (Format-SystemCell $m.reference), (Format-SystemCell $m.target), (Get-Int $m.openFindings), (Get-Int $m.actionableCssDiffs), ([string]$m.complete).ToLowerInvariant())
        if ($m.harnessError) { $line += " | HARNESS-ERROR: " + $m.harnessError }
        Write-Host $line
    }
    if ($s.gate.pending) { Write-Host ("Needs you: [{0}] {1} (since {2})" -f $s.gate.name, $s.gate.question, (Get-IsoString $s.gate.since)) }
    Write-Host ("Fix loop: round {0} of {1}, clean {2}; plan signedOff {3} (revision {4})" -f $s.fixLoop.round, $s.fixLoop.maxRounds, ([string]$s.fixLoop.clean).ToLowerInvariant(), ([string]$s.plan.signedOff).ToLowerInvariant(), $s.plan.signedOffRevision)
    Write-Host ("Progress: .kiro/app-testing/{0}/PROGRESS.md" -f $Page)
}

# ============================================================================
# Actions: build-module / render / wait
# ============================================================================
function Get-ModuleArg {
    if (-not $Module) { return '' }
    $X = $Module.Trim().ToUpperInvariant()
    if ($X -notmatch '^[A-Z0-9]+$') { Stop-Lifecycle 1 "Invalid -Module '$Module' (letters/digits only)" }
    return $X
}

function Invoke-BuildModule {
    # Author tool: no page lock; writes only Modules/{Page}.Mod{X}/ (csproj when missing) and runs/Module{X}/build.json.
    Assert-Page -Create
    $X = Get-ModuleArg
    if (-not $X) { Stop-Lifecycle 1 '-Module is required for build-module' }
    if (-not (Test-Path -LiteralPath $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null }
    $proj = Get-ModuleProject $X
    if (-not (Test-Path -LiteralPath $proj)) {
        $d = Split-Path -Parent $proj
        if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        [IO.File]::WriteAllText($proj, (Get-ModuleCsprojText $X), $Utf8NoBom)
        Write-Host ("[build-module] created Modules/{0}.Mod{1}/{0}.Mod{1}.csproj" -f $Page, $X)
    }
    [void](Invoke-HarnessBuild)
    $res = Invoke-ModuleBuilds @($X)
    $r = $res[$X]
    $b = $r.build
    $secs = ([Math]::Round($r.ms / 1000.0, 1)).ToString([Globalization.CultureInfo]::InvariantCulture)
    if (-not $r.ok) {
        Write-Host ("[build-module] {0} FAIL {1}s ({2}); log=logs/build-{0}.log" -f $X, $secs, $r.why) -ForegroundColor Red
        foreach ($e in @($r.errors)) { Write-Host ("  " + $e) -ForegroundColor Red }
        Stop-Lifecycle 1 ("build-module {0} failed: {1}; see logs/build-{0}.log" -f $X, $r.why)
    }
    $missing = @()
    foreach ($sys in @('reference', 'target')) {
        $t = $b[$sys]
        if (-not $t.exists) {
            Write-Host ("[build-module] WARN {0} {1} test file not authored yet: {2}" -f $X, $sys, $t.file) -ForegroundColor Yellow
        } elseif (-not $t.methodFound) {
            $missing += ("method 'public async Task {0}(' not found in {1}" -f $t.test, $t.file)
        }
    }
    $ref = $b.reference
    $tgt = $b.target
    $refText = if ($ref.exists) { "Ref {0} steps/{1} assertion calls" -f $ref.steps, $ref.assertionCalls } else { 'Ref not authored' }
    $tgtText = if ($tgt.exists) { "Tgt {0}/{1}" -f $tgt.steps, $tgt.assertionCalls } else { 'Tgt not authored' }
    $rev = $ref.revision
    if ($null -eq $rev) { $rev = $tgt.revision }
    $revText = if ($null -eq $rev) { 'rev none' } else { "rev $rev" }
    if ($null -ne $ref.revision -and $null -ne $tgt.revision -and $ref.revision -ne $tgt.revision) { $revText = "rev Ref $($ref.revision)/Tgt $($tgt.revision)" }
    if ($missing.Count -gt 0) {
        Write-Host ("[build-module] {0} FAIL test method not found ({1}, {2}, {3})" -f $X, $refText, $tgtText, $revText) -ForegroundColor Red
        foreach ($m in $missing) { Write-Host ("  " + $m) -ForegroundColor Red }
        Stop-Lifecycle 1 ("build-module {0}: {1}" -f $X, ($missing -join '; '))
    }
    Write-Host ("[build-module] {0} PASS {1}s ({2}, {3}, {4})" -f $X, $secs, $refText, $tgtText, $revText) -ForegroundColor Green
    Write-Host ("[build-module] wrote .kiro/app-testing/{0}/runs/Module{1}/build.json (attempts {2})" -f $Page, $X, $b.attempts)
}

function Get-ChatIndex([string]$Path) {
    $idx = $null
    try { $idx = Read-JsonFile $Path } catch { $idx = $null }
    if (-not ($idx -is [System.Collections.IDictionary])) { $idx = [ordered]@{ schemaVersion = 1; page = $Page; entries = [System.Collections.ArrayList]::new() } }
    [void](Get-List $idx 'entries')
    return $idx
}

function Invoke-RenderCore([string]$View, [string]$X, [bool]$Runner) {
    # Returns @{ Ok; Entry; Abs; Error; Log }. Throws only for Harness/Reports build failures.
    $chatDir = Join-Path $script:PageDir 'chat'
    if (-not (Test-Path -LiteralPath $chatDir)) { New-Item -ItemType Directory -Path $chatDir -Force | Out-Null }
    if ($Runner) {
        # [review M1] the renderer reads status.json / timings.json from disk: flush the in-memory run first.
        Save-Status -NonFatal
        Save-Timings
    }
    [void](Invoke-HarnessBuild -WithReports)
    $progressLine = Get-CurrentPhaseLine
    $wall = $null
    if ($Runner -and $View -eq 'run-summary' -and $script:Ctx) {
        $st = ConvertTo-DateSafe $script:Ctx.RunEntry.startedAt
        if ($st) { $wall = [string][long]((Get-Date) - $st).TotalMilliseconds }
    }
    $mtx = New-Object System.Threading.Mutex($false, ("Local\MigrationTests.{0}.chat" -f $Page))
    $acquired = $false
    try {
        $acquired = Enter-NamedMutex $mtx 30000 'chat'
        if (-not $acquired) { return @{ Ok = $false; Error = 'chat lock timeout'; Log = '' } }
        $idxPath = Join-Path $chatDir 'index.json'
        $idx = Get-ChatIndex $idxPath
        $seq = 0
        foreach ($e in @(Get-Items $idx 'entries')) { if ((Get-Int $e.seq) -gt $seq) { $seq = Get-Int $e.seq } }
        $seq++
        $name = ('{0:D2}-{1}' -f $seq, $View)
        if ($X) { $name += "-$X" }
        $name += '.md'
        $out = Join-Path $chatDir $name
        $log = Join-Path $script:LogDir ('render-{0:D2}.log' -f $seq)
        $err = Join-Path $script:LogDir ('render-{0:D2}.err.log' -f $seq)
        $argString = "`"$ReportsDll`" render --page-dir `"$($script:PageDir)`" --view $View"
        if ($X) { $argString += " --module $X" }
        $argString += " --out `"$out`""
        $t0 = (Get-Date).AddSeconds(-2)
        $saved = Set-TestEnv @{ LIFECYCLE_PROGRESS_LINE = $progressLine; LIFECYCLE_RUN_WALL_MS = $wall }
        try { $r = Start-Logged -FilePath 'dotnet' -ArgString $argString -Log $log -ErrLog $err -TimeoutSec $ReportTimeoutSec }
        finally { Restore-TestEnv $saved }
        $logRel = 'logs/' + (Split-Path -Leaf $log)
        $ok = (-not $r.TimedOut) -and ($r.ExitCode -eq 0) -and (Test-Path -LiteralPath $out) -and ((Get-Item -LiteralPath $out).LastWriteTime -ge $t0)
        if (-not $ok) {
            $first = @(@(Get-FileTail $err 50) | Where-Object { $_.Trim() } | Select-Object -First 1)
            $why = if ($first.Count -gt 0) { Limit-Text $first[0] 200 } elseif ($r.TimedOut) { 'timeout' } else { "tool exit $($r.ExitCode), no output file" }
            return @{ Ok = $false; Error = $why; Log = $logRel; ToolExit = $r.ExitCode }
        }
        $summary = ''
        $partFiles = @()
        foreach ($l in @((Get-SharedText $log) -split "`r?`n")) {
            if ($l -match '^SUMMARY:\s*(.*)$') { $summary = $Matches[1].Trim() }
            elseif ($l -match '^PART:\s*(.+)$') { $partFiles += $Matches[1].Trim() }
        }
        $runId = $null
        if ($Runner -and $script:Ctx) { $runId = $script:Ctx.RunId }
        else {
            $stDisk = $null
            try { $stDisk = Read-JsonFile (Get-StatusPath) } catch { $stDisk = $null }
            $runId = [string](Get-Nested $stDisk @('currentRun', 'runId'))
            if (-not $runId) { $runId = $null }
        }
        # P4: one index entry per part (same seq, part 0..n), relayed in this order.
        $entries = @()
        $absAll = @($out) + @($partFiles)
        for ($pi = 0; $pi -lt $absAll.Count; $pi++) {
            $leaf = Split-Path -Leaf $absAll[$pi]
            $entries += [ordered]@{
                seq = $seq; part = $pi; view = $View; module = $(if ($X) { $X } else { $null }); path = "chat/$leaf"; at = (Get-NowIso)
                summary = $summary; source = $(if ($Runner) { 'runner' } else { 'agent' }); runId = $runId
            }
        }
        foreach ($e in $entries) { [void](Get-List $idx 'entries').Add($e) }
        if (-not (Write-AtomicFile $idxPath (ConvertTo-JsonText $idx))) { return @{ Ok = $false; Error = 'chat/index.json write failed'; Log = $logRel } }
        return @{ Ok = $true; Entry = $entries[0]; Entries = $entries; Abs = $out; AbsAll = $absAll; Log = $logRel }
    } finally {
        if ($acquired) { try { $mtx.ReleaseMutex() } catch { Write-Verbose ("chat mutex release failed: {0}" -f $_.Exception.Message) } }
        $mtx.Dispose()
    }
}

function Render-View([string]$View, [string]$X = '', [switch]$Runner) {
    # Shared by -Action render (agent) and the runner. With -Runner a failure never aborts the run:
    # WARN + status event 'render-failed', no index entry, exit code unchanged.
    $label = $View
    if ($X) { $label += " $X" }
    $savedCode = $script:ExitCode
    $savedMsg = $script:ExitMessage
    $res = $null
    try { $res = Invoke-RenderCore $View $X ([bool]$Runner) }
    catch {
        if (-not $Runner) { throw }
        $why = $_.Exception.Message
        if (Test-IsLifecycleExit $_) { $why = $script:ExitMessage }
        $script:ExitCode = $savedCode
        $script:ExitMessage = $savedMsg
        $res = @{ Ok = $false; Error = $why; Log = '' }
    }
    if ($res.Ok) {
        if ($Runner) { foreach ($e in @($res.Entries)) { Write-Line ("[render] {0} -> .kiro/app-testing/{1}/{2}" -f $label, $Page, $e.path) } }
        else {
            # One CHAT-RENDER line per part, in relay order, then the summary line.
            foreach ($a in @($res.AbsAll)) { Write-Host ("CHAT-RENDER " + $a) }
            Write-Host $res.Entry.summary
        }
        return $res.Entry
    }
    if ($Runner) {
        $see = ''
        if ($res.Log) { $see = "; see " + $res.Log }
        Write-Line ("WARN: render {0} failed: {1}{2}" -f $label, $res.Error, $see) 'Yellow'
        if ($script:S) {
            $rid = $null
            if ($script:Ctx) { $rid = $script:Ctx.RunId }
            Add-StructuredEvent $script:S ([ordered]@{ type = 'render-failed'; view = $View; module = $(if ($X) { $X } else { $null }); message = $res.Error; runId = $rid })
            Add-StatusEvent $script:S ("render {0} failed: {1}" -f $label, $res.Error)
            Save-Status -NonFatal
        }
        return $null
    }
    Write-Host ("[render] FAIL {0}" -f $res.Error) -ForegroundColor Red
    if ($res.Log) { Write-Host ("[render] log: .kiro/app-testing/{0}/{1}" -f $Page, $res.Log) }
    $script:QuietExit = $true
    Stop-Lifecycle 1 ("[render] FAIL " + $res.Error)
}

function Invoke-RenderAction {
    Assert-Page
    if (-not (Test-Path -LiteralPath $script:PageDir)) { Stop-Lifecycle 1 ("page folder not found: .kiro/app-testing/{0}" -f $Page) }
    if (-not $View) { Stop-Lifecycle 1 '-View plan|tests-authored|live-status|module-result|run-summary|fix-round|final is required for render' }
    $X = Get-ModuleArg
    if ($View -eq 'module-result' -and -not $X) { Stop-Lifecycle 1 '-Module is required for -View module-result' }
    [void](Render-View $View $X)
    Save-RunHistory
}

function Invoke-Wait {
    # Blocks (<= -TimeoutSec) until the waited run publishes new runner renders, finishes or crashes.
    # No page lock; writes only chat/relay.json (as the last action before printing).
    Assert-Page
    if (-not (Test-Path -LiteralPath $script:PageDir)) { Stop-Lifecycle 1 ("page folder not found: .kiro/app-testing/{0}" -f $Page) }
    $script:QuietExit = $true
    $chatDir = Join-Path $script:PageDir 'chat'
    $relayPath = Join-Path $chatDir 'relay.json'
    $idxPath = Join-Path $chatDir 'index.json'
    $lastRunPath = Join-Path $script:PageDir 'runs\last-run.json'
    $relay = $null
    try { $relay = Read-JsonFile $relayPath } catch { $relay = $null }
    $cursor = 0
    if ($relay) { $cursor = Get-Int $relay.lastSeq }
    $ls = Get-LockState
    $runId = ''
    if ($ls.Info -and $ls.Info.action -eq 'run-page' -and $ls.Info.runId) { $runId = [string]$ls.Info.runId }
    else {
        $stDisk = $null
        try { $stDisk = Read-JsonFile (Get-StatusPath) } catch { $stDisk = $null }
        $runId = [string](Get-Nested $stDisk @('currentRun', 'runId'))
    }
    $start = Get-Date
    $deadline = $start.AddSeconds($TimeoutSec)
    $state = 'running'
    $lastRun = $null
    $entries = @()
    while ($true) {
        $idx = $null
        try { $idx = Read-JsonFile $idxPath } catch { $idx = $null }
        # [review M2] only renders of the waited run, written by the runner.
        $entries = @(@(Get-Items $idx 'entries') | Where-Object { $_ -and $_.source -eq 'runner' -and [string]$_.runId -eq $runId -and (Get-Int $_.seq) -gt $cursor } | Sort-Object { Get-Int $_.seq }, { Get-Int $_.part })
        try { $lastRun = Read-JsonFile $lastRunPath } catch { $lastRun = $null }
        if ($runId -and $lastRun -and [string]$lastRun.runId -eq $runId) { $state = 'finished' }
        else {
            $ls = Get-LockState
            $alive = $ls.Alive -and $ls.Info -and ([string]$ls.Info.runId -eq $runId)
            if (-not $alive) {
                # Close the write-then-unlock race: re-read last-run.json once before declaring a crash.
                Start-Sleep -Milliseconds 500
                try { $lastRun = Read-JsonFile $lastRunPath } catch { $lastRun = $null }
                if ($runId -and $lastRun -and [string]$lastRun.runId -eq $runId) { $state = 'finished' } else { $state = 'crashed' }
            }
        }
        if ($state -ne 'running') {
            try { $idx = Read-JsonFile $idxPath } catch { $idx = $null }
            $entries = @(@(Get-Items $idx 'entries') | Where-Object { $_ -and $_.source -eq 'runner' -and [string]$_.runId -eq $runId -and (Get-Int $_.seq) -gt $cursor } | Sort-Object { Get-Int $_.seq }, { Get-Int $_.part })
            break
        }
        if ($entries.Count -gt 0) { break }
        if ((Get-Date) -ge $deadline) { break }
        Start-Sleep -Seconds 2
    }
    $out = @()
    $maxSeq = $cursor
    # One group per render (same seq): every part's CHAT-RENDER line in part order, then the summary once.
    for ($ei = 0; $ei -lt $entries.Count; $ei++) {
        $e = $entries[$ei]
        $out += ("CHAT-RENDER " + (Join-Path $script:PageDir (([string]$e.path) -replace '/', '\')))
        $lastOfGroup = ($ei -eq $entries.Count - 1) -or ((Get-Int $entries[$ei + 1].seq) -ne (Get-Int $e.seq))
        if ($lastOfGroup) { $out += [string]$e.summary }
        if ((Get-Int $e.seq) -gt $maxSeq) { $maxSeq = Get-Int $e.seq }
    }
    $code = 0
    if ($state -eq 'finished') {
        $code = Get-Int $lastRun.exitCode
        $msg = [string]$lastRun.message
        $last = ("[wait] run=finished exit={0}" -f $code)
        if ($msg) { $last += ' ' + $msg }
        $out += $last
    } elseif ($state -eq 'crashed') {
        $code = 1
        $con = @(Get-ChildItem -LiteralPath $script:LogDir -Filter 'run-page-*.console.log' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
        $conText = 'logs/run-page-<ts>.console.log'
        if ($con.Count -gt 0) { $conText = 'logs/' + $con[0].Name }
        $why = 'runner ended without a result'
        if (-not $runId) { $why = 'no run to wait for (no lock and no status.currentRun.runId)' }
        $out += ("[wait] run=crashed exit=1 ({0}; see {1})" -f $why, $conText)
    } elseif ($entries.Count -gt 0) {
        $out += '[wait] run=running'
    } else {
        $out += ("[wait] run=running (no new results in {0}s)" -f [int]((Get-Date) - $start).TotalSeconds)
    }
    # Cursor is advanced as the LAST action before printing: a wait killed earlier re-prints next time.
    if ($maxSeq -gt $cursor -or $null -eq $relay) {
        if (-not (Test-Path -LiteralPath $chatDir)) { New-Item -ItemType Directory -Path $chatDir -Force | Out-Null }
        [void](Write-AtomicFile $relayPath (ConvertTo-JsonText ([ordered]@{ lastSeq = $maxSeq; runId = $runId; at = (Get-NowIso) })))
    }
    foreach ($l in $out) { Write-Host $l }
    $script:ExitCode = $code
    $script:ExitMessage = ''
}

# ============================================================================
# Legacy actions (kept working): status (no -Page), preflight, build, run-module, stop
# ============================================================================
function Write-Result([hashtable]$Result) {
    $Result | ConvertTo-Json -Depth 5 | Set-Content $ResultFile -Encoding UTF8
    $color = if ($Result.status -eq "PASS") { "Green" } elseif ($Result.status -eq "BLOCKED") { "Yellow" } else { "Red" }
    Write-Host "`nRESULT: $($Result.status)" -ForegroundColor $color
    if ($Result.message) { Write-Host $Result.message }
    if ($Result.nextAction) { Write-Host "Next: $($Result.nextAction)" -ForegroundColor Cyan }
}

function Test-ServiceHealth([string]$Url) {
    if (-not $Url) { return $false }
    try {
        $uri = [System.Uri]$Url
        $conn = Get-NetTCPConnection -LocalPort $uri.Port -State Listen -ErrorAction SilentlyContinue
        return ($null -ne $conn -and @($conn).Count -gt 0)
    } catch { return $false }
}

function Invoke-Status {
    Read-Settings
    Write-Host "`n--- STATUS ---"
    Write-Host "  Target URL:      $($Settings.BaseUrl)"
    Write-Host "  Reference URL:   $($Settings.ReferenceBaseUrl)"
    Write-Host "  Auth State:      $($Settings.AuthStateFilePath)"
    $appStates = @()
    Write-Host ""
    foreach ($a in @(Get-Apps (Get-SettingsData))) {
        $up = Test-ServiceHealth $a.healthUrl
        Write-Host ("  {0,-10} {1,-6} {2}" -f $a.name, $(if ($up) { '[OK]' } else { '[DOWN]' }), $a.healthUrl)
        $appStates += , @{ name = $a.name; healthUrl = $a.healthUrl; running = $up }
    }
    $appUp = Test-ServiceHealth $Settings.BaseUrl
    $authOk = Test-AuthState $Settings.AuthStateFilePath
    Write-Host "  Auth:      $(if($authOk){'[OK]'}else{'[EXPIRED]'})"
    Write-Result @{ status="INFO"; apps=$appStates; appRunning=$appUp; authValid=$authOk }
}

function Invoke-Preflight {
    if ($Page) {
        Assert-Page -Create
        Enter-Lock 'preflight'
        $script:S = Read-Status -Create
    } else {
        $script:LogDir = Join-Path $TestProjectDir 'logs'
    }
    Write-Host "`n=== PRE-FLIGHT (doctor + apps + auth) ==="
    $doc = Invoke-Doctor
    Write-DoctorOutput $doc
    if ($doc.exitCode -ne 0) {
        Write-Result @{ status = "FAIL"; step = 'doctor'; message = (Get-FirstDoctorFailure $doc) }
        Stop-Lifecycle 3 (Get-FirstDoctorFailure $doc)
    }
    [void](Invoke-EnsureApps)
    if (Test-AuthRequired) {
        Read-Settings
        if (Test-AuthState $Settings.AuthStateFilePath) {
            Write-Host "  Auth state valid - skipping login"
        } else {
            Write-Host "  +--------------------------------------------------+"
            Write-Host "  | HUMAN ACTION: Complete authentication in browser   |"
            Write-Host "  +--------------------------------------------------+"
            # [review N4] Harness is built inside the build mutex; Harness.Tests then builds without
            # touching project references.
            [void](Invoke-HarnessBuild)
            [void](Invoke-DotnetBuild $HarnessTestsCsproj 'build-harness-tests.log' 'Harness.Tests' '-p:BuildProjectReferences=false')
            Push-Location (Split-Path -Parent $HarnessTestsCsproj)
            & dotnet test "$HarnessTestsCsproj" --filter "FullyQualifiedName~LoginSetupTest" --no-build -- NUnit.NumberOfTestWorkers=1 2>&1 | Out-Null
            $loginExit = $LASTEXITCODE
            Pop-Location
            if ($loginExit -ne 0 -or -not (Test-AuthState $Settings.AuthStateFilePath)) {
                Write-Result @{ status = "BLOCKED"; step = 'auth'; message = "Login failed or auth state missing after login" }
                Stop-Lifecycle 3 'Login failed or auth state missing after login'
            }
        }
    } else {
        Write-Host "  Auth: Auth.LoginUrl is empty - no sign-in needed"
    }
    if ($script:S) {
        Add-StatusEvent $script:S 'preflight passed (doctor + apps)'
        Save-Status
        Update-ProgressFile
    }
    Write-Result @{ status = "PASS"; message = "All systems ready."; nextAction = ".\.kiro\test-project\lifecycle.ps1 -Action run-page -Page <Page> -Modules A" }
}

function Invoke-Build {
    # Legacy: Harness + Reports, then every Modules/* project in parallel; last-build-output.txt from the logs.
    Write-Host "`n=== BUILD ==="
    $script:LogDir = Join-Path $TestProjectDir 'logs'
    $outFile = Join-Path $ScriptDir "last-build-output.txt"
    $ok = $true
    $text = New-Object System.Text.StringBuilder
    try { [void](Invoke-HarnessBuild -WithReports) }
    catch {
        if (-not (Test-IsLifecycleExit $_)) { throw }
        $ok = $false
        [void]$text.AppendLine($script:ExitMessage)
    }
    if ($ok) {
        $items = @()
        foreach ($p in @(Get-ModuleCsprojFiles)) {
            $key = [IO.Path]::GetFileNameWithoutExtension($p)
            $items += , @{ Key = $key; Project = $p; Log = (Join-Path $script:LogDir ("build-$key.log")) }
        }
        $res = Invoke-ProjectBuilds $items
        foreach ($k in @($res.Keys)) {
            $r = $res[$k]
            $line = ("{0}: {1} {2}ms log={3}" -f $k, $(if ($r.ok) { 'PASS' } else { 'FAIL ' + $r.why }), $r.ms, (Get-RelPath $r.log))
            Write-Host ("  " + $line)
            [void]$text.AppendLine($line)
            foreach ($e in @($r.errors)) { [void]$text.AppendLine("  " + $e); Write-Host ("    " + $e) -ForegroundColor Red }
            if (-not $r.ok) { $ok = $false }
        }
    }
    [IO.File]::WriteAllText($outFile, $text.ToString(), $Utf8NoBom)
    if ($ok) {
        Write-Host "  [OK] Build succeeded"
        Write-Result @{ status="PASS"; message="Build succeeded." }
    } else {
        Write-Host "  [FAIL] Build failed"
        Write-Result @{ status="FAIL"; message="Build failed. See last-build-output.txt" }
        Stop-Lifecycle 1 'Build failed. See .kiro/test-project/last-build-output.txt'
    }
}

function Invoke-RunModule {
    if (-not $Page) { Write-Error "-Page required"; exit 1 }
    if (-not $Module) { Write-Error "-Module required"; exit 1 }
    if (-not $System) { Write-Error "-System required (Reference or Target)"; exit 1 }
    Read-Settings

    Write-Host "`n=== RUN: $System $Page Module $Module ==="

    if ((Test-AuthRequired) -and -not (Test-AuthState $Settings.AuthStateFilePath)) {
        Write-Result @{ status="BLOCKED"; message="Auth expired. Run: .\lifecycle.ps1 -Action preflight" }; return
    }

    $filter = $TestFilter
    if (-not $filter) {
        $prefix = if ($System -eq "Reference") { "Reference" } else { "" }
        $filter = "Name=${prefix}${Page}_Mod${Module}"
    }
    Write-Host "  Filter: $filter"

    $X = $Module.Trim().ToUpperInvariant()
    $proj = Get-ModuleProject $X
    if (-not (Test-Path -LiteralPath $proj)) { Write-Result @{ status="FAIL"; message="Module project not found: $proj (run -Action build-module first)" }; Stop-Lifecycle 1 "Module project not found: $proj" }
    $env:APP_TEST_MODE = "validate"
    Push-Location (Split-Path -Parent $proj)
    $output = & dotnet test "$proj" --filter $filter --no-build --logger "console;verbosity=detailed" -- NUnit.NumberOfTestWorkers=1 2>&1
    $exitCode = $LASTEXITCODE
    Pop-Location
    $env:APP_TEST_MODE = $null

    $outputFile = Join-Path $ScriptDir "last-test-output.txt"
    $output | Set-Content $outputFile -Encoding UTF8

    if ($exitCode -eq 0) {
        Write-Host "  [OK] Module $Module PASSED"
        Write-Result @{ status="PASS"; message="$System $Page Module $Module passed."; outputFile=$outputFile }
        Write-Host "Progress: .kiro/app-testing/$Page/PROGRESS.md"
    } else {
        Write-Host "  [FAIL] Module $Module failed"
        Write-Result @{ status="FAIL"; message="$System $Page Module $Module failed."; outputFile=$outputFile }
        Write-Host "Progress: .kiro/app-testing/$Page/PROGRESS.md"
    }
}

function Invoke-Stop {
    Write-Host "`n=== STOP ==="
    Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
        try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; Write-Host "  Stopped PID $($_.Id)" }
        catch { Write-Verbose ("Stop-Process failed: {0}" -f $_.Exception.Message) }
    }
    Write-Result @{ status="PASS"; message="Processes stopped." }
}

# ============================================================================
# MAIN
# ============================================================================
try {
    switch ($Action) {
        "doctor"       { Invoke-DoctorAction }
        "run-page"     { Invoke-RunPage }
        "rerun-target" { Invoke-RerunTarget }
        "fix-round"    { Invoke-FixRound }
        "gate"         { Invoke-Gate }
        "progress"     { Invoke-ProgressAction }
        "report"       { Invoke-ReportAction }
        "reset"        { Invoke-Reset }
        "status"       { if ($Page) { Invoke-PageStatus } else { Invoke-Status } }
        "preflight"    { Invoke-Preflight }
        "build"        { Invoke-Build }
        "run-module"   { Invoke-RunModule }
        "stop"         { Invoke-Stop }
        "build-module" { Invoke-BuildModule }
        "render"       { Invoke-RenderAction }
        "wait"         { Invoke-Wait }
    }
} catch {
    if (-not (Test-IsLifecycleExit $_)) {
        $script:QuietExit = $false
        $script:ExitCode = 1
        $script:ExitMessage = ("Harness exception: {0} (lifecycle.ps1 line {1})" -f $_.Exception.Message, $_.InvocationInfo.ScriptLineNumber)
        if ($script:RunLog) { try { [IO.File]::AppendAllText($script:RunLog, ($_.ScriptStackTrace + "`r`n"), $Utf8NoBom) } catch { Write-Verbose ("run log append failed: {0}" -f $_.Exception.Message) } }
    }
} finally {
    try { Complete-RunAction } catch { Write-Host "WARN: could not finalize status/PROGRESS: $($_.Exception.Message)" -ForegroundColor Yellow }
    Exit-Lock
}
if ($script:QuietExit) {
    # wait / render print their own last line.
} elseif ($script:ExitCode -ne 0) {
    $exitText = "[exit] {0} {1}" -f $script:ExitCode, $script:ExitMessage
    Write-Host $exitText -ForegroundColor Red
    if ($script:RunLog) { try { [IO.File]::AppendAllText($script:RunLog, ((Get-Date).ToString('HH:mm:ss') + ' ' + $exitText + "`r`n"), $Utf8NoBom) } catch { Write-Verbose ("run log append failed: {0}" -f $_.Exception.Message) } }
} elseif ($script:RunAction) {
    Write-Line ("[exit] 0 Progress: .kiro/app-testing/{0}/PROGRESS.md" -f $Page)
}
exit $script:ExitCode
