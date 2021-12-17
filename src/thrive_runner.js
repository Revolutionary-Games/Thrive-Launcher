// Handles starting the child thrive process
"use strict";

const log = require("electron-log");
const remote = require("@electron/remote");

const win = remote.getCurrentWindow();

const fs = remote.require("fs");
const path = require("path");
const child_process = remote.require("child_process");

const {settings} = require("./settings.js");
const {
    maxDelayBetweenExitAfterErrorSignal, closeDelayAfterAutoStart,
    minimizeDelayAfterGameStart, checkLauncherProcessIsRunningDelay,
} = require("./config");
const {findBinInRelease, getThriveExecutableName} = require("./unpack");
const {checkIsDehydrated} = require("./rehydrate");

let customLDPreload = null;
let lastGameStartedTime = null;

function setLDPreload(value){
    if(value)
        log.info("LD_PRELOAD for Thrive launch set to:", value);
    customLDPreload = value;
}

function onCanRun(installFolder, status, onClose, onEnded){
    const {binFolder, exename} = findExecutableToRun(installFolder, status);

    if(binFolder === null)
        return;

    const {launchArgs, launchEnv} = prepareLaunchArguments();

    console.log("Launching thrive from folder '" + binFolder + "' with arguments: ",
        launchArgs);

    const appendMessage = prepareGameOutputWriteFunction(status);

    lastGameStartedTime = new Date();

    if(settings.closeLauncherOnGameStart){
        // Need to specially start the child process in a detached way to make it outlive us
        const thrive = child_process.spawn(path.join(binFolder, exename),
            launchArgs,
            {
                cwd: binFolder, detached: true, stdio: ["ignore", "ignore", "ignore"],
                env: launchEnv,
            });

        appendMessage("Detached Process Started");

        const runData = registerDefaultProcessExitHandlers(thrive, () => {
        }, status, binFolder, onClose, onEnded, appendMessage);

        window.setTimeout(() => {
            if(runData.exitHandlerRan || thrive.exitCode != null){
                log.error("Detached child process has ended already");

                if(!runData.exitHandlerRan){
                    // TODO: this seems to hit very often, are normal callbacks not working
                    // for detached mode?
                    log.warn("Normal exit handler hasn't ran, doing our basic run of it");

                    handleChildProcessEnd(status, thrive.exitCode, null, binFolder, onClose,
                        onEnded, appendMessage);
                }

                appendMessage("Error: child process has already ended, not closing the" +
                    " launcher");
                appendMessage("To see game output for clues as to what went wrong, " +
                    "please disable the auto start game option.");

                return;
            }

            thrive.unref();

            log.info("Closing launcher as Thrive process has been started and " +
                "configured to do so");
            win.close();
        }, closeDelayAfterAutoStart);

        return;
    }

    // Cwd is where relative to things are installed
    const thrive = child_process.spawn(path.join(binFolder, exename),
        launchArgs,
        {cwd: binFolder, env: launchEnv});

    appendMessage("Process Started");

    thrive.stdout.on("data", (data) => {
        for(const line of data.toString().split(/\r?\n/g))
            appendMessage(line);
    });

    thrive.stderr.on("data", (data) => {
        appendMessage(data, "red");
    });

    const showLauncher = () => {
        if(settings.hideLauncherOnPlay){
            log.debug("showing the launcher again");
            win.show();
        }
    };

    const runData = registerDefaultProcessExitHandlers(thrive, showLauncher, status, binFolder,
        onClose, onEnded, appendMessage);

    window.setTimeout(() => {
        if(runData.exitHandlerRan){
            log.warn("Game process already exited, not hiding the launcher");
            return;
        }

        if(settings.hideLauncherOnPlay){
            win.minimize();
        }

    }, minimizeDelayAfterGameStart);

    window.setTimeout(() => {
        if(runData.exitHandlerRan){
            // Exit already detected, don't need to do anything
            return;
        }

        // This seems to sometimes hit when the game exits very fast
        if(thrive.exitCode != null){
            log.error("Started child process has ended already without it being detected, " +
                "force running our exit handler");

            runData.exitHandlerRan = true;

            showLauncher();

            handleChildProcessEnd(status, thrive.exitCode, null, binFolder, onClose,
                onEnded, appendMessage);

            appendMessage("Error: child process has already ended but it was not detected" +
                " normally");
        }
    }, checkLauncherProcessIsRunningDelay);
}

function findExecutableToRun(installFolder, status){
    status.textContent = "preparing to launch";

    // Find bin folder //
    const binFolder = findBinInRelease(installFolder);

    if(!fs.existsSync(binFolder)){

        status.textContent = "Error 'bin' folder is missing! To redownload delete " +
            installFolder;
        return {binFolder: null, exename: null};
    }

    log.info("Detected bin folder as:", binFolder);

    // Check that executable is there //
    const exename = getThriveExecutableName();

    if(!fs.existsSync(path.join(binFolder, exename))){

        status.textContent = "Error: Thrive executable is missing! To redownload delete " +
            installFolder;
        return {binFolder: null, exename: null};
    }

    status.textContent = "launching...";
    return {binFolder, exename};
}

function prepareLaunchArguments(){
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

    return {launchArgs, launchEnv};
}

function prepareGameOutputWriteFunction(status){
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

    return (text, color) => {

        const message = document.createElement("div");
        message.textContent = text;
        if(color){
            message.style.color = color;
        }
    
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
}

function registerDefaultProcessExitHandlers(thrive, customExit, status, binFolder, onClose,
    onEnded, appendMessage){
    const runData = {exitHandlerRan: false};

    thrive.on("exit", (code, signal) => {
        console.debug("game process exit handler called:", code, signal);

        if(runData.exitHandlerRan)
            return;

        runData.exitHandlerRan = true;

        customExit();

        handleChildProcessEnd(status, code, signal, binFolder, onClose, onEnded,
            appendMessage);
    });

    thrive.on("error", (error) => {
        log.error("Error running detached Thrive process:", error);

        // Process with delay allowing "exit" callback to happen preferably
        window.setTimeout(() => {
            if(runData.exitHandlerRan)
                return;

            runData.exitHandlerRan = true;

            customExit();

            // TODO: should the error actually be passed through as is and not as a string?
            handleChildProcessEnd(status, "" + error, null, binFolder, onClose, onEnded,
                appendMessage);
        }, maxDelayBetweenExitAfterErrorSignal);
    });

    return runData;
}

function handleChildProcessEnd(status, code, signal, binFolder, onClose, onEnded,
    appendMessage){
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

    if(lastGameStartedTime == null){
        log.error("Last start time is not set, using current time");
        lastGameStartedTime = new Date();
    }

    // Let crash reporter do things
    onEnded(binFolder, signal != null ? signal : code, closeContainer,
        new Date() - lastGameStartedTime);

    // Final log message is printed here to make sure it is visible
    if(signal){
        log.info(`child process exited due to signal ${signal}`);
        appendMessage(`child process exited due to signal ${signal}`);

    } else {
        log.info(`child process exited with code ${code}`);
        appendMessage(`child process exited with code ${code}`);

        if(code === 0)
            appendMessage("Thrive has exited normally (exit code 0).");
    }
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
