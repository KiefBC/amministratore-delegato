using Sandbox;
using System.Linq;

public sealed class ComputerTerminalSystem : GameObjectSystem<ComputerTerminalSystem>
{
	private GameObject _displayObject;
	private ComputerTerminalPanel _panel;
	public static bool IsAnyTerminalOpen { get; private set; }

	public ComputerTerminalSystem( Scene scene ) : base( scene )
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

	public static void SetTerminalOpen( bool open )
	{
		IsAnyTerminalOpen = open;
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

	public static ComputerTerminalPanel OpenForScene( Scene scene )
	{
		PhoneBookSystem.Current?.Close();

		var panel = EnsurePanel( scene );
		panel?.Open();
		return panel;
	}

	public static void ShowMessageForScene( Scene scene, int kind, string title, string message, float shownDuration )
	{
		EnsurePanel( scene )?.ShowMessage( kind, title, message, shownDuration );
	}

	private static ComputerTerminalPanel EnsurePanel( Scene scene )
	{
		if ( scene is null ) return null;

		var existing = scene.GetAllComponents<ComputerTerminalPanel>().FirstOrDefault( x => x.IsValid() );
		if ( existing.IsValid() ) return existing;

		var displayObject = new GameObject( "Computer Terminal" );
		displayObject.NetworkMode = NetworkMode.Never;

		var screen = displayObject.Components.Create<ScreenPanel>();
		screen.ZIndex = 260;

		return displayObject.Components.Create<ComputerTerminalPanel>();
	}
}
