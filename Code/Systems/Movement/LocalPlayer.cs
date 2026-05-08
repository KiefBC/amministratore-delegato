using System.Linq;

namespace Sandbox.Systems.Movement;

public static class LocalPlayer
{
	public static PlayerController Controller( Scene scene )
	{
		if ( scene is null ) return null;

		var controllers = scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsValid() && p.GameObject.IsValid() && p.GameObject.Active && !p.IsProxy )
			.ToList();

		var local = Connection.Local;
		if ( local is not null )
		{
			var owned = controllers.FirstOrDefault( p => p.GameObject.Network.Owner == local || p.GameObject.Root.Network.Owner == local );
			if ( owned.IsValid() ) return owned;
		}

		return controllers.FirstOrDefault();
	}

	public static GameObject GameObject( Scene scene )
	{
		return Controller( scene )?.GameObject;
	}

	public static bool Owns( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return false;
		if ( !gameObject.Active ) return false;

		var controller = gameObject.Components.GetInAncestorsOrSelf<PlayerController>();
		return controller.IsValid() && !controller.IsProxy;
	}

	public static T Component<T>( Scene scene ) where T : class
	{
		return GameObject( scene )?.Components.GetInDescendantsOrSelf<T>();
	}
}
