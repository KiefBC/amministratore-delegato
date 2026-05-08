using Sandbox;
using Sandbox.Network;
using System.Collections.Generic;
using System.Linq;

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

		if ( StartLobbyOnLoad && !Networking.IsActive && !Networking.IsConnecting )
		{
			Networking.CreateLobby( new LobbyConfig
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
		if ( !Networking.IsHost ) return;
		if ( connection is null ) return;

		var key = ConnectionKey( connection );
		if ( _players.ContainsKey( key ) ) return;

		var source = PlayerPrefab.IsValid() ? PlayerPrefab : (_template.IsValid() ? _template : FindPlayerTemplate());
		if ( !source.IsValid() )
		{
			Log.Error( $"[Network] No player prefab or scene template named '{PlayerTemplateName}' was found." );
			Log.Error( $"[Network] Could not spawn {connection.DisplayName}: no player source is available." );
			return;
		}

		var spawn = PickSpawnTransform( out var spawnOffset );
		var player = source.Clone( spawn );
		player.WorldPosition += spawnOffset;
		player.Name = $"Player - {connection.DisplayName}";
		player.Enabled = true;
		PreparePlayerForNetwork( player );

		var spawned = player.NetworkSpawn( connection );
		if ( !spawned )
		{
			Log.Error( $"[Network] Failed to NetworkSpawn player for {connection.DisplayName}." );
			player.Destroy();
			return;
		}

		_players[key] = player;
		Log.Info( $"[Network] Spawned player for {connection.DisplayName}." );
	}

	public void OnDisconnected( Connection connection )
	{
		if ( !Networking.IsHost ) return;
		if ( connection is null ) return;

		var key = ConnectionKey( connection );
		if ( !_players.Remove( key, out var player ) ) return;

		if ( player.IsValid() )
		{
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

		foreach ( var profile in player.Components.GetAll<PlayerProfileComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( profile.GameObject );
		}

		foreach ( var stats in player.Components.GetAll<PlayerStatsComponent>( FindMode.EverythingInSelfAndDescendants ) )
		{
			ConfigureNetworkObject( stats.GameObject );
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
