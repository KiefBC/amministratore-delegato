using Sandbox;

public enum TeamType
{
	[Icon("account_circle")]
	[Description("The red team is the best team!")]
	Player,
	[Icon("account_circle")]
	[Description("The blue team is the worst team!")]
	Enemy
}

public sealed class UnitComponent : Component
{
	/// <summary>
	/// Displayed name
	/// </summary>
	[Property]
	public string Name { get; set;}
	
	/// <summary>
	/// Which team this unit belongs to
	/// </summary>
	[Property]
	public TeamType Team { get; set;}
	
	/// <summary>
	/// What the max health is
	/// </summary>
	[Property] 
	[Range(10f, 300f)]
	[Step(10f)]
	public float MaxHealth { get; set; } = 100f;
	
	[Property]
	public SkinnedModelRenderer ModelRenderer { get; set; }

	/// <summary>
	/// Optional ModelPhysics component. If assigned, MotionEnabled flips to true on death so the unit ragdolls.
	/// </summary>
	[Property]
	public ModelPhysics Physics { get; set; }

	/// <summary>
	/// Optional PatrolComponent. If assigned, it is disabled on death so the corpse stops walking.
	/// </summary>
	[Property]
	public PatrolComponent Patrol { get; set; }

	private float _health;

	public float Health
	{
		get
		{
			return _health;
		}
		set
		{
			UpdateHealth( value );
		}
	}

	public bool IsDead => _health <= 0f;
	
	[Button("Hurt 10", "success")]
	public void HurtDebug() 
	{
		Damage( MaxHealth );
	}
	
	[Button("Heal 10", "favorite")]
	public void HealDebug() 
	{ 		
	Damage( -10f );
	}

			

	/// <summary>
	/// Apply damage to this unit. This will reduce the health by the specified amount, and trigger the death logic if health reaches 0.
	/// Positive values will hurt the unit, negative values will heal it.
	/// </summary>
	/// <param name="damage"></param>
	public void Damage( float damage )
	{
		Health -= damage;
	}
	
	protected override void OnUpdate()
	{
		 // Nothing
	}

	protected override void OnStart()
	{
		_health = MaxHealth;
	}

	private void UpdateHealth( float newHealth )
	{
		if (!ModelRenderer.IsValid) return;
		if ( IsDead ) return;

		var difference = newHealth - _health;
		if ( difference < 0f )
		{
			// This will convert our health value to a percentage, so we can use it to drive a shader effect
			// We negate the difference because we want to map the damage taken, not the health remaining
			// Then we remap the damage taken to a value between 0 and 100, which is the range our shader expects
			var remappedDamage = MathX.Remap( -difference, 0f, MaxHealth, 100f );
		}
		var remappedHealth = MathX.Remap( Health, 0f, MaxHealth, 100f );
		_health = float.Clamp( newHealth, 0f, MaxHealth );
		Log.Info( $"Health changed by {difference}, new health is {_health}" );

		if ( _health <= 0f )
		{
			Die();
		}
	}

	private void Die()
	{
		Log.Info( $"{Name} died" );

		if ( Physics.IsValid() )
		{
			Physics.MotionEnabled = true;
		}
		else
		{
			Log.Warning( $"{Name}: Physics property is not assigned — cannot ragdoll on death" );
		}

		if ( Patrol.IsValid() )
		{
			Patrol.Enabled = false;
		}
	}
}
