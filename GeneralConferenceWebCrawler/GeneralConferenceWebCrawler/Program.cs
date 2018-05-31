using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace GeneralConferenceWebCrawler
{
    class Program
    {
        static string FileName = @"C:\temp\generalconference.csv";
        static string StartUrl = "https://www.lds.org/general-conference/conferences?lang=eng";
        static void Main(string[] args)
        {
            GeneralConferenceGet().Wait();
            Console.WriteLine("processing complete");
            Console.ReadLine();
        }

        static async Task GeneralConferenceGet()
        {
            var allSessions = new List<ConferenceSession>();
            //download the HTML for the top-level site
            var allConfLanding = await DownloadHtmlString(StartUrl);
            //parse the top-level HTML, to get links to all "conferences" (annual or semi-annual)
            var confUrlList = ExtractConferenceUrlList(allConfLanding);
            if (confUrlList != null && confUrlList.Count > 0)
            {
                Console.WriteLine("Found {0} General Conference links", confUrlList.Count);
                //walk through each conference
                foreach (var confUrl in confUrlList)
                {
                    //Console.WriteLine(confUrl);
                    //download the HTML for one conference (each has multiple sessions)
                    var confLanding = await DownloadHtmlString(confUrl);
                    //extract all talks in the conference, with their session designations
                    var sessionList = ExtractSessionList(confLanding);
                    if (sessionList != null && sessionList.Count > 0)
                    {
                        //grab the first one, as an example
                        var firstTalk = sessionList.First();
                        Console.WriteLine("Found {0} sessions in {1} {2}", sessionList.Count, firstTalk.Month, firstTalk.Year);
                        //5 or 6 sessions per conference
                        foreach (var session in sessionList)
                        {
                            if (session.Talks != null && session.Talks.Count > 0)
                            {
                                //several talks per session
                                foreach (var talk in session.Talks)
                                {
                                    var talkLanding = await DownloadHtmlString(talk.Url);
                                    //add details to the existing talk object
                                    ExtractTalkDetails(talkLanding, talk);
                                }
                                Console.WriteLine("\tFound {0} talks in {1}", session.Talks.Count, session.Title);
                            }
                        }
                        allSessions.AddRange(sessionList);
                    }
                }
            }
            if (allSessions.Count > 0)
            {
                //prep the CSV to hold the data
                WriteCsvHeader();
                //write out each talk to the CSV
                AddTalksToCsv(allSessions);
            }
        }

        #region top level
        private static List<string> ExtractConferenceUrlList(string htmlContent)
        {
            /// Parses the HTML content and pulls out URLs that link to conference-specific HTML pages
            /// E.g. the page for just "April 2008" or "October 1971"
            var allConferences = new List<string>();
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);
            //the /1 and /2 are for the year
            //there are other section headers in the HTML, but the ones that have 19-- or 20-- are for conferences
            var findAnchors = doc.DocumentNode.Descendants("a")
                .Where(p => p.Attributes["href"] != null)
                .Where(p => (p.Attributes["href"].Value.StartsWith("/general-conference/1") || p.Attributes["href"].Value.StartsWith("/general-conference/2")))
                .ToList();
            if (findAnchors != null && findAnchors.Count > 0)
            {
                foreach (var anchor in findAnchors)
                {
                    var conferenceLink = String.Format("https://www.lds.org{0}", anchor.Attributes["href"].Value);
                    //four of the conferences are duplicated on the page (menu or recent events)
                    if (!allConferences.Contains(conferenceLink))
                        allConferences.Add(conferenceLink);
                    //Console.WriteLine(anchor.Attributes["href"].Value);
                    //Console.WriteLine(conferenceLink);
                }
            }
            return allConferences;
        }
        private static string ExtractConferenceTitle(HtmlDocument doc)
        {
            var confTitle = String.Empty;
            //find the HTML that holds the "title" ex: "April 2018"
            var confTitleNode = doc.DocumentNode.Descendants("h1")
                .Where(p => p.Attributes["class"] != null)
                .Where(p => p.Attributes["class"].Value.Equals("title"))
                .FirstOrDefault();
            if (confTitleNode != null)
                confTitle = confTitleNode.InnerText.Trim();
            return confTitle;
        }
        #endregion

        #region conference level
        private static List<ConferenceSession> ExtractSessionList(string htmlContent)
        {
            //at the end of this, I should have a list of 5 or 6 session objects
            //  each with a list of talks
            var result = new List<ConferenceSession>();
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);
            var confTitle = ExtractConferenceTitle(doc);

            if (!String.IsNullOrWhiteSpace(confTitle))
            {
                var spaceIndex = confTitle.IndexOf(' ');
                //extract the month
                var confMonth = confTitle.Substring(0, spaceIndex);
                //extract the year
                var confYear = Int32.Parse(confTitle.Substring(spaceIndex + 1));
                var sessionNodeList = doc.DocumentNode.Descendants("div")
                                    .Where(p => p.Attributes["class"] != null)
                                    .Where(p => p.Attributes["class"].Value.Equals("section tile-wrapper layout--3 lumen-layout__item"))
                                    .ToList();
                if (sessionNodeList != null && sessionNodeList.Count > 0)
                {
                    foreach (var sessionNode in sessionNodeList)
                    {
                        var sessionTitle = ExtractSessionTitle(sessionNode);
                        //filter out just the ones that are "sessions"
                        if (!String.IsNullOrWhiteSpace(sessionTitle) && sessionTitle.Contains("Session"))
                        {
                            var talkContainerNodeList = sessionNode.Descendants("div")
                                    .Where(p => p.Attributes["class"] != null)
                                    .Where(p => p.Attributes["class"].Value.Equals("lumen-tile lumen-tile--horizontal lumen-tile--list"))
                                    .ToList();
                            //var ExtractTalkList(talkContainerNodeList, confMonth, confYear, sessionTitle));

                            //don't return anything yet
                            //return ExtractSessions(doc, confMonth, confYear);
                            var talksInSession = ExtractTalkList(talkContainerNodeList);
                            var thisSession = new ConferenceSession
                            {
                                Month = confMonth,
                                Talks = talksInSession,
                                Title = sessionTitle,
                                Year = confYear
                            };
                            result.Add(thisSession);
                        }
                        //else, ignore these
                    }
                }
            }
            else
                throw new ApplicationException("Could not find the title for that link");
            return result;
        }
        private static string ExtractSessionTitle(HtmlNode sessionNode)
        {
            var sessionTitle = String.Empty;
            var sessionTitleNode = sessionNode.Descendants("span").FirstOrDefault();
            if (sessionTitleNode != null)
                sessionTitle = sessionTitleNode.InnerText;
            return sessionTitle;
        }
        #endregion

        #region talk level
        private static string ExtractKicker(HtmlDocument doc)
        {
            //get kicker (summary, just under the video)
            var kicker = doc.DocumentNode.Descendants("p")
                .Where(p => p.Attributes["class"] != null)
                .Where(p => p.Attributes["class"].Value.Equals("kicker"))
                .FirstOrDefault();
            if (kicker != null)
            {
                //Console.WriteLine(kicker.InnerText);
                return kicker.InnerText;
            }
            return null;
        }
        private static void ExtractTalkDetails(string htmlContent, ConferenceTalk talk)
        {
            if (!String.IsNullOrWhiteSpace(htmlContent))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);
                talk.Summary = ExtractKicker(doc);
                talk.VideoUrl = ExtractVideoUrl(doc);
            }
        }
        private static ConferenceTalk ExtractTalk(HtmlNode talkNode, string talkUrl)
        {
            var talkTitle = String.Empty;
            var talkSpeaker = String.Empty;
            var talkPropList = talkNode.Descendants("div").ToList();
            if (talkPropList != null && talkPropList.Count == 3)
            {
                var titleDiv = talkPropList[1];
                if (titleDiv != null)
                {
                    talkTitle = titleDiv.InnerText;
                }
                var speakerDiv = talkPropList[2];
                if (speakerDiv != null)
                {
                    talkSpeaker = speakerDiv.InnerText;
                }
            }
            //at this point, all properties should be populated
            var talk = new ConferenceTalk
            {
                Speaker = talkSpeaker,
                Title = talkTitle,
                Url = talkUrl
            };
            return talk;
        }
        private static List<ConferenceTalk> ExtractTalkList(List<HtmlNode> talkContainerNodeList)
        {
            var result = new List<ConferenceTalk>();
            if (talkContainerNodeList != null && talkContainerNodeList.Count > 0)
            {
                foreach (var talkContainerNode in talkContainerNodeList)
                {
                    //grab the URL of the talk
                    var talkUrl = ExtractTalkUrl(talkContainerNode);
                    var talkNodeList = talkContainerNode.Descendants("div")
                            .Where(p => p.Attributes["class"] != null)
                            .Where(p => p.Attributes["class"].Value.Equals("lumen-tile__text-wrapper"))
                            .ToList();
                    if (talkNodeList != null && talkNodeList.Count > 0)
                    {
                        foreach (var talkNode in talkNodeList)
                        {
                            result.Add(ExtractTalk(talkNode, talkUrl));
                        }
                    }
                }
            }
            return result;
        }
        private static string ExtractTalkUrl(HtmlNode talkContainerNode)
        {
            var talkUrl = String.Empty;
            var talkNode = talkContainerNode.Descendants("a").FirstOrDefault();
            if (talkNode != null)
            {
                var rawUrl = talkNode.Attributes["href"].Value;
                //strip off the language indicator
                var paramIndex = rawUrl.IndexOf('?');
                talkUrl = String.Format("https://www.lds.org{0}", rawUrl.Substring(0, paramIndex));
            }
            return talkUrl;
        }
        private static string ExtractVideoUrl(HtmlDocument doc)
        {
            //get media drawer
            var downloadsNode = doc.DocumentNode.Descendants("ul")
                .Where(p => p.Attributes["class"] != null)
                .Where(p => p.Attributes["class"].Value.Equals("drawer drawer--inline drawer--light downloads"))
                .FirstOrDefault();
            if (downloadsNode != null)
            {
                //filter out only the videos
                var downloadAnchors = downloadsNode.Descendants("a")
                    .Where(p => p.Attributes["href"] != null)
                    .Where(p => p.Attributes["href"].Value.Contains("mp4"))
                    .ToList();
                if (downloadAnchors != null && downloadAnchors.Count > 0)
                {
                    var videoUrl = String.Empty;
                    HtmlNode anchor = null;
                    if (downloadAnchors.Count == 1)
                        anchor = downloadAnchors.First();
                    else
                        anchor = downloadAnchors.Last(); //highest resolution
                    if (anchor != null)
                    {
                        videoUrl = anchor.Attributes["href"].Value;
                        return videoUrl;
                    }
                }
            }
            return null;
        }
        #endregion

        #region CSV management
        private static void AddTalksToCsv(List<ConferenceSession> allSessions)
        {
            //"Year,Month,Session,Speaker,Title,Summary,Url,Video";
            var writeFormat = "{0},{1},{2},{3},{4},{5},{6},{7}";
            if (allSessions != null && allSessions.Count > 0)
            {
                //tried ASCII, UTF8- CSV doesn't like them
                using (var file = new StreamWriter(FileName, true, Encoding.Unicode))
                {
                    foreach (var session in allSessions)
                    {
                        if (session.Talks != null && session.Talks.Count > 0)
                        {
                            foreach (var talk in session.Talks)
                            {
                                //some of the "talks" are actually headers or section blocks (no speaker or title)
                                if (!String.IsNullOrWhiteSpace(talk.Speaker) && !String.IsNullOrWhiteSpace(talk.Title))
                                {
                                    var scrubbedSession = ScrubCsvString(session.Title);
                                    var scrubbedSpeaker = ScrubCsvString(talk.Speaker);
                                    var scrubbedTitle = ScrubCsvString(talk.Title);
                                    var scrubbedSummary = ScrubCsvString(talk.Summary);
                                    var lineToWrite = String.Format(writeFormat,
                                        session.Year, session.Month, scrubbedSession, scrubbedSpeaker,
                                        scrubbedTitle, scrubbedSummary, talk.Url, talk.VideoUrl);
                                    file.WriteLine(lineToWrite);
                                }
                            }
                        }
                    }
                }
            }
        }
        private static string ScrubCsvString(string data)
        {
            if (data == null)
                return null;

            if (data.Contains("\""))
                data = data.Replace("\"", "\"\"");
            if (data.Contains("&#x27;"))
                data = data.Replace("&#x27;", "'");
            //if (data.Contains("â€™"))
            //    data = data.Replace("â€™", "\'");
            ////TODO: might need just a normal "-"
            //if (data.Contains("â€”"))
            //    data = data.Replace("â€”", "—");
            //if (data.Contains("â€¦"))
            //    data = data.Replace("â€¦", "…");
            if (data.Contains("*"))
                data = data.Replace("*", "");

            if (data.Contains(","))
                data = String.Format("\"{0}\"", data);
            if (data.Contains(Environment.NewLine))
                data = String.Format("\"{0}\"", data);

            return data;
        }
        private static void WriteCsvHeader()
        {
            var columns = "Year,Month,Session,Speaker,Title,Summary,Url,Video";
            using (var file = new StreamWriter(FileName, false))
            {
                file.WriteLine(columns);
            }
        }
        #endregion

        #region private helpers
        private static async Task<string> DownloadHtmlString(string url)
        {
            var result = String.Empty;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                result = await client.GetStringAsync(url);
                //Console.WriteLine(result);
            }

            return result;
        }
        #endregion
    }
}
