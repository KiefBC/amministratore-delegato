using Sandbox;

namespace Sandbox.Systems.UI;

public sealed class ChatMessageEntry
{
	public int Id { get; set; }
	public GameObject Sender { get; set; }
	public string SenderName { get; set; } = "Player";
	public string Message { get; set; } = "";
	public float Age { get; set; }
}

public sealed class ChatSystem : GameObjectSystem<ChatSystem>
{
	private const int MaxMessageLength = 240;
	private const int MaxHistory = 100;
	private const float SendCooldown = 0.75f;
	private const float CooldownPruneInterval = 30f;
	private static readonly (string Sender, string Message)[] DefaultMessages =
	{
		("Reception", "Welcome to the floor. Keep your ledger close."),
		("Broker", "Rumor says the next contract is already overpriced."),
		("Compliance", "All workplace incidents must be reported in triplicate."),
		("Courier", "Someone left a Glock by the loading bay again."),
	};

	private readonly List<ChatMessageEntry> _messages = new();
	private readonly Dictionary<string, float> _nextAllowedSendTime = new();
	private GameObject _displayObject;
	private int _nextId;
	private float _nextCooldownPruneTime;

	public IReadOnlyList<ChatMessageEntry> Messages => _messages;
	public int ChatVersion { get; private set; }
	public float SecondsSinceLastMessage => _messages.Count == 0 ? 0f : _messages[^1].Age;

	public ChatSystem( Scene scene ) : base( scene )
	{
		SeedDefaultMessages();
		Listen( Stage.StartUpdate, 60, OnTick, nameof( OnTick ) );
	}

	public bool TrySendMessage( GameObject player, string rawMessage, Connection caller )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !player.IsValid() )
		{
			GameLogSystem.Current?.Warning( "chat", "Chat rejected because player was invalid", connection: caller );
			return false;
		}

		var message = SanitizeMessage( rawMessage );
		if ( string.IsNullOrWhiteSpace( message ) ) return false;
		if ( IsRateLimited( player, caller ) )
		{
			GameLogSystem.Current?.Warning( "chat", "Chat rejected by cooldown", player, caller );
			return false;
		}

		var senderName = ResolveSenderName( player, caller );
		LogAcceptedMessage( player, caller, senderName, message );
		GameLogSystem.Current?.Info( "chat", "Chat message accepted", player, caller, GameLogSystem.Fields(
			("senderName", senderName),
			("message", message) ) );
		GameNetworkRpc.BroadcastChatMessage( player, senderName, message );
		return true;
	}

	public void AddNetworkMessage( GameObject sender, string senderName, string message )
	{
		message = SanitizeMessage( message );
		if ( string.IsNullOrWhiteSpace( message ) ) return;

		senderName = SanitizeSenderName( senderName );
		if ( string.IsNullOrWhiteSpace( senderName ) ) senderName = ResolveSenderName( sender, null );

		_messages.Add( new ChatMessageEntry
		{
			Id = _nextId++,
			Sender = sender,
			SenderName = senderName,
			Message = message,
			Age = 0f,
		} );

		while ( _messages.Count > MaxHistory )
		{
			_messages.RemoveAt( 0 );
		}

		ChatVersion++;
	}

	private void SeedDefaultMessages()
	{
		foreach ( var (sender, message) in DefaultMessages )
		{
			_messages.Add( new ChatMessageEntry
			{
				Id = _nextId++,
				SenderName = sender,
				Message = message,
				Age = 0f,
			} );
		}

		ChatVersion++;
	}

	private void OnTick()
	{
		EnsureDisplay();
		TickMessages();
		PruneCooldowns();
	}

	private void EnsureDisplay()
	{
		if ( _displayObject.IsValid() ) return;

		var existing = Scene.GetAllComponents<ChatPanel>().FirstOrDefault( x => x.IsValid() );
		if ( existing.IsValid() )
		{
			_displayObject = existing.GameObject;
			return;
		}

		_displayObject = new GameObject( "Chat" );
		_displayObject.NetworkMode = NetworkMode.Never;

		var screen = _displayObject.Components.Create<ScreenPanel>();
		screen.ZIndex = 120;

		_displayObject.Components.Create<ChatPanel>();
	}

	private void TickMessages()
	{
		if ( _messages.Count == 0 ) return;

		foreach ( var message in _messages )
		{
			message.Age += Time.Delta;
		}
	}

	private bool IsRateLimited( GameObject player, Connection caller )
	{
		var key = RateLimitKey( player, caller );
		if ( _nextAllowedSendTime.TryGetValue( key, out var nextAllowed ) && Time.Now < nextAllowed )
		{
			return true;
		}

		_nextAllowedSendTime[key] = Time.Now + SendCooldown;
		return false;
	}

	private void PruneCooldowns()
	{
		if ( Time.Now < _nextCooldownPruneTime ) return;

		_nextCooldownPruneTime = Time.Now + CooldownPruneInterval;
		foreach ( var pair in _nextAllowedSendTime.ToArray() )
		{
			if ( pair.Value <= Time.Now ) _nextAllowedSendTime.Remove( pair.Key );
		}
	}

	private static string RateLimitKey( GameObject player, Connection caller )
	{
		if ( caller is not null ) return caller.Id.ToString();

		var owner = player.Network.Owner;
		if ( owner is null ) owner = player.Root.Network.Owner;
		if ( owner is not null ) return owner.Id.ToString();

		return player.Id.ToString();
	}

	private static string ResolveSenderName( GameObject player, Connection caller )
	{
		if ( caller is not null && !string.IsNullOrWhiteSpace( caller.DisplayName ) ) return caller.DisplayName;

		var owner = player.IsValid() ? player.Network.Owner : null;
		if ( owner is null && player.IsValid() ) owner = player.Root.Network.Owner;
		if ( owner is not null && !string.IsNullOrWhiteSpace( owner.DisplayName ) ) return owner.DisplayName;

		if ( Connection.Local is not null && !string.IsNullOrWhiteSpace( Connection.Local.DisplayName ) ) return Connection.Local.DisplayName;

		if ( player.IsValid() && !string.IsNullOrWhiteSpace( player.Name ) )
		{
			const string spawnedPlayerPrefix = "Player - ";
			var name = player.Name;
			if ( name.StartsWith( spawnedPlayerPrefix, System.StringComparison.OrdinalIgnoreCase ) )
			{
				name = name[spawnedPlayerPrefix.Length..];
			}

			if ( !string.IsNullOrWhiteSpace( name ) ) return name.Trim();
		}

		return "Player";
	}

	private static string SanitizeMessage( string message )
	{
		if ( string.IsNullOrWhiteSpace( message ) ) return "";

		message = message.Replace( '\r', ' ' ).Replace( '\n', ' ' ).Trim();
		while ( message.Contains( "  " ) ) message = message.Replace( "  ", " " );

		return message.Length > MaxMessageLength ? message[..MaxMessageLength] : message;
	}

	private static string SanitizeSenderName( string senderName )
	{
		if ( string.IsNullOrWhiteSpace( senderName ) ) return "";

		senderName = senderName.Replace( '\r', ' ' ).Replace( '\n', ' ' ).Trim();
		return senderName.Length > 48 ? senderName[..48] : senderName;
	}

	private static void LogAcceptedMessage( GameObject player, Connection caller, string senderName, string message )
	{
		var owner = player.IsValid() ? player.Network.Owner : null;
		if ( owner is null && player.IsValid() ) owner = player.Root.Network.Owner;

		var connection = caller ?? owner;
		var connectionId = connection?.Id.ToString() ?? "unknown";
		var steamId = connection is null ? "unknown" : connection.SteamId.ToString();
		var playerName = player.IsValid() ? player.Name : "unknown";

		Log.Info( $"[Chat] {System.DateTime.UtcNow:O} connection={connectionId} steamId={steamId} player=\"{EscapeLogValue( playerName )}\" name=\"{EscapeLogValue( senderName )}\" message=\"{EscapeLogValue( message )}\"" );
	}

	private static string EscapeLogValue( string value )
	{
		return (value ?? "").Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
	}
}
