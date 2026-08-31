using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Cardio.Backend
{
    /// <summary>One HTTP response, reduced to what this project needs from it.</summary>
    public readonly struct BackendResponse
    {
        public readonly long StatusCode;
        public readonly string Body;
        public readonly string Error;

        /// <summary>2xx. Anything else is a failure, including a transport error.</summary>
        public readonly bool Success;

        /// <summary>
        /// True when the request never reached the server, or the server could
        /// not answer.
        ///
        /// Kept separate from a plain failure because the two need opposite
        /// responses: a 4xx means this payload is wrong and retrying it forever
        /// would be a loop, while a transport failure means the payload is
        /// probably fine and the network was not. Only the latter should keep a
        /// row in the offline queue.
        /// </summary>
        public readonly bool IsTransportFailure;

        public BackendResponse(long statusCode, string body, string error, bool isTransportFailure)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            Error = error ?? string.Empty;
            Success = statusCode >= 200 && statusCode < 300 && string.IsNullOrEmpty(error);
            IsTransportFailure = isTransportFailure;
        }

        public static BackendResponse Ok(string body = "") => new BackendResponse(200, body, null, false);
        public static BackendResponse Offline(string reason = "no connection")
            => new BackendResponse(0, string.Empty, reason, true);
    }

    /// <summary>
    /// The HTTP seam.
    ///
    /// Exists so the offline queue can be tested without a network. Queue
    /// behaviour is the part of this phase most likely to be wrong and least
    /// likely to be exercised by hand - nobody unplugs their router while
    /// finishing a level - so it needs a fake that can be told to fail, recover,
    /// and fail again on demand.
    ///
    /// The same pattern <see cref="Cardio.Player.PlayerInputReader"/> uses for
    /// input.
    /// </summary>
    public interface ISupabaseTransport
    {
        IEnumerator Send(string method, string url, string jsonBody,
                         IDictionary<string, string> headers, int timeoutSeconds,
                         Action<BackendResponse> onComplete);
    }

    /// <summary>
    /// The production transport: plain <see cref="UnityWebRequest"/>.
    ///
    /// Supabase is PostgREST and GoTrue over HTTPS, so no SDK is needed and none
    /// is used. That also removes the platform caveat Firebase carried - there
    /// is nothing here that behaves differently on a Windows desktop build than
    /// anywhere else.
    /// </summary>
    public class UnityWebRequestTransport : ISupabaseTransport
    {
        public IEnumerator Send(string method, string url, string jsonBody,
                                IDictionary<string, string> headers, int timeoutSeconds,
                                Action<BackendResponse> onComplete)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                if (!string.IsNullOrEmpty(jsonBody))
                {
                    byte[] payload = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                    request.uploadHandler = new UploadHandlerRaw(payload);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                yield return request.SendWebRequest();

                bool transportFailure = request.result == UnityWebRequest.Result.ConnectionError
                                        || request.result == UnityWebRequest.Result.DataProcessingError;

                onComplete?.Invoke(new BackendResponse(
                    request.responseCode,
                    request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                    request.result == UnityWebRequest.Result.Success ? null : request.error,
                    transportFailure));
            }
        }
    }
}
