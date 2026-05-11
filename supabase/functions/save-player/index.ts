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
	const sessionId = readString( payload, "session_id", "sessionId" );
	const checkpointId = readString( payload, "checkpoint_id", "checkpointId" );
	const reason = readString( payload, "reason" ) || "checkpoint";
	const snapshot = readSnapshot( payload );

	if ( !sessionId ) return errorResponse( "Missing session_id." );
	if ( !checkpointId ) return errorResponse( "Missing checkpoint_id." );
	if ( !snapshot ) return errorResponse( "Invalid snapshot." );

	const supabase = createAdminClient();
	const { data, error } = await supabase
		.rpc( "save_player_checkpoint", {
			p_steam_id: steamId,
			p_display_name: displayName,
			p_session_id: sessionId,
			p_checkpoint_id: checkpointId,
			p_reason: reason,
			p_snapshot: snapshot,
			p_player_level: readInteger( payload, "player_level", "playerLevel" ),
			p_business_level: readInteger( payload, "business_level", "businessLevel" ),
			p_businesses_owned: readInteger( payload, "businesses_owned", "businessesOwned" ),
			p_net_worth: readInteger( payload, "net_worth", "netWorth" ),
			p_credit_score: readInteger( payload, "credit_score", "creditScore" ),
			p_session_seconds_delta: readInteger( payload, "session_seconds_delta", "sessionSecondsDelta" ),
		} )
		.single();

	if ( error ) return errorResponse( error.message, 400 );

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
		player_snapshot: row.player_snapshot,
		save_version: row.save_version,
		last_checkpoint_at: row.last_checkpoint_at_unix,
		player_level: row.player_level,
		business_level: row.business_level,
		businesses_owned: row.businesses_owned,
		net_worth: row.net_worth,
		credit_score: row.credit_score,
		hours_played_seconds: row.hours_played_seconds,
		message: row.message,
	} );
} );

function readSnapshot( payload: Record<string, unknown> )
{
	const direct = payload.snapshot ?? payload.player_snapshot ?? payload.playerSnapshot;
	if ( direct && typeof direct === "object" && !Array.isArray( direct ) ) return direct as Record<string, unknown>;

	const json = readString( payload, "snapshot_json", "snapshotJson" );
	if ( !json ) return null;

	try {
		const parsed = JSON.parse( json );
		return parsed && typeof parsed === "object" && !Array.isArray( parsed )
			? parsed as Record<string, unknown>
			: null;
	} catch {
		return null;
	}
}

function readInteger( payload: Record<string, unknown>, ...names: string[] )
{
	for ( const name of names ) {
		const value = payload[name];
		if ( typeof value === "number" && Number.isSafeInteger( value ) ) return value;
		if ( typeof value === "string" ) {
			const parsed = Number.parseInt( value, 10 );
			if ( Number.isSafeInteger( parsed ) ) return parsed;
		}
	}

	return 0;
}
