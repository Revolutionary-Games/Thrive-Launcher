"use strict";

const $ = require("jquery");

const ellipsis = "\u2026";


function truncateElement(element, o){

    element.contents().each(function(){

        const current = $(this);

        if(o.length < 1){

            // Need to truncate always
            // Also if we don't remove youtube players and stuff might stay behind
            current.remove();

            // If(current.text()){

            //     current.text("");


            // }

        } else {

            let text = current.text();
            text = $.trim(text) || "";

            if(text.length > o.length){

                // Need to cut //
                const truncatedText = text.substring(0, o.length - 1) + ellipsis;

                // Special case thing which makes stuff work //
                if(current.length > 0 && typeof current[0].textContent){

                    current[0].textContent = truncatedText;

                } else {

                    console.log("Special case truncate not matched. This text element was " +
                                "probably not cut correctly");

                    current.text(truncatedText);
                }

                // Verify that it worked
                const actualData = current.text();

                if(actualData == text){
                    console.error("Cutting failed for string: " + actualData +
                                  " shouldn't equal initial text: " + text);
                }

                o.truncated = true;

                o.length -= o.length;

            } else {

                // Reduce available length
                o.length -= current.text().length;
            }
        }

        // Handle sub stuff //
        current.contents().each(function(){

            truncateElement($(this), o);
        });
    });
}

// Truncates jquery elements in place
function truncate(htmlElement, length){

    const o = {
        length: length,
        truncated: false,
    };

    // Length is how much text is allowed

    const element = $(htmlElement);

    truncateElement(element, o);

    return o.truncated;
}

module.exports = truncate;
