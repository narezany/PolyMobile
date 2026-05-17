// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Touch;

public partial class SprintButton : Button
{
	private Player? LocalPlayer => World.Current?.Players?.LocalPlayer;
	private bool _sprintActionDown = false;

	public override void _Ready()
	{
		FocusMode = FocusModeEnum.None;
		Visible = World.Current?.Input.IsTouchscreen == true;
		ToggleMode = true;
		ActionMode = ActionModeEnum.Press;
		Text = "RUN";
		Pressed += ToggleSprint;
		AddThemeFontSizeOverride("font_size", 18);
	}

	public override void _Process(double delta)
	{
		Player? player = LocalPlayer;
		if (player == null || player.IsDeleted)
		{
			ButtonPressed = false;
			SetSprintAction(false);
			return;
		}

		bool active = ButtonPressed || player.SprintOverride;
		if (ButtonPressed != active)
		{
			SetPressedNoSignal(active);
		}
		player.SprintOverride = active;
		SetSprintAction(active);
		Disabled = player.IsDead || !player.CanMove;
	}

	private void ToggleSprint()
	{
		Player? player = LocalPlayer;
		if (player == null || player.IsDeleted || player.IsDead || !player.CanMove) return;

		bool active = ButtonPressed;
		player.SprintOverride = active;
		SetSprintAction(active);
	}

	private void SetSprintAction(bool pressed)
	{
		if (_sprintActionDown == pressed) return;
		_sprintActionDown = pressed;

		Input.ParseInputEvent(new InputEventAction
		{
			Action = "sprint",
			Pressed = pressed,
			Strength = pressed ? 1.0f : 0.0f
		});
	}
}
