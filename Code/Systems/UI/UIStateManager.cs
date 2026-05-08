using Sandbox;

namespace Sandbox.Systems.UI;

/// <summary>
/// Compatibility wrapper for older player prefabs. New UI code should use
/// <see cref="UiModeSystem"/> so modal state, cursor visibility, and control locks
/// are scene-wide instead of attached to one player component.
/// </summary>
public sealed class UIStateManager : Component
{
	[Property] public PlayerController PlayerController { get; set; }
	[Property] public string CursorHoldInput { get; set; } = "Walk";

	public bool IsAnyUIOpen => UiModeSystem.Current?.IsCursorVisible == true;

	public void RequestUIFocus( object owner )
	{
		UiModeSystem.Current?.RequestCursorFocus( owner );
	}

	public void ReleaseUIFocus( object owner )
	{
		UiModeSystem.Current?.ReleaseCursorFocus( owner );
	}
}
