"use strict";

const electron = require('electron')
// Module to control application life.
const app = electron.app
// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow

const path = require('path')
const url = require('url')

const os = require('os');



//
// Set this to true if you want to open the dev console
//
const openDev = false;

// When true links are opened in an external browser
const openLinksInExternal = true;

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow = null;

// Setup code from the electron quickstart
function createWindow () {
    // Create the browser window.
    mainWindow = new BrowserWindow({
        width: 1200 + (openDev ? 400 : 0), height: 700,
        // This would disable the system title bar and frame window
        // so if this is false we need a custom window top bar
        frame: true,

        // This breaks initial layout with dev console enabled
        show: openDev ? true : false,
        
        backgroundColor: '#404040'
    });

    if(!openDev){
        mainWindow.once('ready-to-show', () => {
            mainWindow.show();
        });
    }
    
    // and load the index.html of the app.
    mainWindow.loadURL(url.format({
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
        mainWindow = null
    });

    // Open in browser for links //
    // Because the forums don't display correctly
    if(openLinksInExternal){
        mainWindow.webContents.on('new-window', function(e, url) {
            e.preventDefault();
            require('electron').shell.openExternal(url);
        });
    }

    // Startup checks //


    // Version info stuff
    // process.versions.node process.versions.chrome process.versions.electron
    //console.log("os: " + os.platform() + " arch: " + os.arch());
    
    
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on('ready', createWindow)

// Quit when all windows are closed.
app.on('window-all-closed', function () {
    // On OS X it is common for applications and their menu bar
    // to stay active until the user quits explicitly with Cmd + Q
    if (process.platform !== 'darwin') {
        app.quit()
    }
})

app.on('activate', function () {
    // On OS X it's common to re-create a window in the app when the
    // dock icon is clicked and there are no other windows open.
    if (mainWindow === null) {
        createWindow()
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

