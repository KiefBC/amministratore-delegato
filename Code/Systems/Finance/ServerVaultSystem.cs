using Sandbox;

/// <summary>
/// Host-only scene vault for server-owned economy sinks. Stock trade fees go here
/// now so a later lotto system can consume the accumulated total.
/// </summary>
public sealed class ServerVaultSystem : GameObjectSystem<ServerVaultSystem>
{
	public const decimal DefaultStockTradeFeePercent = 2m;

	public decimal StockTradeFeePercent { get; set; } = DefaultStockTradeFeePercent;
	public long StockTradeFeeVaultTotal { get; private set; }
	public int VaultVersion { get; private set; }

	public ServerVaultSystem( Scene scene ) : base( scene )
	{
	}

	public int CalculateStockTradeFee( int grossAmount )
	{
		return CalculateStockTradeFee( grossAmount, StockTradeFeePercent );
	}

	public static int CalculateStockTradeFee( int grossAmount, decimal feePercent = DefaultStockTradeFeePercent )
	{
		if ( grossAmount <= 0 ) return 0;
		if ( feePercent <= 0m ) return 0;

		var fee = decimal.ToInt32( decimal.Ceiling( grossAmount * (feePercent / 100m) ) );
		return int.Max( 1, fee );
	}

	public bool AddStockTradeFee( GameObject player, string symbol, int grossAmount, int fee, string side )
	{
		if ( !Networking.IsHost ) return false;
		if ( fee <= 0 ) return false;

		StockTradeFeeVaultTotal += fee;
		VaultVersion++;

		Log.Info( $"[ServerVault] Stock trade fee added; side={side}; player={PlayerLogName( player )}; symbol={symbol}; gross=${grossAmount:N0}; feePercent={StockTradeFeePercent:0.##}%; fee=${fee:N0}; total=${StockTradeFeeVaultTotal:N0}." );
		return true;
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( !player.IsValid() ) return "<invalid>";
		return string.IsNullOrWhiteSpace( player.Name ) ? "<unnamed>" : player.Name;
	}
}
