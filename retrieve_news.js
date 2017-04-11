//
// Module for retrieving Thrive news
//
"use strict";

const request = require('request');
const assert = require('assert');

const FeedParser = require('feedparser');


// If true all feed items appear at once
const feedWaitForAll = true;



function parseFeed(feed, resultObj){

    return new Promise((resolve, reject) => {

        let errorCallback = (error) => {
            
            resultObj.htmlNodes = null;
            resultObj.error = "Error failed to get content: " + error;

            resolve();
            return;
        }
        
        // Define our streams
        var req = request(feed, {timeout: 10000, pool: false});
        req.setMaxListeners(50);
        // Some feeds do not respond without user-agent and accept headers.
        req.setHeader('user-agent', 'Thrive-Launcher');
        req.setHeader('accept', 'text/html,application/xhtml+xml');

        let feedparser = new FeedParser();

        // Define our handlers
        req.on('error', errorCallback);
        req.on('response', function(res) {
            
            if(res.statusCode != 200)
                return this.emit('error', new Error('Bad status code'));

            // There could be charset translation here, but iconv requires native extensions
            //var charset = getParams(res.headers['content-type'] || '').charset;
            //res = maybeTranslate(res, charset);
            
            // And boom goes the dynamite
            res.pipe(feedparser);
        });

        feedparser.on('error', errorCallback);

        
        feedparser.on('end', function(){

            resolve();
        });
        
        feedparser.on('readable', function() {
            var post;
            while (post = this.read()) {
                //console.log(JSON.stringify(post, ' ', 4));

                let span = document.createElement("span");

                let title = document.createElement("h3");

                title.textContent = post.title;

                span.append(title);

                let link = document.createElement("span");

                link.classList.add("FeedLink");

                link.innerHTML = "<a href='" + post.link + "'>" + post.link  + "</a>";

                span.append(link);

                resultObj.htmlNodes.append(span);
            }
        });
    });
}

function retrieveNews(callback, url = "http://revolutionarygamesstudio.com/"){

    let news = {
        htmlNodes: document.createElement("div")
    };
    
    let devposts = {
        htmlNodes: document.createElement("div")
    };

    let newsPromise = parseFeed("http://revolutionarygamesstudio.com/feed/", news);
    let devsPromise = parseFeed("http://forum.revolutionarygamesstudio.com/posts.rss",
                                devposts);


    if(feedWaitForAll){

        newsPromise.then(() => {
            devsPromise.then(() => {

                callback(news, devposts);
            });
        });
        
    } else {

        callback(news, devposts);
    }


    
    return;

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

