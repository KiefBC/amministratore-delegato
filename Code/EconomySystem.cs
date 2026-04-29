using Sandbox;

/// <summary>
/// Scene-scoped singleton for the player's money. Host-authoritative.
/// Subscribe via <see cref="IMoneyChangedListener"/> to react to balance changes.
///
/// Usage from anywhere: <c>EconomySystem.Current.Add( 100 );</c>
/// </summary>
public sealed class EconomySystem : GameObjectSystem<EconomySystem>
{
	/// <summary>
	/// Current balance. Synced from host. Mutate via <see cref="Add"/> / <see cref="TrySpend"/>.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public int Money { get; set; }

	public EconomySystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 0, OnTick, nameof( OnTick ) );
	}

	private void OnTick()
	{
		// Future: walk owned businesses, sum their per-tick income, call Add( total ).
		// Empty today — the seam exists, the implementation does not.
	}

	/// <summary>
	/// Add money. Host-only — non-host calls are ignored until Rpc forwarding is wired.
	/// Negative amounts are rejected; use <see cref="TrySpend"/> for spending.
	/// </summary>
	public void Add( int amount )
	{
		if ( amount <= 0 ) return;
		if ( !Networking.IsHost ) return;

		Money += amount;
		BroadcastChanged( amount );
	}

	/// <summary>
	/// Try to spend. Host-only. Returns false if insufficient funds, non-host, or non-positive amount.
	/// </summary>
	public bool TrySpend( int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Networking.IsHost ) return false;
		if ( Money < amount ) return false;

		Money -= amount;
		BroadcastChanged( -amount );
		return true;
	}

	[Rpc.Broadcast]
	private void BroadcastChanged( int delta )
	{
		Scene.RunEvent<IMoneyChangedListener>( l => l.OnMoneyChanged( Money, delta ) );
	}
}
