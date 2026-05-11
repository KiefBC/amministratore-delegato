using Sandbox;

namespace Sandbox.Systems.Cloud;

public readonly record struct CloudStatsProjection(
	int PlayerLevel,
	int BusinessLevel,
	int BusinessesOwned,
	long NetWorth,
	int CreditScore,
	int HoursPlayedSeconds,
	int JobsCompleted );

public static class CloudStatsProjectionBuilder
{
	public const string PlayerLevelStat = "player_level";
	public const string BusinessLevelStat = "business_level";
	public const string BusinessesOwnedStat = "businesses_owned";
	public const string NetWorthStat = "net_worth";
	public const string CreditScoreStat = "credit_score";
	public const string HoursPlayedStat = "hours_played";
	public const string JobsCompletedStat = "jobs_completed";

	public static CloudStatsProjection FromPlayer( GameObject player, int hoursPlayedSeconds = 0 )
	{
		var stats = player.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		var progress = player.Components.GetInDescendantsOrSelf<CloudPlayerProgressComponent>();

		var playerLevel = stats.IsValid() ? stats.PlayerLevel : 1;
		var businessLevel = stats.IsValid() ? stats.BusinessLevel : 1;
		var businessesOwned = BusinessesOwned( finance );
		var netWorth = NetWorth( player );

		return new CloudStatsProjection(
			playerLevel,
			businessLevel,
			businessesOwned,
			netWorth,
			CreditScore( finance, netWorth ),
			int.Max( 0, hoursPlayedSeconds ),
			progress.IsValid() ? progress.JobCompletions : 0 );
	}

	public static void PublishLocal( CloudStatsProjection projection )
	{
		try
		{
			Sandbox.Services.Stats.SetValue( PlayerLevelStat, projection.PlayerLevel );
			Sandbox.Services.Stats.SetValue( BusinessLevelStat, projection.BusinessLevel );
			Sandbox.Services.Stats.SetValue( BusinessesOwnedStat, projection.BusinessesOwned );
			Sandbox.Services.Stats.SetValue( NetWorthStat, projection.NetWorth );
			Sandbox.Services.Stats.SetValue( CreditScoreStat, projection.CreditScore );
			Sandbox.Services.Stats.SetValue( HoursPlayedStat, projection.HoursPlayedSeconds );
			Sandbox.Services.Stats.SetValue( JobsCompletedStat, projection.JobsCompleted );
			Sandbox.Services.Stats.Flush();
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[CloudStats] Failed to publish stats projection: {e.Message}" );
		}
	}

	private static long NetWorth( GameObject player )
	{
		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		long total = 0;

		if ( backpack.IsValid() )
		{
			total += backpack.Wallet;
			foreach ( var item in backpack.Items.Values )
			{
				if ( !item.IsValid ) continue;

				var definition = backpack.GetDefinition( item );
				if ( definition is null || definition.IsCurrency ) continue;

				total += (long)definition.Value * item.StackCount;
			}
		}

		if ( finance.IsValid() )
		{
			total += finance.BankBalance;
			total += StockValue( finance );
			total += CryptoValue( finance );
			total += BusinessValue( finance );
			total -= finance.DebtBalance;
		}

		return long.Max( 0, total );
	}

	private static int BusinessesOwned( PlayerFinanceComponent finance )
	{
		if ( !finance.IsValid() ) return 0;

		var total = 0;
		foreach ( var pair in finance.OwnedBusinesses )
		{
			if ( pair.Value > 0 ) total += pair.Value;
		}

		return total;
	}

	private static int CreditScore( PlayerFinanceComponent finance, long netWorth )
	{
		if ( !finance.IsValid() ) return 650;

		var score = 650;
		if ( finance.DebtBalance <= 0 ) score += 50;
		if ( netWorth >= 10000 ) score += 25;
		if ( finance.BankBalance >= finance.DebtBalance ) score += 25;
		if ( finance.DebtBalance > finance.BankBalance * 2 ) score -= 75;
		return int.Clamp( score, 300, 850 );
	}

	private static long StockValue( PlayerFinanceComponent finance )
	{
		decimal total = 0m;
		foreach ( var pair in finance.StockShares )
		{
			if ( pair.Value <= 0 ) continue;
			var price = MarketDataSystem.Game?.StockPrice( pair.Key ) ?? FinanceCatalog.Stock( pair.Key )?.FallbackPrice ?? 0m;
			total += pair.Value * price;
		}

		return decimal.ToInt64( decimal.Round( total ) );
	}

	private static long CryptoValue( PlayerFinanceComponent finance )
	{
		decimal total = 0m;
		foreach ( var pair in finance.CryptoMilliUnits )
		{
			if ( pair.Value <= 0 ) continue;
			var price = MarketDataSystem.Game?.CryptoPrice( pair.Key ) ?? FinanceCatalog.Coin( pair.Key )?.FallbackPrice ?? 0m;
			total += (pair.Value / 1000m) * price;
		}

		return decimal.ToInt64( decimal.Round( total ) );
	}

	private static long BusinessValue( PlayerFinanceComponent finance )
	{
		long total = 0;
		foreach ( var pair in finance.OwnedBusinesses )
		{
			if ( pair.Value <= 0 ) continue;
			var offer = FinanceCatalog.Business( pair.Key );
			if ( offer is null ) continue;

			total += (long)offer.Cost * pair.Value;
		}

		return total;
	}
}
