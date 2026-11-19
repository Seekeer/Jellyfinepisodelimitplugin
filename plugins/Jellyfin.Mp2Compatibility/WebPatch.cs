using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mp2Compatibility;

/// <summary>Owns the version-specific compatibility edit and refuses unknown Web code.</summary>
public sealed class WebPatch
{
    public const string OriginalHash = "30e2a813095468c43d143aebefcdcfa93ae0cacd24a579b3e1c51a08f6f967e0";
    public const string PatchedHash = "bd40e1917a014574f3243ad3cfbfe98f7c5f4d03f2f3756c9a46839b4f7eb955";
    public const string OriginalUrl = "main.jellyfin.bundle.js?fd083178eda672854efd";
    public const string PatchedUrl = "main.jellyfin.bundle.js?mp2-bd40e1917a014574";
    private const string Addition = "R&&(_.push(\"mp2\"),M.push(\"mp2\"),O.push(\"mp2\"),L.push(\"mp2\"))";

    private WebPatch(byte[]? bundle, string status)
    {
        Bundle = bundle;
        Status = status;
    }

    public byte[]? Bundle { get; }
    public string Status { get; }
    public bool Active => Bundle is not null;

    public static WebPatch Load(string webPath, ILogger<WebPatch> logger)
    {
        WebPatch patch;
        try
        {
            patch = Create(File.ReadAllBytes(Path.Combine(webPath, "main.jellyfin.bundle.js")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "MP2 Audio Compatibility: unable to read Web bundle; serving original Web.");
            return new WebPatch(null, "Web bundle could not be read");
        }

        if (patch.Active)
        {
            logger.LogInformation("MP2 Audio Compatibility: enabled for verified Jellyfin Web 12.2; MP2 removed from all four audio lists.");
        }
        else
        {
            logger.LogWarning("MP2 Audio Compatibility: {Status}. No assets will be changed. Verify the plugin before updating Jellyfin Web.", patch.Status);
        }

        return patch;
    }

    public static WebPatch Create(byte[] original)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(original));
        if (hash != OriginalHash)
        {
            return new WebPatch(null, $"Unsupported or already patched Web bundle SHA-256: {hash}");
        }

        var source = Encoding.UTF8.GetString(original);
        var offset = source.IndexOf(Addition, StringComparison.Ordinal);
        if (offset < 0 || source.IndexOf(Addition, offset + Addition.Length, StringComparison.Ordinal) >= 0)
        {
            return new WebPatch(null, "Expected one MP2 compatibility expression");
        }

        var patched = Encoding.UTF8.GetBytes(source.Remove(offset, Addition.Length).Insert(offset, "0"));
        return Convert.ToHexStringLower(SHA256.HashData(patched)) == PatchedHash
            ? new WebPatch(patched, "Active: Jellyfin Web 12.2")
            : new WebPatch(null, "Patched bundle checksum mismatch");
    }

    public string RewriteIndex(string html) => Active
        ? html.Replace(OriginalUrl, PatchedUrl, StringComparison.Ordinal)
        : html;
}
