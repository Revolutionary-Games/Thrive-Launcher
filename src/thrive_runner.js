// Handles starting the child thrive process
"use strict";

const log = require("electron-log");
const remote = require("@electron/remote");

const win = remote.getCurrentWindow();

const fs = remote.require("fs");
const path = require("path");
const child_process = remote.require("child_process");

const {settings} = require("./settings.js");
const {findBinInRelease, getThriveExecutableName} = require("./unpack");
const {checkIsDehydrated} = require("./rehydrate");

let customLDPreload = null;

function setLDPreload(value){
    if(value)
        log.info("LD_PRELOAD for Thrive launch set to:", value);
    customLDPreload = value;
}

function onCanRun(installFolder, status, onClose, onEnded){
    status.textContent = "preparing to launch";

    // Find bin folder //
    const binFolder = findBinInRelease(installFolder);

    if(!fs.existsSync(binFolder)){

        status.textContent = "Error 'bin' folder is missing! To redownload delete " +
            installFolder;
        return;
    }

    log.info("Detected bin folder as:", binFolder);

    // Check that executable is there //
    const exename = getThriveExecutableName();

    if(!fs.existsSync(path.join(binFolder, exename))){

        status.textContent = "Error: Thrive executable is missing! To redownload delete " +
            installFolder;
        return;
    }

    status.textContent = "launching...";

    const launchArgs = [];
    const launchEnv = Object.create(process.env);

    if(customLDPreload){
        log.info("Launching with custom LD_PRELOAD:", customLDPreload);
        launchEnv.LD_PRELOAD = customLDPreload;
    } else {
        launchEnv.LD_PRELOAD = "";
    }

    if(settings.launchOptionSingleProcess)
        launchArgs.push("--single-process");

    if(settings.launchOptionNoGUISandbox)
        launchArgs.push("--no-sandbox");

    if(settings.launchOptionNoGUIGPU)
        launchArgs.push("--disable-gpu");

    console.log("launching thrive from folder '" + binFolder + "' with arguments: ",
        launchArgs);

    if(settings.closeLauncherOnGameStart){
        // Need to specially start the child process in a detached way to make it outlive us
        const thrive = child_process.spawn(path.join(binFolder, exename),
            launchArgs,
            {cwd: binFolder, detached: true, stdio: ["ignore", "ignore", "ignore"]});

        thrive.unref();

        log.info("Closing launcher as Thrive process has been started and configured to do" +
            " so");
        win.close();
        return;
    }

    // Cwd is where relative to things are installed
    const thrive = child_process.spawn(path.join(binFolder, exename),
        launchArgs,
        {cwd: binFolder, env: launchEnv});

    if(settings.hideLauncherOnPlay){
        win.minimize();
    }

    status.innerHTML = "";

    const processOutput = document.createElement("div");
    processOutput.classList.add("gameOutput");

    const beginningOutput = document.createElement("div");
    const truncatedWarning = document.createElement("p");
    truncatedWarning.textContent = "Output is too long, it was truncated! See the log file" +
        " for full output.";
    truncatedWarning.style.display = "none";
    const endingOutput = document.createElement("div");

    processOutput.append(beginningOutput);
    processOutput.append(truncatedWarning);
    processOutput.append(endingOutput);

    const gameOutputStats = {
        totalLines: 0,
        currentLines: 0,
        appendToEnd: false,
    };

    const titleSpan = document.createElement("div");
    titleSpan.textContent = "Thrive is running. Log output: ";
    status.append(titleSpan);

    status.append(processOutput);

    const appendMessage = (text) => {

        const message = document.createElement("div");
        message.textContent = text;

        gameOutputStats.totalLines += 1;
        gameOutputStats.currentLines += 1;

        if(!gameOutputStats.appendToEnd){
            if(gameOutputStats.currentLines > settings.beginningKeptGameOutput){
                // Switch to outputting to the end
                gameOutputStats.appendToEnd = true;
                gameOutputStats.currentLines = 1;
                truncatedWarning.style.display = "block";
            } else {
                beginningOutput.append(message);
            }
        }

        if(gameOutputStats.appendToEnd){
            // Remove from beginning (of the second part) if too many messages
            if(gameOutputStats.currentLines > settings.lastKeptGameOutput){
                gameOutputStats.currentLines -= 1;
                endingOutput.removeChild(endingOutput.children[0]);
            }

            endingOutput.append(message);
        }

        // For some reason the jquery thing is not working so this is at least a decent choice
        message.scrollIntoView(false);
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

        // Final log message is printed here to make sure it is visible
        if(signal){
            console.log(`child process exited due to signal ${signal}`);
            appendMessage(`child process exited due to signal ${signal}`);

        } else {
            console.log(`child process exited with code ${code}`);
            appendMessage(`child process exited with code ${code}`);

            if(code === 0)
                appendMessage("Thrive has exited normally (exit code 0).");
        }
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
exports.setLDPreload = setLDPreload;
