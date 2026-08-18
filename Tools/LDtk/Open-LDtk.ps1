[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $LdtkArguments
)

$ErrorActionPreference = 'Stop'

$toolRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $toolRoot '..\..'))
$executablePath = Join-Path $toolRoot 'LDtk.exe'
$archivePath = Join-Path $toolRoot 'LDtk.exe.zip'
$expectedExecutableHash = '9216419429B3C90C2ED630BF2D675CEA386AC83197928C0426A7E5BC87D36082'

if (-not (Test-Path -LiteralPath $executablePath)) {
    if (-not (Test-Path -LiteralPath $archivePath)) {
        throw "LDtk executable and archive are both missing: $toolRoot"
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $toolRoot
}

$actualExecutableHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
if ($actualExecutableHash -ne $expectedExecutableHash) {
    throw "LDtk executable hash mismatch. expected=$expectedExecutableHash actual=$actualExecutableHash"
}

$quotedArguments = foreach ($argument in $LdtkArguments) {
    '"' + $argument.Replace('"', '\"') + '"'
}

$startParameters = @{
    FilePath = $executablePath
    WorkingDirectory = $repositoryRoot
    PassThru = $true
}
if ($quotedArguments.Count -gt 0) {
    $startParameters.ArgumentList = $quotedArguments
}

$process = Start-Process @startParameters

Write-Output "Started project LDtk. pid=$($process.Id)"
