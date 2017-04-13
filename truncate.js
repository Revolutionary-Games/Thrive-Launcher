const $ = require('jquery');

const ellipsis = '\u2026';


function truncateElement(element, o){

    element.contents().each(function(){

        let current = $(this);

        if(o.length < 1){

            // Need to truncate always
            // Also if we don't remove youtube players and stuff might stay behind
            current.remove();
            
            // if(current.text()){
                
            //     current.text("");


            // }
            
        } else {

            let text = current.text();
            text = $.trim(text) || "";

            if(text.length > o.length){

                // Need to cut //
                current.text(text.substring(0, o.length - 1) + ellipsis);

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
function truncate(htmlElement, length) {

    let o = {
        length: length,
        truncated: false
    };

    // length is how much text is allowed

    let element = $(htmlElement);

    truncateElement(element, o);
    
    return o.truncated;
};

module.exports = truncate;
