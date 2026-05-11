const FacepunchAuthUrl = "https://services.facepunch.com/sbox/auth/token";

export type AuthResult =
	| { ok: true; steamId: string }
	| { ok: false; status: number; message: string };

export function readSteamId( payload: Record<string, unknown> )
{
	const value = payload.steam_id ?? payload.steamId ?? payload.SteamId;
	if ( typeof value === "string" ) return normalizeSteamId( value );
	if ( typeof value === "number" && Number.isSafeInteger( value ) ) return normalizeSteamId( value.toString() );
	return "";
}

export async function validateSboxAuth( req: Request, steamId: string ): Promise<AuthResult>
{
	if ( !isSteamId( steamId ) ) return { ok: false, status: 400, message: "Invalid Steam ID." };

	const token = readSboxAuthToken( req );
	if ( token.length <= 0 ) return { ok: false, status: 401, message: "Missing s&box auth token." };

	const body = `{"steamid":${steamId},"token":${JSON.stringify( token )}}`;
	const response = await fetch( FacepunchAuthUrl, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body,
	} );

	if ( !response.ok ) return { ok: false, status: 401, message: "s&box auth token was rejected." };

	const text = await response.text();
	const status = matchString( text, "status" );
	const returnedSteamId = matchDigits( text, "steamid" );
	if ( status.toLowerCase() !== "ok" || returnedSteamId !== steamId ) {
		return { ok: false, status: 401, message: "s&box auth token does not match Steam ID." };
	}

	return { ok: true, steamId };
}

function readSboxAuthToken( req: Request )
{
	const explicit = req.headers.get( "x-sbox-auth-token" )?.trim();
	if ( explicit ) return explicit;

	const authorization = req.headers.get( "authorization" )?.trim() ?? "";
	if ( authorization.toLowerCase().startsWith( "bearer " ) ) return authorization.slice( 7 ).trim();
	return authorization;
}

function normalizeSteamId( value: string )
{
	return value.trim();
}

function isSteamId( value: string )
{
	return /^[0-9]{15,20}$/.test( value );
}

function matchString( json: string, key: string )
{
	const pattern = new RegExp( `"${key}"\\s*:\\s*"([^"]*)"`, "i" );
	return pattern.exec( json )?.[1] ?? "";
}

function matchDigits( json: string, key: string )
{
	const pattern = new RegExp( `"${key}"\\s*:\\s*"?([0-9]+)"?`, "i" );
	return pattern.exec( json )?.[1] ?? "";
}
