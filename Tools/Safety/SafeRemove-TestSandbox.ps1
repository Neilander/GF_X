[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SandboxPath,

    [string]$AllowedRoot = 'E:\Avenge_TestSandboxes',

    [switch]$ConfirmDeletion
)

$ErrorActionPreference = 'Stop'
$sentinelName = '.avenge-test-sandbox'
$sentinelVersion = 'AvengeTestSandbox:v1'

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Equals($pathRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $pathRoot
    }

    return $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathInsideRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $prefix = if ($Root.EndsWith($separator)) { $Root } else { $Root + $separator }
    return $Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoPathOverlap {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ProtectedPath
    )

    if ($Path.Equals($ProtectedPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-PathInsideRoot -Path $Path -Root $ProtectedPath) -or
        (Test-PathInsideRoot -Path $ProtectedPath -Root $Path)) {
        throw "Deletion target overlaps a protected path: target=$Path protected=$ProtectedPath"
    }
}

function Assert-OrdinaryItem {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse point detected; deletion refused: $($Item.FullName)"
    }
}

function Get-ValidatedEntries {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    $directories = [System.Collections.Generic.List[System.IO.DirectoryInfo]]::new()
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $pending = [System.Collections.Generic.Queue[System.IO.DirectoryInfo]]::new()
    $rootItem = Get-Item -LiteralPath $RootPath -Force
    Assert-OrdinaryItem $rootItem
    $pending.Enqueue($rootItem)

    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        $directories.Add($directory)

        foreach ($child in Get-ChildItem -LiteralPath $directory.FullName -Force) {
            Assert-OrdinaryItem $child
            $childPath = Get-CanonicalPath $child.FullName
            if (-not (Test-PathInsideRoot -Path $childPath -Root $RootPath)) {
                throw "Child escaped sandbox root: child=$childPath root=$RootPath"
            }

            if ($child -is [System.IO.DirectoryInfo]) {
                $pending.Enqueue($child)
            }
            else {
                $files.Add($child)
            }
        }
    }

    return [pscustomobject]@{
        Directories = $directories
        Files = $files
    }
}

$workspaceRoot = Get-CanonicalPath (Join-Path $PSScriptRoot '..\..')
$allowedRootPath = Get-CanonicalPath $AllowedRoot
$targetPath = Get-CanonicalPath $SandboxPath

if (-not (Test-Path -LiteralPath $allowedRootPath -PathType Container)) {
    throw "Allowed root does not exist: $allowedRootPath"
}

$allowedRootItem = Get-Item -LiteralPath $allowedRootPath -Force
Assert-OrdinaryItem $allowedRootItem

if (-not (Test-PathInsideRoot -Path $targetPath -Root $allowedRootPath)) {
    throw "Deletion target must be a strict child of the allowed root: target=$targetPath root=$allowedRootPath"
}

foreach ($protectedPath in @(
    $workspaceRoot,
    (Join-Path $workspaceRoot 'Assets'),
    (Join-Path $workspaceRoot 'Packages'),
    (Join-Path $workspaceRoot 'ProjectSettings')
)) {
    Assert-NoPathOverlap -Path $targetPath -ProtectedPath (Get-CanonicalPath $protectedPath)
}

if (-not (Test-Path -LiteralPath $targetPath -PathType Container)) {
    throw "Sandbox does not exist: $targetPath"
}

$sentinelPath = Join-Path $targetPath $sentinelName
if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
    throw "Sandbox sentinel is missing: $sentinelPath"
}

$sentinelItem = Get-Item -LiteralPath $sentinelPath -Force
Assert-OrdinaryItem $sentinelItem
$sentinelLines = [System.IO.File]::ReadAllLines($sentinelPath, [System.Text.Encoding]::UTF8)
if ($sentinelLines.Length -ne 2 -or
    $sentinelLines[0] -ne $sentinelVersion -or
    $sentinelLines[1] -ne "Path=$targetPath") {
    throw "Sandbox sentinel is invalid or belongs to another path: $sentinelPath"
}

$entries = Get-ValidatedEntries -RootPath $targetPath
Write-Output "Validated sandbox: $targetPath"
Write-Output "Files: $($entries.Files.Count)"
Write-Output "Directories: $($entries.Directories.Count)"

if (-not $ConfirmDeletion) {
    Write-Output 'Validation only. Pass -ConfirmDeletion to perform deletion.'
    return
}

if (-not $PSCmdlet.ShouldProcess($targetPath, 'Delete validated test sandbox')) {
    return
}

foreach ($file in $entries.Files) {
    $current = Get-Item -LiteralPath $file.FullName -Force
    Assert-OrdinaryItem $current
    $currentPath = Get-CanonicalPath $current.FullName
    if (-not (Test-PathInsideRoot -Path $currentPath -Root $targetPath)) {
        throw "File escaped sandbox root before deletion: $currentPath"
    }

    Remove-Item -LiteralPath $currentPath -Force
}

$orderedDirectories = $entries.Directories | Sort-Object {
    $_.FullName.Split([System.IO.Path]::DirectorySeparatorChar).Length
} -Descending

foreach ($directory in $orderedDirectories) {
    $current = Get-Item -LiteralPath $directory.FullName -Force
    Assert-OrdinaryItem $current
    $currentPath = Get-CanonicalPath $current.FullName
    if (-not ($currentPath.Equals($targetPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-PathInsideRoot -Path $currentPath -Root $targetPath))) {
        throw "Directory escaped sandbox root before deletion: $currentPath"
    }

    Remove-Item -LiteralPath $currentPath -Force
}

Write-Output "Deleted test sandbox: $targetPath"
