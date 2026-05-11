using Sandbox;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Systems.Cloud;

/// <summary>
/// Host-synced cloud progression mirror for one player. The backend remains the
/// source of truth; this component exists so UI/gameplay can read confirmed state.
/// </summary>
public sealed class CloudPlayerProgressComponent : Component
{
	[Sync( SyncFlags.FromHost )] public int JobXp { get; set; }
	[Sync( SyncFlags.FromHost )] public int JobCompletions { get; set; }
	[Sync( SyncFlags.FromHost )] public long LastJobAtUnix { get; set; }
	[Sync( SyncFlags.FromHost )] public int CloudProgressVersion { get; set; }

	private CancellationTokenSource _loadCts;
	private bool _loadRequested;

	protected override void OnUpdate()
	{
		TryStartLocalAutoLoad();
	}

	protected override void OnDisabled()
	{
		CancelLoad();
	}

	protected override void OnDestroy()
	{
		CancelLoad();
	}

	public void ApplyJobSnapshot( int jobXp, int jobCompletions, long lastJobAtUnix )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		JobXp = int.Max( 0, jobXp );
		JobCompletions = int.Max( 0, jobCompletions );
		LastJobAtUnix = long.Max( 0, lastJobAtUnix );
		CloudProgressVersion++;
	}

	private void TryStartLocalAutoLoad()
	{
		if ( _loadRequested ) return;
		if ( !Sandbox.Systems.Movement.LocalPlayer.Owns( GameObject.Root ) ) return;

		var config = CloudProgressionSystem.Current?.Config;
		if ( !config.IsValid() || !config.AutoLoadPlayers || !config.HasBackend ) return;

		_loadRequested = true;
		_loadCts = new CancellationTokenSource();
		_ = RequestLoadAsync( config, _loadCts.Token );
	}

	private async Task RequestLoadAsync( CloudProgressionConfig config, CancellationToken cancellationToken )
	{
		try
		{
			var token = await Sandbox.Services.Auth.GetToken( config.ResolvedAuthServiceName(), cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !GameObject.IsValid() ) return;

			GameNetworkRpc.RequestLoadCloudPlayer( GameObject.Root, token );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			if ( cancellationToken.IsCancellationRequested || !GameObject.IsValid() ) return;
			Log.Warning( $"[Cloud] Failed to request player cloud load token: {e.Message}" );
		}
	}

	private void CancelLoad()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
	}
}
