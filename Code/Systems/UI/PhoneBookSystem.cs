using Sandbox;
using System.Linq;

namespace Sandbox.Systems.UI;

public sealed class PhoneBookSystem : GameObjectSystem<PhoneBookSystem>
{
	private GameObject _displayObject;
	private PhoneBookPanel _panel;
	public static bool IsAnyPhoneBookOpen { get; private set; }

	public PhoneBookSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, OnTick, nameof( OnTick ) );
	}

	public void Open()
	{
		_panel = OpenForScene( Scene );
		_displayObject = _panel?.GameObject;
	}

	public void Close()
	{
		_panel?.Close();
	}

	public static void SetPhoneBookOpen( bool open )
	{
		IsAnyPhoneBookOpen = open;
	}

	private void OnTick()
	{
		EnsureDisplay();
	}

	private void EnsureDisplay()
	{
		if ( _panel.IsValid() ) return;

		_panel = EnsurePanel( Scene );
		_displayObject = _panel?.GameObject;
	}

	public static PhoneBookPanel OpenForScene( Scene scene )
	{
		ComputerTerminalSystem.Current?.Close();

		var panel = EnsurePanel( scene );
		panel?.Open();
		return panel;
	}

	private static PhoneBookPanel EnsurePanel( Scene scene )
	{
		if ( scene is null ) return null;

		var existing = scene.GetAllComponents<PhoneBookPanel>().FirstOrDefault( x => x.IsValid() );
		if ( existing.IsValid() ) return existing;

		var displayObject = new GameObject( "Phone Book" );
		displayObject.NetworkMode = NetworkMode.Never;

		var screen = displayObject.Components.Create<ScreenPanel>();
		screen.ZIndex = 255;

		return displayObject.Components.Create<PhoneBookPanel>();
	}
}
