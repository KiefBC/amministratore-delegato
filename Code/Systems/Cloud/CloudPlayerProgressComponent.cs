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
	private const float CheckpointDelaySeconds = 3f;
	private const float MinimumCheckpointIntervalSeconds = 10f;

	[Sync( SyncFlags.FromHost )] public int JobXp { get; set; }
	[Sync( SyncFlags.FromHost )] public int JobCompletions { get; set; }
	[Sync( SyncFlags.FromHost )] public long LastJobAtUnix { get; set; }
	[Sync( SyncFlags.FromHost )] public int CloudProgressVersion { get; set; }

	private CancellationTokenSource _loadCts;
	private CancellationTokenSource _checkpointCts;
	private bool _loadRequested;
	private bool _checkpointStateInitialized;
	private bool _checkpointInFlight;
	private float _nextCheckpointTime;
	private float _lastCheckpointRequestTime;
	private string _checkpointReason = "checkpoint";
	private int _lastInventoryVersion;
	private int _lastStatsVersion;
	private int _lastFinanceVersion;
	private int _lastCloudProgressVersion;
	private string _lastAffiliationId = "";

	protected override void OnUpdate()
	{
		TryStartLocalAutoLoad();
		TrackLocalCheckpointState();
	}

	protected override void OnDisabled()
	{
		CancelRequests();
	}

	protected override void OnDestroy()
	{
		CancelRequests();
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

	private void TrackLocalCheckpointState()
	{
		if ( !Sandbox.Systems.Movement.LocalPlayer.Owns( GameObject.Root ) ) return;

		var config = CloudProgressionSystem.Current?.Config;
		if ( !config.IsValid() || !config.HasBackend ) return;

		var backpack = GameObject.Root.Components.GetInDescendantsOrSelf<Backpack>();
		var stats = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		var finance = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		var profile = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerProfileComponent>();

		var inventoryVersion = backpack.IsValid() ? backpack.InventoryVersion : 0;
		var statsVersion = stats.IsValid() ? stats.StatsVersion : 0;
		var financeVersion = finance.IsValid() ? finance.FinanceVersion : 0;
		var profileId = profile.IsValid() ? profile.AffiliationId ?? "" : "";

		if ( !_checkpointStateInitialized )
		{
			_checkpointStateInitialized = true;
			_lastInventoryVersion = inventoryVersion;
			_lastStatsVersion = statsVersion;
			_lastFinanceVersion = financeVersion;
			_lastCloudProgressVersion = CloudProgressVersion;
			_lastAffiliationId = profileId;
			ScheduleCheckpoint( "initial cloud checkpoint", 8f );
			return;
		}

		var changed = false;
		changed |= UpdateVersion( ref _lastInventoryVersion, inventoryVersion );
		changed |= UpdateVersion( ref _lastStatsVersion, statsVersion );
		changed |= UpdateVersion( ref _lastFinanceVersion, financeVersion );
		changed |= UpdateVersion( ref _lastCloudProgressVersion, CloudProgressVersion );

		if ( _lastAffiliationId != profileId )
		{
			_lastAffiliationId = profileId;
			changed = true;
		}

		if ( changed ) ScheduleCheckpoint( "state changed", CheckpointDelaySeconds );
		TryRunCheckpoint( config );
	}

	private void ScheduleCheckpoint( string reason, float delay )
	{
		_checkpointReason = string.IsNullOrWhiteSpace( reason ) ? "checkpoint" : reason;
		var earliest = _lastCheckpointRequestTime > 0f ? _lastCheckpointRequestTime + MinimumCheckpointIntervalSeconds : 0f;
		var target = Time.Now + float.Max( 0f, delay );
		_nextCheckpointTime = float.Max( target, earliest );
	}

	private void TryRunCheckpoint( CloudProgressionConfig config )
	{
		if ( _checkpointInFlight ) return;
		if ( _nextCheckpointTime <= 0f || Time.Now < _nextCheckpointTime ) return;

		_checkpointInFlight = true;
		_nextCheckpointTime = 0f;
		_checkpointCts?.Cancel();
		_checkpointCts?.Dispose();
		_checkpointCts = new CancellationTokenSource();
		_ = RequestCheckpointAsync( config, _checkpointReason, _checkpointCts.Token );
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

	private async Task RequestCheckpointAsync( CloudProgressionConfig config, string reason, CancellationToken cancellationToken )
	{
		try
		{
			var token = await Sandbox.Services.Auth.GetToken( config.ResolvedAuthServiceName(), cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !GameObject.IsValid() ) return;

			_lastCheckpointRequestTime = Time.Now;
			GameNetworkRpc.RequestSaveCloudPlayer( GameObject.Root, token, reason );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			if ( cancellationToken.IsCancellationRequested || !GameObject.IsValid() ) return;
			Log.Warning( $"[Cloud] Failed to request player checkpoint token: {e.Message}" );
		}
		finally
		{
			_checkpointInFlight = false;
		}
	}

	private static bool UpdateVersion( ref int stored, int current )
	{
		if ( stored == current ) return false;

		stored = current;
		return true;
	}

	private void CancelRequests()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;

		_checkpointCts?.Cancel();
		_checkpointCts?.Dispose();
		_checkpointCts = null;
	}
}
