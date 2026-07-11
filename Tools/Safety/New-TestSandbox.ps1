[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Name,

    [string]$AllowedRoot = 'E:\Avenge_TestSandboxes'
)

$ErrorActionPreference = 'Stop'
$sentinelName = '.avenge-test-sandbox'
$sentinelVersion = 'AvengeTestSandbox:v1'

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathInsideRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoWorkspaceOverlap {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot
    )

    if ($Path.Equals($WorkspaceRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-PathInsideRoot -Path $Path -Root $WorkspaceRoot) -or
        (Test-PathInsideRoot -Path $WorkspaceRoot -Root $Path)) {
        throw "Sandbox path overlaps the workspace: path=$Path workspace=$WorkspaceRoot"
    }
}

if ([System.IO.Path]::GetFileName($Name) -ne $Name -or $Name -in @('.', '..')) {
    throw "Sandbox name must be a single directory name: $Name"
}

$workspaceRoot = Get-CanonicalPath (Join-Path $PSScriptRoot '..\..')
$allowedRootPath = Get-CanonicalPath $AllowedRoot
$sandboxPath = Get-CanonicalPath (Join-Path $allowedRootPath $Name)

Assert-NoWorkspaceOverlap -Path $allowedRootPath -WorkspaceRoot $workspaceRoot
if (-not (Test-PathInsideRoot -Path $sandboxPath -Root $allowedRootPath)) {
    throw "Sandbox must be a strict child of the allowed root: sandbox=$sandboxPath root=$allowedRootPath"
}

if (Test-Path -LiteralPath $sandboxPath) {
    throw "Sandbox already exists: $sandboxPath"
}

if (-not (Test-Path -LiteralPath $allowedRootPath)) {
    New-Item -ItemType Directory -Path $allowedRootPath | Out-Null
}

$rootItem = Get-Item -LiteralPath $allowedRootPath -Force
if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Allowed root is a reparse point: $allowedRootPath"
}

New-Item -ItemType Directory -Path $sandboxPath | Out-Null
$sentinelPath = Join-Path $sandboxPath $sentinelName
$sentinelContent = $sentinelVersion + [Environment]::NewLine + "Path=$sandboxPath" + [Environment]::NewLine
[System.IO.File]::WriteAllText(
    $sentinelPath,
    $sentinelContent,
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Created test sandbox: $sandboxPath"
Write-Output "Sentinel: $sentinelPath"
