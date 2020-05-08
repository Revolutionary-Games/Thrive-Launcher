// Adds functionality to custom title bar
"use strict";

const remote = require("electron").remote;
const win = remote.getCurrentWindow();

const loadTitleBar = ()=>{
    const windowCloseButton = document.getElementById("windowClose");
    const windowMaximizeButton = document.getElementById("windowMaximize");
    const windowMinimizeButton = document.getElementById("windowMinimize");

    const windowMaximizeIcon = document.getElementById("maximizeIconPath");
    const maximizedIcon =
    `M7 7V3a1 1 0 0 1 1-1h13a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-4v3.993c0 .556-.449
    1.007-1.007 1.007H3.007A1.006 1.006 0 0 1 2 20.993l.003-12.986C2.003 7.451 2.452
    7 3.01 7H7zm2 0h6.993C16.549 7 17 7.449 17 8.007V15h3V4H9v3zM4.003 9L4 20h11V9H4.003z`;
    const unmaximizedIcon =
    "M4 3h16a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zm1 2v14h14V5H5z";

    windowCloseButton.addEventListener("click", ()=> win.close());
    windowMaximizeButton.addEventListener("click", ()=>{
        if (win.isMaximized()){
            win.unmaximize();
            windowMaximizeIcon.setAttribute("d", unmaximizedIcon);
        }else{
            win.maximize();
            windowMaximizeIcon.setAttribute("d", maximizedIcon);
        }
        windowMinimizeButton.addEventListener("click", ()=> win.minimize());
    });
};

exports.loadTitleBar = loadTitleBar;
