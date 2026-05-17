// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile;

public static class MobileDevSettings
{
	private const string SettingsPath = "user://mobile_dev_settings.cfg";
	private const string Section = "dev";
	private const string ClientScriptsKey = "client_scripts";
	private const string DisclaimerAcceptedKey = "disclaimer_accepted";

	public static bool ClientScriptsEnabled { get; private set; } = true;
	public static bool DisclaimerAccepted { get; private set; } = false;

	static MobileDevSettings()
	{
		Load();
	}

	public static void Load()
	{
		ConfigFile cfg = new();
		Error err = cfg.Load(SettingsPath);
		if (err != Error.Ok)
		{
			ClientScriptsEnabled = true;
			return;
		}

		ClientScriptsEnabled = (bool)cfg.GetValue(Section, ClientScriptsKey, true);
		DisclaimerAccepted = (bool)cfg.GetValue(Section, DisclaimerAcceptedKey, false);
	}

	public static void SetClientScriptsEnabled(bool enabled)
	{
		ClientScriptsEnabled = enabled;

		ConfigFile cfg = new();
		cfg.SetValue(Section, ClientScriptsKey, enabled);
		cfg.SetValue(Section, DisclaimerAcceptedKey, DisclaimerAccepted);
		cfg.Save(SettingsPath);
	}

	public static void SetDisclaimerAccepted(bool accepted)
	{
		DisclaimerAccepted = accepted;

		ConfigFile cfg = new();
		cfg.SetValue(Section, ClientScriptsKey, ClientScriptsEnabled);
		cfg.SetValue(Section, DisclaimerAcceptedKey, accepted);
		cfg.Save(SettingsPath);
	}
}
