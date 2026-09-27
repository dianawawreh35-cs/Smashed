using Serilog.Events;

namespace CallCenter.Server;

/// <summary>
/// How loudly each request is logged (M-D03 of the 27 Sep review).
/// </summary>
/// <remarks>
/// The server has one disk, and every request was an Information line: each
/// laptop's block-list refresh (which is also how the server knows it is still
/// there, S-20), and the supervisor's dashboard and phone badges, which refresh
/// themselves. A day of those buried the lines worth reading and filled the
/// log. Those few, when they succeed, drop to Debug; a failure of any of them
/// is still logged as one.
/// </remarks>
public static class RequestLogLevels
{
    /// <summary>The GETs that are asked for on a timer, not by a person.</summary>
    private static readonly string[] Polled =
    [
        "/api/contacts/blocked-numbers",
        "/api/pbx/agents",
        "/api/reports/dashboard/today",
        "/api/pbx/blacklist",
        "/health",
    ];

    public static LogEventLevel For(HttpContext context, double elapsedMs, Exception? ex)
    {
        var status = context.Response.StatusCode;

        // As Serilog's own default: a server error is an Error, the rest Information.
        if (ex is not null || status >= 500)
        {
            return LogEventLevel.Error;
        }

        if (status >= 400)
        {
            return LogEventLevel.Information;
        }

        var path = context.Request.Path;
        return HttpMethods.IsGet(context.Request.Method)
               && Polled.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            ? LogEventLevel.Debug
            : LogEventLevel.Information;
    }
}
