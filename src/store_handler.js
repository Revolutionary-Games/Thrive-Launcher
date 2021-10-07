"use strict";

const {hideElement} = require("./utils");

const info = {
    store: null,
    isStoreVersion: false,
};

function getStylizedName(){
    if(!info.isStoreVersion){
        return "Not Store Version";
    }

    if(info.store === "steam"){
        return "Steam";
    } else if(info.store === "itch"){
        return "itch.io";
    }

    return info.store;
}

function applyHiddenElements(){
    if(!info.isStoreVersion){
        // Hide options that apply only in store version
        hideElement("storeVersionSettings");
        hideElement("installedVersionsNoteForStore");
        return;
    }

    hideElement("donateLink");

    if(info.store === "steam"){
        hideElement("patreonLink");
        hideElement("thriveItchLink");
    }
}

exports.storeInfo = info;
exports.getStylizedName = getStylizedName;
exports.applyHiddenElements = applyHiddenElements;
