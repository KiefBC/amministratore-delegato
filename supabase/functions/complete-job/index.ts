import { readSteamId, validateSboxAuth } from "../_shared/auth.ts";
import { errorResponse, jsonResponse, readJsonObject, readString } from "../_shared/http.ts";
import { createAdminClient } from "../_shared/supabase.ts";

Deno.serve( async ( req ) => {
	if ( req.method !== "POST" ) return errorResponse( "Method not allowed.", 405 );

	const payload = await readJsonObject( req );
	const steamId = readSteamId( payload );
	const auth = await validateSboxAuth( req, steamId );
	if ( !auth.ok ) return errorResponse( auth.message, auth.status );

	const sessionId = readString( payload, "session_id", "sessionId" );
	const jobId = readString( payload, "job_id", "jobId" );
	const claimId = readString( payload, "claim_id", "claimId" );
	const displayName = readString( payload, "display_name", "displayName" ) || "Player";
	if ( !sessionId ) return errorResponse( "Missing session_id." );
	if ( !jobId ) return errorResponse( "Missing job_id." );
	if ( !claimId ) return errorResponse( "Missing claim_id." );

	const supabase = createAdminClient();
	const { data, error } = await supabase
		.rpc( "complete_job", {
			p_steam_id: steamId,
			p_display_name: displayName,
			p_session_id: sessionId,
			p_job_id: jobId,
			p_claim_id: claimId,
		} )
		.single();

	if ( error ) return errorResponse( error.message, 400 );

	const row = data as Record<string, unknown>;
	const message = typeof row.message === "string" ? row.message : "";
	const rewardMoney = typeof row.reward_money === "number" ? row.reward_money : 0;
	const rewardXp = typeof row.reward_xp === "number" ? row.reward_xp : 0;
	const ok = message === "Job complete." || message === "Duplicate claim ignored.";

	return jsonResponse( {
		ok,
		bank_balance: row.bank_balance,
		debt_balance: row.debt_balance,
		next_debt_accrual_at: row.next_debt_accrual_at_unix,
		job_xp: row.job_xp,
		job_completions: row.job_completions,
		last_job_at: row.last_job_at_unix,
		reward_money: rewardMoney,
		reward_xp: rewardXp,
		message,
	}, ok ? 200 : 429 );
} );
