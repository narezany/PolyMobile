// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI;

public partial class UIViewLicensesButton : Button
{
	private const string WindowScenePath = "res://scenes/shared/licenses/licenses_window.tscn";
	private Window? _licenseWindow;

	public override void _Pressed()
	{
		if (_licenseWindow == null)
		{
			_licenseWindow = GD.Load<PackedScene>(WindowScenePath).Instantiate<Window>();
			_licenseWindow.CloseRequested += () => { _licenseWindow.QueueFree(); _licenseWindow = null; };
			AddChild(_licenseWindow);
		}
		_licenseWindow.PopupCentered();
		base._Pressed();
	}
}
