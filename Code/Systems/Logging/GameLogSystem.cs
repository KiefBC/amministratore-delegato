using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;

public enum GameLogSeverity
{
	Info,
	Warning,
	Error,
	Security,
}

public sealed class GameLogActor
{
	public string ConnectionId { get; set; } = "unknown";
	public string SteamId { get; set; } = "unknown";
	public string DisplayName { get; set; } = "unknown";
	public string PlayerName { get; set; } = "unknown";
}

public sealed class GameLogEntry
{
	public string TimeUtc { get; set; } = "";
	public string SessionId { get; set; } = "";
	public string WorldId { get; set; } = "";
	public string Category { get; set; } = "system";
	public string Severity { get; set; } = "info";
	public GameLogActor Actor { get; set; }
	public string Message { get; set; } = "";
	public Dictionary<string, string> Data { get; set; } = new();
}

public sealed class GameLogFile
{
	public int Version { get; set; } = 1;
	public string SessionId { get; set; } = "";
	public string WorldId { get; set; } = "";
	public string StartedUtc { get; set; } = "";
	public List<GameLogEntry> Entries { get; set; } = new();
}

/// <summary>
/// Host-side persistent audit log. This is separate from world persistence and can be
/// deleted without affecting saved gameplay state.
/// </summary>
public sealed class GameLogSystem : GameObjectSystem<GameLogSystem>
{
	private const float FlushIntervalSeconds = 5f;
	private const int ImmediateFlushCount = 25;
	private const int MaxEntries = 2000;

	private readonly GameLogFile _file;
	private readonly string _fileName;
	private bool _dirty;
	private float _nextFlushTime;

	public string SessionId { get; }
	public string WorldId { get; private set; } = "minimal";
	public string FileName => _fileName;

	public GameLogSystem( Scene scene ) : base( scene )
	{
		var started = DateTimeOffset.UtcNow;
		SessionId = Guid.NewGuid().ToString( "N" );
		_fileName = $"game_log_{started:yyyyMMdd_HHmmss}_{SessionId[..8]}.json";
		_file = new GameLogFile
		{
			SessionId = SessionId,
			WorldId = WorldId,
			StartedUtc = FormatTime( started ),
		};

		Listen( Stage.StartUpdate, 80, OnTick, nameof( OnTick ) );
		Info( "system", "Game log session started", data: Fields( ("file", _fileName) ), flush: true );
	}

	public void Info( string category, string message, GameObject actor = null, Connection connection = null, Dictionary<string, string> data = null, bool flush = false )
	{
		Record( GameLogSeverity.Info, category, message, actor, connection, data, flush );
	}

	public void Warning( string category, string message, GameObject actor = null, Connection connection = null, Dictionary<string, string> data = null, bool flush = true )
	{
		Record( GameLogSeverity.Warning, category, message, actor, connection, data, flush );
	}

	public void Error( string category, string message, GameObject actor = null, Connection connection = null, Dictionary<string, string> data = null, bool flush = true )
	{
		Record( GameLogSeverity.Error, category, message, actor, connection, data, flush );
	}

	public void Security( string category, string message, GameObject actor = null, Connection connection = null, Dictionary<string, string> data = null, bool flush = true )
	{
		Record( GameLogSeverity.Security, category, message, actor, connection, data, flush );
	}

	public void Record( GameLogSeverity severity, string category, string message, GameObject actor = null, Connection connection = null, Dictionary<string, string> data = null, bool flush = false )
	{
		if ( Networking.IsActive && !Networking.IsHost ) return;

		var entry = new GameLogEntry
		{
			TimeUtc = FormatTime( DateTimeOffset.UtcNow ),
			SessionId = SessionId,
			WorldId = WorldId,
			Category = string.IsNullOrWhiteSpace( category ) ? "system" : category,
			Severity = severity.ToString().ToLowerInvariant(),
			Actor = BuildActor( actor, connection ),
			Message = message ?? "",
			Data = data ?? new Dictionary<string, string>(),
		};

		_file.Entries.Add( entry );
		while ( _file.Entries.Count > MaxEntries ) _file.Entries.RemoveAt( 0 );

		_dirty = true;
		if ( flush || severity is GameLogSeverity.Warning or GameLogSeverity.Error or GameLogSeverity.Security || _file.Entries.Count % ImmediateFlushCount == 0 ) Flush();
	}

	public static Dictionary<string, string> Fields( params (string Key, object Value)[] fields )
	{
		var data = new Dictionary<string, string>();
		foreach ( var (key, value) in fields )
		{
			if ( string.IsNullOrWhiteSpace( key ) ) continue;
			data[key] = FormatValue( value );
		}

		return data;
	}

	private void OnTick()
	{
		if ( !_dirty || Time.Now < _nextFlushTime ) return;
		Flush();
	}

	private void Flush()
	{
		if ( !_dirty ) return;

		try
		{
			FileSystem.Data.WriteJson( _fileName, _file );
			_dirty = false;
			_nextFlushTime = Time.Now + FlushIntervalSeconds;
		}
		catch ( Exception e )
		{
			_nextFlushTime = Time.Now + FlushIntervalSeconds;
			Log.Warning( $"[GameLog] Failed to write {_fileName}: {e.Message}" );
		}
	}

	private static GameLogActor BuildActor( GameObject actor, Connection connection )
	{
		var owner = ResolveConnection( actor, connection );
		return new GameLogActor
		{
			ConnectionId = owner?.Id.ToString() ?? "unknown",
			SteamId = owner is null ? "unknown" : owner.SteamId.ToString(),
			DisplayName = string.IsNullOrWhiteSpace( owner?.DisplayName ) ? "unknown" : owner.DisplayName,
			PlayerName = PlayerLogName( actor ),
		};
	}

	private static Connection ResolveConnection( GameObject actor, Connection connection )
	{
		if ( connection is not null ) return connection;
		if ( !actor.IsValid() ) return null;

		var owner = actor.Network.Owner;
		if ( owner is null ) owner = actor.Root.Network.Owner;
		return owner;
	}

	private static string PlayerLogName( GameObject actor )
	{
		if ( !actor.IsValid() ) return "unknown";

		var root = actor.Root;
		if ( root.IsValid() && !string.IsNullOrWhiteSpace( root.Name ) ) return root.Name;
		return !string.IsNullOrWhiteSpace( actor.Name ) ? actor.Name : "unknown";
	}

	private static string FormatTime( DateTimeOffset time )
	{
		return time.UtcDateTime.ToString( "O", CultureInfo.InvariantCulture );
	}

	private static string FormatValue( object value )
	{
		if ( value is null ) return "";
		if ( value is IFormattable formattable ) return formattable.ToString( null, CultureInfo.InvariantCulture );
		return value.ToString() ?? "";
	}
}
