using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Systems.Roles;

[AssetType( Name = "Role Config", Extension = "rolecfg", Category = "Amministratore" )]
public partial class RoleConfig : GameResource
{
	public const string DefaultPath = "config/roles.rolecfg";

	private static RoleConfig _fallback;

	[Property] public PlayerRole DefaultRole { get; set; } = PlayerRole.User;
	[Property] public List<string> AdminSteamIds { get; set; } = new();
	[Property] public List<string> ModeratorSteamIds { get; set; } = new();

	[Hide] public int AdminCount => AdminSteamIds?.Count ?? 0;
	[Hide] public int ModeratorCount => ModeratorSteamIds?.Count ?? 0;

	public PlayerRole ResolveRole( string steamId )
	{
		var normalized = NormalizeSteamId( steamId );
		if ( string.IsNullOrWhiteSpace( normalized ) ) return DefaultRole;

		if ( ContainsSteamId( AdminSteamIds, normalized ) ) return PlayerRole.Admin;
		if ( ContainsSteamId( ModeratorSteamIds, normalized ) ) return PlayerRole.Moderator;

		return DefaultRole;
	}

	public bool IsAdminSteamId( string steamId )
	{
		return ContainsSteamId( AdminSteamIds, NormalizeSteamId( steamId ) );
	}

	public bool IsModeratorSteamId( string steamId )
	{
		return ContainsSteamId( ModeratorSteamIds, NormalizeSteamId( steamId ) );
	}

	public static RoleConfig Resolve( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) path = DefaultPath;

		var resource = ResourceLibrary.Get<RoleConfig>( path );
		if ( resource is not null ) return resource;

		return _fallback ??= new RoleConfig();
	}

	private static bool ContainsSteamId( IEnumerable<string> steamIds, string needle )
	{
		return steamIds?.Any( x => NormalizeSteamId( x ) == needle ) == true;
	}

	private static string NormalizeSteamId( string steamId )
	{
		return string.IsNullOrWhiteSpace( steamId ) ? "" : steamId.Trim();
	}

	private static string NormalizePath( string path )
	{
		return string.IsNullOrWhiteSpace( path )
			? ""
			: path.Replace( '\\', '/' ).Trim().ToLowerInvariant();
	}
}
