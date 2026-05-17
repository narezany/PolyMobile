// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Mobile.UI;

public partial class WorldsGrid : Control
{
	private const string PlaceCardPath = "res://scenes/mobile/components/shared/place_card.tscn";
	public PackedScene _placeCardPacked = null!;
	private readonly List<PlaceCard> _cards = [];
	private LineEdit? _searchEdit;

	public override void _Ready()
	{
		_placeCardPacked = GD.Load<PackedScene>(PlaceCardPath);
		_searchEdit = GetNodeOrNull<LineEdit>("../../SearchEdit");
		if (_searchEdit != null)
		{
			_searchEdit.TextSubmitted += ApplySearch;
		}
		LoadWorlds();
	}

	private async void LoadWorlds(string search = "")
	{
		MobileUI.Singleton.LoadingScreen.ShowScreen();
		try
		{
			APIWorldsData[] worlds;
			if (PolyMobileAuthAPI.UseOfflineMocks)
			{
				worlds = MobileMockData.Worlds;
			}
			else
			{
				APIWorldsRoot root = await PolyAPI.GetWorlds(search);
				worlds = root.Data;
			}

			foreach (PlaceCard c in _cards) c.QueueFree();
			_cards.Clear();

			foreach (APIWorldsData item in worlds)
			{
				AddPlaceCard(item);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			foreach (PlaceCard c in _cards) c.QueueFree();
			_cards.Clear();
			foreach (APIWorldsData item in MobileMockData.Worlds)
			{
				AddPlaceCard(item);
			}
		}
		MobileUI.Singleton.LoadingScreen.HideScreen();
	}

	private void AddPlaceCard(APIWorldsData item)
	{
		PlaceCard card = _placeCardPacked.Instantiate<PlaceCard>();
		card.PlaceData = item;
		_cards.Add(card);
		AddChild(card);
	}

	private void ApplySearch(string query)
	{
		LoadWorlds(query.Trim());
	}
}
