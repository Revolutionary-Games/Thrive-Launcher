// Code for getting the public launcher signing key for signature validation
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");

const openpgp = require("openpgp");

// Use getLauncherKey instead
let launcherKey = null;

// Loads the key required for verifying version information //
function getLauncherKey(){
    return new Promise(function(resolve, reject){
        if(launcherKey){
            resolve(launcherKey);
        } else {
            fs.readFile(path.join(remote.app.getAppPath(), "version_data/launcher_key.pgp"),
                "utf8",
                function(err, data){

                    if(err){
                        const msg = "Can't read launcher version info signing key";

                        reject(new Error(msg + ". " + err));
                        console.log(err);
                        return;
                    }

                    openpgp.key.readArmored(data).then((key) => {

                        launcherKey = key.keys;

                        let keyid = null;

                        try{
                            keyid = launcherKey["0"].primaryKey.keyid.toHex();
                        } catch(err){
                            reject(new Error("Loaded signing key but it is invalid " +
                                "(property error): " + err));
                            return;
                        }

                        // Console.log("Key: " + launcherKey);
                        console.log("Signing key loaded: " + keyid);
                        resolve(launcherKey);

                    }, (err) => {
                        reject(new Error("Couldn't parse signing key: " + err));
                    });
                });
        }
    });
}

module.exports.getLauncherKey = getLauncherKey;
