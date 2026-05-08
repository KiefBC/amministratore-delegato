using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Centralizes "is the player in a UI mode that should suppress mouse-look?"
/// Any panel that wants to use the cursor (inventory, future shop / map / pause)
/// calls <see cref="RequestUIFocus"/> on open and <see cref="ReleaseUIFocus"/>
/// on close. The first request disables <c>PlayerController.UseLookControls</c>
/// and shows the cursor; the last release restores both.
///
/// Lives on the player root (alongside <see cref="PlayerController"/> and
/// <see cref="RarityConfig"/>). Use a HashSet so multiple panels can hold focus
/// concurrently without stomping each other.
/// </summary>
public sealed class UIStateManager : Component
{
	[Property] public PlayerController PlayerController { get; set; }
	[Property] public string CursorHoldInput { get; set; } = "Walk";

	private readonly HashSet<object> _focusOwners = new();
	private readonly object _cursorHoldFocusOwner = new();
	private bool _cachedUseLookControls;
	private MouseVisibility _cachedMouseVisibility;
	private bool _cursorHoldFocusActive;

	public bool IsAnyUIOpen => _focusOwners.Count > 0;

	protected override void OnStart()
	{
		PlayerController ??= Components.Get<PlayerController>();
	}

	protected override void OnUpdate()
	{
		PlayerController ??= Components.Get<PlayerController>();
		if ( IsProxy )
		{
			SetCursorHoldFocus( false );
			return;
		}

		var wantsCursor = !string.IsNullOrWhiteSpace( CursorHoldInput ) && Input.Down( CursorHoldInput );
		SetCursorHoldFocus( wantsCursor );
	}

	protected override void OnDisabled()
	{
		SetCursorHoldFocus( false );
	}

	public void RequestUIFocus( object owner )
	{
		if ( owner is null ) return;
		var wasEmpty = _focusOwners.Count == 0;
		if ( !_focusOwners.Add( owner ) ) return;

		if ( wasEmpty && PlayerController.IsValid() )
		{
			_cachedUseLookControls = PlayerController.UseLookControls;
			_cachedMouseVisibility = Sandbox.Mouse.Visibility;
			PlayerController.UseLookControls = false;
			Sandbox.Mouse.Visibility = MouseVisibility.Visible;
		}
	}

	public void ReleaseUIFocus( object owner )
	{
		if ( owner is null ) return;
		if ( !_focusOwners.Remove( owner ) ) return;

		if ( _focusOwners.Count == 0 && PlayerController.IsValid() )
		{
			PlayerController.UseLookControls = _cachedUseLookControls;
			Sandbox.Mouse.Visibility = _cachedMouseVisibility;
		}
	}

	private void SetCursorHoldFocus( bool active )
	{
		if ( _cursorHoldFocusActive == active ) return;

		_cursorHoldFocusActive = active;
		if ( active )
		{
			RequestUIFocus( _cursorHoldFocusOwner );
			return;
		}

		ReleaseUIFocus( _cursorHoldFocusOwner );
	}
}
