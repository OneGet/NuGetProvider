<#
Create a NuGet package from an output location
#>
[CmdletBinding()]
param(
	# e.g. C:\git\nugetprovider\bin\Release
    [Parameter(Mandatory = $true)]
    [string]
    $OutputRootDir,

    [Parameter(Mandatory = $false)]
    [string]
    $PackageId = "Microsoft.PowerShell.PackageManagement.NuGetProvider",

    [Parameter(Mandatory = $false)]
    [string]
    $Authors = "Microsoft",

    [Parameter(Mandatory = $false)]
    [string]
    $Owners = "brywang",

    [Parameter(Mandatory = $false)]
    [string[]]
    $ReleaseNotes,

    [Parameter(Mandatory = $false)]
    [string]
    $ReleaseNotesFile,

    [Parameter(Mandatory = $false)]
    [switch]
    $IncludePdbs,

    [Parameter(Mandatory = $false)]
    [switch]
    $RefreshNuGetClient,

    [Parameter(Mandatory = $false)]
    [string]
    $PackageOutDir,

    [Parameter(Mandatory = $false)]
    [string]
    $PrereleaseString
)

if (-not $PackageOutDir) {
    $PackageOutDir = $PSScriptRoot
}

$nugetCommand = Get-Command nuget -ErrorAction Ignore
if ((-not $nugetCommand) -or $RefreshNuGetClient) {
    Write-Verbose "Downloading latest NuGet.exe"
    $null = Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile (Join-Path -Path $PSScriptRoot -ChildPath nuget.exe)
    $nugetCommand = Get-Command nuget -ErrorAction Ignore
}

Write-Verbose "Using NuGet version: $($nugetCommand.Version)"
$nugetPath = $nugetCommand.Source
$tempDir = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ([System.IO.Path]::GetRandomFileName())
$null = New-Item -Path $tempDir -ItemType Directory
try {
    $version = $null
    foreach ($frameworkPathItem in (Get-ChildItem -Path $OutputRootDir -Directory)) {
		$framework = Split-Path -Path $frameworkPathItem.FullName -Leaf
        $libDir = Join-Path -Path $tempDir -ChildPath 'lib' | Join-Path -ChildPath $framework
        if (-not (Test-Path -Path $libDir)) {
            $null = New-Item -Path $libDir -ItemType Directory
        }

		$dllPath = Join-Path -Path $frameworkPathItem.FullName -ChildPath "Microsoft.PackageManagement.NuGetProvider.dll"
		Write-Verbose "Preparing file: $($dllPath)"
        Copy-Item -Path $dllPath -Destination $libDir
		if (-not $version) {
            $version = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
		}
		
        if ($IncludePdbs) {
            $dllPath = Join-Path -Path $frameworkPathItem.FullName -ChildPath "Microsoft.PackageManagement.NuGetProvider.pdb"
			Write-Verbose "Preparing file: $($dllPath)"
			Copy-Item -Path $dllPath -Destination $libDir
        }
    }

    if (-not $version) {
        throw "Couldn't discover version of package - is Microsoft.PackageManagement.dll there?"
    }

    if ($PrereleaseString) {
        $version += "-$PrereleaseString"
    }

    $nuspecContents = Get-Content -Path (Join-Path -Path $PSScriptRoot -ChildPath 'NuGetProvider.nuspec') | Out-String
    Write-Verbose "Setting Package.Id = $PackageId"
    $nuspecContents = $nuspecContents -replace "{name}",$PackageId
    Write-Verbose "Setting Package.Version = $version"
    $nuspecContents = $nuspecContents -replace "{version}",$version
    Write-Verbose "Setting Package.Authors = $Authors"
    $nuspecContents = $nuspecContents -replace "{authors}",$Authors
    Write-Verbose "Setting Package.Owners = $Owners"
    $nuspecContents = $nuspecContents -replace "{owners}",$Owners
    if ($ReleaseNotesFile) {
        if (-not (Test-Path -Path $ReleaseNotesFile)) {
            throw "Release notes file '$ReleaseNotesFile' does not exist"
        }

        $ReleaseNotes = Get-Content -Path $ReleaseNotesFile | Out-String
    } else {
        $ReleaseNotes = $ReleaseNotes | Out-String
    }

    Write-Verbose "Setting Package.ReleaseNotes = '$ReleaseNotes'"
    $nuspecContents = $nuspecContents -replace "{releaseNotes}",$ReleaseNotes
    $nuspecName = "temp.nuspec"
    $nuspecContents | Out-File -FilePath (Join-Path -Path $tempDir -ChildPath $nuspecName)
    Write-Verbose "Packing..."
    Push-Location -Path $tempDir
    & $nugetPath pack $nuspecName
    Pop-Location
    if ($LastExitCode -gt 0) {
        throw 'NuGet.exe pack failed. See previous error messages.'
    }

    Write-Verbose "Copying package to $PackageOutDir"
    Get-ChildItem -Path (Join-Path -Path $tempDir -ChildPath "*.nupkg") | Select-Object -First 1 | Select-Object -ExpandProperty FullName | Copy-Item -Destination $PackageOutDir
} finally {
    $tries = 3
    while ((Test-Path -Path $tempDir) -and ($tries -gt 0)) {
        try {
            $null = Remove-Item -Path $tempDir -Recurse -Force -ErrorAction Ignore
        } catch {
        }

        if ((Test-Path -Path $tempDir) -and ($tries -gt 0)) {
            Start-Sleep -Milliseconds (100 * $tries)
            $tries = $tries - 1
        }
    }

    if (Test-Path -path $tempDir) {
        Write-Warning "Failed to remove temp directory: $tempDir"
    }
}

Write-Verbose "Done"