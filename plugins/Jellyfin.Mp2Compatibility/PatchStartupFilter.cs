using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Mp2Compatibility;

public sealed class PatchStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<PatchMiddleware>();
        next(app);
    };
}

/// <summary>Serves a guarded bundle and updates only its URL in the existing Web shell.</summary>
public sealed class PatchMiddleware(RequestDelegate next, WebPatch patch)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isGet = HttpMethods.IsGet(context.Request.Method);
        var isHead = HttpMethods.IsHead(context.Request.Method);
        if (!patch.Active || (!isGet && !isHead))
        {
            await next(context);
            return;
        }

        if (path.EndsWith("/web/main.jellyfin.bundle.js", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-MP2-Compatibility"] = "1.0.1";
            await Results.File(patch.Bundle!, "text/javascript; charset=utf-8",
                entityTag: new EntityTagHeaderValue('"' + WebPatch.PatchedHash + '"'),
                enableRangeProcessing: false).ExecuteAsync(context);
            return;
        }

        var isIndex = path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);
        if (!isIndex || !isGet)
        {
            await next(context);
            return;
        }

        // Ask the downstream static handler and other plugins for a complete plain document.
        // Removing validators also prevents an old index from causing an unpatched 304.
        string[] removedHeaders = ["Accept-Encoding", "Range", "If-Range", "If-None-Match", "If-Modified-Since"];
        var savedHeaders = removedHeaders.ToDictionary(name => name, name => context.Request.Headers[name]);
        foreach (var name in removedHeaders)
        {
            context.Request.Headers.Remove(name);
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            foreach (var (name, value) in savedHeaders)
            {
                if (value.Count > 0)
                {
                    context.Request.Headers[name] = value;
                }
            }
        }

        var bytes = buffer.ToArray();
        if (context.Response.StatusCode == 200
            && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
            && !context.Response.Headers.ContainsKey("Content-Encoding"))
        {
            var html = Encoding.UTF8.GetString(bytes);
            var rewritten = patch.RewriteIndex(html);
            if (rewritten != html)
            {
                bytes = Encoding.UTF8.GetBytes(rewritten);
                context.Response.ContentLength = bytes.Length;
                context.Response.Headers.Remove("ETag");
                context.Response.Headers.Remove("Last-Modified");
                context.Response.Headers.Remove("Accept-Ranges");
                context.Response.Headers.Remove("Content-Range");
                context.Response.Headers.CacheControl = "no-cache";
            }
        }

        await originalBody.WriteAsync(bytes, context.RequestAborted);
    }
}
