using Sandbox;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Systems.Cloud;

/// <summary>
/// World workstation that submits a job completion intent to the host, then to
/// the configured backend. Rewards are applied only from backend-confirmed data.
/// </summary>
public sealed class CloudJobWorkstation : Component, IInteractable, IClientInteractable
{
	[Property] public string JobId { get; set; } = "starter_workstation";
	[Property] public string PromptText { get; set; } = "Press E to Work";
	[Property, Range( 10f, 500f ), Step( 10f )] public float WorkRange { get; set; } = 100f;

	private readonly HashSet<string> _pendingPlayers = new();
	private CancellationTokenSource _cts;
	private bool _clientRequesting;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => WorkRange;
	string IInteractable.Prompt => PromptText;

	bool IInteractable.CanInteract( GameObject player )
	{
		if ( !player.IsValid() ) return false;
		if ( string.IsNullOrWhiteSpace( JobId ) ) return false;
		return !_pendingPlayers.Contains( PlayerKey( player ) );
	}

	void IInteractable.Interact( GameObject player )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		if ( !player.IsValid() ) return;
		if ( !((IInteractable)this).CanInteract( player ) ) return;

		var range = WorkRange;
		if ( WorldPosition.DistanceSquared( player.WorldPosition ) > range * range ) return;

		var key = PlayerKey( player );
		if ( !_pendingPlayers.Add( key ) ) return;

		_cts ??= new CancellationTokenSource();
		_ = CompleteLocalHostAsync( player, key, _cts.Token );
	}

	bool IClientInteractable.TryClientInteract( GameObject player )
	{
		if ( _clientRequesting ) return true;
		if ( !player.IsValid() ) return true;

		_clientRequesting = true;
		_ = CompleteClientAsync( player );
		return true;
	}

	protected override void OnDisabled()
	{
		CancelRequests();
	}

	protected override void OnDestroy()
	{
		CancelRequests();
	}

	public void TryCompleteOnHost( GameObject player, string authToken, Connection caller = null )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() ) return;
		if ( !((IInteractable)this).CanInteract( player ) ) return;

		var range = WorkRange;
		if ( WorldPosition.DistanceSquared( player.WorldPosition ) > range * range ) return;

		var key = PlayerKey( player );
		if ( !_pendingPlayers.Add( key ) ) return;

		_cts ??= new CancellationTokenSource();
		_ = CompleteOnHostAsync( player, authToken, caller, key, _cts.Token );
	}

	private async Task CompleteClientAsync( GameObject player )
	{
		try
		{
			var config = CloudProgressionSystem.Current?.Config;
			var authToken = "";

			if ( config.IsValid() && config.HasBackend )
			{
				_cts ??= new CancellationTokenSource();
				authToken = await Sandbox.Services.Auth.GetToken( config.ResolvedAuthServiceName(), _cts.Token );
				if ( _cts.IsCancellationRequested || !player.IsValid() || !GameObject.IsValid() ) return;
			}

			GameNetworkRpc.RequestCompleteCloudJob( player, GameObject, authToken );
		}
		catch ( OperationCanceledException ) when ( _cts?.IsCancellationRequested == true )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Failed to request workstation auth token: {e.Message}" );
			NotificationSystem.Current?.Notify( NotificationKind.BadNews, "Cloud Auth Failed", "Could not authenticate the job request.", 3f );
		}
		finally
		{
			_clientRequesting = false;
		}
	}

	private async Task CompleteLocalHostAsync( GameObject player, string key, CancellationToken cancellationToken )
	{
		try
		{
			var config = CloudProgressionSystem.Current?.Config;
			var authToken = "";

			if ( config.IsValid() && config.HasBackend )
			{
				authToken = await Sandbox.Services.Auth.GetToken( config.ResolvedAuthServiceName(), cancellationToken );
				if ( cancellationToken.IsCancellationRequested || !player.IsValid() || !GameObject.IsValid() ) return;
			}

			await CompleteOnHostAsync( player, authToken, null, key, cancellationToken );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Failed to request host workstation auth token: {e.Message}" );
			NotificationSystem.Current?.Notify( NotificationKind.BadNews, "Cloud Auth Failed", "Could not authenticate the job request.", 3f );
		}
		finally
		{
			_pendingPlayers.Remove( key );
		}
	}

	private async Task CompleteOnHostAsync( GameObject player, string authToken, Connection caller, string key, CancellationToken cancellationToken )
	{
		try
		{
			var system = CloudProgressionSystem.Current;
			if ( system is null ) return;

			await system.CompleteJobAsync( player, this, authToken, caller, cancellationToken );
		}
		finally
		{
			_pendingPlayers.Remove( key );
		}
	}

	private void CancelRequests()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
		_pendingPlayers.Clear();
		_clientRequesting = false;
	}

	private static string PlayerKey( GameObject player )
	{
		if ( !player.IsValid() ) return "invalid";

		var owner = player.Network.Owner;
		if ( owner is not null ) return owner.SteamId.ToString();

		return player.Id.ToString();
	}
}
