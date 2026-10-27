namespace AgenticLab.Web;

/// <summary>
/// Serves the app under a URL prefix (<c>PathBase</c>, e.g. <c>/agentic-lab</c>) on a host shared through a
/// reverse proxy. A request that still carries the prefix has it moved from the path to the path base; one whose
/// prefix the proxy already stripped is given it as its path base. Either way the base href, links, cookies and
/// the sign-in redirect URI include the prefix. Unset, the app is served from the root as before.
/// </summary>
internal static class PathBase
{
    /// <summary>The configuration key holding the prefix.</summary>
    public const string ConfigurationKey = "PathBase";

    /// <summary>Reads and normalises the prefix, or returns an empty path when none is configured.</summary>
    /// <exception cref="InvalidOperationException">The prefix does not start with <c>/</c>.</exception>
    public static PathString Read(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(value)) return PathString.Empty;
        if (!value.StartsWith('/'))
            throw new InvalidOperationException($"Invalid configuration '{ConfigurationKey}'. Use a URL prefix such as /agentic-lab.");
        return new PathString(value);
    }

    /// <summary>Applies the configured prefix ahead of routing; call first in the pipeline.</summary>
    public static WebApplication UseConfiguredPathBase(this WebApplication app)
    {
        var prefix = Read(app.Configuration);
        if (!prefix.HasValue) return app;

        app.Use((context, next) =>
        {
            Apply(context.Request, prefix);
            return next(context);
        });
        // Routing must run after the path base is set; WebApplication would otherwise add it first.
        app.UseRouting();
        return app;
    }

    internal static void Apply(HttpRequest request, PathString prefix)
    {
        if (request.Path.StartsWithSegments(prefix, out var matched, out var remaining))
        {
            request.PathBase = request.PathBase.Add(matched);
            request.Path = remaining;
        }
        else
        {
            request.PathBase = request.PathBase.Add(prefix);
        }
    }
}
