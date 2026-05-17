// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Polytoria.Utils;

public static class PolyAPI
{
	private static readonly PTHttpClient _client = new();
	private static readonly Dictionary<string, string> _cookies = [];
	private static string? _csrfToken;
	private static string _csrfSource = "none";
	private static int _lastCsrfFetchStatus = 0;

	public static void SetAuthToken(string userToken)
	{
		// Remove Authorization if exists
		if (_client.DefaultRequestHeaders.ContainsKey("Authorization"))
		{
			_client.DefaultRequestHeaders.Remove("Authorization");
		}
		_client.DefaultRequestHeaders.Add("Authorization", "Bearer " + userToken);
	}

	public static Task<APIUserInfo> GetUserFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString()),
			APIGenerationContext.Default.APIUserInfo
		);
	}

	public static Task<APIFriendsResponse> GetUserFriends(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/users/{userID}/friends"),
			SocialAPIGenerationContext.Default.APIFriendsResponse
		);
	}

	public static Task<APIMeResponse> GetCurrentUser()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/users/me"),
			APIGenerationContext.Default.APIMeResponse
		);
	}

	public static async Task<APIJoinPlaceResponse> RequestJoinGame(APIJoinPlaceRequest req)
	{
		await RefreshCsrfCookies(req.PlaceID);

		string json = BuildJoinGameJson(req);
		using HttpRequestMessage request = new(HttpMethod.Post, Globals.MainEndpoint.PathJoin("/api/places/join"))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
		request.Headers.TryAddWithoutValidation("Origin", Globals.MainEndpoint);
		request.Headers.TryAddWithoutValidation("Referer", Globals.MainEndpoint.PathJoin("/places/" + req.PlaceID.ToString()));
		AddCookiesToRequest(request);
		AddCsrfHeadersToRequest(request);

		using HttpResponseMessage response = await SendAndStoreCookies(request);
		string responseText = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Join game failed: {(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(responseText)} {BuildCsrfDebugInfo()}"
			);
		}

		APIJoinPlaceResponse result = JsonSerializer.Deserialize(
			responseText,
			APIGenerationContext.Default.APIJoinPlaceResponse
		);

		return result;
	}

	private static async Task RefreshCsrfCookies(int placeID)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, Globals.MainEndpoint.PathJoin("/places/" + placeID.ToString()));
		request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
		AddCookiesToRequest(request);

		using HttpResponseMessage response = await SendAndStoreCookies(request);
		_lastCsrfFetchStatus = (int)response.StatusCode;
		string body = await response.Content.ReadAsStringAsync();
		_csrfToken = ExtractCsrfToken(body);
		_csrfSource = _csrfToken != null ? "html" : "none";

		if (_csrfToken == null)
		{
			_csrfToken = GetCsrfTokenFromCookies();
			_csrfSource = _csrfToken != null ? "cookie" : "none";
		}
	}

	private static async Task<HttpResponseMessage> SendAndStoreCookies(HttpRequestMessage request)
	{
		HttpResponseMessage response = await _client.SendAsync(request);
		StoreCookiesFromResponse(response);
		return response;
	}

	private static void AddCookiesToRequest(HttpRequestMessage request)
	{
		if (_cookies.Count == 0)
		{
			return;
		}

		request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(cookie => cookie.Key + "=" + cookie.Value)));
	}

	private static void AddCsrfHeadersToRequest(HttpRequestMessage request)
	{
		string? token = _csrfToken ?? GetCsrfTokenFromCookies();
		if (string.IsNullOrWhiteSpace(token))
		{
			return;
		}

		request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);
		request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
		request.Headers.TryAddWithoutValidation("CSRF-Token", token);
	}

	private static void StoreCookiesFromResponse(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
		{
			return;
		}

		foreach (string setCookie in setCookies)
		{
			string pair = setCookie.Split(';', 2)[0];
			int separator = pair.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}

			string name = pair[..separator].Trim();
			string value = pair[(separator + 1)..].Trim();
			if (!string.IsNullOrWhiteSpace(name))
			{
				_cookies[name] = value;
			}
		}
	}

	private static string TrimBody(string body)
	{
		body = body.Replace("\n", " ").Replace("\r", " ").Trim();
		return body.Length <= 260 ? body : body[..260] + "...";
	}

	private static string BuildJoinGameJson(APIJoinPlaceRequest req)
	{
		string? csrf = _csrfToken ?? GetCsrfTokenFromCookies();
		if (string.IsNullOrWhiteSpace(csrf))
		{
			return JsonSerializer.Serialize(req, APIGenerationContext.Default.APIJoinPlaceRequest);
		}

		return $$"""{"placeID":{{req.PlaceID}},"isBeta":{{req.IsBeta.ToString().ToLowerInvariant()}},"_csrf":{{EncodeJsonString(csrf)}}}""";
	}

	private static string EncodeJsonString(string value)
	{
		StringBuilder builder = new("\"");
		foreach (char c in value)
		{
			switch (c)
			{
				case '"':
					builder.Append("\\\"");
					break;
				case '\\':
					builder.Append("\\\\");
					break;
				case '\b':
					builder.Append("\\b");
					break;
				case '\f':
					builder.Append("\\f");
					break;
				case '\n':
					builder.Append("\\n");
					break;
				case '\r':
					builder.Append("\\r");
					break;
				case '\t':
					builder.Append("\\t");
					break;
				default:
					if (char.IsControl(c))
					{
						builder.Append("\\u");
						builder.Append(((int)c).ToString("x4"));
					}
					else
					{
						builder.Append(c);
					}
					break;
			}
		}

		builder.Append('"');
		return builder.ToString();
	}

	private static string BuildCsrfDebugInfo()
	{
		string tokenState = string.IsNullOrWhiteSpace(_csrfToken) ? "missing" : "present";
		string cookieNames = _cookies.Count == 0 ? "none" : string.Join(",", _cookies.Keys);
		return $"[csrf fetch={_lastCsrfFetchStatus} token={tokenState} source={_csrfSource} cookies={cookieNames}]";
	}

	private static string? GetCsrfTokenFromCookies()
	{
		foreach (string cookieName in new[] { "XSRF-TOKEN", "csrf-token", "csrfToken", "_csrf" })
		{
			if (_cookies.TryGetValue(cookieName, out string? token) && !string.IsNullOrWhiteSpace(token))
			{
				return HttpUtility.UrlDecode(token);
			}
		}

		return null;
	}

	private static string? ExtractCsrfToken(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return null;
		}

		foreach (string pattern in new[]
		{
			"""<meta[^>]+name=["']csrf-token["'][^>]+content=["']([^"']+)["']""",
			"""<meta[^>]+content=["']([^"']+)["'][^>]+name=["']csrf-token["']""",
			"""<input[^>]+name=["']_csrf["'][^>]+value=["']([^"']+)["']""",
			"""<input[^>]+value=["']([^"']+)["'][^>]+name=["']_csrf["']"""
		})
		{
			Match match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return HttpUtility.HtmlDecode(match.Groups[1].Value);
			}
		}

		return null;
	}

	public static Task<APIAvatarResponse> GetUserAvatarFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString() + "/avatar"),
			APIGenerationContext.Default.APIAvatarResponse
		);
	}

	public static Task<APIPlaceInfo> GetWorldFromID(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString()),
			APIGenerationContext.Default.APIPlaceInfo
		);
	}

	public static Task<APIPlaceMedia[]?> GetWorldMedia(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString() + "/media"),
			APIGenerationContext.Default.APIPlaceMediaArray
		);
	}

	public static Task<APIFeedPostRoot> GetFeedPosts(int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/feed?page=" + page.ToString()),
			APIGenerationContext.Default.APIFeedPostRoot
		);
	}

	public static Task<APIWorldsRoot> GetWorlds(string search = "")
	{
		string url = "/api/places";
		if (!string.IsNullOrWhiteSpace(search))
		{
			url += $"?search={HttpUtility.UrlEncode(search)}";
		}
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin(url),
			APIGenerationContext.Default.APIWorldsRoot
		);
	}

	public static Task<APIStoreItem> GetStoreItem(int id)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/store/" + id),
			APIGenerationContext.Default.APIStoreItem
		);
	}

#if CREATOR
	public static Task<APILibraryResponse> GetLibrary(LibraryQueryTypeEnum type, int page = 1, string searchQuery = "")
	{
		string queryType = type switch
		{
			LibraryQueryTypeEnum.Model => "model",
			LibraryQueryTypeEnum.Image => "decal",
			LibraryQueryTypeEnum.Audio => "audio",
			LibraryQueryTypeEnum.Mesh => "mesh",
			LibraryQueryTypeEnum.Addon => "addon",
			_ => ""
		};
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin($"/api/library?page={page}&search={searchQuery}&type={queryType}"),
			APIGenerationContext.Default.APILibraryResponse
		);
	}
#endif

	public static Task<string> GetProfanityList()
	{
		return _client.GetStringAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/profanity"));
	}
}
