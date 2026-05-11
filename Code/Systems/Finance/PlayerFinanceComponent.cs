using Sandbox;

namespace Sandbox.Systems.Finance;

/// <summary>
/// Host-owned, session-only banking and portfolio state for one player.
/// Values are intentionally simple integers: stocks are whole shares, crypto is milli-units.
/// </summary>
public sealed class PlayerFinanceComponent : Component
{
	private const int CryptoUnitScale = 1000;

	[Property, Range( 0f, 100f ), Step( 0.25f )] public float DebtHourlyInterestPercent { get; set; } = 2f;
	[Property, Range( 1f, 86400f ), Step( 60f )] public float DebtAccrualIntervalSeconds { get; set; } = 3600f;

	[Sync( SyncFlags.FromHost )] public int BankBalance { get; set; }
	[Sync( SyncFlags.FromHost )] public int DebtBalance { get; set; }
	[Sync( SyncFlags.FromHost )] public float NextDebtAccrualTime { get; set; }
	[Sync( SyncFlags.FromHost )] public int FinanceVersion { get; set; }
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> StockShares { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> StockCostBasis { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> CryptoMilliUnits { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, int> OwnedBusinesses { get; set; } = new();

	protected override void OnUpdate()
	{
		if ( UsesCloudFinance() ) return;

		AccrueDebtIfDue();
	}

	public PlayerFinanceSaveData CreateSaveData()
	{
		if ( Sandbox.Networking.IsHost ) AccrueDebtIfDue();

		var remainingDebtSeconds = DebtBalance > 0 && NextDebtAccrualTime > Time.Now
			? NextDebtAccrualTime - Time.Now
			: 0f;

		return new PlayerFinanceSaveData
		{
			BankBalance = BankBalance,
			DebtBalance = DebtBalance,
			DebtHourlyInterestPercent = DebtHourlyInterestPercent,
			DebtAccrualIntervalSeconds = DebtAccrualIntervalSeconds,
			DebtAccrualRemainingSeconds = remainingDebtSeconds,
			StockShares = StockShares.ToDictionary( x => x.Key, x => x.Value ),
			StockCostBasis = StockCostBasis.ToDictionary( x => x.Key, x => x.Value ),
			CryptoMilliUnits = CryptoMilliUnits.ToDictionary( x => x.Key, x => x.Value ),
			OwnedBusinesses = OwnedBusinesses.ToDictionary( x => x.Key, x => x.Value ),
		};
	}

	public void RestoreSaveData( PlayerFinanceSaveData data )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( data is null ) return;

		BankBalance = int.Max( 0, data.BankBalance );
		DebtBalance = int.Max( 0, data.DebtBalance );
		DebtHourlyInterestPercent = float.Clamp( data.DebtHourlyInterestPercent, 0f, 100f );
		DebtAccrualIntervalSeconds = data.DebtAccrualIntervalSeconds > 0f
			? float.Clamp( data.DebtAccrualIntervalSeconds, 1f, 86400f )
			: DebtAccrualIntervalSeconds;

		ClearDictionary( StockShares );
		ClearDictionary( StockCostBasis );
		ClearDictionary( CryptoMilliUnits );
		ClearDictionary( OwnedBusinesses );

		RestoreStocks( data );
		RestorePositiveValues( CryptoMilliUnits, data.CryptoMilliUnits, FinanceCatalog.Coin );
		RestorePositiveValues( OwnedBusinesses, data.OwnedBusinesses, FinanceCatalog.Business );

		if ( DebtBalance <= 0 )
		{
			NextDebtAccrualTime = 0f;
		}
		else
		{
			var remaining = data.DebtAccrualRemainingSeconds > 0f ? data.DebtAccrualRemainingSeconds : DebtAccrualIntervalSeconds;
			NextDebtAccrualTime = Time.Now + float.Max( 1f, remaining );
		}

		Touch();
	}

	public bool TryDeposit( int amount )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 ) return false;

		var backpack = Backpack();
		if ( !backpack.IsValid() || !backpack.TrySpend( amount ) ) return false;

		BankBalance += amount;
		Touch();
		return true;
	}

	public bool TryWithdraw( int amount )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 || BankBalance < amount ) return false;

		var backpack = Backpack();
		if ( !backpack.IsValid() ) return false;
		if ( !backpack.AddMoney( amount ) ) return false;

		BankBalance -= amount;
		Touch();
		return true;
	}

	public bool TrySpend( FinanceAccountSource source, int amount )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
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

	public bool AddMoney( FinanceAccountSource source, int amount )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 ) return false;

		if ( source == FinanceAccountSource.Bank )
		{
			BankBalance += amount;
			Touch();
			return true;
		}

		return Backpack()?.AddMoney( amount ) == true;
	}

	public void ApplyCloudBankBalance( int bankBalance, string reason = "cloud snapshot" )
	{
		ApplyCloudFinanceSnapshot( bankBalance, null, null, reason );
	}

	public void ApplyCloudFinanceSnapshot( int? bankBalance, int? debtBalance, long? nextDebtAccrualAtUnix, string reason = "cloud snapshot" )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		var changed = false;
		if ( bankBalance.HasValue )
		{
			var normalized = int.Max( 0, bankBalance.Value );
			if ( BankBalance != normalized )
			{
				BankBalance = normalized;
				changed = true;
			}
		}

		if ( debtBalance.HasValue )
		{
			var normalized = int.Max( 0, debtBalance.Value );
			if ( DebtBalance != normalized )
			{
				DebtBalance = normalized;
				changed = true;
			}
		}

		if ( nextDebtAccrualAtUnix.HasValue )
		{
			var nextTime = CloudDebtAccrualTime( nextDebtAccrualAtUnix.Value );
			if ( !NearlyEqual( NextDebtAccrualTime, nextTime ) )
			{
				NextDebtAccrualTime = nextTime;
				changed = true;
			}
		}

		if ( !changed ) return;

		Touch();
		GameLogSystem.Current?.Info( "finance", "Cloud finance snapshot applied", GameObject.Root, data: GameLogSystem.Fields(
			("reason", reason),
			("bankBalance", BankBalance),
			("debtBalance", DebtBalance),
			("nextDebtAccrualTime", NextDebtAccrualTime) ) );
	}

	public bool TryBuyBusiness( string businessId )
	{
		if ( !Sandbox.Networking.IsHost ) return false;

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
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 || price <= 0m ) return false;
		if ( transactionFee < 0 ) return false;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return false;

		var shares = decimal.ToInt32( decimal.Floor( amount / price ) );
		return TryBuyStockShares( symbol, shares, price, source, transactionFee );
	}

	public bool TryBuyStockShares( string symbol, int shares, decimal price, FinanceAccountSource source, int transactionFee = 0 )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
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
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( shares <= 0 || price <= 0m ) return false;
		if ( transactionFee < 0 ) return false;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return false;
		if ( !StockShares.TryGetValue( offer.Symbol, out var owned ) || owned < shares ) return false;

		var proceeds = decimal.ToInt32( decimal.Floor( shares * price ) );
		if ( transactionFee > proceeds ) return false;
		var netProceeds = proceeds - transactionFee;

		if ( netProceeds > 0 )
		{
			var backpack = Backpack();
			if ( !backpack.IsValid() || !backpack.AddMoney( netProceeds ) ) return false;
		}

		var remaining = owned - shares;
		var basis = StockCostBasis.TryGetValue( offer.Symbol, out var existingBasis ) ? existingBasis : 0;
		var removedBasis = owned > 0 ? int.Min( basis, decimal.ToInt32( decimal.Ceiling( basis * (shares / (decimal)owned) ) ) ) : basis;
		var remainingBasis = int.Max( 0, basis - removedBasis );

		if ( remaining > 0 ) StockShares[offer.Symbol] = remaining;
		else StockShares.Remove( offer.Symbol );

		if ( remaining > 0 && remainingBasis > 0 ) StockCostBasis[offer.Symbol] = remainingBasis;
		else StockCostBasis.Remove( offer.Symbol );

		Touch();
		return true;
	}

	public bool TryTakeLoan( int amount, FinanceAccountSource destination )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 ) return false;
		if ( DebtBalance > int.MaxValue - amount ) return false;

		var oldDebt = DebtBalance;
		var backpack = Backpack();

		if ( destination == FinanceAccountSource.Bank )
		{
			if ( BankBalance > int.MaxValue - amount ) return false;
			BankBalance += amount;
		}
		else
		{
			if ( !backpack.IsValid() ) return false;
			if ( !backpack.AddMoney( amount ) ) return false;
		}

		DebtBalance += amount;
		EnsureDebtAccrualScheduled();
		Touch();

		Log.Info( $"[FinanceDebt] Loan taken; player={PlayerLogName()}; amount=${amount:N0}; destination={destination}; debt=${oldDebt:N0}->${DebtBalance:N0}; nextAccrual={NextDebtAccrualTime:0.##}." );
		GameLogSystem.Current?.Info( "debt", "Loan taken", GameObject.Root, data: GameLogSystem.Fields(
			("amount", amount),
			("destination", destination),
			("oldDebt", oldDebt),
			("newDebt", DebtBalance),
			("nextAccrual", NextDebtAccrualTime) ) );
		return true;
	}

	public bool TryRepayDebt( int amount, FinanceAccountSource source )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0 || DebtBalance <= 0 ) return false;

		var payment = int.Min( amount, DebtBalance );
		if ( !TrySpend( source, payment ) ) return false;

		var oldDebt = DebtBalance;
		DebtBalance -= payment;
		if ( DebtBalance <= 0 ) NextDebtAccrualTime = 0f;
		else EnsureDebtAccrualScheduled();
		Touch();

		Log.Info( $"[FinanceDebt] Debt repaid; player={PlayerLogName()}; source={source}; paid=${payment:N0}; debt=${oldDebt:N0}->${DebtBalance:N0}; nextAccrual={NextDebtAccrualTime:0.##}." );
		GameLogSystem.Current?.Info( "debt", "Debt repaid", GameObject.Root, data: GameLogSystem.Fields(
			("source", source),
			("paid", payment),
			("oldDebt", oldDebt),
			("newDebt", DebtBalance),
			("nextAccrual", NextDebtAccrualTime) ) );
		return true;
	}

	public void AccrueDebtIfDue()
	{
		if ( !Sandbox.Networking.IsHost ) return;

		if ( DebtBalance <= 0 )
		{
			if ( NextDebtAccrualTime > 0f )
			{
				NextDebtAccrualTime = 0f;
				Touch();
			}

			return;
		}

		if ( DebtHourlyInterestPercent <= 0f ) return;

		if ( NextDebtAccrualTime <= 0f )
		{
			EnsureDebtAccrualScheduled();
			Touch();
			return;
		}

		if ( Time.Now < NextDebtAccrualTime ) return;

		var oldDebt = DebtBalance;
		var interest = CalculateDebtInterest( DebtBalance, DebtHourlyInterestPercent );
		DebtBalance = (int)System.Math.Min( int.MaxValue, (long)DebtBalance + interest );
		EnsureDebtAccrualScheduled();
		Touch();

		Log.Info( $"[FinanceDebt] Debt interest accrued; player={PlayerLogName()}; rate={DebtHourlyInterestPercent:0.##}%; interest=${interest:N0}; debt=${oldDebt:N0}->${DebtBalance:N0}; nextAccrual={NextDebtAccrualTime:0.##}." );
		GameLogSystem.Current?.Info( "debt", "Debt interest accrued", GameObject.Root, data: GameLogSystem.Fields(
			("ratePercent", DebtHourlyInterestPercent),
			("interest", interest),
			("oldDebt", oldDebt),
			("newDebt", DebtBalance),
			("nextAccrual", NextDebtAccrualTime) ) );
	}

	public static int CalculateDebtInterest( int debtBalance, float hourlyInterestPercent )
	{
		if ( debtBalance <= 0 || hourlyInterestPercent <= 0f ) return 0;

		var interest = decimal.ToInt32( decimal.Ceiling( debtBalance * ((decimal)hourlyInterestPercent / 100m) ) );
		return int.Max( 1, interest );
	}

	public bool TryBuyCrypto( string coinId, int amount, decimal price, FinanceAccountSource source )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
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

	private void RestoreStocks( PlayerFinanceSaveData data )
	{
		foreach ( var pair in data.StockShares ?? new Dictionary<string, int>() )
		{
			var offer = FinanceCatalog.Stock( pair.Key );
			if ( offer is null || pair.Value <= 0 ) continue;

			StockShares[offer.Symbol] = pair.Value;
			if ( data.StockCostBasis?.TryGetValue( pair.Key, out var basis ) == true && basis > 0 )
			{
				StockCostBasis[offer.Symbol] = basis;
			}
		}
	}

	private static void RestorePositiveValues<T>( NetDictionary<string, int> target, Dictionary<string, int> source, System.Func<string, T> resolve ) where T : class
	{
		foreach ( var pair in source ?? new Dictionary<string, int>() )
		{
			var key = pair.Key ?? "";
			if ( string.IsNullOrWhiteSpace( key ) || pair.Value <= 0 ) continue;
			if ( resolve( key ) is null ) continue;

			target[key] = pair.Value;
		}
	}

	private static void ClearDictionary( NetDictionary<string, int> dictionary )
	{
		foreach ( var key in dictionary.Keys.ToList() )
		{
			dictionary.Remove( key );
		}
	}

	private Backpack Backpack()
	{
		return GameObject.Root.Components.GetInDescendantsOrSelf<Backpack>();
	}

	private static float CloudDebtAccrualTime( long nextDebtAccrualAtUnix )
	{
		if ( nextDebtAccrualAtUnix <= 0 ) return 0f;

		var remaining = nextDebtAccrualAtUnix - System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		return Time.Now + float.Max( 1f, remaining );
	}

	private static bool NearlyEqual( float left, float right )
	{
		return float.Abs( left - right ) < 0.1f;
	}

	private bool UsesCloudFinance()
	{
		var config = CloudProgressionSystem.Current?.Config;
		return config.IsValid() && config.HasBackend;
	}

	private void EnsureDebtAccrualScheduled()
	{
		if ( DebtBalance <= 0 )
		{
			NextDebtAccrualTime = 0f;
			return;
		}

		if ( NextDebtAccrualTime > Time.Now ) return;
		NextDebtAccrualTime = Time.Now + float.Max( 1f, DebtAccrualIntervalSeconds );
	}

	private string PlayerLogName()
	{
		return GameObject.Root.IsValid() && !string.IsNullOrWhiteSpace( GameObject.Root.Name )
			? GameObject.Root.Name
			: GameObject.Name;
	}

	private void Touch()
	{
		FinanceVersion++;
		PlayerPersistenceSystem.Current?.RequestSaveSoon( GameObject.Root, "finance changed" );
	}
}
