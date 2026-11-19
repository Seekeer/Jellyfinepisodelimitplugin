using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Mp2Compatibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Mp2Compatibility.Tests;

public sealed class WebPatchTests
{
    private static byte[] Original => File.ReadAllBytes(
        Environment.GetEnvironmentVariable("MP2_BUNDLE_PATH")
        ?? throw new InvalidOperationException("Set MP2_BUNDLE_PATH to the stock Jellyfin Web 12.2 bundle; see build.sh."));

    [Fact]
    public void Verified_bundle_produces_exact_reviewed_patch()
    {
        var original = Original;
        var patch = WebPatch.Create(original);
        Assert.True(patch.Active);
        Assert.Equal(WebPatch.OriginalHash, Convert.ToHexStringLower(SHA256.HashData(original)));
        Assert.Equal(WebPatch.PatchedHash, Convert.ToHexStringLower(SHA256.HashData(patch.Bundle!)));
        Assert.Equal(original.Length - 59, patch.Bundle!.Length);
    }

    [Fact]
    public void Unknown_or_already_patched_bundles_are_not_transformed()
    {
        var original = Original;
        var patched = WebPatch.Create(original).Bundle!;
        original[0] ^= 1;
        Assert.False(WebPatch.Create(original).Active);
        Assert.False(WebPatch.Create(patched).Active);
    }

    [Fact]
    public void Index_rewrite_is_idempotent_and_preserves_other_plugins_and_unicode()
    {
        var patch = WebPatch.Create(Original);
        var html = $"<html lang=ru><script src='{WebPatch.OriginalUrl}'></script><body>Привет<!-- Other plugin --><script src='../JavaScriptInjector/public.js'></script></body></html>";
        var result = patch.RewriteIndex(html);
        Assert.Equal(html.Replace(WebPatch.OriginalUrl, WebPatch.PatchedUrl), result);
        Assert.Equal(result, patch.RewriteIndex(result));
        Assert.Equal(html, WebPatch.Create([]).RewriteIndex(html));
    }

    [Theory]
    [InlineData("/web/main.jellyfin.bundle.js?old-version")]
    [InlineData("/jellyfin/web/main.jellyfin.bundle.js?new-version")]
    public async Task Bundle_get_head_and_conditional_requests_have_correct_metadata(string path)
    {
        using var server = await Server(WebPatch.Create(Original));
        using var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-8");
        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WebPatch.PatchedHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(bytes.Length, response.Content.Headers.ContentLength);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.True(response.Headers.CacheControl!.NoCache);
        Assert.Equal("1.0.1", response.Headers.GetValues("X-MP2-Compatibility").Single());

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(bytes.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(response.Headers.ETag, head.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var unchanged = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        using var oldConditional = new HttpRequestMessage(HttpMethod.Get, path);
        oldConditional.Headers.TryAddWithoutValidation("If-None-Match", "\"old-static-etag\"");
        using var changed = await client.SendAsync(oldConditional);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    }

    [Theory]
    [InlineData("/web/")]
    [InlineData("/web")]
    [InlineData("/web/index.html")]
    [InlineData("/jellyfin/web/")]
    [InlineData("/jellyfin/web/index.html")]
    public async Task Index_is_complete_even_with_old_validators_compression_and_ranges(string path)
    {
        using var server = await Server(WebPatch.Create(Original));
        using var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br,gzip");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"old\"");
        request.Headers.TryAddWithoutValidation("If-Modified-Since", "Thu, 19 Nov 2026 12:00:00 GMT");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-8");
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebPatch.PatchedUrl, html);
        Assert.DoesNotContain(WebPatch.OriginalUrl, html);
        Assert.Contains("<!-- Other plugin -->", html);
        Assert.Contains("Привет", html);
        Assert.Null(response.Headers.ETag);
        Assert.Equal(Encoding.UTF8.GetByteCount(html), response.Content.Headers.ContentLength);
    }

    [Theory]
    [InlineData("/web/other.js")]
    [InlineData("/Items/123/PlaybackInfo")]
    public async Task Unrelated_routes_are_unchanged(string path)
    {
        using var server = await Server(WebPatch.Create(Original));
        using var client = server.GetTestClient();
        Assert.Equal("unrelated", await client.GetStringAsync(path));
    }

    [Fact]
    public async Task Unsupported_bundle_and_non_get_requests_pass_through()
    {
        using var server = await Server(WebPatch.Create([]));
        using var client = server.GetTestClient();
        Assert.Equal("stock bundle", await client.GetStringAsync("/web/main.jellyfin.bundle.js"));
        Assert.Contains(WebPatch.OriginalUrl, await client.GetStringAsync("/web/index.html"));

        using var active = await Server(WebPatch.Create(Original));
        using var postClient = active.GetTestClient();
        using var response = await postClient.PostAsync("/web/main.jellyfin.bundle.js", null);
        Assert.Equal("stock bundle", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Downstream_exception_restores_response_stream_without_flushing_partial_html()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/web/index.html";
        context.Request.Headers.AcceptEncoding = "gzip";
        var originalStream = new MemoryStream();
        context.Response.Body = originalStream;
        var middleware = new PatchMiddleware(async ctx =>
        {
            await ctx.Response.WriteAsync("partial");
            throw new InvalidOperationException("downstream failed");
        }, WebPatch.Create(Original));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.Same(originalStream, context.Response.Body);
        Assert.Equal(0, originalStream.Length);
        Assert.Equal("gzip", context.Request.Headers.AcceptEncoding.ToString());
    }

    private static async Task<WebApplication> Server(WebPatch patch)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(patch);
        var app = builder.Build();
        app.UseMiddleware<PatchMiddleware>();
        app.Run(async ctx =>
        {
            var path = ctx.Request.Path.Value!;
            if (path.EndsWith(".js"))
            {
                await ctx.Response.WriteAsync(path.EndsWith("/main.jellyfin.bundle.js") ? "stock bundle" : "unrelated");
                return;
            }

            if (!path.Contains("/web"))
            {
                await ctx.Response.WriteAsync("unrelated");
                return;
            }

            // Reproduce a static-file handler that would otherwise send a stale 304/206.
            Assert.False(ctx.Request.Headers.ContainsKey("If-None-Match"));
            Assert.False(ctx.Request.Headers.ContainsKey("If-Modified-Since"));
            Assert.False(ctx.Request.Headers.ContainsKey("Accept-Encoding"));
            Assert.False(ctx.Request.Headers.ContainsKey("Range"));
            ctx.Response.ContentType = "text/html;charset=utf-8";
            ctx.Response.Headers.ETag = "\"old\"";
            var html = $"<html><script src='{WebPatch.OriginalUrl}'></script><body>Привет<!-- Other plugin --></body></html>";
            ctx.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
            await ctx.Response.WriteAsync(html);
        });
        await app.StartAsync();
        return app;
    }
}
