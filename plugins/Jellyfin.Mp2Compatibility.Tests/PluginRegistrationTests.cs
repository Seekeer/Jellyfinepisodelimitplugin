using System.Reflection;
using Jellyfin.Plugin.Mp2Compatibility;
using PluginEntry = Jellyfin.Plugin.Mp2Compatibility.Plugin;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Mp2Compatibility.Tests;

public sealed class PluginRegistrationTests
{
    [Fact]
    public void Plugin_manager_can_read_version_paths_and_info_after_DI_construction()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DispatchProxy.Create<IApplicationPaths, PathsProxy>());
        services.AddSingleton(DispatchProxy.Create<IXmlSerializer, UnusedSerializerProxy>());
        using var provider = services.BuildServiceProvider();
        var plugin = (IPlugin)ActivatorUtilities.CreateInstance(provider, typeof(PluginEntry));

        // PluginManager immediately uses Version.ToString() and plugin metadata.
        Assert.NotNull(plugin.Version);
        Assert.Equal("1.0.1.0", plugin.Version.ToString());
        Assert.Equal(typeof(PluginEntry).Assembly.Location, plugin.AssemblyFilePath);
        Assert.False(string.IsNullOrWhiteSpace(plugin.DataFolderPath));
        var info = plugin.GetPluginInfo();
        Assert.Equal("MP2 Audio Compatibility", info.Name);
        Assert.Equal(new Guid("a236529a-ea3c-4f69-93a9-f8b3a68c41dc"), info.Id);
    }

    public class PathsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType == typeof(string)
            ? Path.GetTempPath()
            : throw new NotSupportedException(method?.Name);
    }

    public class UnusedSerializerProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }
}
