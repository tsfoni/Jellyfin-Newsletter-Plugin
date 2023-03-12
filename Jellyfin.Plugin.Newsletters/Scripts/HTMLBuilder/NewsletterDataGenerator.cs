#pragma warning disable 1591, SYSLIB0014, CA1002
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Scripts.Scraper;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
// using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.Scripts.NLDataGenerator;

public class NewsletterDataGenerator
{
    // Global Vars
    // Readonly
    private readonly PluginConfiguration config;
    private readonly string newslettersDir;
    private readonly string newsletterDataFile;

    private readonly string currRunList;
    private readonly string archiveFile;
    private readonly string myDataDir;
    private Logger logger;

    // Non-readonly
    private static string append = "Append";
    private static string write = "Overwrite";
    private IProgress<double> progress;
    private List<JsonFileObj> archiveSeriesList;
    // private List<string> fileList;

    public NewsletterDataGenerator(IProgress<double> passedProgress)
    {
        logger = new Logger();
        config = Plugin.Instance!.Configuration;
        progress = passedProgress;
        myDataDir = config.TempDirectory + "/Newsletters";

        archiveFile = config.MyDataDir + config.ArchiveFileName; // curlist/archive
        currRunList = config.MyDataDir + config.CurrRunListFileName;
        newsletterDataFile = config.MyDataDir + config.NewsletterDataFileName;

        archiveSeriesList = new List<JsonFileObj>();
        newslettersDir = config.NewsletterDir; // newsletterdir
        Directory.CreateDirectory(newslettersDir);

        // WriteFile(write, "/ssl/htmlbuilder.log", newslettersDir); // testing
    }

    public Task GenerateDataForNextNewsletter()
    {
        progress.Report(25);
        archiveSeriesList = PopulateFromArchive(); // Files that shouldn't be processed again
        progress.Report(50);
        GenerateData();
        progress.Report(75);
        CopyCurrRunDataToNewsletterData();
        progress.Report(99);

        return Task.CompletedTask;
    }

    public List<JsonFileObj> PopulateFromArchive()
    {
        List<JsonFileObj> myObj = new List<JsonFileObj>();
        if (File.Exists(archiveFile))
        {
            StreamReader sr = new StreamReader(archiveFile);
            string arFile = sr.ReadToEnd();
            foreach (string series in arFile.Split(";;"))
            {
                JsonFileObj? currArcObj = JsonConvert.DeserializeObject<JsonFileObj?>(series);
                if (currArcObj is not null)
                {
                    myObj.Add(currArcObj);
                }
            }

            sr.Close();
        }

        return myObj;
    }

    private void GenerateData()
    {
        StreamReader sr = new StreamReader(currRunList); // curlist/archive
        string readScrapeFile = sr.ReadToEnd();

        foreach (string? ep in readScrapeFile.Split(";;"))
        {
            JsonFileObj? obj = JsonConvert.DeserializeObject<JsonFileObj?>(ep);
            if (obj is not null)
            {
                JsonFileObj currObj = new JsonFileObj();
                currObj.Title = obj.Title;
                archiveSeriesList.Add(currObj);
            }

            break;
        }

        sr.Close();
    }

    public string FetchImagePoster(string title)
    {
        string url = "https://www.googleapis.com/customsearch/v1?key=" + config.ApiKey + "&cx=" + config.CXKey + "&num=1&searchType=image&fileType=jpg&q=" + string.Join("%", (title + " series + cover + art").Split(" "));
        // google API image search: curl 'https://www.googleapis.com/customsearch/v1?key=AIzaSyBbh1JoIyThpTHa_WT8k1apsMBUC9xUCEs&cx=4688c86980c2f4d18&num=1&searchType=image&fileType=jpg&q=my%hero%academia'
        logger.Debug("Image Search URL: " + url);
        // return "https://m.media-amazon.com/images/W/IMAGERENDERING_521856-T1/images/I/91eNqTeYvzL.jpg";

        // HttpClient hc = new HttpClient();
        // string res = await hc.GetStringAsync(url).ConfigureAwait(false);
        WebClient wc = new WebClient();
        string res = wc.DownloadString(url);
        string urlResFile = myDataDir + "/.lasturlresponse";

        WriteFile(write, urlResFile, res);

        bool testForItems = false;

        foreach (string line in File.ReadAllLines(urlResFile))
        {
            WriteFile(write, "/ssl/testUrlReader.txt", line); // testing
            if (testForItems)
            {
                if (line.Contains("\"link\":", StringComparison.OrdinalIgnoreCase))
                {
                    string fetchedURL = line.Split("\"")[3];

                    logger.Info("Fetched Image: " + fetchedURL);
                    if (fetchedURL.Length == 0)
                    {
                        logger.Warn("Image URL failed to be captured. Is this an error?");
                    }

                    return fetchedURL; // Actual URL
                }
            }
            else
            {
                if (line.Contains("\"items\":", StringComparison.OrdinalIgnoreCase))
                {
                    testForItems = true;
                }
            }
        }

        return string.Empty;
    }

    private void CopyCurrRunDataToNewsletterData()
    {
        if (File.Exists(currRunList)) // archiveFile
        {
            Stream input = File.OpenRead(currRunList);
            Stream output = new FileStream(newsletterDataFile, FileMode.Append, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            File.Delete(currRunList);
        }
    }

    private void WriteFile(string method, string path, string value)
    {
        if (method == append)
        {
            File.AppendAllText(path, value);
        }
        else if (method == write)
        {
            File.WriteAllText(path, value);
        }
    }
}