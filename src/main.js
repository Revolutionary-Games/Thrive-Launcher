"use strict";

// Command line parsing

// use `npm run start-dev` if you want the dev tools
// Set this to true if you want to open the dev console
let openDev = false;
let skipAutoUpdate = false;
let ldPreload = "";
let ignoreAutoStart = false;

let loadLd = false;

// Slice of the program name, we don't need to handle that
const args = process.argv.slice(1);
args.forEach((val, index) => {

    if(loadLd){
        ldPreload = val;
        loadLd = false;
        return;
    }

    if(!val.match(/\S/))
        return;

    // When developing the launcher, the second argument can be a '.' so ignore that
    if(index === 0 && val === ".")
        return;

    if(val === "--open-dev"){
        openDev = true;
    } else if(val === "--skip-autoupdate"){
        skipAutoUpdate = true;
    } else if(val === "--no-autorun" || val === "--no-autorun"){
        ignoreAutoStart = true;
    } else if(val === "--game-ld-preload"){
        loadLd = true;
    } else if(val === "--no-sandbox"){
        console.log("chromium sandbox disable flag is used");
    } else if(/--remote-debugging-port.*/i.test(val)){
        // Chrome handles this
    } else {
        console.log("Invalid argument (" + index + "): " + val + " (given command line: " +
            args + ")");
        process.exit();
    }
});

const log = require("electron-log");
Object.assign(console, log.functions);
log.catchErrors();

if(openDev){
    log.transports.file.level = "debug";
} else {
    log.transports.file.level = "info";
}

const electron = require("electron");
const remoteMain = require("@electron/remote/main");
remoteMain.initialize();

const {autoUpdater} = require("electron-updater");

// Logging for the updater
autoUpdater.logger = log;

// Module to control application life.
const app = electron.app;
const ipcMain = electron.ipcMain;
const shell = electron.shell;

// Disable a deprecation warning
app.allowRendererProcessReuse = true;

// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow;

const path = require("path");
const url = require("url");

const fs = require("fs");
const os = require("os");

const openpgp = require("openpgp");
const open = require("open");

const pjson = require("../package.json");

openpgp.config.aead_protect = true; // Activate fast AES-GCM mode (not yet OpenPGP standard)

// Used for provided services to the renderer process
const zlib = require("zlib");
const {pipeline} = require("stream");

// Maximum time that unzipping a file may take
const maximumUnzipTime = 3 * 60 * 1000;

// When true links are opened in an external browser
const openLinksInExternal = true;
const useOpenPackage = true;

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow = null;

let updateCheckStarted = false;

function startUpdateChecksIfNotStarted(){
    if(updateCheckStarted)
        return;
    updateCheckStarted = true;

    if(skipAutoUpdate){
        log.debug("auto update is disabled");
        return;
    }

    log.debug("Starting updates check");
    autoUpdater.checkForUpdatesAndNotify();
    log.debug("Updates check is probably running");
}


// Setup code from the electron quickstart
function createWindow(){

    // This does not work. So this is directly in index.html
    // Setup a security policy to make this thing more secure (and quiet a warning)
    // electron.session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    //     callback({ responseHeaders: Object.assign({
    //         "Content-Security-Policy": [ "default-src 'self'" ]
    //     }, details.responseHeaders)});
    // });

    // Could also probably use 64x64 icon here
    const iconFile = path.join(app.getAppPath(), "assets/icons/128x128.png");


    // Const iconFile = path.join(app.getAppPath(), "assets/icons/64x64.png");
    if(!fs.existsSync(iconFile)){

        console.error("Missing icon file. Did you forget to run 'CreateIcons.rb'?");
        app.quit();
        return;
    }

    // Store files are not packed, so we need special handling, this needs to match the one
    // in utils.js
    let storeAppPath = app.getAppPath();

    if(storeAppPath.includes("app.asar")){
        // Packaged version
        storeAppPath = path.dirname(app.getPath("exe"));
    }

    const steamVersionFile = path.join(storeAppPath, "steam_appid.txt");

    const isSteamVersion = fs.existsSync(steamVersionFile);

    const itchVersionFile = path.join(storeAppPath, "itch_readme.txt");

    const isItchVersion = fs.existsSync(itchVersionFile);

    const isStoreVersion = isSteamVersion || isItchVersion;

    let store = "";

    if(isStoreVersion){
        // Disable auto update for store versions
        skipAutoUpdate = true;
        log.info("This is a special store version of Thrive Launcher");

        if(isSteamVersion){
            store = "steam";
        } else if(isItchVersion){
            store = "itch";
        } else {
            console.error("Logic error in store detection, no store specific variable set");
            app.quit();
            return;
        }
    }

    // Workaround for menu appearing (https://github.com/electron/electron/issues/16521)
    if(!openDev)
        electron.Menu.setApplicationMenu(null);

    // Create the browser window.
    mainWindow = new BrowserWindow({
        width: 950 + (openDev ? 700 : 0), height: 625,

        // No default titlebar, we use a custom one (except when using devtools)
        frame: openDev,

        autoHideMenuBar: !openDev,

        webPreferences: {
            nodeIntegration: true,
            contextIsolation: false,
            preload: path.join(app.getAppPath(), "src/preload.js"),

            // Sandbox must always be disabled as otherwise node integration is not available
            sandbox: false,
        },

        backgroundColor: "#404040",

        icon: iconFile,
    });

    remoteMain.enable(mainWindow.webContents);

    if(!openDev){
        // Might work now as the old api was removed?
        mainWindow.removeMenu();
    }

    mainWindow.once("ready-to-show", () => {
        mainWindow.show();

        startUpdateChecksIfNotStarted();
    });

    mainWindow.once("show", () => {
        setTimeout(startUpdateChecksIfNotStarted, 200);
    });

    // And load the index.html of the app.
    mainWindow.loadURL(url.format({
        // This could probably also be __dirname
        pathname: path.join(app.getAppPath(), "src", "index.html"),
        protocol: "file:",
        slashes: true,
        search: `isStoreVersion=${isStoreVersion}&store=${store}&ldPreload=${ldPreload}` +
        `&ignoreAutoStart=${ignoreAutoStart}`,
    }));

    // Open the DevTools.
    if(openDev){
        mainWindow.webContents.openDevTools();
        log.info("Started with dev tools enabled");
    }

    mainWindow.webContents.once("did-stop-loading", () => {
    });

    // Emitted when the window is closed.
    mainWindow.on("closed", function(){
        // Dereference the window object, usually you would store windows
        // in an array if your app supports multi windows, this is the time
        // when you should delete the corresponding element.
        mainWindow = null;
    });

    // Open in browser for links //
    // Because the forums don't display correctly
    if(openLinksInExternal){
        mainWindow.webContents.setWindowOpenHandler((details) => {
            handleExternalLinkOpen(details.url);

            return {action: "deny"};
        });
    }

    // Open links in a browser
    mainWindow.webContents.on("will-navigate", function(e, url){

        if(url !== mainWindow.webContents.getURL() && openLinksInExternal){
            e.preventDefault();
            handleExternalLinkOpen(url);
        }
    });

    // Startup checks //


    // Version info stuff
    // process.versions.node process.versions.chrome process.versions.electron
    log.info("Started Thrive Launcher version: " + pjson.version + " os: " + os.platform() +
        " arch: " + os.arch() + " is store version: " + isStoreVersion);

    // Just to make sure this is fired
    setTimeout(startUpdateChecksIfNotStarted, 800);
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on("ready", createWindow);

// Quit when all windows are closed.
app.on("window-all-closed", function(){
    // On OS X it is common for applications and their menu bar
    // to stay active until the user quits explicitly with Cmd + Q
    if(process.platform !== "darwin"){
        log.info("Quitting as all windows are closed");
        app.quit();
    }
});

app.on("activate", function(){
    // On OS X it's common to re-create a window in the app when the
    // dock icon is clicked and there are no other windows open.
    if(mainWindow === null){
        createWindow();
    }
});


app.on("browser-window-created", function(e, window){

    if(window === mainWindow || mainWindow === null){

        // Main window

    } else {

        // Extra window //
        window.setSize(1000, 900);
        window.center();
    }

    // Remove the menu bar with entries like "file" and "edit"
    if(!openDev)
        window.setMenu(null);
});

function handleExternalLinkOpen(url){
    if(useOpenPackage){
        if(url.startsWith("http")){
            open(url).catch((error) => {
                console.error("Failed to open external link: ", error);
            });
        }
    } else {
        shell.openExternal(url).catch((error) => {
            console.error("Failed to open external link: ", error);
        });
    }
}

function unGZipMain(file, target, respondTo){
    // Timeout prevention
    const timeoutId = setTimeout(() => {
        log.warn("Hit stuck timeout for file extract");
        mainWindow.webContents.send(respondTo, {
            error: "File unzipping took too long, it" +
                " probably got stuck",
        });
    }, maximumUnzipTime);

    if(os.platform() === "win32"){
        // This approach seems to not get stuck on Windows
        // But has a chance to get stuck on Linux
        pipeline(fs.createReadStream(file), zlib.createGunzip(), fs.createWriteStream(target),
            (error) => {
                if(error){
                    mainWindow.webContents.send(respondTo, {error: "" + error});
                } else {
                    mainWindow.webContents.send(respondTo, {error: null});
                }

                clearTimeout(timeoutId);
            });
    } else {
        try{
            const gzip = zlib.createGunzip();
            const source = fs.createReadStream(file);
            const destination = fs.createWriteStream(target);

            destination.on("close", () => {
                mainWindow.webContents.send(respondTo, {error: null});
                clearTimeout(timeoutId);
            });

            destination.on("error", (error) => {
                mainWindow.webContents.send(respondTo, {
                    error: "Error on destination" +
                        " stream: " + error,
                });
            });

            source.on("error", (error) => {
                mainWindow.webContents.send(respondTo, {
                    error: "Error on source" +
                        " stream: " + error,
                });
            });

            gzip.on("error", (error) => {
                mainWindow.webContents.send(respondTo, {
                    error: "Error on gzip" +
                        " stream: " + error,
                });
            });

            source.pipe(gzip).pipe(destination);

        } catch(error){
            mainWindow.webContents.send(respondTo, {error: "" + error});
            clearTimeout(timeoutId);
        }
    }
}

ipcMain.on("restartAndUpdate", () => {
    log.info("Quitting and installing update");
    autoUpdater.quitAndInstall();
});

autoUpdater.on("update-available", () => {
    log.info("Sending update available message");
    mainWindow.webContents.send("updateAvailable");
});

autoUpdater.on("update-downloaded", () => {
    log.info("sending update downloaded message");
    mainWindow.webContents.send("updateDownloaded");
});

ipcMain.on("requestGunzip", (event, arg) => {
    unGZipMain(arg.file, arg.target, arg.responseEvent);
});
