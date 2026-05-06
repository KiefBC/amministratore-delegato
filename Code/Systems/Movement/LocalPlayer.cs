using System.Linq;

namespace Sandbox;

public static class LocalPlayer
{
	public static PlayerController Controller( Scene scene )
	{
		if ( scene is null ) return null;

		return scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.IsValid() && !p.IsProxy );
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
