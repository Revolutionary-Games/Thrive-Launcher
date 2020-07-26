Thrive Launcher
===============

### Build Status [![CircleCI](https://circleci.com/gh/Revolutionary-Games/Thrive-Launcher.svg?style=svg)](https://circleci.com/gh/Revolutionary-Games/Thrive-Launcher)

Thrive launcher is a desktop application that manages downloading and
installing the game releases.

For more information, visit [Revolutionary Games' Website](http://revolutionarygamesstudio.com/), 
[Thrive repository](https://github.com/Revolutionary-Games/Thrive).



### Required packages

On linux these packages need to be installed for the launcher to run
correctly: `libXScrnSaver`


### Required windows version

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

You first need npm before you can build Thrive Launcher. So
check [here](https://docs.npmjs.com/getting-started/installing-node)
how to install it.

### Downloading

First clone this repository with `git clone
https://github.com/Revolutionary-Games/Thrive-Launcher.git` now go to
the created directory and run `npm install` this should install all
required modules and electron. You may need to run this again after pulling updates
if the required modules have changed.

### Icons

In order for the icons to work you need to run `./CreateIcons.rb` to
create all the icon files from the source images. The icon creation
script requires you to
have [ImageMagick](https://www.imagemagick.org/) installed.

### Running

Now you should have everything set up. You can run Thrive launcher
with `npm start` in the thrive-launcher directory.

### Issues

If you have issues first make that electron is properly installed,
then make sure that you ran `npm install`. Also make sure that you are
in the correct directory, you should be in the base thrive launcher
directory that contains the file `package.json`.


Creating releases
-----------------

Packages are now made with electron builder. There are targets for
making the releases included in package.json. They can be ran like
this: `npm run dist` and `npm run dist:win`





