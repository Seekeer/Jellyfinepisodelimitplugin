using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodeLimit.Services;

/// <summary>
/// v2: registers the player UI script with the JavaScript Injector plugin
/// (https://github.com/n00bcodr/Jellyfin-JavaScript-Injector) so the moon
/// button loads in every web client with no index.html patching and no
/// install.sh. Uses reflection, so there is no compile-time dependency on
/// the injector. If the injector is not installed, one clear log line says so.
/// </summary>
public class ScriptInjectorService : IHostedService
{
    private const string InjectorAssemblyName = "Jellyfin.Plugin.JavaScriptInjector";
    private const string InjectorPluginTypeName = "Jellyfin.Plugin.JavaScriptInjector.Plugin";
    private const string RegistrationServiceTypeName = "Jellyfin.Plugin.JavaScriptInjector.Services.IJavaScriptRegistrationService";
    private const string PayloadTypeName = "Jellyfin.Plugin.JavaScriptInjector.Model.JavaScriptRegistrationPayload";

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

                // NotReadyYet: the injector is installed but not fully up yet. Wait and retry.
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }

            _logger.LogError(
                "Episode Limit 2.0: could not register the UI script with the JavaScript Injector after {Attempts} attempts.",
                MaxAttempts);
        }
        catch (OperationCanceledException)
        {
            // Server shutting down, nothing to do.
        }
    }

    private RegistrationResult TryRegister()
    {
        var injectorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, InjectorAssemblyName, StringComparison.OrdinalIgnoreCase));

        if (injectorAssembly == null)
        {
            return RegistrationResult.InjectorMissing;
        }

        var pluginType = injectorAssembly.GetType(InjectorPluginTypeName, throwOnError: false);
        var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (instance == null)
        {
            return RegistrationResult.NotReadyYet;
        }

        var provider = pluginType?.GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)?.GetValue(instance) as IServiceProvider;
        var serviceType = injectorAssembly.GetType(RegistrationServiceTypeName, throwOnError: false);
        if (provider == null || serviceType == null)
        {
            return RegistrationResult.NotReadyYet;
        }

        var registrationService = provider.GetService(serviceType);
        if (registrationService == null)
        {
            return RegistrationResult.NotReadyYet;
        }

        var payloadType = injectorAssembly.GetType(PayloadTypeName, throwOnError: false);
        if (payloadType == null)
        {
            _logger.LogError("Episode Limit 2.0: found the JavaScript Injector but could not load its payload type. Version mismatch?");
            return RegistrationResult.Failed;
        }

        var payload = Activator.CreateInstance(payloadType);
        if (payload == null)
        {
            return RegistrationResult.Failed;
        }

        SetPayloadProperty(payloadType, payload, "Id", "episode-limit-moon-button");
        SetPayloadProperty(payloadType, payload, "Name", "Episode Limit (moon button)");
        SetPayloadProperty(payloadType, payload, "Src", "/EpisodeLimit/episodelimit.js");
        SetPayloadProperty(payloadType, payload, "Enabled", true);
        SetPayloadProperty(payloadType, payload, "Module", false);
        SetPayloadProperty(payloadType, payload, "RequiresAuthentication", false);
        SetPayloadProperty(payloadType, payload, "PluginId", "a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        SetPayloadProperty(payloadType, payload, "PluginName", "Episode Limit");
        SetPayloadProperty(payloadType, payload, "PluginVersion", "2.0.0.0");

        var registerMethod = serviceType.GetMethod("RegisterScript", new[] { payloadType });
        var success = registerMethod?.Invoke(registrationService, new[] { payload }) as bool?;

        if (success == true)
        {
            _logger.LogInformation(
                "Episode Limit 2.0: registered episodelimit.js with the JavaScript Injector. The moon button will appear in web clients after a browser refresh.");
            return RegistrationResult.Success;
        }

        _logger.LogError("Episode Limit 2.0: the JavaScript Injector rejected the script registration.");
        return RegistrationResult.Failed;
    }

    private static void SetPayloadProperty(Type payloadType, object payload, string name, object value)
    {
        payloadType.GetProperty(name)?.SetValue(payload, value);
    }

    private enum RegistrationResult
    {
        Success,
        InjectorMissing,
        NotReadyYet,
        Failed
    }
}
