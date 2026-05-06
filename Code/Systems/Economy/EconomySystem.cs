using Sandbox;

/// <summary>
/// Scene-scoped singleton for the player's money. Host-authoritative.
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
	/// Add money. Only the host can mint income/rewards; clients receive the synced result.
	/// Negative amounts are rejected; use <see cref="TrySpend"/> for spending.
	/// </summary>
	public void Add( int amount )
	{
		if ( amount <= 0 ) return;
		if ( !Networking.IsHost ) return;

		Money += amount;
	}

	/// <summary>
	/// Try to spend. Host calls return the real result. Non-host calls are forwarded
	/// asynchronously and return false until the synced balance updates.
	/// </summary>
	public bool TrySpend( int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Networking.IsHost )
		{
			GameNetworkRpc.RequestSpendMoney( amount );
			return false;
		}
		if ( Money < amount ) return false;

		Money -= amount;
		return true;
	}
}
