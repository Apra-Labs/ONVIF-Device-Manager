#pragma once

// The player's decisions about keeping an RTSP session alive and opening it again, as plain
// functions with no dependencies, so that they can be unit-tested (odm.player.tests).

namespace onvifmp{
	namespace policy{

		///<summary>Seconds between keep-alives: half the session timeout the camera gave in its
		///SETUP reply (60 s if it gave none), kept between 5 and 25 s.</summary>
		inline int KeepAliveInterval(unsigned sessionTimeout){
			int timeout = sessionTimeout != 0 ? (int)sessionTimeout : 60;
			int half = timeout / 2;
			return half < 5 ? 5 : half > 25 ? 25 : half;
		}

		enum class KeepAlive{ GetParameter, Options };

		///<summary>The keep-alive request to send. GET_PARAMETER, even when the camera's OPTIONS
		///reply doesn't list it: live555 2013 sends OPTIONS without a Session header, so some cameras
		///don't count it as session activity. OPTIONS only once the camera has refused GET_PARAMETER.</summary>
		inline KeepAlive KeepAliveMethod(bool getParameterRefused){
			return getParameterRefused ? KeepAlive::Options : KeepAlive::GetParameter;
		}

		///<summary>Whether a GET_PARAMETER reply means the camera doesn't take it (an RTSP error
		///status such as 405, 501 or 551). live555 reports 0 for success, the RTSP status for an
		///error reply, and a negative value for a network error, which says nothing about the request.
		///401 isn't a refusal of the method.</summary>
		inline bool RefusesGetParameter(int resultCode){
			return resultCode >= 400 && resultCode != 401;
		}

		///<summary>Whether the stream has stalled and should be opened again: only video counts
		///(a metadata-only stream may be silent for a long time), and only once video has arrived in
		///this session (a stream whose RTP never comes, e.g. UDP blocked, shows NO SIGNAL rather than
		///being reopened over and over).</summary>
		inline bool Stalled(bool hasVideo, unsigned videoPacketsReceived, int secondsWithoutVideo){
			return hasVideo && videoPacketsReceived > 0 && secondsWithoutVideo >= 10;
		}

		///<summary>Seconds to wait before attempt number `attempt` (1, 2, ...) to open the stream
		///again: 2, 4, 8, 16, 32, then 60 s.</summary>
		inline int ReconnectDelay(int attempt){
			int delay = 2;
			for(int i = 1; i < attempt && delay < 60; ++i){
				delay *= 2;
			}
			return delay > 60 ? 60 : delay;
		}

		///<summary>Whether to try again after an attempt to open the stream failed with the given
		///RTSP status (0 when there was none, e.g. the camera didn't answer). Not after 401: the
		///credentials are wrong, and repeated failed logins can lock the account on some cameras.</summary>
		inline bool RetryAfter(int rtspStatus){
			return rtspStatus != 401;
		}
	}
}
