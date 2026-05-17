// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Schemas.API;
using System;
using System.Linq;

namespace Polytoria.Mobile.Utils;

public static class MobileMockData
{
	public static readonly APIMeResponse User = new()
	{
		Id = 1,
		Username = "Mobile Tester",
		AvatarID = "",
		MembershipType = "None",
		Description = "Offline Android shell user",
		Signature = ""
	};

	public static readonly APIWorldsData[] Worlds =
	[
		new()
		{
			Id = 1001,
			Name = "Baseplate Mobile Preview",
			Description = "A local placeholder place used to validate the Android UI shell.",
			Genre = "Sandbox",
			CreatorName = "Polytoria",
			Playing = 12,
			Visits = 2450,
			Rating = 0.94
		},
		new()
		{
			Id = 1002,
			Name = "Touch Controls Test",
			Description = "A mock place entry reserved for later client launch testing.",
			Genre = "Testing",
			CreatorName = "Polytoria",
			Playing = 3,
			Visits = 760,
			Rating = 0.88
		},
		new()
		{
			Id = 1003,
			Name = "Avatar Showcase",
			Description = "Offline data for browsing and layout checks.",
			Genre = "Social",
			CreatorName = "Community",
			Playing = 0,
			Visits = 420,
			Rating = null
		}
	];

	public static APIPlaceInfo GetPlaceInfo(int id)
	{
		APIWorldsData world = Worlds.FirstOrDefault(world => world.Id == id);
		if (world.Id == 0)
		{
			world = Worlds[0];
		}

		return new APIPlaceInfo
		{
			Id = world.Id,
			Name = world.Name,
			Description = world.Description,
			Genre = world.Genre,
			Playing = world.Playing,
			Visits = world.Visits,
			MaxPlayers = 12,
			IsActive = true,
			Creator = new APIPlaceCreator
			{
				Id = world.CreatorID,
				Name = string.IsNullOrEmpty(world.CreatorName) ? "Polytoria" : world.CreatorName,
				Type = "User"
			},
			Rating = new APIPlaceRating
			{
				Likes = world.Rating.HasValue ? (int)Math.Round(world.Rating.Value * 100) : 0,
				Dislikes = world.Rating.HasValue ? (int)Math.Round((1 - world.Rating.Value) * 100) : 0,
				Percent = world.Rating.HasValue ? $"{Math.Round(world.Rating.Value * 100)}%" : "--"
			},
			AccessType = "Public",
			CreatedAt = DateTime.UtcNow
		};
	}
}
