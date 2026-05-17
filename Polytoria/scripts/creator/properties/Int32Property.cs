// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Creator.Properties;

public sealed partial class Int32Property : SpinBox, IProperty<int>
{
	private int _value;

	public new int Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public new event Action<object?>? ValueChanged;

	public new object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		if (value == null) return;
		Value = (int)value;
	}

	public void Refresh()
	{
		SetValueNoSignal(Value);
	}

	public override void _Ready()
	{
		Refresh();

		Godot.Range range = this;

		range.ValueChanged += value =>
		{
			ValueChanged?.Invoke((int)value);
		};
	}
}
