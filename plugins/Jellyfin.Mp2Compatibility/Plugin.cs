using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mp2Compatibility;

public sealed class Plugin : BasePlugin<BasePluginConfiguration>
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer)
    {
    }

    public override string Name => "MP2 Audio Compatibility";

    public override string Description =>
        "Automatically converts MP2 audio for Jellyfin Web while allowing video stream copy. " +
        "Supports the verified Jellyfin Web 12.2 bundle; unknown bundles are left unchanged. Restart after disabling or uninstalling.";

    public override Guid Id => new("a236529a-ea3c-4f69-93a9-f8b3a68c41dc");
}

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton(provider => WebPatch.Load(
            provider.GetRequiredService<IServerConfigurationManager>().ApplicationPaths.WebPath,
            provider.GetRequiredService<ILogger<WebPatch>>()));
        services.AddSingleton<IStartupFilter, PatchStartupFilter>();
    }
}
