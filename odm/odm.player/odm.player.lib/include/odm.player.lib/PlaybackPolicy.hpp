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

		///<summary>Whether a GET_PARAMETER reply means the camera doesn't take the method: 405 Method
		///Not Allowed, 501 Not Implemented or 551 Option not supported. live555 reports 0 for success,
		///the RTSP status for an error reply, and a negative value for a network error. Any other error
		///(401, 454 Session Not Found after the camera dropped the session, a transient 5xx) says nothing
		///about the method, and the choice is kept for the rest of the player's life.</summary>
		inline bool RefusesGetParameter(int resultCode){
			return resultCode == 405 || resultCode == 501 || resultCode == 551;
		}

		///<summary>The RTSP status at the start of live555's result message after a failed request
		///(it sets the message to the reply's status line after "RTSP/1.0 ", e.g. "401 Unauthorized"),
		///or 0 when the message isn't a reply (a connection error, a timeout, nothing).</summary>
		inline int RtspStatusOf(const char* resultMsg){
			if(resultMsg == nullptr){
				return 0;
			}
			int status = 0;
			for(int i = 0; i < 3; ++i){
				if(resultMsg[i] < '0' || resultMsg[i] > '9'){
					return 0;
				}
				status = status * 10 + (resultMsg[i] - '0');
			}
			char after = resultMsg[3];
			if(after != '\0' && after != ' ' && after != '\t' && after != '\r' && after != '\n'){
				return 0;
			}
			return status >= 100 && status <= 599 ? status : 0;
		}

		///<summary>Whether the stream has stalled and should be opened again, after 10 s without new
		///video packets. Only video counts (a metadata-only stream may be silent for a long time).
		///Before any video has arrived in this player, a stream whose RTP never comes (e.g. UDP blocked)
		///shows NO SIGNAL rather than being reopened over and over; once video has arrived at least once
		///(videoArrivedBefore, kept across reopenings), a reopened session that brings no video, or
		///whose video subsession couldn't be set up, is reopened again on the backoff schedule.</summary>
		inline bool Stalled(bool hasVideo, unsigned videoPacketsReceived, int secondsWithoutVideo, bool videoArrivedBefore){
			if(secondsWithoutVideo < 10){
				return false;
			}
			return videoArrivedBefore || (hasVideo && videoPacketsReceived > 0);
		}

		///<summary>Seconds to wait before attempt number `attempt` (1, 2, ...) to open the stream
		///again: 2, 4, 8, 16, 32, then 60 s. The count goes back to 1 only once video arrives again,
		///not when a session merely opens.</summary>
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
