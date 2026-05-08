using Sandbox;

namespace Sandbox.Systems.UI;

/// <summary>
/// Local-only world title shown above a player. Name and level are derived from
/// synced/network state; the UI object itself is not networked.
/// </summary>
public sealed class PlayerTitleComponent : Component
{
	[Property] public float HeightOffset { get; set; } = 92f;
	[Property] public float TitleWorldScale { get; set; } = 0.55f;
	[Property] public Vector2 PanelSize { get; set; } = new( 2400f, 360f );
	[Property] public float CardMinWidth { get; set; } = 1150f;
	[Property] public float NameFontSize { get; set; } = 58f;
	[Property] public float LevelFontSize { get; set; } = 36f;
	[Property] public float CardPaddingX { get; set; } = 42f;
	[Property] public float CardPaddingY { get; set; } = 20f;
	[Property, Range( 0f, 1f ), Step( 0.05f )] public float Opacity { get; set; } = 0.75f;
	[Property] public bool ShowForLocalPlayer { get; set; } = true;

	private GameObject _titleObject;
	private PlayerTitlePanel _panel;
	private PlayerController _controller;
	private Sandbox.WorldPanel _worldPanel;

	protected override void OnStart()
	{
		EnsureTitleObject();
	}

	protected override void OnUpdate()
	{
		EnsureTitleObject();
		UpdateTitleTransform();
		UpdateTitleVisibility();
	}

	protected override void OnDisabled()
	{
		ClearTitleObject();
	}

	public void CopySettingsFrom( PlayerTitleComponent source )
	{
		if ( !source.IsValid() || source == this ) return;

		HeightOffset = source.HeightOffset;
		TitleWorldScale = source.TitleWorldScale;
		PanelSize = source.PanelSize;
		CardMinWidth = source.CardMinWidth;
		NameFontSize = source.NameFontSize;
		LevelFontSize = source.LevelFontSize;
		CardPaddingX = source.CardPaddingX;
		CardPaddingY = source.CardPaddingY;
		Opacity = source.Opacity;
		ShowForLocalPlayer = source.ShowForLocalPlayer;
	}

	private void EnsureTitleObject()
	{
		if ( _titleObject.IsValid() && _panel.IsValid() && _worldPanel.IsValid() ) return;

		ClearTitleObject();

		_titleObject = new GameObject( "Player Title" );
		_titleObject.SetParent( GameObject.Root, false );
		_titleObject.NetworkMode = NetworkMode.Never;

		_worldPanel = _titleObject.Components.Create<Sandbox.WorldPanel>();

		_panel = _titleObject.Components.Create<PlayerTitlePanel>();
		_panel.Player = GameObject.Root;
		_panel.Title = this;

		ApplyWorldPanelSettings();
	}

	private void UpdateTitleTransform()
	{
		if ( !_titleObject.IsValid() ) return;
		ApplyWorldPanelSettings();

		_controller ??= GameObject.Root.Components.GetInDescendantsOrSelf<PlayerController>();
		var height = _controller.IsValid() ? _controller.BodyHeight + 20f : HeightOffset;
		_titleObject.WorldPosition = GameObject.Root.WorldPosition + Vector3.Up * float.Max( HeightOffset, height );

		if ( Scene?.Camera is null ) return;

		_titleObject.WorldRotation = Rotation.LookAt( Scene.Camera.WorldPosition - _titleObject.WorldPosition );
	}

	private void ApplyWorldPanelSettings()
	{
		if ( !_worldPanel.IsValid() ) return;

		_worldPanel.WorldScale = TitleWorldScale;
		_worldPanel.PanelSize = PanelSize;
	}

	private void UpdateTitleVisibility()
	{
		if ( !_titleObject.IsValid() ) return;

		var isLocalPlayer = Sandbox.Systems.Movement.LocalPlayer.Owns( GameObject.Root );
		_titleObject.Enabled = ShowForLocalPlayer || !isLocalPlayer;
	}

	private void ClearTitleObject()
	{
		if ( _titleObject.IsValid() )
		{
			_titleObject.Destroy();
		}

		_titleObject = null;
		_panel = null;
		_worldPanel = null;
	}
}
