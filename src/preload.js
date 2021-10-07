"use strict";

const log = require("electron-log");

window.log = log.functions;
Object.assign(console, log.functions);
