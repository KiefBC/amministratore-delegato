using Sandbox;

namespace Sandbox.Systems;

public sealed class PlayerProfileComponent : Component
{
	public const string IndependentAffiliationId = "independent";

	[Sync( SyncFlags.FromHost )]
	public string AffiliationId { get; set; } = IndependentAffiliationId;

	[Hide]
	public string AffiliationDisplayName => DisplayNameForAffiliation( AffiliationId );

	protected override void OnStart()
	{
		if ( !Sandbox.Networking.IsHost ) return;

		AffiliationId = NormalizeAffiliationId( AffiliationId );
	}

	public bool TrySetAffiliation( string affiliationId )
	{
		if ( !Sandbox.Networking.IsHost ) return false;

		var normalized = NormalizeAffiliationId( affiliationId );
		if ( AffiliationId == normalized ) return false;

		AffiliationId = normalized;
		PlayerPersistenceSystem.Current?.MarkDirty( GameObject.Root, "profile changed" );
		return true;
	}

	public PlayerProfileSaveData CreateSaveData()
	{
		return new PlayerProfileSaveData
		{
			AffiliationId = NormalizeAffiliationId( AffiliationId ),
		};
	}

	public void RestoreSaveData( PlayerProfileSaveData data )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( data is null ) return;

		AffiliationId = NormalizeAffiliationId( data.AffiliationId );
	}

	public static string DisplayNameForAffiliation( string affiliationId )
	{
		var normalized = NormalizeAffiliationId( affiliationId );
		if ( normalized == IndependentAffiliationId ) return "Independent";

		return HumanizeId( normalized );
	}

	public static string NormalizeAffiliationId( string affiliationId )
	{
		if ( string.IsNullOrWhiteSpace( affiliationId ) ) return IndependentAffiliationId;

		return affiliationId.Trim().ToLowerInvariant();
	}

	private static string HumanizeId( string id )
	{
		var parts = id.Replace( '_', ' ' ).Replace( '-', ' ' ).Split( ' ', System.StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 0 ) return "Independent";

		for ( var i = 0; i < parts.Length; i++ )
		{
			var part = parts[i];
			parts[i] = part.Length <= 1
				? part.ToUpperInvariant()
				: char.ToUpperInvariant( part[0] ) + part[1..];
		}

		return string.Join( " ", parts );
	}
}
