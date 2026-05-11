using Sandbox;
using Sandbox.Network;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Systems.Networking;

/// <summary>
/// Starts the lobby and spawns one owned player object per active connection.
/// The existing scene player is used as a disabled template for now; once the
/// player hierarchy stabilizes, it can be moved into a real prefab and assigned
/// to <see cref="PlayerPrefab"/>.
/// </summary>
public sealed class GameNetworkManager : Component, Component.INetworkListener
{
	[Property] public bool StartLobbyOnLoad { get; set; } = true;
	[Property] public int MaxPlayers { get; set; } = 64;
	[Property] public string LobbyName { get; set; } = "Amministratore Delegato";
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public string PlayerTemplateName { get; set; } = "Player Controller";
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();
	[Property] public int StartingWallet { get; set; } = 1000;
	[Property] public Sandbox.Systems.Roles.RoleConfig RoleConfig { get; set; }
	[Property] public string RoleConfigPath { get; set; } = Sandbox.Systems.Roles.RoleConfig.DefaultPath;

	private readonly Dictionary<string, GameObject> _players = new();
	private GameObject _template;
	private int _spawnIndex;

	protected override void OnStart()
	{
		_template = FindPlayerTemplate();

		if ( _template.IsValid() )
		{
			_template.Enabled = false;
		}

		if ( StartLobbyOnLoad && !Sandbox.Networking.IsActive && !Sandbox.Networking.IsConnecting )
		{
			Sandbox.Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = MaxPlayers,
				Name = LobbyName,
				Privacy = LobbyPrivacy.Public,
				DestroyWhenHostLeaves = true,
			} );
		}
	}

	public void OnActive( Connection connection )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( connection is null ) return;

		var key = ConnectionKey( connection );
		if ( _players.ContainsKey( key ) ) return;

		var source = PlayerPrefab.IsValid() ? PlayerPrefab : (_template.IsValid() ? _template : FindPlayerTemplate());
		if ( !source.IsValid() )
		{
			Log.Error( $"[Network] No player prefab or scene template named '{PlayerTemplateName}' was found." );
			Log.Error( $"[Network] Could not spawn {connection.DisplayName}: no player source is available." );
			GameLogSystem.Current?.Error( "network", "Player spawn failed because no player source was available", connection: connection, data: GameLogSystem.Fields(
				("templateName", PlayerTemplateName),
				("displayName", connection.DisplayName) ) );
			return;
		}

		var spawn = PickSpawnTransform( out var spawnOffset );
		var player = source.Clone( spawn );
		player.WorldPosition += spawnOffset;
		player.Name = $"Player - {connection.DisplayName}";
		player.Enabled = true;
		PreparePlayerForNetwork( player );
		AssignPlayerRole( player, connection );
		CopyPlayerTitleSettings( source, player );
		var restoredSave = PlayerPersistenceSystem.Current?.TryLoadPlayer( connection, player ) == true;
		if ( !restoredSave ) InitializeStartingWallet( player );

		var spawned = player.NetworkSpawn( connection );
		if ( !spawned )
		{
			Log.Error( $"[Network] Failed to NetworkSpawn player for {connection.DisplayName}." );
			GameLogSystem.Current?.Error( "network", "Player NetworkSpawn failed", player, connection, GameLogSystem.Fields(
				("displayName", connection.DisplayName) ) );
			player.Destroy();
			return;
		}

		_players[key] = player;
		PlayerPersistenceSystem.Current?.TrackPlayer( connection, player );
		if ( !restoredSave ) PlayerPersistenceSystem.Current?.RequestSaveSoon( player, "new player initialized" );
		var assignedRole = AssignedRoleName( player );
		Log.Info( $"[Network] Spawned player for {connection.DisplayName} as {assignedRole}; restoredSave={restoredSave}." );
		GameLogSystem.Current?.Info( "network", "Player spawned", player, connection, GameLogSystem.Fields(
			("displayName", connection.DisplayName),
			("role", assignedRole),
			("startingWallet", restoredSave ? 0 : StartingWallet),
			("restoredSave", restoredSave) ) );
	}

	public void OnDisconnected( Connection connection )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( connection is null ) return;

		var key = ConnectionKey( connection );
		if ( !_players.Remove( key, out var player ) ) return;
		PlayerPersistenceSystem.Current?.SaveAndUntrackPlayer( connection );

		if ( player.IsValid() )
		{
			GameLogSystem.Current?.Info( "network", "Player disconnected", player, connection, GameLogSystem.Fields(
				("displayName", connection.DisplayName) ) );
			player.Destroy();
		}
	}

	private GameObject FindPlayerTemplate()
	{
		return Scene.GetAllObjects( true )
			.FirstOrDefault( go => go.IsValid() && go.Name == PlayerTemplateName );
	}

	private Transform PickSpawnTransform( out Vector3 offset )
	{
		offset = Vector3.Zero;

		var validSpawnPoints = SpawnPoints.Where( x => x.IsValid() ).ToList();
		if ( validSpawnPoints.Count > 0 )
		{
			var point = validSpawnPoints[_spawnIndex++ % validSpawnPoints.Count];
			return point.WorldTransform;
		}

		offset = FallbackSpawnOffset( _spawnIndex++ );

		if ( _template.IsValid() )
		{
			return _template.WorldTransform;
		}

		return WorldTransform;
	}

	private static Vector3 FallbackSpawnOffset( int index )
	{
		if ( index <= 0 ) return Vector3.Zero;

		var angle = (index - 1) * ((System.MathF.PI * 2f) / 8f);
		var radius = 96f * (1 + ((index - 1) / 8));
		return new Vector3( System.MathF.Cos( angle ) * radius, System.MathF.Sin( angle ) * radius, 0f );
	}

	private static void PreparePlayerForNetwork( GameObject player )
	{
		if ( !player.IsValid() ) return;

		ConfigureNetworkObject( player );
		EnsurePlayerProfile( player );
		EnsurePlayerStats( player );
		EnsurePlayerFinance( player );
		EnsureCloudProgress( player );
		EnsurePlayerAppearance( player );
		EnsurePlayerTitle( player );
		EnsurePlayerRole( player );

		foreach ( var profile in player.Components.GetAll<PlayerProfileComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( profile.GameObject );
		}

		foreach ( var stats in player.Components.GetAll<PlayerStatsComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( stats.GameObject );
		}

		foreach ( var finance in player.Components.GetAll<PlayerFinanceComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( finance.GameObject );
		}

		foreach ( var progress in player.Components.GetAll<CloudPlayerProgressComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( progress.GameObject );
		}

		foreach ( var role in player.Components.GetAll<Sandbox.Systems.Roles.PlayerRoleComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( role.GameObject );
		}

		foreach ( var backpack in player.Components.GetAll<Backpack>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( backpack.GameObject );
		}

		foreach ( var unit in player.Components.GetAll<UnitComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( unit.GameObject );
		}
	}

	private static void EnsurePlayerStats( GameObject player )
	{
		if ( !player.IsValid() ) return;

		var stats = player.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		if ( stats.IsValid() ) return;

		player.Components.Create<PlayerStatsComponent>();
	}

	private static void EnsurePlayerProfile( GameObject player )
	{
		if ( !player.IsValid() ) return;

		var profile = player.Components.GetInDescendantsOrSelf<PlayerProfileComponent>();
		if ( profile.IsValid() ) return;

		player.Components.Create<PlayerProfileComponent>();
	}

	private static void EnsurePlayerFinance( GameObject player )
	{
		if ( !player.IsValid() ) return;

		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		if ( finance.IsValid() ) return;

		player.Components.Create<PlayerFinanceComponent>();
	}

	private static void EnsureCloudProgress( GameObject player )
	{
		if ( !player.IsValid() ) return;

		var progress = player.Components.GetInDescendantsOrSelf<CloudPlayerProgressComponent>();
		if ( progress.IsValid() ) return;

		player.Components.Create<CloudPlayerProgressComponent>();
	}

	private static void EnsurePlayerAppearance( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( player.Components.GetInDescendantsOrSelf<PlayerAppearanceComponent>().IsValid() ) return;

		player.Components.Create<PlayerAppearanceComponent>();
	}

	private static void EnsurePlayerTitle( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( player.Components.GetInDescendantsOrSelf<Sandbox.Systems.UI.PlayerTitleComponent>().IsValid() ) return;

		player.Components.Create<Sandbox.Systems.UI.PlayerTitleComponent>();
	}

	private static void EnsurePlayerRole( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( player.Components.GetInDescendantsOrSelf<Sandbox.Systems.Roles.PlayerRoleComponent>().IsValid() ) return;

		player.Components.Create<Sandbox.Systems.Roles.PlayerRoleComponent>();
	}

	private void AssignPlayerRole( GameObject player, Connection connection )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() || connection is null ) return;

		var roleComponent = player.Components.GetInDescendantsOrSelf<Sandbox.Systems.Roles.PlayerRoleComponent>();
		if ( !roleComponent.IsValid() ) return;

		var config = RoleConfig ?? Sandbox.Systems.Roles.RoleConfig.Resolve( RoleConfigPath );
		var steamId = connection.SteamId.ToString();
		var role = config.ResolveRole( steamId );
		roleComponent.TrySetRole( role );
		var configPath = string.IsNullOrWhiteSpace( config.ResourcePath ) ? "fallback/default" : config.ResourcePath;
		var adminMatch = config.IsAdminSteamId( steamId );
		var moderatorMatch = config.IsModeratorSteamId( steamId );
		Log.Info( $"[Roles] {connection.DisplayName} ({steamId}) assigned {role}. config={configPath}; admins={config.AdminCount}; moderators={config.ModeratorCount}; adminMatch={adminMatch}; moderatorMatch={moderatorMatch}." );

		GameLogSystem.Current?.Info( "roles", "Player role assigned", player, connection, GameLogSystem.Fields(
			("config", configPath),
			("steamId", steamId),
			("adminMatch", adminMatch.ToString()),
			("moderatorMatch", moderatorMatch.ToString()),
			("role", role.ToString()) ) );
	}

	private static string AssignedRoleName( GameObject player )
	{
		if ( !player.IsValid() ) return "unknown";

		var role = player.Components.GetInDescendantsOrSelf<Sandbox.Systems.Roles.PlayerRoleComponent>();
		return role.IsValid() ? role.Role.ToString() : "unknown";
	}

	private static void CopyPlayerTitleSettings( GameObject source, GameObject player )
	{
		if ( !source.IsValid() || !player.IsValid() ) return;

		var sourceTitle = source.Root.Components.Get<Sandbox.Systems.UI.PlayerTitleComponent>();
		sourceTitle ??= source.Components.GetInAncestorsOrSelf<Sandbox.Systems.UI.PlayerTitleComponent>();
		sourceTitle ??= source.Components.GetInDescendantsOrSelf<Sandbox.Systems.UI.PlayerTitleComponent>();
		if ( !sourceTitle.IsValid() ) return;

		var playerTitle = player.Components.GetInDescendantsOrSelf<Sandbox.Systems.UI.PlayerTitleComponent>();
		if ( !playerTitle.IsValid() ) return;

		playerTitle.CopySettingsFrom( sourceTitle );
	}

	private void InitializeStartingWallet( GameObject player )
	{
		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		if ( !backpack.IsValid() ) return;

		var amount = int.Max( 0, StartingWallet );
		if ( amount <= 0 ) return;

		if ( !backpack.AddMoney( amount ) )
		{
			Log.Warning( $"[Networking] Could not add starting cash; player={player.Name}; amount=${amount:N0}." );
		}
	}

	private static void ConfigureNetworkObject( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return;

		gameObject.NetworkMode = NetworkMode.Object;
		gameObject.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
	}

	private static string ConnectionKey( Connection connection )
	{
		return connection.Id.ToString();
	}
}
