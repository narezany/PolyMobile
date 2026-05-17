// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Mobile;
using Polytoria.Shared;

namespace Polytoria.Mobile.UI;

public partial class ViewTestPage : MobileViewBase
{
	private const string LocalBaseplatePath = "res://samples/worlds/baseplate.poly";
	private const string DiscordInviteUrl = "https://discord.gg/SavCzeNTgx";
	private Button LocalPlaceButton = null!;
	private CheckButton ClientScriptsToggle = null!;

	public override void _Ready()
	{
		LocalPlaceButton = GetNode<Button>("LocalPlaceButton");
		ClientScriptsToggle = GetNode<CheckButton>("ClientScriptsToggle");
		GetNode<Button>("RestartApp").Pressed += () =>
		{
			Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
		};
		GetNode<Label>("Version").Text = $"Running v{Globals.AppVersion}";
		LocalPlaceButton.Pressed += LocalPlacePressed;
		ClientScriptsToggle.ButtonPressed = MobileDevSettings.ClientScriptsEnabled;
		ClientScriptsToggle.Toggled += MobileDevSettings.SetClientScriptsEnabled;
		GetNode<Button>("DiscordButton").Pressed += () => OS.ShellOpen(DiscordInviteUrl);
	}

	private void LocalPlacePressed()
	{
		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ClientEntry.ClientEntryData entryData = new()
			{
				TestIsServer = true,
				TestOfflineLocal = true,
				TestWorldPath = LocalBaseplatePath
			};
			ce.Entry(entryData);
		}
	}
}
