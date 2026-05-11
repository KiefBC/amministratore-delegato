using Sandbox.Network;
using System;

namespace Sandbox.Systems.Persistence;

public sealed class PlayerPersistenceSystem : GameObjectSystem<PlayerPersistenceSystem>
{
	private const int SaveVersion = 1;
	private const float AutosaveIntervalSeconds = 60f;
	private const float NormalSaveDelaySeconds = 10f;
	private const float UrgentSaveDelaySeconds = 2f;
	private const float NormalMinSaveIntervalSeconds = 20f;
	private const float UrgentMinSaveIntervalSeconds = 3f;

	private readonly IPlayerPersistenceStore _store = new FilePlayerPersistenceStore();
	private readonly Dictionary<string, TrackedPlayer> _trackedPlayers = new();

	public PlayerPersistenceSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 70, OnTick, nameof( OnTick ) );
	}

	public bool TryLoadPlayer( Connection connection, GameObject player )
	{
		if ( !IsAuthoritative ) return false;
		if ( connection is null || !player.IsValid() ) return false;

		var steamId = SteamIdFor( connection );
		if ( string.IsNullOrWhiteSpace( steamId ) ) return false;
		if ( !_store.Exists( steamId ) ) return false;

		try
		{
			var data = _store.Load( steamId );
			if ( !IsValidSaveData( data, steamId ) )
			{
				Log.Warning( $"[Persistence] Ignored invalid player save; player={connection.DisplayName}; steamId={steamId}; file={_store.FileNameFor( steamId )}." );
				GameLogSystem.Current?.Warning( "persistence", "Invalid player save ignored", player, connection, GameLogSystem.Fields(
					("steamId", steamId),
					("file", _store.FileNameFor( steamId )) ) );
				return false;
			}

			RestorePlayer( player, data );
			Log.Info( $"[Persistence] Loaded player save; player={connection.DisplayName}; steamId={steamId}; file={_store.FileNameFor( steamId )}." );
			GameLogSystem.Current?.Info( "persistence", "Player save loaded", player, connection, GameLogSystem.Fields(
				("steamId", steamId),
				("file", _store.FileNameFor( steamId )),
				("version", data.Version) ) );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Persistence] Failed to load player save; player={connection.DisplayName}; steamId={steamId}; error={e.Message}" );
			GameLogSystem.Current?.Warning( "persistence", "Player save load failed", player, connection, GameLogSystem.Fields(
				("steamId", steamId),
				("error", e.Message) ) );
			return false;
		}
	}

	public void TrackPlayer( Connection connection, GameObject player )
	{
		if ( !IsAuthoritative ) return;
		if ( connection is null || !player.IsValid() ) return;

		var steamId = SteamIdFor( connection );
		if ( string.IsNullOrWhiteSpace( steamId ) ) return;

		_trackedPlayers[ConnectionKey( connection )] = new TrackedPlayer
		{
			Connection = connection,
			DisplayName = connection.DisplayName,
			Player = player,
			SteamId = steamId,
			NextAutosaveTime = NextStaggeredAutosaveTime(),
		};
	}

	public void MarkDirty( GameObject player, string reason = "state changed" )
	{
		ScheduleSave( player, reason, NormalSaveDelaySeconds, urgent: false );
	}

	public void RequestSaveSoon( GameObject player, string reason = "important state changed" )
	{
		ScheduleSave( player, reason, UrgentSaveDelaySeconds, urgent: true );
	}

	public void SaveAndUntrackPlayer( Connection connection )
	{
		if ( connection is null ) return;

		var key = ConnectionKey( connection );
		if ( _trackedPlayers.Remove( key, out var tracked ) )
		{
			SaveTrackedPlayer( tracked, "disconnect" );
		}
	}

	private void OnTick()
	{
		if ( !IsAuthoritative ) return;

		foreach ( var pair in _trackedPlayers.ToArray() )
		{
			var tracked = pair.Value;
			if ( !tracked.Player.IsValid() )
			{
				_trackedPlayers.Remove( pair.Key );
				continue;
			}

			if ( ShouldSaveDirty( tracked ) )
			{
				SaveTrackedPlayer( tracked, tracked.DirtyReason );
				continue;
			}

			if ( Time.Now >= tracked.NextAutosaveTime )
			{
				if ( tracked.LastSaveTime > 0f && Time.Now - tracked.LastSaveTime < NormalMinSaveIntervalSeconds )
				{
					tracked.NextAutosaveTime = tracked.LastSaveTime + NormalMinSaveIntervalSeconds;
					continue;
				}

				SaveTrackedPlayer( tracked, "autosave" );
			}
		}
	}

	private void ScheduleSave( GameObject player, string reason, float delay, bool urgent )
	{
		if ( !IsAuthoritative ) return;
		if ( !player.IsValid() ) return;
		if ( !TryFindTrackedPlayer( player, out var tracked ) ) return;

		tracked.Dirty = true;
		tracked.DirtyUrgent |= urgent;
		tracked.DirtyReason = string.IsNullOrWhiteSpace( reason ) ? "state changed" : reason;

		var saveTime = Time.Now + float.Max( 0f, delay );
		if ( tracked.NextSaveTime <= 0f || saveTime < tracked.NextSaveTime )
		{
			tracked.NextSaveTime = saveTime;
		}
	}

	private bool ShouldSaveDirty( TrackedPlayer tracked )
	{
		if ( !tracked.Dirty ) return false;
		if ( tracked.NextSaveTime > 0f && Time.Now < tracked.NextSaveTime ) return false;

		var minInterval = tracked.DirtyUrgent ? UrgentMinSaveIntervalSeconds : NormalMinSaveIntervalSeconds;
		return tracked.LastSaveTime <= 0f || Time.Now - tracked.LastSaveTime >= minInterval;
	}

	private bool SaveTrackedPlayer( TrackedPlayer tracked, string reason )
	{
		if ( !IsAuthoritative ) return false;
		if ( string.IsNullOrWhiteSpace( tracked.SteamId ) || !tracked.Player.IsValid() ) return false;

		try
		{
			var data = CapturePlayer( tracked.Player, tracked.Connection, tracked.SteamId, tracked.DisplayName );
			_store.Save( data );
			tracked.Dirty = false;
			tracked.DirtyUrgent = false;
			tracked.DirtyReason = "";
			tracked.NextSaveTime = 0f;
			tracked.LastSaveTime = Time.Now;
			tracked.NextAutosaveTime = Time.Now + AutosaveIntervalSeconds;
			Log.Info( $"[Persistence] Saved player; player={tracked.DisplayName}; steamId={tracked.SteamId}; reason={reason}; file={_store.FileNameFor( tracked.SteamId )}." );
			GameLogSystem.Current?.Info( "persistence", "Player save written", tracked.Player, tracked.Connection, GameLogSystem.Fields(
				("steamId", tracked.SteamId),
				("reason", reason),
				("file", _store.FileNameFor( tracked.SteamId )) ) );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Persistence] Failed to save player; player={tracked.DisplayName}; steamId={tracked.SteamId}; reason={reason}; error={e.Message}" );
			GameLogSystem.Current?.Warning( "persistence", "Player save failed", tracked.Player, tracked.Connection, GameLogSystem.Fields(
				("steamId", tracked.SteamId),
				("reason", reason),
				("error", e.Message) ) );
			return false;
		}
	}

	private static PlayerSaveData CapturePlayer( GameObject player, Connection connection, string steamId, string displayName )
	{
		var root = player.Root.IsValid() ? player.Root : player;
		var data = new PlayerSaveData
		{
			Version = SaveVersion,
			SteamId = steamId,
			LastKnownName = string.IsNullOrWhiteSpace( displayName ) ? connection?.DisplayName ?? root.Name : displayName,
			SavedUtc = DateTimeOffset.UtcNow.ToString( "O" ),
		};

		var stats = root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		if ( stats.IsValid() ) data.Stats = stats.CreateSaveData();

		var unit = root.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( unit.IsValid() ) data.Vitals = unit.CreateSaveData();

		var backpack = root.Components.GetInDescendantsOrSelf<Backpack>();
		if ( backpack.IsValid() ) data.Inventory = backpack.CreateSaveData();

		var finance = root.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		if ( finance.IsValid() ) data.Finance = finance.CreateSaveData();

		var profile = root.Components.GetInDescendantsOrSelf<PlayerProfileComponent>();
		if ( profile.IsValid() ) data.Profile = profile.CreateSaveData();

		return data;
	}

	public static PlayerSaveData CapturePlayerSnapshot( GameObject player, Connection connection, string steamId, string displayName )
	{
		return CapturePlayer( player, connection, steamId, displayName );
	}

	public static PlayerSaveData CreateCloudCheckpointData( PlayerSaveData data )
	{
		if ( data is null ) return null;

		var json = System.Text.Json.JsonSerializer.Serialize( data );
		var cloudData = System.Text.Json.JsonSerializer.Deserialize<PlayerSaveData>( json ) ?? new PlayerSaveData();

		// TODO: Decide whether exact current HP/stamina should persist to Supabase.
		// For now cloud checkpoints keep these as sentinel values and restore spawn/default pools.
		cloudData.Vitals.Health = 0f;
		cloudData.Vitals.Stamina = 0f;
		cloudData.Vitals.WasDead = false;
		return cloudData;
	}

	private static void RestorePlayer( GameObject player, PlayerSaveData data )
	{
		if ( data is null || !player.IsValid() ) return;

		var root = player.Root.IsValid() ? player.Root : player;

		var stats = root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		if ( stats.IsValid() ) stats.RestoreSaveData( data.Stats );

		var backpack = root.Components.GetInDescendantsOrSelf<Backpack>();
		if ( backpack.IsValid() ) backpack.RestoreSaveData( data.Inventory );

		var finance = root.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		if ( finance.IsValid() ) finance.RestoreSaveData( data.Finance );

		var profile = root.Components.GetInDescendantsOrSelf<PlayerProfileComponent>();
		if ( profile.IsValid() ) profile.RestoreSaveData( data.Profile );

		var unit = root.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( unit.IsValid() ) unit.RestoreSaveData( data.Vitals );
	}

	public static void RestoreCloudSnapshot( GameObject player, PlayerSaveData data )
	{
		if ( data is null || !player.IsValid() ) return;

		var root = player.Root.IsValid() ? player.Root : player;

		var stats = root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		if ( stats.IsValid() ) stats.RestoreSaveData( data.Stats );

		var backpack = root.Components.GetInDescendantsOrSelf<Backpack>();
		if ( backpack.IsValid() ) backpack.RestoreSaveData( data.Inventory );

		var finance = root.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		if ( finance.IsValid() ) finance.RestoreSaveData( data.Finance );

		var profile = root.Components.GetInDescendantsOrSelf<PlayerProfileComponent>();
		if ( profile.IsValid() ) profile.RestoreSaveData( data.Profile );

		// TODO: Cloud snapshots intentionally do not restore exact HP/stamina yet.
		// The saved values are sentinels until the design decides whether combat state should survive sessions.
	}

	private static bool IsAuthoritative => !Sandbox.Networking.IsActive || Sandbox.Networking.IsHost;

	private bool TryFindTrackedPlayer( GameObject player, out TrackedPlayer tracked )
	{
		tracked = null;
		var root = player.Root.IsValid() ? player.Root : player;

		foreach ( var candidate in _trackedPlayers.Values )
		{
			if ( !candidate.Player.IsValid() ) continue;

			var candidateRoot = candidate.Player.Root.IsValid() ? candidate.Player.Root : candidate.Player;
			if ( candidate.Player == player || candidate.Player == root || candidateRoot == player || candidateRoot == root )
			{
				tracked = candidate;
				return true;
			}
		}

		return false;
	}

	private float NextStaggeredAutosaveTime()
	{
		var index = _trackedPlayers.Count;
		var spread = (index % 64) * (AutosaveIntervalSeconds / 64f);
		return Time.Now + AutosaveIntervalSeconds + spread;
	}

	private static bool IsValidSaveData( PlayerSaveData data, string steamId )
	{
		if ( data is null ) return false;
		if ( data.Version <= 0 ) return false;
		return data.SteamId == steamId;
	}

	private static string SteamIdFor( Connection connection )
	{
		return connection?.SteamId.ToString() ?? "";
	}

	private static string ConnectionKey( Connection connection )
	{
		return connection.Id.ToString();
	}

	private sealed class TrackedPlayer
	{
		public Connection Connection { get; set; }
		public string DisplayName { get; set; } = "Player";
		public GameObject Player { get; set; }
		public string SteamId { get; set; } = "";
		public bool Dirty { get; set; }
		public bool DirtyUrgent { get; set; }
		public string DirtyReason { get; set; } = "";
		public float NextSaveTime { get; set; }
		public float LastSaveTime { get; set; }
		public float NextAutosaveTime { get; set; }
	}
}

public interface IPlayerPersistenceStore
{
	bool Exists( string steamId );
	PlayerSaveData Load( string steamId );
	void Save( PlayerSaveData data );
	string FileNameFor( string steamId );
}

public sealed class FilePlayerPersistenceStore : IPlayerPersistenceStore
{
	public bool Exists( string steamId )
	{
		return FileSystem.Data.FileExists( FileNameFor( steamId ) );
	}

	public PlayerSaveData Load( string steamId )
	{
		return FileSystem.Data.ReadJsonOrDefault( FileNameFor( steamId ), new PlayerSaveData() );
	}

	public void Save( PlayerSaveData data )
	{
		if ( data is null ) return;

		FileSystem.Data.WriteJson( FileNameFor( data.SteamId ), data );
	}

	public string FileNameFor( string steamId )
	{
		var sanitized = SanitizeSteamId( steamId );
		return $"player_{sanitized}.json";
	}

	private static string SanitizeSteamId( string steamId )
	{
		var sanitized = new string( (steamId ?? "").Where( char.IsDigit ).ToArray() );
		return string.IsNullOrWhiteSpace( sanitized ) ? "unknown" : sanitized;
	}
}

public sealed class PlayerSaveData
{
	public int Version { get; set; } = 1;
	public string SteamId { get; set; } = "";
	public string LastKnownName { get; set; } = "";
	public string SavedUtc { get; set; } = "";
	public PlayerStatsSaveData Stats { get; set; } = new();
	public PlayerVitalsSaveData Vitals { get; set; } = new();
	public PlayerInventorySaveData Inventory { get; set; } = new();
	public PlayerFinanceSaveData Finance { get; set; } = new();
	public PlayerProfileSaveData Profile { get; set; } = new();
}

public sealed class PlayerStatsSaveData
{
	public float HealthXp { get; set; }
	public float StaminaXp { get; set; }
	public float PunchingXp { get; set; }
	public float RangedXp { get; set; }
	public float BusinessXp { get; set; }
	public int PlayerLevel { get; set; }
	public int HealthLevel { get; set; }
	public int StaminaLevel { get; set; }
	public int PunchingLevel { get; set; }
	public int RangedLevel { get; set; }
	public int BusinessLevel { get; set; }
}

public sealed class PlayerVitalsSaveData
{
	public float Health { get; set; }
	public float Stamina { get; set; }
	public float Armor { get; set; }
	public float Hydration { get; set; }
	public float Nutrition { get; set; }
	public bool WasDead { get; set; }
}

public sealed class PlayerInventorySaveData
{
	public int EquippedInstanceId { get; set; }
	public List<InventoryItemState> Items { get; set; } = new();
}

public sealed class PlayerFinanceSaveData
{
	public int BankBalance { get; set; }
	public int DebtBalance { get; set; }
	public float DebtHourlyInterestPercent { get; set; }
	public float DebtAccrualIntervalSeconds { get; set; }
	public float DebtAccrualRemainingSeconds { get; set; }
	public Dictionary<string, int> StockShares { get; set; } = new();
	public Dictionary<string, int> StockCostBasis { get; set; } = new();
	public Dictionary<string, int> CryptoMilliUnits { get; set; } = new();
	public Dictionary<string, int> OwnedBusinesses { get; set; } = new();
}

public sealed class PlayerProfileSaveData
{
	public string AffiliationId { get; set; } = PlayerProfileComponent.IndependentAffiliationId;
}
