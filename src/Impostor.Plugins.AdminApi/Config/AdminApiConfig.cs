namespace Impostor.Plugins.AdminApi.Config;

public class AdminApiConfig
{
    public const string SectionName = "AdminApi";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// IP to bind the listener to. Use "127.0.0.1" for host-only, or "0.0.0.0" when running
    /// in Docker where access is controlled by the port mapping (e.g. "127.0.0.1:8081:8081").
    /// </summary>
    public string ListenIp { get; set; } = "0.0.0.0";

    public int ListenPort { get; set; } = 8081;

    /// <summary>
    /// Optional API key. If set, clients must send it in the X-Admin-Key header.
    /// Leave empty for no authentication when access is already restricted at the network layer
    /// (e.g. Docker port mapping to 127.0.0.1 or firewall).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
