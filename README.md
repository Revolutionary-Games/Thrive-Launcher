Thrive Launcher
===============

### [Build Status](https://dev.revolutionarygamesstudio.com/ci/2)

Thrive launcher is a desktop application that manages downloading and
installing the game releases.

For more information, visit [Revolutionary Games' Website](http://revolutionarygamesstudio.com/), 
[Thrive repository](https://github.com/Revolutionary-Games/Thrive).



### Requirements on Linux

On Linux it is recommended to use the [Flatpak
version](https://flathub.org/apps/com.revolutionarygamesstudio.ThriveLauncher)
of the launcher.

For the manually installable version a distro that is as new as
AlmaLinux 9 is required.


### Required Windows version

Due to the used framework at least Windows 10 or newer is
required. Windows 7 may still work for some time before new .NET
versions no longer run on it.

Releases
--------

Releases are available here:
[Thrive-Launcher releases](https://github.com/Revolutionary-Games/Thrive-Launcher/releases)


Building
--------

If you can't find a precompiled release or you want to develop the
launcher you will need to follow these instructions to build it.

### Dependencies

In order to properly clone this repository you need to make sure you
have [Git LFS](https://git-lfs.github.com/) installed.

You first need dotnet sdk before you can build Thrive Launcher. So
check [here](https://dotnet.microsoft.com/en-us/download)
how to install it.

### Downloading

First clone this repository with `git clone
https://github.com/Revolutionary-Games/Thrive-Launcher.git --recursive` now go to
the created directory and run `dotnet restore` this should install all
required packages. You may need to run this again after pulling updates
if your IDE doesn't do so automatically.

### Icons

In order for the icons to work you need to run `dotnet run --project
Scripts -- icons` to create all the icon files from the source
images. The icon creation script requires you to have
[ImageMagick](https://www.imagemagick.org/) installed. On Windows add
the folder with `magick.exe` to the PATH environment variable
manually.

### Running

Now you should have everything set up. You can run Thrive launcher
with `dotnet run` in the `Thrive-Launcher/ThriveLauncher`
subdirectory. Or use a C# IDE to open the project files to run.

### Issues

If you have issues first make sure that dotnet SDK is properly
installed and you have ran `dotnet restore` in the launcher's folder.


Creating releases
-----------------

This section details what to do to create Launcher packages and
installers. Note that due to various limitations you need to make the
packages for each platform on that respective platform, which means
you need a Windows, Linux, and Mac systems.

The following extra tools are needed for creating packages on Linux:
```sh
podman
```

If required on Linux, Rcedit can be downloaded from
[here](https://github.com/electron/rcedit/releases).


On Windows creating packages requires:
- Icons being generated (see above)
- NSIS, needs to be added to PATH

To create build containers for Linux:

```sh
dotnet run --project Scripts -- container --export false --image ReleaseBuilder latest
```

On Mac you need the xcode tools installed as well as allowing
terminal / your IDE to automate Finder.

### Packaging

With the build containers created on Linux or on other platforms with
their requirements fullfilled, run the following:

```sh
dotnet run --project Scripts -- package
```

### After packaging

Before publishing test that the created builds didn't have broken
packages. If they did a clean needs to be run and then the packaging
again:
```sh
dotnet run --project Scripts -- clean
dotnet run --project Scripts -- package
```

To test on Linux, do not build with podman as that always results in
clean working builds, but other builds might be broken. So build
natively on the current platform and test the launcher works:
```
dotnet run --project Scripts -- package --compress false --podman false Linux
```

CI Images
---------

CI image can be created with the container script like this:

```sh
dotnet run --project Scripts -- container --image CI 3
```

Miscellaneous
-------------

### Updating Copyright Year

When updating the copyright year it needs to be updated in
`LICENSE.md` and also in `ThriveLauncher/ThriveLauncher.csproj`.

### Updating dotnet version

Currently version 8 is used. If updated the installer files in
`DependencyInstallers` need to be updated and `PackageTool` as well as
`Scripts/launcher.nsi.template` need to be updated to refer to the new version.
