// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Polytoria.Creator;

public static class DeviceLinker
{
	public static string? GetLocalIP()
	{
		// Prefer the outbound interface if the device has any route at all.
		// Fall back to a local IPv4 interface so LAN-only setups still work.
		try
		{
			using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
			socket.Connect("8.8.8.8", 65530);

			if (socket.LocalEndPoint is IPEndPoint endPoint)
			{
				return endPoint.Address.ToString();
			}
		}
		catch
		{
			// Ignore and fall back to interface enumeration below.
		}

		foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (networkInterface.OperationalStatus != OperationalStatus.Up)
			{
				continue;
			}

			foreach (UnicastIPAddressInformation addressInfo in networkInterface.GetIPProperties().UnicastAddresses)
			{
				if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork)
				{
					continue;
				}

				if (IPAddress.IsLoopback(addressInfo.Address))
				{
					continue;
				}

				return addressInfo.Address.ToString();
			}
		}

		return null;
	}

	public static string? GetConnectAddress()
	{
		string? ip = GetLocalIP();
		if (ip == null) return null;
		return $"polytoria://test/{ip}";
	}
}
