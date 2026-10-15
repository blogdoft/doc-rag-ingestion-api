using System.Reflection;

namespace DocRag.Ingestion.Api;

/// <summary>The running build's version, as published (<c>-p:Version</c>) or <c>0.0.0-dev</c>.</summary>
public static class AppVersion
{
    /// <summary>Gets the informational version without build metadata.</summary>
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0-dev";
}
