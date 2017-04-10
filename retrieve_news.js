//
// Module for retrieving Thrive news
//
"use strict";

const request = require('request');
const assert = require('assert');

//class 

const devForumNameRegex = /develop.*forum/gi;
const newsFeedNameRegex = /news/gi;

function retrieveNews(callback, url = "http://revolutionarygamesstudio.com/"){

    request(url, function (error, response, body){

        if(error){

            let errObj = {
                error: "Error failed to get content: " + error
            };
            
            callback(errObj, errObj);
            return;
        }

        // response.headers['content-type'] should be text/html
        let parser = new DOMParser();
        let doc = parser.parseFromString(body, "text/html");

        assert(doc);

        let newsDiv = document.createElement("div");
        let devDiv = document.createElement("div");

        for(let element of doc.querySelectorAll("div .feedzy-rss")){

            assert(element.parentNode);

            let titleNode = element.parentNode.querySelector("h2");

            if(!titleNode)
                continue;

            //console.log("title thing is: " + titleNode.textContent);

            if(devForumNameRegex.test(titleNode.textContent)){

                devDiv.append(element);
                continue;
            }

            if(newsFeedNameRegex.test(titleNode.textContent)){

                newsDiv.append(element);
                continue;
            }
        }
        
        
        let news = {
            htmlNodes: newsDiv
        };
        
        let devposts = {
            htmlNodes: devDiv
        };

        callback(news, devposts);
    });
}


module.exports.retrieveNews = retrieveNews;

