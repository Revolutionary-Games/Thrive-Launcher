"use strict";

// Command line parsing

// use `npm run start-dev` if you want the dev tools
// Set this to true if you want to open the dev console
let openDev = false;

let args = process.argv.slice(2);
args.forEach((val, index) =>{

    switch(val){
    case "--open-dev":
        openDev = true;
        break;
    default:
        console.log("Invalid argument (" + index + "): " + val);
        process.exit();
    }
});

const electron = require('electron');


// Module to control application life.
const app = electron.app;

// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow;

const path = require('path');
const url = require('url');

const os = require('os');

const fs = require('fs');

const openpgp = require('openpgp');

// Hopefully this is the right place to do this
openpgp.initWorker({ path:'openpgp.worker.js' });

openpgp.config.aead_protect = true; // activate fast AES-GCM mode (not yet OpenPGP standard)


// When true links are opened in an external browser
const openLinksInExternal = true;

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow = null;


// Setup code from the electron quickstart
function createWindow () {

    // This does not work. So this is directly in index.html
    // Setup a security policy to make this thing more secure (and quiet a warning)
    // electron.session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    //     callback({ responseHeaders: Object.assign({
    //         "Content-Security-Policy": [ "default-src 'self'" ]
    //     }, details.responseHeaders)});
    // });

    // Could also probably use 64x64 icon here
    const iconFile = path.join(app.getAppPath(), "assets/icons/128x128.png");
    //const iconFile = path.join(app.getAppPath(), "assets/icons/64x64.png");
    if(!fs.existsSync(iconFile)){

        console.error("Missing icon file. Did you forget to run 'CreateIcons.rb'?");
        app.quit();
        return;
    }
    
    // Create the browser window.
    mainWindow = new BrowserWindow({
        width: 1200 + (openDev ? 700 : 0), height: 700,
        // This would disable the system title bar and frame window
        // so if this is false we need a custom window top bar
        frame: true,

        // This breaks initial layout with dev console enabled
        show: openDev ? true : false,

        webPreferences: {
            nodeIntegration: true
        },
        
        backgroundColor: '#404040',

        icon: iconFile
    });

    if(!openDev){
        mainWindow.once('ready-to-show', () => {
            mainWindow.show();
        });
    }
    
    // and load the index.html of the app.
    mainWindow.loadURL(url.format({
        // This could probably also be __dirname
        pathname: path.join(app.getAppPath(), 'index.html'),
        protocol: 'file:',
        slashes: true
    }));

    // Open the DevTools.
    if(openDev){
        mainWindow.webContents.openDevTools();
    }

    // Emitted when the window is closed.
    mainWindow.on('closed', function () {
        // Dereference the window object, usually you would store windows
        // in an array if your app supports multi windows, this is the time
        // when you should delete the corresponding element.
        mainWindow = null;
    });

    // Open in browser for links //
    // Because the forums don't display correctly
    if(openLinksInExternal){
        mainWindow.webContents.on('new-window', function(e, url) {
            e.preventDefault();
            require('electron').shell.openExternal(url);
        });
    }

    // Open links in a browser, could probably also open a new electron window
    // depending on openLinksInExternal, but that hasn't been done
    mainWindow.webContents.on('will-navigate', function(e, url) {

        if(url != mainWindow.webContents.getURL()) {
            e.preventDefault();
            require('electron').shell.openExternal(url);
        } 
    });

    // Startup checks //


    // Version info stuff
    // process.versions.node process.versions.chrome process.versions.electron
    //console.log("os: " + os.platform() + " arch: " + os.arch());
    
    
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on('ready', createWindow);

// Quit when all windows are closed.
app.on('window-all-closed', function () {
    // On OS X it is common for applications and their menu bar
    // to stay active until the user quits explicitly with Cmd + Q
    if (process.platform !== 'darwin') {
        app.quit();
    }
})

app.on('activate', function () {
    // On OS X it's common to re-create a window in the app when the
    // dock icon is clicked and there are no other windows open.
    if (mainWindow === null) {
        createWindow();
    }
})


electron.app.on('browser-window-created',function(e, window) {

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

