using System.Text.Json;
using Aptabase.Features.GeoIP;
using Aptabase.Features.Ingestion.Buffer;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Ingestion;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EventsController : Controller
{
    private readonly ILogger _logger;
    private readonly IIngestionCache _cache;
    private readonly IEventBuffer _buffer;
    private readonly GeoIPClient _geoIP;

    public EventsController(IIngestionCache cache,
                            IEventBuffer buffer,
                            GeoIPClient geoIP,
                            ILogger<EventsController> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _geoIP = geoIP ?? throw new ArgumentNullException(nameof(geoIP));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("/api/v0/event")]
    [EnableCors("AllowAny")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Single(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromHeader(Name = "User-Agent")] string? userAgent,
        [FromBody] EventBody body,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        var (valid, validationMessage) = IsValidBody(body);
        if (!valid)
        {
            _logger.LogWarning($"Dropping event from {appKey} because: {validationMessage}.");
            return BadRequest(validationMessage);
        }

        var app = await _cache.FindByAppKey(appKey, cancellationToken);
        if (string.IsNullOrEmpty(app.Id))
            return AppNotFound(appKey);

        if (app.IsLocked) 
            return BadRequest($"Owner account is locked.");

        // We never expect the Web SDK to send the OS name, so it's safe to assume that if it's missing the event is coming from a browser
        var isWeb = string.IsNullOrEmpty(body.SystemProps.OSName);

        // For web events, we need to parse the user agent to get the OS name and version
        if (isWeb && !string.IsNullOrEmpty(userAgent))
        {
            var (osName, osVersion) = UserAgentParser.ParseOperatingSystem(userAgent);
            body.SystemProps.OSName = osName;
            body.SystemProps.OSVersion = osVersion;

            var (engineName, engineVersion) = UserAgentParser.ParseBrowser(userAgent);
            body.SystemProps.EngineName = engineName;
            body.SystemProps.EngineVersion = engineVersion;
        }

        // We can't rely on User-Agent header sent by the SDK for non-web events, so we fabricate one
        // This can be removed when this issue is implemented: https://github.com/aptabase/aptabase/issues/23
        if (!isWeb)
            userAgent = $"{body.SystemProps.OSName}/{body.SystemProps.OSVersion} {body.SystemProps.EngineName}/{body.SystemProps.EngineVersion} {body.SystemProps.Locale}";

        var clientIp = HttpContext.ResolveClientIpAddress();
        var location = _geoIP.GetClientLocation(HttpContext);
        var trackingEvent = NewTrackingEvent(app.Id, location.CountryCode, location.RegionName, clientIp, userAgent ?? "", body);
        _buffer.Add(ref trackingEvent);

        return Ok(new { });
    }

    [HttpPost("/api/v0/events")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Multiple(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromHeader(Name = "User-Agent")] string? userAgent,
        [FromBody] EventBody[] events,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        if (events.Length > 25)
            return BadRequest($"Too many events ({events.Length}) in a single request. Maximum is 25.");

        var validEvents = events.Where(e => { 
            var (valid, validationMessage) = IsValidBody(e);
            if (!valid)
                _logger.LogWarning("Dropping event from {AppKey}. {ValidationMessage}", appKey, validationMessage);
            return valid;
        }).ToArray();

        if (!validEvents.Any())
            return Ok(new { });

        var app = await _cache.FindByAppKey(appKey, cancellationToken);
        if (string.IsNullOrEmpty(app.Id))
            return AppNotFound(appKey);

        if (app.IsLocked) 
            return BadRequest($"Owner account is locked.");

        var clientIp = HttpContext.ResolveClientIpAddress();
        var location = _geoIP.GetClientLocation(HttpContext);
        var trackingEvents = validEvents.Select(e => NewTrackingEvent(app.Id, location.CountryCode, location.RegionName, clientIp, userAgent ?? "", e));

        _buffer.AddRange(ref trackingEvents);

        return Ok(new { });
    }

    private IActionResult AppNotFound(string appKey)
    {
        _logger.LogWarning("Appplication not found with given app key: {AppKey}", appKey);
        return NotFound($"Appplication not found with given app key: {appKey}");
    }

    private (bool, string) IsValidBody(EventBody? body)
    {
        if (body is null)
            return (false, "Missing event body.");

        if (body.Timestamp > DateTime.UtcNow.AddMinutes(1))
            return (false, "Future events are not allowed.");

        if (body.Timestamp < DateTime.UtcNow.AddDays(-1))
        {
            _logger.LogWarning("Event timestamp {EventTimestamp} is too old.", body.Timestamp);
            return (false, "Event is too old.");
        }

        var locale = LocaleFormatter.FormatLocale(body.SystemProps.Locale);
        if (locale is null)
            _logger.LogWarning("Invalid locale {Locale} received from {OS} using {SdkVersion}", locale, body.SystemProps.OSName, body.SystemProps.SdkVersion);

        body.SystemProps.Locale = locale;

        if (body.Props is not null)
        {
            if (body.Props.RootElement.ValueKind == JsonValueKind.String)
            {
                var valueAsString = body.Props.RootElement.GetString() ?? "";
                if (TryParseDocument(valueAsString, out var doc) && doc is not null)
                    body.Props = doc;
                else 
                    return (false, $"Props must be an object or a valid stringified JSON, was: {body.Props.RootElement.GetRawText()}");
            }

            if (body.Props.RootElement.ValueKind != JsonValueKind.Object)
                return (false, $"Props must be an object or a valid stringified JSON, was: {body.Props.RootElement.GetRawText()}");

            foreach (var prop in body.Props.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(prop.Name))
                    return (false, "Property key must not be empty.");

                if (prop.Name.Length > 40)
                    return (false, $"Property key '{prop.Name}' must be less than or equal to 40 characters. Props was: {body.Props.RootElement.GetRawText()}");

                if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    return (false, $"Value of key '{prop.Name}' must be a primitive type. Props was: {body.Props.RootElement.GetRawText()}");
            }
        }

        return (true, string.Empty);
    }

    private static TrackingEvent NewTrackingEvent(string appId, string countryCode, string regionName, string clientIp, string userAgent, EventBody body)
    {
        var (stringProps, numericProps) = body.SplitProps();
        return new TrackingEvent
        {
            ClientIpAddress = clientIp,
            UserAgent = userAgent,

            AppId = appId,
            EventName = body.EventName,
            Timestamp = body.Timestamp.ToUniversalTime(),
            SessionId = body.SessionId,
            OSName = body.SystemProps.OSName ?? "",
            OSVersion = body.SystemProps.OSVersion ?? "",
            Locale = body.SystemProps.Locale ?? "",
            AppVersion = body.SystemProps.AppVersion ?? "",
            EngineName = body.SystemProps.EngineName ?? "",
            EngineVersion = body.SystemProps.EngineVersion ?? "",
            AppBuildNumber = body.SystemProps.AppBuildNumber ?? "",
            SdkVersion = body.SystemProps.SdkVersion ?? "",
            CountryCode = countryCode,
            RegionName = regionName,
            StringProps = stringProps.ToJsonString(),
            NumericProps = numericProps.ToJsonString(),
            IsDebug = body.SystemProps.IsDebug,
        };
    }

    private static bool TryParseDocument(string json, out JsonDocument? doc)
    {
        try
        {
            doc = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            doc = null;
            return false;
        }
    }
}
