// Handles running the godot pck tool
"use strict";

const remote = require("@electron/remote");

const os = remote.require("os");
const path = require("path");
const fs = remote.require("fs");

const {spawn} = remote.require("child_process");

const {convertPackedExecutablePath} = require("./utils");

function getPckToolPath(){
    let tool = null;

    if(os.platform() === "win32"){
        tool = path.join(remote.app.getAppPath(), "tools\\pck\\godotpcktool.exe");
    } else if(os.platform() === "linux"){
        tool = path.join(remote.app.getAppPath(), "tools/pck/godotpcktool");
    } else {
        throw "Unknown platform for pcktool";
    }

    const finalPath = convertPackedExecutablePath(tool);

    if(!fs.existsSync(finalPath)){
        throw "godotpcktool is missing. It should have been included in the launcher...";
    }

    return finalPath;
}

// Runs the pck tool with a operations list to add to the pck
async function runJSONRepackOperation(pckFile, operations){
    for(const entry of operations){
        if(!entry.file || !entry.target)
            throw "invalid operation (missing file or target inside pck in object)";
    }

    const processedOps = JSON.stringify(operations);

    const tool = getPckToolPath();

    return new Promise(function(resolve, reject){
        let message = "";

        // -aoa is overwrite all
        const toolProcess = spawn(tool,
            [pckFile, "--action", "add", "-"]);

        if(!toolProcess){
            reject(new Error("godotpcktool process wasn't started for some reason"));
            return;
        }
        toolProcess.stdin.setEncoding("utf-8");
        toolProcess.stdin.write(processedOps);
        toolProcess.stdin.end();

        toolProcess.stdout.on("data", (data) => {
            message += data;
        });

        toolProcess.stderr.on("data", (data) => {
            message += data;
        });

        toolProcess.on("error", (err) => {
            reject(new Error("godotpcktool failed to start with error: " + err));
        });

        toolProcess.on("close", (code) => {
            if(code !== 0){
                console.log(`godotpcktool exited with code ${code}`);

                reject(new Error("godotpcktool exited with code: " + code + ", message: " +
                    message));
                return;
            }

            resolve();
        });
    });
}

exports.runJSONRepackOperation = runJSONRepackOperation;
