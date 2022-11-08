Thrive Launcher
===============

### [Build Status](https://dev.revolutionarygamesstudio.com/ci/2)

Thrive launcher is a desktop application that manages downloading and
installing the game releases.

For more information, visit [Revolutionary Games' Website](http://revolutionarygamesstudio.com/), 
[Thrive repository](https://github.com/Revolutionary-Games/Thrive).



### Requirements on Linux

On Linux a new enough OS is needed to include new enough system
libraries. Ubuntu 20.04 LTS is the oldest guaranteed distro to work.


### Required Windows version

Due to the used framework at least Windows 7 or newer is required.

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

In order for the icons to work you need to run `dotnet run --project Scripts -- icons` to
create all the icon files from the source images. The icon creation
script requires you to
have [ImageMagick](https://www.imagemagick.org/) installed.

### Running

Now you should have everything set up. You can run Thrive launcher
with `dotnet run` in the Thrive-Launcher directory. Or use a C# IDE to
open the project files to run.

### Issues

If you have issues first make sure that dotnet SDK is properly
installed and you have ran `dotnet restore` in the launcher's folder.


Creating releases
-----------------

TODO: new packaging approach

Packaging is currently done on Linux for Linux and Windows, and mac
packages need to be made on a mac with development tools installed.

```sh
dotnet run --project Scripts -- container --export false --image ReleaseBuilder latest
```

With the build containers created, then just run:

```sh
dotnet run --project Scripts -- package
```

On a mac the build containers are not needed, so just run:
```sh
dotnet run --project Scripts -- package
```

CI Images
---------

CI image can be created with the container script like this:

```sh
dotnet run --project Scripts -- container --image CI 3
```


