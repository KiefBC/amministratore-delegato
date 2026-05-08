using System.Collections.Generic;

public enum FinanceAccountSource
{
	Wallet,
	Bank,
}

public sealed record BusinessOffer( string Id, string Name, string Sector, int Cost, int IncomePerMinute, string Description );

public sealed record StockOffer( string Symbol, string Name, decimal FallbackPrice, decimal ChangePercent );

public sealed record CryptoOffer( string Id, string Symbol, string Name, decimal FallbackPrice, decimal ChangePercent );

public static class FinanceCatalog
{
	public static readonly IReadOnlyList<BusinessOffer> Businesses = new[]
	{
		new BusinessOffer( "coffee_cart", "Coffee Cart", "Food Service", 2500, 90, "Low-risk street retail with steady foot traffic." ),
		new BusinessOffer( "laundromat", "Laundromat", "Local Services", 12500, 420, "Reliable neighborhood cash flow and modest upkeep." ),
		new BusinessOffer( "corner_store", "Corner Store", "Retail", 32000, 980, "Higher operating costs, stronger daily revenue." ),
		new BusinessOffer( "logistics_office", "Logistics Office", "Transport", 85000, 2600, "Regional contracts with larger capital requirements." ),
	};

	public static readonly IReadOnlyList<StockOffer> Stocks = new[]
	{
		new StockOffer( "AAPL", "Apple Inc.", 182.41m, 1.18m ),
		new StockOffer( "MSFT", "Microsoft Corp.", 417.88m, 0.82m ),
		new StockOffer( "NVDA", "NVIDIA Corp.", 913.56m, 2.45m ),
		new StockOffer( "TSLA", "Tesla Inc.", 174.72m, -1.12m ),
		new StockOffer( "SPY", "S&P 500 ETF", 522.18m, 0.64m ),
	};

	public static readonly IReadOnlyList<CryptoOffer> Crypto = new[]
	{
		new CryptoOffer( "bitcoin", "BTC", "Bitcoin", 64250m, 1.74m ),
		new CryptoOffer( "ethereum", "ETH", "Ethereum", 3180m, 1.22m ),
		new CryptoOffer( "solana", "SOL", "Solana", 146m, -0.46m ),
	};

	public static BusinessOffer Business( string id )
	{
		foreach ( var item in Businesses )
		{
			if ( item.Id == id ) return item;
		}

		return null;
	}

	public static StockOffer Stock( string symbol )
	{
		symbol = (symbol ?? "").ToUpperInvariant();
		foreach ( var item in Stocks )
		{
			if ( item.Symbol == symbol ) return item;
		}

		return null;
	}

	public static CryptoOffer Coin( string id )
	{
		foreach ( var item in Crypto )
		{
			if ( item.Id == id ) return item;
		}

		return null;
	}
}
