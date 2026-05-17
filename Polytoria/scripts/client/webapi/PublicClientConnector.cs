// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.WebAPI.Interfaces;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Polytoria.Client.WebAPI;

internal sealed class PublicClientConnector : IClientConnector
{
	private const string LegacyTelemetryPassphrase = "5ZnNWJHc7KmntXxc";
	private readonly PTHttpClient _client = new();
	private string _token = "";
	private static string LastTokenDebug = "not checked";

	private static readonly string[] ConnectEndpoints =
	[
		Globals.ApiEndpoint.PathJoin("/v1/game/client/connect")
	];

	private static readonly string[] StatusEndpoints =
	[
		Globals.ApiEndpoint.PathJoin("/v1/game/client/status")
	];

	public void SetToken(string token)
	{
		_token = token;
		LastTokenDebug = string.IsNullOrWhiteSpace(token) ? "empty" : $"opaque len={token.Length} telemetry=plain-v3";
		CrashReporter.SetBreadcrumb("clientToken", LastTokenDebug);
		if (_client.DefaultRequestHeaders.ContainsKey("Authorization"))
		{
			_client.DefaultRequestHeaders.Remove("Authorization");
		}
	}

	public async Task<APIServerStatus> CheckServerStatus()
	{
		return await TryEndpoints(
			StatusEndpoints,
			async endpoint => await SendJsonRequest<APIServerStatus>(endpoint, HttpMethod.Get),
			"server status"
		);
	}

	public async Task<APIClientAuthResponseMessage> Connect()
	{
		if (TryReadClientAuthFromToken(_token, out APIClientAuthResponseMessage tokenData))
		{
			return tokenData;
		}

		return await TryEndpoints(
			ConnectEndpoints,
			async endpoint => await SendLegacyConnectRequest(endpoint),
			"client connect"
		);
	}

	private async Task<APIClientAuthResponseMessage> SendLegacyConnectRequest(string endpoint)
	{
		using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		request.Headers.TryAddWithoutValidation("Authorization", _token);
		CrashReporter.SetBreadcrumb("lastApiRequest", "POST " + ShortEndpoint(endpoint));

		string requestBody = BuildLegacyConnectBody();
		CrashReporter.SetBreadcrumb("lastApiRequestBody", $"legacy form body, tokenLen={_token.Length}");
		request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(requestBody));
		request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

		using HttpResponseMessage response = await _client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();
		CrashReporter.SetBreadcrumb("lastApiResponse", $"{(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(body)}");
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(DescribeHttpFailure(response, body));
		}

		try
		{
			return JsonSerializer.Deserialize(body, AuthAPIGenerationContext.Default.APIClientAuthResponseMessage);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Invalid JSON response from {endpoint}: {TrimBody(body)}", ex);
		}
	}

	private async Task<T> SendJsonRequest<T>(string url, HttpMethod method)
	{
		using HttpRequestMessage request = new(method, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		CrashReporter.SetBreadcrumb("lastApiRequest", method.Method + " " + ShortEndpoint(url));

		if (method == HttpMethod.Post)
		{
			string requestBody = BuildTokenBody();
			CrashReporter.SetBreadcrumb("lastApiRequestBody", $"redacted token body, tokenLen={_token.Length}");
			request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
		}

		using HttpResponseMessage response = await _client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();
		CrashReporter.SetBreadcrumb("lastApiResponse", $"{(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(body)}");
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(DescribeHttpFailure(response, body));
		}

		try
		{
			return typeof(T) == typeof(APIServerStatus)
				? (T)(object)JsonSerializer.Deserialize(body, AuthAPIGenerationContext.Default.APIServerStatus)
				: (T)(object)JsonSerializer.Deserialize(body, AuthAPIGenerationContext.Default.APIClientAuthResponseMessage);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Invalid JSON response from {url}: {TrimBody(body)}", ex);
		}
	}

	private static async Task<T> TryEndpoints<T>(string[] endpoints, Func<string, Task<T>> request, string action)
	{
		Exception? lastError = null;
		List<string> attempts = [];
		foreach (string endpoint in endpoints)
		{
			try
			{
				return await request(endpoint);
			}
			catch (Exception ex)
			{
				lastError = ex;
				attempts.Add($"{ShortEndpoint(endpoint)} => {TrimBody(ex.Message)}");
				PT.PrintWarn($"Public connector {action} failed at {endpoint}: {ex.Message}");
			}
		}

		throw new InvalidOperationException(
			$"Could not complete Polytoria {action}. Token: {TrimBody(LastTokenDebug)}. {string.Join(" | ", attempts)}",
			lastError
		);
	}

	private static string ShortEndpoint(string endpoint)
	{
		return endpoint
			.Replace(Globals.MainEndpoint, "main:/")
			.Replace(Globals.ApiEndpoint, "api:/");
	}

	private static string TrimBody(string body)
	{
		body = body.Replace("\n", " ").Replace("\r", " ").Trim();
		return body.Length <= 180 ? body : body[..180] + "...";
	}

	private static string DescribeHttpFailure(HttpResponseMessage response, string body)
	{
		int statusCode = (int)response.StatusCode;
		string trimmed = TrimBody(body);

		if (body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || body.Contains("<html", StringComparison.OrdinalIgnoreCase))
		{
			if (body.Contains("cf-mitigated", StringComparison.OrdinalIgnoreCase) || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
			{
				return $"{statusCode} {response.ReasonPhrase}: Cloudflare challenge HTML";
			}

			return $"{statusCode} {response.ReasonPhrase}: HTML response";
		}

		return $"{statusCode} {response.ReasonPhrase}: {trimmed}";
	}

	private string BuildTokenBody()
	{
		CrashReporter.SetBreadcrumb("body", "start");
		if (string.IsNullOrWhiteSpace(_token))
		{
			return "{}";
		}

		string encodedToken = EncodeJsonString(_token);
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		string telemetry = "mobile";
		CrashReporter.SetBreadcrumb("body", $"before-signature ts={timestamp}");
		string signature = CreateSignature(_token, timestamp, telemetry);
		CrashReporter.SetBreadcrumb("body", "after-signature");
		return $$"""{"clientToken":{{encodedToken}},"token":{{encodedToken}},"telemetry":{{EncodeJsonString(telemetry)}},"timestamp":{{timestamp}},"signature":{{EncodeJsonString(signature)}}}""";
	}

	private string BuildLegacyConnectBody()
	{
		CrashReporter.SetBreadcrumb("body", "legacy-start");
		if (string.IsNullOrWhiteSpace(_token))
		{
			return "";
		}

		CrashReporter.SetBreadcrumb("body", "legacy-before-telemetry");
		string telemetryJson = BuildLegacyTelemetryJson();
		CrashReporter.SetBreadcrumb("body", "legacy-before-encrypt");
		string telemetry = EncryptLegacyTelemetry(telemetryJson);
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		CrashReporter.SetBreadcrumb("body", "legacy-before-signature");
		string signature = Convert.ToBase64String(ManagedSha256Bytes(telemetry + timestamp + _token));
		CrashReporter.SetBreadcrumb("body", $"legacy-ready ts={timestamp} telemetryLen={telemetry.Length}");

		return string.Join(
			"&",
			"telemetry=" + Uri.EscapeDataString(telemetry),
			"timestamp=" + Uri.EscapeDataString(timestamp.ToString()),
			"token=" + Uri.EscapeDataString(_token),
			"signature=" + Uri.EscapeDataString(signature)
		);
	}

	private static string EncryptLegacyTelemetry(string telemetryJson)
	{
		try
		{
			byte[] key = ManagedSha256Bytes(LegacyTelemetryPassphrase);
			byte[] plain = Encoding.ASCII.GetBytes(telemetryJson);
			byte[] encrypted = ManagedAesCbcPkcs7Encrypt(plain, key, new byte[16]);
			return Convert.ToBase64String(encrypted);
		}
		catch (Exception ex)
		{
			CrashReporter.Report("PublicClientConnector.EncryptLegacyTelemetry", ex);
			throw;
		}
	}

	private static string BuildLegacyTelemetryJson()
	{
		string deviceModel = string.IsNullOrWhiteSpace(OS.GetModelName()) ? OS.GetName() : OS.GetModelName();
		string gpu = RenderingServer.GetVideoAdapterName();
		string processor = OS.GetProcessorName();
		int processorCount = OS.GetProcessorCount();
		long memoryMb = OS.GetMemoryInfo().TryGetValue("physical", out Variant memory)
			? (long)memory / 1024 / 1024
			: 0;

		return "{"
			+ "\"GameVersion\":" + EncodeJsonString(Globals.AppVersion) + ","
			+ "\"DeviceName\":" + EncodeJsonString(deviceModel) + ","
			+ "\"DeviceModel\":" + EncodeJsonString(deviceModel) + ","
			+ "\"DeviceType\":" + EncodeJsonString(Globals.IsMobileBuild ? "Handheld" : "Desktop") + ","
			+ "\"DeviceUniqueIdentifier\":\"n/a\","
			+ "\"GraphicsDeviceName\":" + EncodeJsonString(gpu) + ","
			+ "\"GraphicsDeviceType\":" + EncodeJsonString(RenderingServer.GetCurrentRenderingDriverName()) + ","
			+ "\"GraphicsMemorySize\":\"0\","
			+ "\"OperatingSystem\":" + EncodeJsonString(OS.GetName() + " " + OS.GetVersion()) + ","
			+ "\"ProcessorCount\":\"" + processorCount + "\","
			+ "\"ProcessorFrequency\":\"0\","
			+ "\"ProcessorType\":" + EncodeJsonString(processor) + ","
			+ "\"SystemMemorySize\":\"" + memoryMb + "\","
			+ "\"Platform\":" + EncodeJsonString(Globals.ResolveCurrentPlatform()) + ","
			+ "\"UnsupportedIdentifier\":\"n/a\","
			+ "\"UnityVersion\":\"6000.3.7f1\""
			+ "}";
	}

	private static string CreateSignature(string token, long timestamp, string telemetry)
	{
		try
		{
			CrashReporter.SetBreadcrumb("signature", "managed-sha256-start");
			string signature = ManagedSha256Hex(token + timestamp.ToString() + telemetry);
			CrashReporter.SetBreadcrumb("signature", "managed-sha256-ok");
			return signature;
		}
		catch (Exception ex)
		{
			CrashReporter.Report("PublicClientConnector.CreateSignature", ex);
			return "";
		}
	}

	private static string ManagedSha256Hex(string value)
	{
		byte[] hash = ManagedSha256Bytes(value);
		StringBuilder builder = new(hash.Length * 2);
		foreach (byte b in hash)
		{
			builder.Append(b.ToString("x2"));
		}

		return builder.ToString();
	}

	private static byte[] ManagedSha256Bytes(string value)
	{
		ReadOnlySpan<byte> input = Encoding.UTF8.GetBytes(value);
		int paddedLength = input.Length + 1 + 8;
		if (paddedLength % 64 != 0)
		{
			paddedLength += 64 - paddedLength % 64;
		}

		byte[] data = new byte[paddedLength];
		input.CopyTo(data);
		data[input.Length] = 0x80;
		ulong bitLength = (ulong)input.Length * 8UL;
		for (int i = 0; i < 8; i++)
		{
			data[paddedLength - 1 - i] = (byte)(bitLength >> (8 * i));
		}

		uint h0 = 0x6a09e667;
		uint h1 = 0xbb67ae85;
		uint h2 = 0x3c6ef372;
		uint h3 = 0xa54ff53a;
		uint h4 = 0x510e527f;
		uint h5 = 0x9b05688c;
		uint h6 = 0x1f83d9ab;
		uint h7 = 0x5be0cd19;

		uint[] w = new uint[64];
		for (int chunk = 0; chunk < data.Length; chunk += 64)
		{
			for (int i = 0; i < 16; i++)
			{
				int offset = chunk + i * 4;
				w[i] = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
			}
			for (int i = 16; i < 64; i++)
			{
				uint s0 = RotateRight(w[i - 15], 7) ^ RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
				uint s1 = RotateRight(w[i - 2], 17) ^ RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
				w[i] = w[i - 16] + s0 + w[i - 7] + s1;
			}

			uint a = h0;
			uint b = h1;
			uint c = h2;
			uint d = h3;
			uint e = h4;
			uint f = h5;
			uint g = h6;
			uint h = h7;

			for (int i = 0; i < 64; i++)
			{
				uint s1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
				uint ch = (e & f) ^ (~e & g);
				uint temp1 = h + s1 + ch + Sha256K[i] + w[i];
				uint s0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
				uint maj = (a & b) ^ (a & c) ^ (b & c);
				uint temp2 = s0 + maj;

				h = g;
				g = f;
				f = e;
				e = d + temp1;
				d = c;
				c = b;
				b = a;
				a = temp1 + temp2;
			}

			h0 += a;
			h1 += b;
			h2 += c;
			h3 += d;
			h4 += e;
			h5 += f;
			h6 += g;
			h7 += h;
		}

		byte[] hash = new byte[32];
		WriteUInt32BigEndian(hash, 0, h0);
		WriteUInt32BigEndian(hash, 4, h1);
		WriteUInt32BigEndian(hash, 8, h2);
		WriteUInt32BigEndian(hash, 12, h3);
		WriteUInt32BigEndian(hash, 16, h4);
		WriteUInt32BigEndian(hash, 20, h5);
		WriteUInt32BigEndian(hash, 24, h6);
		WriteUInt32BigEndian(hash, 28, h7);
		return hash;
	}

	private static void WriteUInt32BigEndian(byte[] target, int offset, uint value)
	{
		target[offset] = (byte)(value >> 24);
		target[offset + 1] = (byte)(value >> 16);
		target[offset + 2] = (byte)(value >> 8);
		target[offset + 3] = (byte)value;
	}

	private static byte[] ManagedAesCbcPkcs7Encrypt(byte[] plain, byte[] key, byte[] iv)
	{
		if (key.Length != 32)
		{
			throw new ArgumentException("AES-256 key must be 32 bytes", nameof(key));
		}
		if (iv.Length != 16)
		{
			throw new ArgumentException("AES IV must be 16 bytes", nameof(iv));
		}

		byte[] roundKeys = ExpandAes256Key(key);
		int pad = 16 - plain.Length % 16;
		byte[] padded = new byte[plain.Length + pad];
		Array.Copy(plain, padded, plain.Length);
		for (int i = plain.Length; i < padded.Length; i++)
		{
			padded[i] = (byte)pad;
		}

		byte[] output = new byte[padded.Length];
		byte[] previous = new byte[16];
		Array.Copy(iv, previous, 16);

		for (int offset = 0; offset < padded.Length; offset += 16)
		{
			byte[] block = new byte[16];
			for (int i = 0; i < 16; i++)
			{
				block[i] = (byte)(padded[offset + i] ^ previous[i]);
			}

			EncryptAesBlock(block, roundKeys);
			Array.Copy(block, 0, output, offset, 16);
			previous = block;
		}

		return output;
	}

	private static byte[] ExpandAes256Key(byte[] key)
	{
		const int expandedLength = 240;
		byte[] expanded = new byte[expandedLength];
		Array.Copy(key, expanded, key.Length);
		int bytesGenerated = key.Length;
		int rconIndex = 1;
		byte[] temp = new byte[4];

		while (bytesGenerated < expandedLength)
		{
			Array.Copy(expanded, bytesGenerated - 4, temp, 0, 4);
			if (bytesGenerated % 32 == 0)
			{
				RotateWord(temp);
				SubWord(temp);
				temp[0] ^= AesRcon[rconIndex++];
			}
			else if (bytesGenerated % 32 == 16)
			{
				SubWord(temp);
			}

			for (int i = 0; i < 4; i++)
			{
				expanded[bytesGenerated] = (byte)(expanded[bytesGenerated - 32] ^ temp[i]);
				bytesGenerated++;
			}
		}

		return expanded;
	}

	private static void EncryptAesBlock(byte[] state, byte[] roundKeys)
	{
		AddRoundKey(state, roundKeys, 0);
		for (int round = 1; round < 14; round++)
		{
			SubBytes(state);
			ShiftRows(state);
			MixColumns(state);
			AddRoundKey(state, roundKeys, round * 16);
		}

		SubBytes(state);
		ShiftRows(state);
		AddRoundKey(state, roundKeys, 14 * 16);
	}

	private static void AddRoundKey(byte[] state, byte[] roundKeys, int offset)
	{
		for (int i = 0; i < 16; i++)
		{
			state[i] ^= roundKeys[offset + i];
		}
	}

	private static void SubBytes(byte[] state)
	{
		for (int i = 0; i < state.Length; i++)
		{
			state[i] = AesSBox[state[i]];
		}
	}

	private static void ShiftRows(byte[] state)
	{
		byte[] copy = new byte[16];
		Array.Copy(state, copy, 16);
		state[0] = copy[0];
		state[4] = copy[4];
		state[8] = copy[8];
		state[12] = copy[12];
		state[1] = copy[5];
		state[5] = copy[9];
		state[9] = copy[13];
		state[13] = copy[1];
		state[2] = copy[10];
		state[6] = copy[14];
		state[10] = copy[2];
		state[14] = copy[6];
		state[3] = copy[15];
		state[7] = copy[3];
		state[11] = copy[7];
		state[15] = copy[11];
	}

	private static void MixColumns(byte[] state)
	{
		for (int c = 0; c < 4; c++)
		{
			int i = c * 4;
			byte a0 = state[i];
			byte a1 = state[i + 1];
			byte a2 = state[i + 2];
			byte a3 = state[i + 3];
			state[i] = (byte)(Gmul2(a0) ^ Gmul3(a1) ^ a2 ^ a3);
			state[i + 1] = (byte)(a0 ^ Gmul2(a1) ^ Gmul3(a2) ^ a3);
			state[i + 2] = (byte)(a0 ^ a1 ^ Gmul2(a2) ^ Gmul3(a3));
			state[i + 3] = (byte)(Gmul3(a0) ^ a1 ^ a2 ^ Gmul2(a3));
		}
	}

	private static byte Gmul2(byte value)
	{
		int shifted = value << 1;
		if ((value & 0x80) != 0)
		{
			shifted ^= 0x1b;
		}

		return (byte)(shifted & 0xff);
	}

	private static byte Gmul3(byte value)
	{
		return (byte)(Gmul2(value) ^ value);
	}

	private static void RotateWord(byte[] word)
	{
		byte first = word[0];
		word[0] = word[1];
		word[1] = word[2];
		word[2] = word[3];
		word[3] = first;
	}

	private static void SubWord(byte[] word)
	{
		for (int i = 0; i < word.Length; i++)
		{
			word[i] = AesSBox[word[i]];
		}
	}

	private static readonly byte[] AesRcon =
	[
		0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1b, 0x36
	];

	private static readonly byte[] AesSBox =
	[
		0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
		0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
		0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
		0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
		0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
		0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
		0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
		0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
		0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
		0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
		0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
		0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
		0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
		0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
		0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
		0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16
	];

	private static uint RotateRight(uint value, int count)
	{
		return (value >> count) | (value << (32 - count));
	}

	private static readonly uint[] Sha256K =
	[
		0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
		0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
		0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
		0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
		0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
		0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
		0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
		0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
	];

#if false
	private static string BuildTelemetryJson()
	{
		string osName = OS.GetName();
		string osVersion = OS.GetVersion();
		string platform = Globals.ResolveCurrentPlatform();
		string renderingMethod = RenderingServer.GetCurrentRenderingMethod();
		string videoAdapter = RenderingServer.GetVideoAdapterName();
		string videoVendor = RenderingServer.GetVideoAdapterVendor();
		string processor = OS.GetProcessorName();
		int processorCount = OS.GetProcessorCount();
		long memoryMb = OS.GetMemoryInfo().TryGetValue("physical", out Variant memory)
			? (long)memory / 1024 / 1024
			: 0;

		return $$"""
		{
			"appVersion":{{EncodeJsonString(Globals.AppVersion)}},
			"platform":{{EncodeJsonString(platform)}},
			"os":{{EncodeJsonString(osName)}},
			"osVersion":{{EncodeJsonString(osVersion)}},
			"deviceModel":{{EncodeJsonString(OS.GetModelName())}},
			"processor":{{EncodeJsonString(processor)}},
			"processorCount":{{processorCount}},
			"memoryMb":{{memoryMb}},
			"renderingMethod":{{EncodeJsonString(renderingMethod)}},
			"videoAdapter":{{EncodeJsonString(videoAdapter)}},
			"videoVendor":{{EncodeJsonString(videoVendor)}},
			"isMobile":{{Globals.IsMobileBuild.ToString().ToLowerInvariant()}},
			"isBeta":{{Globals.IsBetaBuild.ToString().ToLowerInvariant()}}
		}
		""".Replace("\n", "").Replace("\t", "");
	}
#endif

	private static string EncodeJsonString(string value)
	{
		StringBuilder builder = new("\"");
		foreach (char c in value)
		{
			switch (c)
			{
				case '"':
					builder.Append("\\\"");
					break;
				case '\\':
					builder.Append("\\\\");
					break;
				case '\b':
					builder.Append("\\b");
					break;
				case '\f':
					builder.Append("\\f");
					break;
				case '\n':
					builder.Append("\\n");
					break;
				case '\r':
					builder.Append("\\r");
					break;
				case '\t':
					builder.Append("\\t");
					break;
				default:
					if (char.IsControl(c))
					{
						builder.Append("\\u");
						builder.Append(((int)c).ToString("x4"));
					}
					else
					{
						builder.Append(c);
					}
					break;
			}
		}

		builder.Append('"');
		return builder.ToString();
	}

	private static bool TryReadClientAuthFromToken(string token, out APIClientAuthResponseMessage data)
	{
		data = default;
		if (string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		string[] parts = token.Split('.');
		if (parts.Length < 2)
		{
			return false;
		}

		try
		{
			string payload = DecodeBase64Url(parts[1]);
			LastTokenDebug = "jwt keys=" + DescribeJsonKeys(payload);
			using JsonDocument doc = JsonDocument.Parse(payload);
			JsonElement root = doc.RootElement;

			string? ip = GetStringRecursive(root, "ip", "address", "serverIP", "serverIp", "host");
			int? port = GetIntRecursive(root, "port", "serverPort");
			if (string.IsNullOrWhiteSpace(ip) || !port.HasValue)
			{
				LastTokenDebug += " no ip/port";
				return false;
			}

			data = new()
			{
				PlaceName = GetStringRecursive(root, "name", "placeName") ?? "Polytoria",
				IP = ip,
				Port = port.Value,
				WorldID = GetIntRecursive(root, "placeID", "placeId", "worldID", "worldId") ?? 0,
				ServerID = GetIntRecursive(root, "serverID", "serverId") ?? 0
			};
			return true;
		}
		catch (Exception ex)
		{
			LastTokenDebug = "jwt parse failed: " + ex.Message;
			PT.PrintWarn($"Could not read client auth data from token: {ex.Message}");
			return false;
		}
	}

	private static string DescribeJsonKeys(string json)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return doc.RootElement.ValueKind.ToString();
			}

			return string.Join(",", doc.RootElement.EnumerateObject().Select(prop => prop.Name));
		}
		catch
		{
			return "invalid-json";
		}
	}

	private static string DecodeBase64Url(string value)
	{
		string padded = value.Replace('-', '+').Replace('_', '/');
		padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
		return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
	}

	private static string? GetStringRecursive(JsonElement element, params string[] names)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
				{
					return property.Value.ValueKind == JsonValueKind.String
						? property.Value.GetString()
						: property.Value.ToString();
				}

				string? nested = GetStringRecursive(property.Value, names);
				if (!string.IsNullOrWhiteSpace(nested))
				{
					return nested;
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				string? nested = GetStringRecursive(item, names);
				if (!string.IsNullOrWhiteSpace(nested))
				{
					return nested;
				}
			}
		}

		return null;
	}

	private static int? GetIntRecursive(JsonElement element, params string[] names)
	{
		string? value = GetStringRecursive(element, names);
		return int.TryParse(value, out int parsed) ? parsed : null;
	}
}
