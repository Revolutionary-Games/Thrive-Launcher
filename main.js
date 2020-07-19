"use strict";

// Command line parsing

// use `npm run start-dev` if you want the dev tools
// Set this to true if you want to open the dev console
let openDev = false;


const args = process.argv.slice(2);
args.forEach((val, index) => {

    if(val === "--open-dev"){
        openDev = true;
    } else if(/--remote-debugging-port.*/i.test(val)){
        // Chrome handles this
    } else {
        console.log("Invalid argument (" + index + "): " + val);
        process.exit();
    }
});

const log = require("electron-log");
Object.assign(console, log.functions);
log.catchErrors();

const electron = require("electron");
const {autoUpdater} = require("electron-updater");

// Logging for the updater
autoUpdater.logger = log;
autoUpdater.logger.transports.file.level = "info";

// Module to control application life.
const app = electron.app;
const ipcMain = electron.ipcMain;

// Disable a deprecation warning
app.allowRendererProcessReuse = true;

// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow;

const path = require("path");
const url = require("url");

const fs = require("fs");
const os = require("os");

const openpgp = require("openpgp");

const pjson = require("./package.json");

// Hopefully this is the right place to do this
openpgp.initWorker({path: "openpgp.worker.js"});

openpgp.config.aead_protect = true; // Activate fast AES-GCM mode (not yet OpenPGP standard)

// Used for provided services to the renderer process
const zlib = require("zlib");
const {pipeline} = require("stream");



// When true links are opened in an external browser
const openLinksInExternal = true;

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow = null;


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
            enableRemoteModule: true,
        },

        backgroundColor: "#404040",

        icon: iconFile,

        // We extensively just use the node stuff from renderer
        enableRemoteModule: true,
    });

    if(!openDev){
        // Might work now as the old api was removed?
        mainWindow.removeMenu();
    }

    mainWindow.once("ready-to-show", () => {
        mainWindow.show();

        // Hopefully this is not too early to trigger here (and not in did-stop-loading)
        autoUpdater.checkForUpdatesAndNotify();
    });

    mainWindow.once("did-finish-load", () => {
    });

    // And load the index.html of the app.
    mainWindow.loadURL(url.format({
        // This could probably also be __dirname
        pathname: path.join(app.getAppPath(), "index.html"),
        protocol: "file:",
        slashes: true,
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
        mainWindow.webContents.on("new-window", function(e, url){
            e.preventDefault();
            require("electron").shell.openExternal(url);
        });
    }

    // Open links in a browser, could probably also open a new electron window
    // depending on openLinksInExternal, but that hasn't been done
    mainWindow.webContents.on("will-navigate", function(e, url){

        if(url !== mainWindow.webContents.getURL()){
            e.preventDefault();
            require("electron").shell.openExternal(url);
        }
    });

    // Startup checks //


    // Version info stuff
    // process.versions.node process.versions.chrome process.versions.electron
    log.info("Started Thrive Launcher version: " + pjson.version + " os: " + os.platform() +
        " arch: " + os.arch());
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

function unGZipMain(file, target, respondTo){
    pipeline(fs.createReadStream(file), zlib.createGunzip(), fs.createWriteStream(target),
        (error) => {
            mainWindow.webContents.send(respondTo, {error: error});
        });
}

ipcMain.on("restartAndUpdate", () => {
    autoUpdater.quitAndInstall();
});

autoUpdater.on("update-available", () => {
    console.log("Sending update available message");
    mainWindow.webContents.send("updateAvailable");
});

autoUpdater.on("update-downloaded", () => {
    console.log("sending update downloaded message");
    mainWindow.webContents.send("updateDownloaded");
});

ipcMain.on("requestGunzip", (event, arg) => {
    unGZipMain(arg.file, arg.target, arg.responseEvent);
});
