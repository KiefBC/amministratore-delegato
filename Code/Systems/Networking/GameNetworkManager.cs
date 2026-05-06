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

	protected override void OnStart()
	{
		_template = FindPlayerTemplate();

		if ( _template.IsValid() )
		{
			_template.Enabled = false;
		}
		else if ( !PlayerPrefab.IsValid() )
		{
			Log.Error( $"[Network] No player prefab or scene template named '{PlayerTemplateName}' was found." );
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
			Log.Error( $"[Network] Could not spawn {connection.DisplayName}: no player source is available." );
			return;
		}

		var spawn = PickSpawnTransform();
		var player = source.Clone( spawn );
		player.Name = $"Player - {connection.DisplayName}";
		player.Enabled = true;

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

	private Transform PickSpawnTransform()
	{
		foreach ( var point in SpawnPoints )
		{
			if ( point.IsValid() )
			{
				return point.WorldTransform;
			}
		}

		if ( _template.IsValid() )
		{
			return _template.WorldTransform;
		}

		return WorldTransform;
	}

	private static string ConnectionKey( Connection connection )
	{
		return connection.Id.ToString();
	}
}
