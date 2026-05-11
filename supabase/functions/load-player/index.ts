import { readSteamId, validateSboxAuth } from "../_shared/auth.ts";
import { errorResponse, jsonResponse, readJsonObject, readString } from "../_shared/http.ts";
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
		.rpc( "load_player", {
			p_steam_id: steamId,
			p_display_name: displayName,
		} )
		.single();

	if ( error ) return errorResponse( error.message, 500 );
	const row = data as Record<string, unknown>;

	return jsonResponse( {
		ok: true,
		steam_id: row.steam_id,
		display_name: row.display_name,
		bank_balance: row.bank_balance,
		debt_balance: row.debt_balance,
		next_debt_accrual_at: row.next_debt_accrual_at_unix,
		job_xp: row.job_xp,
		job_completions: row.job_completions,
		last_job_at: row.last_job_at_unix,
	} );
} );
