// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DeepLinkAddon;
using Godot;
using Polytoria.Client;
using Polytoria.Mobile.UI;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;

namespace Polytoria.Mobile;

public partial class MobileUI : Control
{
	private const string MobilePortWatermark = "port made by @nrz on polytoria";
	private const string DiscordInviteUrl = "https://discord.gg/SavCzeNTgx";

	public static MobileUI Singleton { get; private set; } = null!;
	public MobileUI()
	{
		Singleton = this;
	}

	public event Action<MobileViewEnum>? ViewPathSwitched;

	private Control _mainView = null!;
	public MobileViewBase? CurrentViewNode;
	public MobileViewEnum CurrentView;

	[Export] public StartupSplash? StartSplash { get; private set; }
	[Export] public NewUserSplash NewUserSplash = null!;
	[Export] public MobileLoadingScreen LoadingScreen = null!;

	private Deeplink _deepLink = new();
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = new();

	public override void _Ready()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		AddChild(_deepLink, true);

		if (Globals.IsMobileBuild)
		{
			GetTree().Root.ContentScaleFactor = Globals.MobileScale;
		}

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		if (StartSplash != null)
		{
			StartSplash!.Visible = true;
		}

		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		PolyMobileAuthAPI.AskForAuthentication += OnAskForAuthentication;

		PolyMobileAuthAPI.SetupClient();
		if (mobileToken != null)
		{
			_ = PolyMobileAuthAPI.LoginWithAuthToken(mobileToken);
		}

		if (mobileCode != null && mobileState != null)
		{
			_ = PolyMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState);
		}

		_deepLink.DeeplinkReceived += OnDeeplinkReceived;
		_deepLink.Initialize();

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
		}

		SwitchTo(MobileViewEnum.Home);
		AddPortWatermark();
		ShowPreviousCrashReportIfAny();
		ShowDisclaimerIfNeeded();
		Callable.From(HandleInitialDeeplink).CallDeferred();
	}

	private void ShowDisclaimerIfNeeded()
	{
		if (MobileDevSettings.DisclaimerAccepted) return;

		Callable.From(() =>
		{
			Control overlay = new()
			{
				MouseFilter = MouseFilterEnum.Stop,
				ZIndex = 8192
			};
			overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

			ColorRect dim = new()
			{
				Color = new Color(0f, 0f, 0f, 0.62f),
				MouseFilter = MouseFilterEnum.Ignore
			};
			dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			overlay.AddChild(dim);

			PanelContainer panel = new()
			{
				CustomMinimumSize = new Vector2(330, 0)
			};
			panel.SetAnchorsPreset(LayoutPreset.Center);
			panel.OffsetLeft = -165;
			panel.OffsetTop = -180;
			panel.OffsetRight = 165;
			panel.OffsetBottom = 180;
			StyleBoxFlat panelStyle = new()
			{
				BgColor = new Color(0.07f, 0.09f, 0.13f),
				BorderColor = new Color(0.23f, 0.35f, 0.48f),
				BorderWidthBottom = 1,
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1
			};
			panelStyle.SetCornerRadiusAll(8);
			panel.AddThemeStyleboxOverride("panel", panelStyle);

			VBoxContainer root = new()
			{
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			root.AddThemeConstantOverride("separation", 14);

			Label title = new()
			{
				Text = "Polytoria Android Port",
				HorizontalAlignment = HorizontalAlignment.Center
			};
			title.AddThemeFontSizeOverride("font_size", 22);

			Label text = new()
			{
				Text = "This is an unofficial experimental port and may violate Polytoria Terms of Service.\n\nPort created by @nrz / narezany.\n\nJoin the Discord server for updates, test builds, and known issues.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				HorizontalAlignment = HorizontalAlignment.Center
			};

			Button discord = new()
			{
				Text = "Join Discord",
				CustomMinimumSize = new Vector2(0, 44)
			};
			discord.AddThemeColorOverride("font_color", Colors.White);
			discord.AddThemeColorOverride("font_hover_color", Colors.White);
			discord.AddThemeColorOverride("font_pressed_color", Colors.White);
			StyleBoxFlat discordStyle = new()
			{
				BgColor = new Color(0.345f, 0.396f, 0.949f)
			};
			discordStyle.SetCornerRadiusAll(8);
			discord.AddThemeStyleboxOverride("normal", discordStyle);
			discord.AddThemeStyleboxOverride("hover", discordStyle);
			discord.AddThemeStyleboxOverride("pressed", discordStyle);
			discord.Pressed += () => OS.ShellOpen(DiscordInviteUrl);

			Button ok = new()
			{
				Text = "I understand",
				CustomMinimumSize = new Vector2(0, 44)
			};
			ok.Pressed += () =>
			{
				MobileDevSettings.SetDisclaimerAccepted(true);
				overlay.QueueFree();
			};

			MarginContainer margin = new()
			{
				OffsetLeft = 18,
				OffsetTop = 18,
				OffsetRight = -18,
				OffsetBottom = -18
			};
			margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			root.AddChild(title);
			root.AddChild(text);
			root.AddChild(discord);
			root.AddChild(ok);
			margin.AddChild(root);
			panel.AddChild(margin);
			overlay.AddChild(panel);
			AddChild(overlay);
		}).CallDeferred();
	}

	private void ShowPreviousCrashReportIfAny()
	{
		string report = CrashReporter.GetPreviousReport();
		if (string.IsNullOrWhiteSpace(report)) return;

		Callable.From(() =>
		{
			Window dialog = new()
			{
				Title = "Previous crash report",
				MinSize = new Vector2I(360, 560),
				Exclusive = true
			};
			VBoxContainer root = new()
			{
				AnchorRight = 1,
				AnchorBottom = 1,
				OffsetLeft = 12,
				OffsetTop = 12,
				OffsetRight = -12,
				OffsetBottom = -12
			};
			HBoxContainer buttons = new();
			Button copyButton = new() { Text = "Copy" };
			Button clearButton = new() { Text = "Clear" };
			Button closeButton = new() { Text = "Close" };
			TextEdit text = new()
			{
				Text = report,
				Editable = false,
				WrapMode = TextEdit.LineWrappingMode.Boundary,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill
			};
			buttons.AddChild(copyButton);
			buttons.AddChild(clearButton);
			buttons.AddChild(closeButton);
			root.AddChild(buttons);
			root.AddChild(text);
			dialog.AddChild(root);
			copyButton.Pressed += () => CrashReporter.CopyToClipboard(report);
			clearButton.Pressed += () =>
			{
				CrashReporter.ClearPreviousReports();
				dialog.Hide();
			};
			closeButton.Pressed += dialog.Hide;
			AddChild(dialog);
			dialog.PopupCenteredRatio(0.9f);
		}).CallDeferred();
	}

	private void AddPortWatermark()
	{
		Label watermark = new()
		{
			Text = MobilePortWatermark,
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 4096,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Modulate = new Color(1f, 1f, 1f, 0.55f)
		};
		watermark.AddThemeFontSizeOverride("font_size", 14);
		watermark.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
		watermark.OffsetLeft = -260;
		watermark.OffsetTop = -28;
		watermark.OffsetRight = -10;
		watermark.OffsetBottom = -8;
		AddChild(watermark, true);
	}

	private void OnUserAuthenticated(APIMeResponse me)
	{
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
	}

	private void OnAskForAuthentication()
	{
		HideStartupSplash();
		if (!Globals.IsInGDEditor)
		{
			NewUserSplash.ShowSplash();
		}
	}

	private void HideStartupSplash()
	{
		if (StartSplash != null)
		{
			StartSplash.HideSplash();
			StartSplash = null;
		}
	}

	private async void OnDeeplinkReceived(DeeplinkURL url)
	{
		// Handle polytoria://auth link
		if (url.Host == "auth")
		{
			NameValueCollection authQuery = HttpUtility.ParseQueryString(url.Query);
			string code = authQuery.Get("code")!;
			string state = authQuery.Get("state")!;

			LoadingScreen.ShowScreen();
			await PolyMobileAuthAPI.LoginWithCodeAndState(code, state);
			LoadingScreen.HideScreen();
			return;
		}

		if (url.Host == "client" || url.Host == "clientbeta")
		{
			string token = url.Path.Trim('/');
			if (!string.IsNullOrEmpty(token)) {
				LaunchGameWithToken(token);
				return;
			}
		}

		if (TryGetPlaceIDFromDeeplink(url, out int placeID))
		{
			LaunchGame(placeID);
		}
	}

	private void HandleInitialDeeplink()
	{
		string initialUrl = _deepLink.GetLinkUrl();
		if (string.IsNullOrWhiteSpace(initialUrl)) return;

		if (Uri.TryCreate(initialUrl, UriKind.Absolute, out Uri? uri))
		{
			if (uri.Host == "client" || uri.Host == "clientbeta")
			{
				string token = uri.AbsolutePath.Trim('/');
				if (!string.IsNullOrEmpty(token)) {
					LaunchGameWithToken(token);
					_deepLink.ClearData();
					return;
				}
			}
		}

		if (TryGetPlaceIDFromUrl(initialUrl, out int placeID))
		{
			LaunchGame(placeID);
			_deepLink.ClearData();
		}
	}

	private static bool TryGetPlaceIDFromUrl(string rawUrl, out int placeID)
	{
		placeID = 0;
		if (Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri))
		{
			string combined = $"{uri.Host}/{uri.AbsolutePath}".Trim('/');
			if (TryGetPlaceIDFromParts(combined, uri.Query.TrimStart('?'), out placeID))
			{
				return true;
			}
		}

		return TryGetPlaceIDFromParts(rawUrl, "", out placeID);
	}

	private static bool TryGetPlaceIDFromDeeplink(DeeplinkURL url, out int placeID)
	{
		placeID = 0;

		string combined = $"{url.Host}/{url.Path}".Trim('/');
		return TryGetPlaceIDFromParts(combined, url.Query, out placeID);
	}

	private static bool TryGetPlaceIDFromParts(string combined, string queryString, out int placeID)
	{
		placeID = 0;
		string[] parts = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);

		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i].Trim();
			if ((part.Equals("places", StringComparison.OrdinalIgnoreCase)
				|| part.Equals("place", StringComparison.OrdinalIgnoreCase)
				|| part.Equals("games", StringComparison.OrdinalIgnoreCase)
				|| part.Equals("game", StringComparison.OrdinalIgnoreCase))
				&& i + 1 < parts.Length
				&& int.TryParse(parts[i + 1], out placeID))
			{
				return true;
			}

			if (int.TryParse(part, out placeID))
			{
				return true;
			}
		}

		NameValueCollection query = HttpUtility.ParseQueryString(queryString);
		return int.TryParse(query.Get("placeId") ?? query.Get("placeID") ?? query.Get("place") ?? query.Get("id"), out placeID);
	}

	public async void LaunchGame(int placeID)
	{
		if (PolyMobileAuthAPI.UseOfflineMocks)
		{
			OS.Alert("Game launch is disabled in the offline Android UI preview.", "Offline Preview");
			return;
		}

		LoadingScreen.ShowScreen();

		try
		{
			APIJoinPlaceResponse res = await PolyAPI.RequestJoinGame(new() { PlaceID = placeID, IsBeta = Globals.IsBetaBuild });
			if (!res.Success)
			{
				throw new Exception(string.IsNullOrWhiteSpace(res.Message) ? "Polytoria rejected the join request." : res.Message);
			}

			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				CrashReporter.StartCrashWatch("online-world-connect");
				ClientEntry.ClientEntryData entryData = new()
				{
					Token = res.Token
				};
				ce.Entry(entryData);
			}
		}
		catch (Exception ex)
		{
			CrashReporter.Report("MobileUI.LaunchGame", ex);
			OS.Alert(ex.Message + "\n\nCrash/report copied if Android allowed it.\nPath: " + CrashReporter.CrashReportGlobalPath, "World join failed");
		}

		LoadingScreen.HideScreen();
	}

	public void LaunchGameWithToken(string token)
	{
		if (PolyMobileAuthAPI.UseOfflineMocks)
		{
			OS.Alert("Game launch is disabled in the offline Android UI preview.", "Offline Preview");
			return;
		}

		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			CrashReporter.StartCrashWatch("online-world-connect");
			ClientEntry.ClientEntryData entryData = new()
			{
				Token = token
			};
			ce.Entry(entryData);
		}
	}

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		if (viewEnum == MobileViewEnum.Web)
		{
			OpenWebPage();
			return;
		}

		if (viewEnum == CurrentView)
		{
			return;
		}

		if (CurrentViewNode != null)
		{
			CurrentViewNode.HideView();
			CurrentViewNode.Visible = false;
		}

		// Check if cached
		if (!_viewCache.TryGetValue(viewEnum, out MobileViewBase? page))
		{
			PT.Print("Loading ", viewEnum);
			string pathToLoad = viewEnum switch
			{
				MobileViewEnum.Home => "res://scenes/mobile/views/home.tscn",
				MobileViewEnum.Worlds => "res://scenes/mobile/views/worlds.tscn",
				MobileViewEnum.PlaceInfo => "res://scenes/mobile/views/place_info.tscn",
				MobileViewEnum.Store => "res://scenes/mobile/views/store_placeholder.tscn",
				MobileViewEnum.Dev => "res://scenes/mobile/views/test.tscn",
				_ => throw new ArgumentOutOfRangeException(nameof(viewEnum),
					 $"No scene defined for {viewEnum}")
			};

			PT.Print("Loading ", viewEnum);

			PackedScene packed = ResourceLoader.Load<PackedScene>(pathToLoad, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
			page = packed.Instantiate<MobileViewBase>();
			_viewCache[viewEnum] = page;
			_mainView.AddChild(page);
			page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		CurrentViewNode = page;
		CurrentView = viewEnum;
		page.ShowView(args);
		page.Visible = true;
		ViewPathSwitched?.Invoke(viewEnum);
	}

	private void OpenWebPage()
	{
		string url = Globals.MainEndpoint;
		string? token = PolyMobileAuthAPI.GetSavedAuthToken();

		// No embedded webview plugin exists in this project, and no browser login-by-token
		// endpoint is present in the available sources, so the mobile port can only hand off
		// to the system browser for now.
		if (!string.IsNullOrWhiteSpace(token))
		{
			PT.Print("Opening Polytoria website from mobile port. Saved token exists, but no supported web login endpoint is available in source.");
		}

		OS.ShellOpen(url);
	}
}

public enum MobileViewEnum
{
	None,
	Home,
	Worlds,
	Web,
	Store,
	Dev,
	PlaceInfo
}
