// Adds functionality to custom title bar
"use strict";

const remote = require("electron").remote;
const win = remote.getCurrentWindow();

const loadTitleBar = ()=>{
    const windowCloseButton = document.getElementById("windowClose");
    const windowMaximizeButton = document.getElementById("windowMaximize");
    const windowMinimizeButton = document.getElementById("windowMinimize");

    windowCloseButton.addEventListener("click", ()=> win.close());
    windowMaximizeButton.addEventListener("click", ()=>{
        win.isMaximized() ? win.unmaximize() : win.maximize();
    });
    windowMinimizeButton.addEventListener("click", ()=> win.minimize());
};

exports.loadTitleBar = loadTitleBar;
