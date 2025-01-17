﻿using Aptabase.Tools.DemoDataGenerator;
using System.Net.Http;
using System.Net.Http.Json;
var random = new Random();

var appKey = "A-DEV-0000000000";
var sessions = 10000;
var maxEventsPerSession = 5;
var minStart = TimeSpan.FromDays(30);
var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("http://localhost:3000");
httpClient.DefaultRequestHeaders.Add("App-Key", appKey);

for (var i=0; i < sessions; i++)
{
    var startedAt = DateTime.UtcNow.AddMinutes(random.Next(Convert.ToInt32(minStart.TotalMinutes * -1), 0));
    var (sessionId, timestamps) = DemoDataSource.NewSession(startedAt, maxEventsPerSession);
    var (countryCode, regionName, city) = DemoDataSource.GetLocation();
    var (osName, osVersion, engineName, engineVersion) = DemoDataSource.GetDeviceInfo();
    var locale = DemoDataSource.GetLocale();
    var ipAddress = DemoDataSource.GetRandomIpAddress();
    var (appVersion, appBuildNumber, sdkVersion) = DemoDataSource.GetAppInfo();

    foreach (var timestamp in timestamps)
    {
        var (eventName, props) = DemoDataSource.NewEvent();
        var body = JsonContent.Create(new
        {
            timestamp = timestamp.ToString("o"),
            sessionId,
            eventName,
            systemProps = new { osName, osVersion, locale, appVersion, appBuildNumber, sdkVersion },
            props
        });

        body.Headers.Add("CloudFront-Viewer-Address", ipAddress);
        var response = await httpClient.PostAsync("/api/v0/event", body);
        Console.WriteLine($"[{response.StatusCode}] {timestamp:o} {eventName}");
    }
}
