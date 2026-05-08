using Sandbox;

/// <summary>
/// Keeps avatar clothing stable across editor host and New Instance clients.
/// Networked players use the owning connection's avatar data; non-networked
/// editor/template objects fall back to the local user's avatar data.
/// </summary>
public sealed class PlayerAppearanceComponent : Component
{
	private Dresser _dresser;
	private Dresser.ClothingSource? _lastSource;

	protected override void OnStart()
	{
		UpdateDresserSource();
	}

	protected override void OnUpdate()
	{
		UpdateDresserSource();
	}

	private void UpdateDresserSource()
	{
		if ( !_dresser.IsValid() ) _dresser = GameObject.Root.Components.Get<Dresser>();
		if ( !_dresser.IsValid() ) return;

		var source = HasNetworkOwner()
			? Dresser.ClothingSource.OwnerConnection
			: Dresser.ClothingSource.LocalUser;
		if ( _lastSource == source ) return;

		_dresser.Source = source;
		_lastSource = source;
	}

	private bool HasNetworkOwner()
	{
		var root = GameObject.Root;
		if ( root.IsValid() && root.Network.Owner is not null ) return true;

		return GameObject.Network.Owner is not null;
	}
}
