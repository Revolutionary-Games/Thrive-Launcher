// Handles starting the child thrive process
"use strict";

const remote = require("electron").remote;

const win = remote.getCurrentWindow();

const fs = remote.require("fs");
const path = require("path");
const child_process = remote.require("child_process");

const {settings} = require("../settings.js");
const {findBinInRelease, getThriveExecutableName} = require("./unpack");
const {checkIsDehydrated} = require("./rehydrate");

function onCanRun(installFolder, status, onClose, onEnded){
    status.textContent = "preparing to launch";

    // Find bin folder //
    const binFolder = findBinInRelease(installFolder);

    if(!fs.existsSync(binFolder)){

        status.textContent = "Error 'bin' folder is missing! To redownload delete " +
            installFolder;
        return;
    }

    // Check that executable is there //
    const exename = getThriveExecutableName();

    if(!fs.existsSync(path.join(binFolder, exename))){

        status.textContent = "Error: Thrive executable is missing! To redownload delete " +
            installFolder;
        return;
    }

    status.textContent = "launching...";

    const launchArgs = [];

    if(settings.launchOptionSingleProcess)
        launchArgs.push("--single-process");

    if(settings.launchOptionNoGUISandbox)
        launchArgs.push("--no-sandbox");

    if(settings.launchOptionNoGUIGPU)
        launchArgs.push("--disable-gpu");

    console.log("launching thrive from folder '" + binFolder + "' with arguments: ",
        launchArgs);

    // Cwd is where relative to things are installed
    const thrive = child_process.spawn(path.join(binFolder, exename),
        launchArgs,
        {cwd: binFolder});

    if(settings.hideLauncherOnPlay){
        win.minimize();
    }

    status.innerHTML = "";

    const titleSpan = document.createElement("span");

    const processOutput = document.createElement("div");

    processOutput.style.overflow = "auto";

    // This needs a fixed size for some reason
    processOutput.style.maxHeight = "410px";
    processOutput.style.height = "410px";
    processOutput.style.paddingTop = "5px";
    processOutput.style.width = "100%";

    status.style.marginBottom = "1px";

    titleSpan.textContent = "Thrive is running. Log output: ";

    titleSpan.append(processOutput);

    status.append(titleSpan);

    const appendMessage = (text) => {

        const message = document.createElement("div");
        message.textContent = text;
        processOutput.append(message);

        const container = $(processOutput);

        // Max number of messages
        while(container.children().length > 1000){

            // Remove elements //
            container.children().first().remove();
        }

        // For some reason the jquery thing is not working so this is at least a decent choice
        message.scrollIntoView(false);

        // Let modalContainer = $( playModal.dialog );

        // let check = $("#playModalDialog");

        // //assert(modalContainer == check);

        // check.scrollTop = check.scrollHeight;

        // // modalContainer.animate({
        // //     scrollTop: modalContainer.scrollHeight
        // // }, 500);
    };

    appendMessage("Process Started");

    thrive.stdout.on("data", (data) => {

        for(const line of data.toString().split(/\r?\n/g))
            appendMessage(line);
    });

    thrive.stderr.on("data", (data) => {

        appendMessage("ERROR: " + data);
    });

    thrive.on("exit", (code, signal) => {

        if(settings.hideLauncherOnPlay){
            win.show();
        }

        if(signal){
            console.log(`child process exited due to signal ${signal}`);
            appendMessage(`child process exited due to signal ${signal}`);

        } else {
            console.log(`child process exited with code ${code}`);
            appendMessage(`child process exited with code ${code}`);

            if(code === 0)
                appendMessage("Thrive has exited normally (exit code 0).");
        }

        const closeContainer = document.createElement("div");

        closeContainer.style.textAlign = "center";

        const close = document.createElement("div");

        close.textContent = "Close";

        close.className = "CloseButton";

        close.addEventListener("click", () => {
            onClose();
        });

        closeContainer.append(close);

        status.append(closeContainer);

        // Let crash reporter do things
        onEnded(binFolder, signal != null ? signal : code, closeContainer);
    });
}

function runThrive(installFolder, status, onClose, onEnded){
    // Destroy the download progress indicator
    status.innerHTML = "";

    // Check if this is a dehydrated build before running
    checkIsDehydrated(installFolder, status).then(() => {
        onCanRun(installFolder, status, onClose, onEnded);
    }).catch((error) => {
        status.textContent = "Error running: " + error;
    });
}

exports.runThrive = runThrive;
