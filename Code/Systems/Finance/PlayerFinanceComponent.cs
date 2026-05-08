using Sandbox;

/// <summary>
/// Host-owned, session-only banking and portfolio state for one player.
/// Values are intentionally simple integers: stocks are whole shares, crypto is milli-units.
/// </summary>
public sealed class PlayerFinanceComponent : Component
{
	private const int CryptoUnitScale = 1000;

	[Sync( SyncFlags.FromHost )] public int BankBalance { get; set; }
	[Sync( SyncFlags.FromHost )] public int FinanceVersion { get; set; }
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> StockShares { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> StockCostBasis { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> CryptoMilliUnits { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> OwnedBusinesses { get; set; } = new();

	public bool TryDeposit( int amount )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0 ) return false;

		var backpack = Backpack();
		if ( !backpack.IsValid() || !backpack.TrySpend( amount ) ) return false;

		BankBalance += amount;
		Touch();
		return true;
	}

	public bool TryWithdraw( int amount )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0 || BankBalance < amount ) return false;

		var backpack = Backpack();
		if ( !backpack.IsValid() ) return false;

		BankBalance -= amount;
		backpack.AddMoney( amount );
		Touch();
		return true;
	}

	public bool TrySpend( FinanceAccountSource source, int amount )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0 ) return false;

		if ( source == FinanceAccountSource.Bank )
		{
			if ( BankBalance < amount ) return false;
			BankBalance -= amount;
			Touch();
			return true;
		}

		return Backpack()?.TrySpend( amount ) == true;
	}

	public void AddMoney( FinanceAccountSource source, int amount )
	{
		if ( !Networking.IsHost ) return;
		if ( amount <= 0 ) return;

		if ( source == FinanceAccountSource.Bank )
		{
			BankBalance += amount;
			Touch();
			return;
		}

		Backpack()?.AddMoney( amount );
	}

	public bool TryBuyBusiness( string businessId )
	{
		if ( !Networking.IsHost ) return false;

		var offer = FinanceCatalog.Business( businessId );
		if ( offer is null ) return false;
		if ( OwnedBusinesses.ContainsKey( offer.Id ) ) return false;
		if ( !TrySpend( FinanceAccountSource.Bank, offer.Cost ) ) return false;

		OwnedBusinesses[offer.Id] = 1;
		Touch();
		return true;
	}

	public bool TryBuyStock( string symbol, int amount, decimal price, FinanceAccountSource source, int transactionFee = 0 )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0 || price <= 0m ) return false;
		if ( transactionFee < 0 ) return false;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return false;

		var shares = decimal.ToInt32( decimal.Floor( amount / price ) );
		return TryBuyStockShares( symbol, shares, price, source, transactionFee );
	}

	public bool TryBuyStockShares( string symbol, int shares, decimal price, FinanceAccountSource source, int transactionFee = 0 )
	{
		if ( !Networking.IsHost ) return false;
		if ( shares <= 0 || price <= 0m ) return false;
		if ( transactionFee < 0 ) return false;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return false;

		var cost = decimal.ToInt32( decimal.Ceiling( shares * price ) );
		if ( cost > int.MaxValue - transactionFee ) return false;
		if ( !TrySpend( source, cost + transactionFee ) ) return false;

		StockShares[offer.Symbol] = StockShares.TryGetValue( offer.Symbol, out var existing ) ? existing + shares : shares;
		StockCostBasis[offer.Symbol] = StockCostBasis.TryGetValue( offer.Symbol, out var existingBasis ) ? existingBasis + cost : cost;
		Touch();
		return true;
	}

	public bool TrySellStock( string symbol, int shares, decimal price, int transactionFee = 0 )
	{
		if ( !Networking.IsHost ) return false;
		if ( shares <= 0 || price <= 0m ) return false;
		if ( transactionFee < 0 ) return false;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return false;
		if ( !StockShares.TryGetValue( offer.Symbol, out var owned ) || owned < shares ) return false;

		var proceeds = decimal.ToInt32( decimal.Floor( shares * price ) );
		if ( transactionFee > proceeds ) return false;

		var remaining = owned - shares;
		var basis = StockCostBasis.TryGetValue( offer.Symbol, out var existingBasis ) ? existingBasis : 0;
		var removedBasis = owned > 0 ? int.Min( basis, decimal.ToInt32( decimal.Ceiling( basis * (shares / (decimal)owned) ) ) ) : basis;
		var remainingBasis = int.Max( 0, basis - removedBasis );

		if ( remaining > 0 ) StockShares[offer.Symbol] = remaining;
		else StockShares.Remove( offer.Symbol );

		if ( remaining > 0 && remainingBasis > 0 ) StockCostBasis[offer.Symbol] = remainingBasis;
		else StockCostBasis.Remove( offer.Symbol );

		Backpack()?.AddMoney( proceeds - transactionFee );
		Touch();
		return true;
	}

	public bool TryBuyCrypto( string coinId, int amount, decimal price, FinanceAccountSource source )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0 || price <= 0m ) return false;

		var offer = FinanceCatalog.Coin( coinId );
		if ( offer is null ) return false;

		var units = decimal.ToInt32( decimal.Floor( (amount / price) * CryptoUnitScale ) );
		if ( units <= 0 ) return false;

		if ( !TrySpend( source, amount ) ) return false;

		CryptoMilliUnits[offer.Id] = CryptoMilliUnits.TryGetValue( offer.Id, out var existing ) ? existing + units : units;
		Touch();
		return true;
	}

	public int BusinessIncomePerMinute()
	{
		var total = 0;
		foreach ( var pair in OwnedBusinesses )
		{
			var offer = FinanceCatalog.Business( pair.Key );
			if ( offer is null ) continue;
			total += offer.IncomePerMinute * int.Max( 1, pair.Value );
		}

		return total;
	}

	private Backpack Backpack()
	{
		return GameObject.Root.Components.GetInDescendantsOrSelf<Backpack>();
	}

	private void Touch()
	{
		FinanceVersion++;
	}
}
