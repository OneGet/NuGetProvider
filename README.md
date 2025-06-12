
[![Build status](https://ci.appveyor.com/api/projects/status/vd65p4kpgi0pvto5?svg=true)](https://ci.appveyor.com/project/jianyunt/nugetprovider)


NuGet provider
==============

NuGetProvider is a OneGet (PackageManagement) provider.
It primarily supports finding and installing packages from [NuGet.org][nugetorg].
It is also used by [PowerShellGet][psget] for finding and installing PowerShell modules from [PowerShellGallery][psgallery].

It supports FullCLR and CoreCLR, meaning works for Windows, Linux, and OSX as part of [PowerShellCore][pscore].

As a git submodule, it is compiled and tested with [OneGet][oneget] together.

For more information, see [OneGet wiki][wiki].

[nugetorg]: https://www.nuget.org
[psgallery]: https://www.PowerShellGallery.com
[psget]: https://github.com/PowerShell/PowerShellget
[oneget]: https://www.oneget.org
[pscore]: https://github.com/PowerShell/PowerShell
[wiki]: https://github.com/oneget/oneget/wiki
