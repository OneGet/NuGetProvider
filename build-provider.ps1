<# Build NuGetProvider to be used immediately by PackageManagement #>
param(
    # Example: C:\code\OneGet
    [Parameter(Mandatory=$true)]
    [string]
    $OneGetRepositoryRoot,

    [Parameter(Mandatory=$false)]
    [string]
    [ValidateSet('net451', 'netcoreapp2.0', 'netstandard1.6', 'all')]
    $Framework = 'net451',

    [Parameter(Mandatory=$false)]
    [string]
    $CopyTo
)

.\Generate-Resources.ps1

if ($Framework -eq 'all') {
    $frameworks = @('net451','netcoreapp2.0','netstandard1.6')
} else {
    $frameworks = @($Framework)
}

foreach ($f in $frameworks) {
    $env:EMBEDPROVIDERMANIFEST = 'true'
    $env:PROVIDERROOTDIR = Join-Path -Path $OneGetRepositoryRoot -ChildPath 'src' | 
        Join-Path -ChildPath 'Microsoft.PackageManagement.NuGetProvider'
    $env:MANIFESTROOTDIR = $PSScriptRoot
    dotnet restore
    dotnet build --framework $f
    if ($CopyTo) {
        $copyDir = $CopyTo.Replace("{Root}", $OneGetRepositoryRoot)
        if ($f -eq 'net451') {
            $copyDir += "\\fullclr"
        } else {
            $copyDir += "\\coreclr\\$f"
        }
        
        if (-not (Test-Path -Path $copyDir)) {
            $null = New-Item -Path $copyDir -ItemType Directory
        }

        $outDir = Join-Path -Path $PSScriptRoot -ChildPath "bin" | Join-Path -ChildPath "Debug" | Join-Path -ChildPath $f
        $dllPath = Join-Path -Path $outDir -ChildPath "Microsoft.PackageManagement.NuGetProvider.dll"
        $pdbPath = Join-Path -Path $outDir -ChildPath "Microsoft.PackageManagement.NuGetProvider.pdb"
        if (Test-Path -Path $dllPath) {
            Copy-Item -Path $dllPath -Destination (Join-Path -Path $copyDir -ChildPath "Microsoft.PackageManagement.NuGetProvider.dll") -Force
        }

        if (Test-Path -Path $pdbPath) {
            Copy-Item -Path $pdbPath -Destination (Join-Path -Path $copyDir -ChildPath "Microsoft.PackageManagement.NuGetProvider.pdb") -Force
        }
    }
}