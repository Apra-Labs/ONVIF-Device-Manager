// Unit tests for the player's keep-alive and reconnect decisions (PlaybackPolicy.hpp).
// A plain console program: prints each check, and exits with the number of failures.

#include "odm.player.lib/PlaybackPolicy.hpp"

#include <cstdio>

using namespace onvifmp::policy;

static int failures = 0;

static void check(bool ok, const char* what){
	printf("%s  %s\n", ok ? "PASS" : "FAIL", what);
	if(!ok){
		++failures;
	}
}

int main(){
	// keep-alive interval: half the session timeout, 60 s when the camera gave none, 5..25 s
	check(KeepAliveInterval(0) == 25, "no session timeout given: 60 s assumed, keep-alive every 25 s (the upper limit)");
	check(KeepAliveInterval(60) == 25, "60 s timeout: every 25 s (half would be 30, over the limit)");
	check(KeepAliveInterval(40) == 20, "40 s timeout: every 20 s");
	check(KeepAliveInterval(30) == 15, "30 s timeout: every 15 s");
	check(KeepAliveInterval(8) == 5, "8 s timeout: every 5 s (the lower limit)");
	check(KeepAliveInterval(1) == 5, "1 s timeout: every 5 s (the lower limit)");
	check(KeepAliveInterval(3600) == 25, "an hour's timeout: every 25 s (the upper limit)");

	// keep-alive request: GET_PARAMETER until the camera refuses it, then OPTIONS
	check(KeepAliveMethod(false) == KeepAlive::GetParameter, "GET_PARAMETER first, even if OPTIONS didn't list it");
	check(KeepAliveMethod(true) == KeepAlive::Options, "OPTIONS once the camera has refused GET_PARAMETER");
	check(!RefusesGetParameter(0), "a reply of 200 (result 0) is no refusal");
	check(RefusesGetParameter(405), "405 Method Not Allowed is a refusal");
	check(RefusesGetParameter(501), "501 Not Implemented is a refusal");
	check(RefusesGetParameter(551), "551 Option not supported is a refusal");
	check(!RefusesGetParameter(401), "401 is about the credentials, not the method");
	check(!RefusesGetParameter(-1), "a network error says nothing about the method");

	// stall: video only, only after video has arrived, 10 s without new video packets
	check(!Stalled(true, 500, 9), "9 s without video: not yet stalled");
	check(Stalled(true, 500, 10), "10 s without video after it had arrived: stalled");
	check(!Stalled(true, 0, 60), "video never arrived (e.g. UDP blocked): not reopened, NO SIGNAL shows");
	check(!Stalled(false, 0, 600), "a metadata-only stream can be silent for long: never stalled");

	// reconnect delay: 2, 4, 8, 16, 32, then 60 s
	const int delays[] = { 2, 4, 8, 16, 32, 60, 60 };
	bool scheduleOk = true;
	for(int attempt = 1; attempt <= 7; ++attempt){
		scheduleOk = scheduleOk && ReconnectDelay(attempt) == delays[attempt - 1];
	}
	check(scheduleOk, "reconnect delays 2, 4, 8, 16, 32, 60, 60 s");
	check(ReconnectDelay(1000) == 60, "the delay stays at 60 s");

	// retry: not after refused credentials
	check(RetryAfter(0), "retry when the camera didn't answer");
	check(RetryAfter(404), "retry after another error");
	check(!RetryAfter(401), "no retry after 401: repeated failed logins can lock the account");

	printf("%d failure(s)\n", failures);
	return failures;
}
