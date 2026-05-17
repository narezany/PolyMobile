// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Polytoria.Schemas.API;

public struct APIFriendRequest
{
	[JsonPropertyName("userID")]
	public int UserID { get; set; }
	[JsonPropertyName("friendID")]
	public int FriendID { get; set; }
}

public struct APIAreFriendsResponse
{
	[JsonPropertyName("areFriends")]
	public bool AreFriends { get; set; }
}

public struct APIFriendItem
{
	[JsonPropertyName("id")]
	public int ID { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }
}

public struct APIFriendsResponse
{
	[JsonPropertyName("data")]
	public APIFriendItem[] Data { get; set; }
}

[JsonSerializable(typeof(APIFriendRequest))]
[JsonSerializable(typeof(APIAreFriendsResponse))]
[JsonSerializable(typeof(APIFriendsResponse))]
[JsonSerializable(typeof(APIFriendItem))]
[JsonSerializable(typeof(APIFriendItem[]))]
[JsonSerializable(typeof(int))]
internal partial class SocialAPIGenerationContext : JsonSerializerContext { }
