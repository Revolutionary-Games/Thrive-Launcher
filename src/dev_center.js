// Everything for handling the devcenter connection and devbuild fetching
"use strict";

const {devCenterURL} = require("./config");

const {settings, saveSettings} = require("../settings");

const defaultConnectionStatus = {
    connected: false,
    error: null,
    username: null,
    email: null,
    developer: false,
    build_of_the_day: null,
};

module.exports.status = Object.assign({}, defaultConnectionStatus);

// Checks if we currently have a devcenter connection and it is valid
function checkConnectionStatus(){

}

module.exports.checkConnectionStatus = checkConnectionStatus;
