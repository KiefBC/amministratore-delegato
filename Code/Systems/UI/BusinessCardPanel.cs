using Sandbox.UI;

namespace Sandbox;

public sealed class BusinessCardPanel : Panel
{
	private readonly Label _name;
	private readonly Label _worth;
	private readonly Label _affiliation;

	public System.Action DragStarted { get; set; }
	public System.Action Dragged { get; set; }
	public System.Action DragStopped { get; set; }
	public System.Action CloseClicked { get; set; }

	public string PlayerName
	{
		get => _name.Text;
		set => _name.Text = value ?? "Player";
	}

	public string NetWorthText
	{
		get => _worth.Text;
		set => _worth.Text = value ?? "$0";
	}

	public string Affiliation
	{
		get => _affiliation.Text;
		set => _affiliation.Text = value ?? "Independent";
	}

	private bool _dragging;

	public BusinessCardPanel()
	{
		var topLine = AddChild<Panel>( "business-card-topline" );
		topLine.AddChild( new Label( "Business Card", "business-card-kicker" ) );

		var close = topLine.AddChild( new Label( "x", "business-card-close" ) );
		close.AddEventListener( "onclick", () => CloseClicked?.Invoke() );

		_name = AddChild( new Label( "Player", "business-card-name" ) );
		_worth = CreateRow( "Net Worth" );
		_affiliation = CreateRow( "Affiliation" );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		e.StopPropagation();
		StartDragging();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		e.StopPropagation();
		StopDragging();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_dragging ) return;
		if ( Mouse.Delta == Vector2.Zero ) return;

		Dragged?.Invoke();
	}

	public override void Delete( bool immediate = false )
	{
		StopDragging();
		base.Delete( immediate );
	}

	private void StartDragging()
	{
		if ( _dragging ) return;

		_dragging = true;
		SetClass( "dragging", true );
		SetMouseCapture( true );
		DragStarted?.Invoke();
	}

	private void StopDragging()
	{
		if ( !_dragging ) return;

		_dragging = false;
		SetClass( "dragging", false );
		SetMouseCapture( false );
		DragStopped?.Invoke();
	}

	private Label CreateRow( string label )
	{
		var row = AddChild<Panel>( "business-card-row" );
		row.AddChild( new Label( label ) );
		return row.AddChild( new Label( "", "business-card-value" ) );
	}
}
