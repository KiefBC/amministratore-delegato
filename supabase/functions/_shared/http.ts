export async function readJsonObject( req: Request )
{
	try {
		const value = await req.json();
		return value && typeof value === "object" && !Array.isArray( value )
			? value as Record<string, unknown>
			: {};
	} catch {
		return {};
	}
}

export function readString( payload: Record<string, unknown>, ...names: string[] )
{
	for ( const name of names ) {
		const value = payload[name];
		if ( typeof value === "string" ) return value.trim();
	}

	return "";
}

export function jsonResponse( body: Record<string, unknown>, status = 200 )
{
	return new Response( JSON.stringify( body ), {
		status,
		headers: { "content-type": "application/json" },
	} );
}

export function errorResponse( message: string, status = 400 )
{
	return jsonResponse( { ok: false, message }, status );
}

export function unixSeconds( value: string | null | undefined )
{
	if ( !value ) return 0;

	const millis = Date.parse( value );
	if ( !Number.isFinite( millis ) ) return 0;
	return Math.floor( millis / 1000 );
}
