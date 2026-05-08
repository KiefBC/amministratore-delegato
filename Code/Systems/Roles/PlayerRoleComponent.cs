using Sandbox;

namespace Sandbox.Systems.Roles;

public sealed class PlayerRoleComponent : Component
{
	[Sync( SyncFlags.FromHost )]
	public PlayerRole Role { get; set; } = PlayerRole.User;

	[Hide] public bool IsUser => Role == PlayerRole.User;
	[Hide] public bool IsModerator => Role == PlayerRole.Moderator;
	[Hide] public bool IsAdmin => Role == PlayerRole.Admin;
	[Hide] public bool IsModeratorOrAdmin => Role is PlayerRole.Moderator or PlayerRole.Admin;

	public bool TrySetRole( PlayerRole role )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( Role == role ) return false;

		Role = role;
		return true;
	}
}
