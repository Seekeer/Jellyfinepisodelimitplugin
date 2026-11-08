using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.EpisodeLimit.Services;

/// <summary>
/// v2.0.0.1: registers the player UI script with the JavaScript Injector plugin
/// (https://github.com/n00bcodr/Jellyfin-JavaScript-Injector) so the moon button
/// loads in web clients with no index.html patching. Uses the injector's
/// documented plugin interface (static PluginInterface.RegisterScript(JObject)),
/// reached via reflection so there is no compile-time dependency. The payload
/// asks the injector to load /EpisodeLimit/episodelimit.js, which this plugin
/// serves anonymously from its own ClientScriptController. Every failure path
/// logs loudly: the 2.0.0.0 build guessed this API wrong and died silently.
/// </summary>
public class ScriptInjectorService : IHostedService
{
    private const string InjectorAssemblyName = "Jellyfin.Plugin.JavaScriptInjector";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.JavaScriptInjector.PluginInterface";
    private const string ScriptId = "episode-limit-moon-button";
    private const string ScriptSrc = "/EpisodeLimit/episodelimit.js";
    private const string PluginId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    private const int MaxAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private readonly ILogger<ScriptInjectorService> _logger;
    private CancellationTokenSource? _cts;

    public ScriptInjectorService(ILogger<ScriptInjectorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _ = RegisterWithRetryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        TryUnregister();
        return Task.CompletedTask;
    }

    private async Task RegisterWithRetryAsync(CancellationToken ct)
    {
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var result = TryRegister();
                if (result == RegistrationResult.Success)
                {
                    return;
                }

                if (result == RegistrationResult.InjectorMissing)
                {
                    _logger.LogWarning(
                        "Episode Limit 2.0: The JavaScript Injector plugin is not installed. " +
                        "The moon button needs it. Add the repository " +
                        "https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/manifest.json " +
                        "in Dashboard, Plugins, then install JavaScript Injector and restart Jellyfin.");
                    return;
                }

                if (result == RegistrationResult.Failed)
                {
                    return; // The failure has already been logged loudly inside TryRegister.
                }

                // NotReadyYet: the injector is installed but its plugin instance is
                // not constructed yet. Wait and retry.
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }

            _logger.LogError(
                "Episode Limit 2.0: could not register the UI script with the JavaScript Injector after {Attempts} attempts. " +
                "The injector never became ready.",
                MaxAttempts);
        }
        catch (OperationCanceledException)
        {
            // Server shutting down, nothing to do.
        }
        catch (Exception ex)
        {
            // Never silent. The 2.0.0.0 build swallowed exactly this and the moon button died quietly.
            _logger.LogError(ex, "Episode Limit 2.0: unexpected error while registering the UI script with the JavaScript Injector.");
        }
    }

    private RegistrationResult TryRegister()
    {
        try
        {
            var injectorAssembly = FindInjectorAssembly();
            if (injectorAssembly == null)
            {
                return RegistrationResult.InjectorMissing;
            }

            var pluginInterfaceType = injectorAssembly.GetType(PluginInterfaceTypeName, throwOnError: false);
            if (pluginInterfaceType == null)
            {
                _logger.LogError(
                    "Episode Limit 2.0: found the JavaScript Injector but not its PluginInterface type. " +
                    "The installed injector version may predate the plugin interface. " +
                    "Update JavaScript Injector from the catalog and restart Jellyfin.");
                return RegistrationResult.Failed;
            }

            var registerMethod = pluginInterfaceType.GetMethod("RegisterScript", BindingFlags.Public | BindingFlags.Static);
            if (registerMethod == null)
            {
                _logger.LogError(
                    "Episode Limit 2.0: the JavaScript Injector has no public static RegisterScript method. Version mismatch?");
                return RegistrationResult.Failed;
            }

            // The injector's documented JObject payload, camelCase field names.
            var payload = new JObject
            {
                ["id"] = ScriptId,
                ["name"] = "Episode Limit (moon button)",
                ["src"] = ScriptSrc,
                ["module"] = false,
                ["enabled"] = true,
                ["requiresAuthentication"] = false,
                ["pluginId"] = PluginId,
                ["pluginName"] = "Episode Limit",
                ["pluginVersion"] = "2.0.0.1"
            };

            var success = registerMethod.Invoke(null, new object[] { payload }) as bool?;
            if (success == true)
            {
                _logger.LogInformation(
                    "Episode Limit 2.0: registered {ScriptSrc} with the JavaScript Injector. " +
                    "The moon button will appear in web clients after a browser refresh.",
                    ScriptSrc);
                return RegistrationResult.Success;
            }

            _logger.LogError(
                "Episode Limit 2.0: the JavaScript Injector rejected the script registration (RegisterScript returned false). " +
                "Check the injector's own log lines for the reason.");
            return RegistrationResult.Failed;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is InvalidOperationException)
        {
            // The injector throws this while its plugin instance is not ready yet.
            return RegistrationResult.NotReadyYet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Episode Limit 2.0: registering the UI script with the JavaScript Injector threw an exception.");
            return RegistrationResult.Failed;
        }
    }

    private void TryUnregister()
    {
        try
        {
            var injectorAssembly = FindInjectorAssembly();
            var pluginInterfaceType = injectorAssembly?.GetType(PluginInterfaceTypeName, throwOnError: false);
            var unregisterMethod = pluginInterfaceType?.GetMethod("UnregisterAllScriptsFromPlugin", BindingFlags.Public | BindingFlags.Static);
            unregisterMethod?.Invoke(null, new object[] { PluginId });
        }
        catch
        {
            // Shutdown path: the injector may already be stopped. Nothing useful to do.
        }
    }

    private static Assembly? FindInjectorAssembly()
    {
        // Jellyfin loads each plugin assembly into its own AssemblyLoadContext,
        // so enumerate those first, then fall back to the AppDomain list.
        var assembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(a => string.Equals(a.GetName().Name, InjectorAssemblyName, StringComparison.OrdinalIgnoreCase));

        if (assembly != null)
        {
            return assembly;
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, InjectorAssemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private enum RegistrationResult
    {
        Success,
        InjectorMissing,
        NotReadyYet,
        Failed
    }
}
