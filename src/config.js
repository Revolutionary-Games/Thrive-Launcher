// Renderer configuration options for easy changing when debugging
"use strict";

module.exports = {
    // Shows output from 7z. Not really useful as it shows no actual progress
    showUnpackMessages: false,

    // If true then the testing data (local, unsigned) file is loaded
    loadTestVersionData: false,

    // When true checks if computer has intel graphics that likely cause problems
    checkGraphicsCard: false,

    devBuildCacheName: "devbuild_cache.json",

    // When true errors in the renderer process is caught with electron-log
    catchErrors: true,

    // For local testing
    // devCenterURL: "http://localhost:5000",
    // devCenterURL: "https://staging.dev.revolutionarygamesstudio.com/",
    devCenterURL: "https://dev.revolutionarygamesstudio.com/",

    // Hides main website link in steam version
    hideMainWebsiteInSteam: false,

    // Hides the DevBuild options (login, selecting DevBuild, clearing cache)
    hideDevBuildsInSteam: true,
};
