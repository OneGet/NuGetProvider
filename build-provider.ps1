<# Build NuGetProvider to be used immediately by PackageManagement #>
param(
    # Example: C:\code\OneGet
    [Parameter(Mandatory=$true)]
    [string]
    $OneGetRepositoryRoot,

    [Parameter(Mandatory=$false)]
    [string]
    [ValidateSet('net451', 'netcoreapp2.0', 'netstandard1.6')]
    $Framework = 'net451'
)

.\Generate-Resources.ps1
$env:EMBEDPROVIDERMANIFEST = 'true'
$env:PROVIDERROOTDIR = Join-Path -Path $OneGetRepositoryRoot -ChildPath 'src' | 
    Join-Path -ChildPath 'Microsoft.PackageManagement.NuGetProvider'
dotnet restore
dotnet build --framework $Framework