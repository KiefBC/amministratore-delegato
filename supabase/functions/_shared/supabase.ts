import { createClient } from "npm:@supabase/supabase-js@2";

export function createAdminClient()
{
	return createClient( requiredEnv( "SUPABASE_URL" ), serviceRoleKey(), {
		auth: {
			autoRefreshToken: false,
			persistSession: false,
		},
	} );
}

function requiredEnv( name: string )
{
	const value = Deno.env.get( name );
	if ( !value ) throw new Error( `Missing ${name}` );
	return value;
}

function serviceRoleKey()
{
	const direct = Deno.env.get( "SUPABASE_SERVICE_ROLE_KEY" ) ?? Deno.env.get( "SERVICE_ROLE_KEY" );
	if ( direct ) return direct;

	const secretKeys = Deno.env.get( "SUPABASE_SECRET_KEYS" );
	if ( secretKeys ) {
		const parsed = JSON.parse( secretKeys ) as Record<string, unknown>;
		for ( const key of ["service_role", "service_role_key", "default"] ) {
			const value = parsed[key];
			if ( typeof value === "string" && value.length > 0 ) return value;
		}
	}

	throw new Error( "Missing Supabase service role key" );
}
