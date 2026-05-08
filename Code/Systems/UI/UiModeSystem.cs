using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Scene-wide local UI mode state. This is the single place that decides whether
/// an interface is open, whether shortcuts/world input are blocked, whether HUD
/// should hide, and whether player look/movement controls are locked.
/// </summary>
public sealed class UiModeSystem : GameObjectSystem<UiModeSystem>
{
	private readonly HashSet<object> _cursorOwners = new();
	private readonly HashSet<object> _movementLockOwners = new();
	private readonly object _cursorHoldOwner = new();
	private GameObject _player;
	private PlayerController _controller;
	private bool _cachedUseLookControls;
	private bool _cachedUseInputControls;
	private MouseVisibility _cachedMouseVisibility;
	private bool _hasLookLock;
	private bool _hasMovementLock;
	private bool _cursorHoldActive;
	private bool _currentHidesHud;

	public UiModeSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, OnTick, nameof( OnTick ) );
	}

	public UiModeKind CurrentKind { get; private set; } = UiModeKind.None;
	public object CurrentOwner { get; private set; }
	public bool IsAnyOpen => CurrentOwner is not null;
	public bool BlocksShortcuts => IsAnyOpen;
	public bool BlocksWorldInput => IsAnyOpen;
	public bool HidesHud => _currentHidesHud;
	public bool IsCursorVisible => _cursorOwners.Count > 0;

	public bool IsOpenByOther( object owner )
	{
		return CurrentOwner is not null && !ReferenceEquals( CurrentOwner, owner );
	}

	public bool TryOpen( object owner, UiModeKind kind, bool lockMovement = false, bool hideHud = false )
	{
		if ( owner is null ) return false;
		if ( kind == UiModeKind.None ) return false;
		if ( CurrentOwner is not null && !ReferenceEquals( CurrentOwner, owner ) ) return false;

		CurrentOwner = owner;
		CurrentKind = kind;
		_currentHidesHud = hideHud;
		RequestCursorFocus( owner );

		if ( lockMovement ) RequestMovementLock( owner );
		else ReleaseMovementLock( owner );

		return true;
	}

	public void Close( object owner )
	{
		if ( owner is null ) return;

		ReleaseCursorFocus( owner );
		ReleaseMovementLock( owner );

		if ( !ReferenceEquals( CurrentOwner, owner ) ) return;

		CurrentOwner = null;
		CurrentKind = UiModeKind.None;
		_currentHidesHud = false;
	}

	public void RequestCursorFocus( object owner )
	{
		if ( owner is null ) return;
		if ( !_cursorOwners.Add( owner ) ) return;

		if ( _cursorOwners.Count == 1 ) ApplyLookLock();
	}

	public void ReleaseCursorFocus( object owner )
	{
		if ( owner is null ) return;
		if ( !_cursorOwners.Remove( owner ) ) return;

		if ( _cursorOwners.Count == 0 ) RestoreLookLock();
	}

	private void OnTick()
	{
		EnsureLocalController();
		SetCursorHoldFocus( CurrentOwner is null && Input.Down( "Walk" ) );
	}

	private void RequestMovementLock( object owner )
	{
		if ( owner is null ) return;
		if ( !_movementLockOwners.Add( owner ) ) return;

		if ( _movementLockOwners.Count == 1 ) ApplyMovementLock();
	}

	private void ReleaseMovementLock( object owner )
	{
		if ( owner is null ) return;
		if ( !_movementLockOwners.Remove( owner ) ) return;

		if ( _movementLockOwners.Count == 0 ) RestoreMovementLock();
	}

	private void SetCursorHoldFocus( bool active )
	{
		if ( _cursorHoldActive == active ) return;

		_cursorHoldActive = active;
		if ( active ) RequestCursorFocus( _cursorHoldOwner );
		else ReleaseCursorFocus( _cursorHoldOwner );
	}

	private void ApplyLookLock()
	{
		EnsureLocalController();
		_cachedMouseVisibility = Mouse.Visibility;
		Mouse.Visibility = MouseVisibility.Visible;

		if ( !_controller.IsValid() ) return;

		_cachedUseLookControls = _controller.UseLookControls;
		_controller.UseLookControls = false;
		_hasLookLock = true;
	}

	private void RestoreLookLock()
	{
		Mouse.Visibility = _cachedMouseVisibility;
		if ( _hasLookLock && _controller.IsValid() ) _controller.UseLookControls = _cachedUseLookControls;
		_hasLookLock = false;
	}

	private void ApplyMovementLock()
	{
		EnsureLocalController();
		if ( !_controller.IsValid() ) return;

		_cachedUseInputControls = _controller.UseInputControls;
		_controller.UseInputControls = false;
		_hasMovementLock = true;
	}

	private void RestoreMovementLock()
	{
		if ( _hasMovementLock && _controller.IsValid() ) _controller.UseInputControls = _cachedUseInputControls;
		_hasMovementLock = false;
	}

	private void EnsureLocalController()
	{
		if ( Sandbox.LocalPlayer.Owns( _player ) && _controller.IsValid() ) return;

		_player = Sandbox.LocalPlayer.GameObject( Scene );
		_controller = _player?.Components.GetInDescendantsOrSelf<PlayerController>();
	}
}
