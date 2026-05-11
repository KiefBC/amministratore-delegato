using Sandbox;

namespace Sandbox.Systems.Cloud;

/// <summary>
/// Scene-level configuration for custom backend progression calls. Keep private
/// secrets on the backend; game clients only need the public endpoint URL.
/// </summary>
public sealed class CloudProgressionConfig : Component
{
	[Property] public string BackendBaseUrl { get; set; } = "";
	[Property] public string AuthServiceName { get; set; } = "amministratore_delegato";
	[Property] public string SessionId { get; set; } = "local-session";
	[Property] public string LoadPlayerEndpoint { get; set; } = "load-player";
	[Property] public string CompleteJobEndpoint { get; set; } = "complete-job";
	[Property] public string FinanceActionEndpoint { get; set; } = "finance-action";
	[Property] public bool AutoLoadPlayers { get; set; }

	[Property] public bool AllowLocalDevFallback { get; set; }
	[Property, Range( 0f, 100000f ), Step( 1f )] public int LocalFallbackJobRewardMoney { get; set; } = 25;
	[Property, Range( 0f, 100000f ), Step( 1f )] public int LocalFallbackJobRewardXp { get; set; } = 10;

	public bool HasBackend => !string.IsNullOrWhiteSpace( BackendBaseUrl );

	public string EndpointUrl( string endpoint )
	{
		var baseUrl = (BackendBaseUrl ?? "").Trim();
		endpoint = (endpoint ?? "").Trim();
		if ( string.IsNullOrWhiteSpace( baseUrl ) || string.IsNullOrWhiteSpace( endpoint ) ) return "";

		return $"{baseUrl.TrimEnd( '/', '\\' )}/{endpoint.TrimStart( '/', '\\' )}";
	}

	public string ResolvedSessionId()
	{
		return string.IsNullOrWhiteSpace( SessionId ) ? "local-session" : SessionId.Trim();
	}

	public string ResolvedAuthServiceName()
	{
		return string.IsNullOrWhiteSpace( AuthServiceName ) ? "amministratore_delegato" : AuthServiceName.Trim();
	}
}
