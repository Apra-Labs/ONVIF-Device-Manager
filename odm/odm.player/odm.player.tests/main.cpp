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
	check(!RefusesGetParameter(454), "454 Session Not Found (the camera dropped the session) is no refusal");
	check(!RefusesGetParameter(503), "503 Service Unavailable (transient) is no refusal");
	check(!RefusesGetParameter(500), "500 Internal Server Error is no refusal");
	check(!RefusesGetParameter(400), "400 Bad Request is no refusal");

	// the RTSP status of a failed request, from live555's result message
	check(RtspStatusOf("401 Unauthorized") == 401, "\"401 Unauthorized\": 401");
	check(RtspStatusOf("404 Stream Not Found") == 404, "\"404 Stream Not Found\": 404");
	check(RtspStatusOf("503") == 503, "a bare \"503\": 503");
	check(RtspStatusOf("connect() failed: 10061 ") == 0, "a connection error: no status");
	check(RtspStatusOf("Failed to find network address for \"10.0.0.401\"") == 0, "a 401 elsewhere in the message: no status");
	check(RtspStatusOf("4010 x") == 0, "four digits: no status");
	check(RtspStatusOf("") == 0 && RtspStatusOf(nullptr) == 0, "no message: no status");

	// stall: video only, only after video has arrived, 10 s without new video packets
	check(!Stalled(true, 500, 9, false), "9 s without video: not yet stalled");
	check(Stalled(true, 500, 10, false), "10 s without video after it had arrived: stalled");
	check(!Stalled(true, 0, 60, false), "video never arrived (e.g. UDP blocked): not reopened, NO SIGNAL shows");
	check(!Stalled(false, 0, 600, false), "a metadata-only stream can be silent for long: never stalled");
	check(Stalled(true, 0, 10, true), "a reopened session that brings no video, after video had arrived before: stalled again");
	check(Stalled(false, 0, 10, true), "a reopened session whose video couldn't be set up, after video had arrived before: stalled again");
	check(!Stalled(true, 0, 9, true), "a reopened session still has 10 s to bring video");

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

	// SETUP and PLAY: live555's result message after a refused request -> stop, as after DESCRIBE
	check(!RetryAfter(RtspStatusOf("401 Unauthorized")), "SETUP/PLAY answered 401 Unauthorized: no retry");
	check(RetryAfter(RtspStatusOf("454 Session Not Found")), "SETUP/PLAY answered 454: retried");
	check(RetryAfter(RtspStatusOf("connect() failed: 10060 ")), "no answer at SETUP/PLAY: retried");

	printf("%d failure(s)\n", failures);
	return failures;
}
