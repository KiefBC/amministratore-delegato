using Sandbox;

namespace Sandbox.Systems.UI;

/// <summary>
/// Ensures player titles are attached locally for every player object visible in
/// this scene, including players spawned before this client joined.
/// </summary>
public sealed class PlayerTitleSystem : GameObjectSystem<PlayerTitleSystem>
{
	public PlayerTitleSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 70, OnTick, nameof( OnTick ) );
	}

	private void OnTick()
	{
		foreach ( var controller in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !controller.IsValid() ) continue;
			if ( !controller.GameObject.IsValid() || !controller.GameObject.Active ) continue;

			var player = controller.GameObject.Root;
			if ( !player.IsValid() || !player.Active ) continue;
			if ( player.Components.GetInDescendantsOrSelf<PlayerTitleComponent>().IsValid() ) continue;

			var title = player.Components.Create<PlayerTitleComponent>();
			var templateTitle = FindTemplateTitle( controller.GameObject );
			if ( templateTitle.IsValid() ) title.CopySettingsFrom( templateTitle );
		}
	}

	private PlayerTitleComponent FindTemplateTitle( GameObject playerObject )
	{
		var title = playerObject.Root.Components.Get<PlayerTitleComponent>();
		if ( title.IsValid() && title.GameObject != playerObject.Root ) return title;

		var scenePlayer = Scene.GetAllObjects( true )
			.FirstOrDefault( x => x.IsValid() && x.Name == "Player" );
		return scenePlayer?.Components.Get<PlayerTitleComponent>();
	}
}
