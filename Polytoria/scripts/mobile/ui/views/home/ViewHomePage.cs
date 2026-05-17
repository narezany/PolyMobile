using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Utils;
using Polytoria.Shared;
using Godot;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewHomePage : MobileViewBase
{
	[Export] private Label _usernameLabel = null!;
	//private PolytorianModel _polytorian = null!;

	public override void _EnterTree()
	{
		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		//_polytorian.AvatarLoaded += OnAvatarLoaded;

		base._EnterTree();
	}

	public override void _ExitTree()
	{
		PolyMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		//_polytorian.AvatarLoaded -= OnAvatarLoaded;

		base._ExitTree();
	}

	private void OnAvatarLoaded()
	{
		//((Node3D)_polytorian.GDNode).Visible = true;
		//_polytorian.Animator.PlayOneShotAnimation("poly_welcome");
		//_polytorian.SetState(CharacterModel.CharacterState.Idle);
	}

	private void OnUserAuthenticated(APIMeResponse response)
	{
		LoadView();
	}

	private void LoadView()
	{
		_usernameLabel.Text = PolyMobileAuthAPI.CurrentUserInfo.Username;
		//_polytorian.LoadAppearance(PolyMobileAuthAPI.CurrentUserInfo.Id);

		Control friendsNode = GetNodeOrNull<Control>("ScrollContainer/VBoxContainer/PanelContainer/Layout/Friends");
		if (friendsNode != null)
		{
			HBoxContainer friendsContainer = friendsNode.GetNodeOrNull<HBoxContainer>("ScrollContainer/HBoxContainer2");
			if (friendsContainer != null)
			{
				PopulateFriendsList(friendsContainer);
			}
		}
	}

	private async void PopulateFriendsList(HBoxContainer container)
	{
		// Clear existing children
		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}

		PackedScene cardScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/user_headshot_card.tscn");

		if (PolyMobileAuthAPI.UseOfflineMocks)
		{
			// Add offline mockup friends
			uint[] mockUserIDs = { 7348, 31707, 1 };
			foreach (uint id in mockUserIDs)
			{
				UserHeadshotCard card = cardScene.Instantiate<UserHeadshotCard>();
				card.UserID = id;
				container.AddChild(card);
			}
			return;
		}

		try
		{
			APIFriendsResponse response = await PolyAPI.GetUserFriends(PolyMobileAuthAPI.CurrentUserInfo.Id);
			if (response.Data != null)
			{
				foreach (var friend in response.Data)
				{
					UserHeadshotCard card = cardScene.Instantiate<UserHeadshotCard>();
					card.UserID = (uint)friend.ID;
					container.AddChild(card);
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("Failed to load friends list: ", ex);
		}
	}

	public override void ShowView(object? args)
	{
		//((Node3D)_polytorian.GDNode).Visible = false;
		//if (_polytorian.IsAvatarLoaded)
		//{
		//	OnAvatarLoaded();
		//}
		base.ShowView(args);
	}
}
