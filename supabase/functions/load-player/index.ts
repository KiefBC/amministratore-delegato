import { readSteamId, validateSboxAuth } from "../_shared/auth.ts";
import { errorResponse, jsonResponse, readJsonObject, readString, unixSeconds } from "../_shared/http.ts";
import { createAdminClient } from "../_shared/supabase.ts";

Deno.serve( async ( req ) => {
	if ( req.method !== "POST" ) return errorResponse( "Method not allowed.", 405 );

	const payload = await readJsonObject( req );
	const steamId = readSteamId( payload );
	const auth = await validateSboxAuth( req, steamId );
	if ( !auth.ok ) return errorResponse( auth.message, auth.status );

	const displayName = readString( payload, "display_name", "displayName" ) || "Player";
	const supabase = createAdminClient();
	const { data, error } = await supabase
		.from( "players" )
		.upsert( {
			steam_id: steamId,
			display_name: displayName,
		}, { onConflict: "steam_id" } )
		.select( "steam_id, display_name, bank_balance, job_xp, job_completions, last_job_at" )
		.single();

	if ( error ) return errorResponse( error.message, 500 );

	return jsonResponse( {
		ok: true,
		steam_id: data.steam_id,
		display_name: data.display_name,
		bank_balance: data.bank_balance,
		job_xp: data.job_xp,
		job_completions: data.job_completions,
		last_job_at: unixSeconds( data.last_job_at ),
	} );
} );
