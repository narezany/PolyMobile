// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Polytoria.Shared;

public static class CrashReporter
{
	private const string CrashReportPath = "user://last_crash.txt";
	private const string StateReportPath = "user://last_crash_state.txt";
	private const string WatchPath = "user://crash_watch_active.txt";
	private static bool _installed;
	private static readonly Dictionary<string, string> Breadcrumbs = [];

	public static string CrashReportGlobalPath => ProjectSettings.GlobalizePath(CrashReportPath);
	public static string StateReportGlobalPath => ProjectSettings.GlobalizePath(StateReportPath);

	public static void Install()
	{
		if (_installed) return;
		_installed = true;

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			Report("UnhandledException", args.ExceptionObject as Exception);
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			Report("UnobservedTaskException", args.Exception);
			args.SetObserved();
		};
	}

	public static string Report(string context, Exception? exception)
	{
		string text = BuildCrashReport(context, exception);

		try
		{
			using FileAccess? file = FileAccess.Open(CrashReportPath, FileAccess.ModeFlags.Write);
			file?.StoreString(text);
		}
		catch (Exception writeException)
		{
			GD.PushError("Failed to write crash report: " + writeException);
		}

		try
		{
			DisplayServer.ClipboardSet(text);
		}
		catch (Exception clipboardException)
		{
			GD.PushWarning("Failed to copy crash report: " + clipboardException.Message);
		}

		GD.PushError(text);
		return text;
	}

	public static void SetBreadcrumb(string key, string value)
	{
		Breadcrumbs[key] = value;
		WriteStateSnapshot();
	}

	public static void StartCrashWatch(string context)
	{
		SetBreadcrumb("watch", context);
		try
		{
			using FileAccess? file = FileAccess.Open(WatchPath, FileAccess.ModeFlags.Write);
			file?.StoreString(context);
		}
		catch (Exception ex)
		{
			GD.PushWarning("Failed to start crash watch: " + ex.Message);
		}
	}

	public static void StopCrashWatch()
	{
		DeleteReportFile(WatchPath);
	}

	public static string GetPreviousReport()
	{
		bool hasCrash = FileAccess.FileExists(CrashReportPath);
		bool hasUncleanWatch = FileAccess.FileExists(WatchPath);
		if (!hasCrash && !hasUncleanWatch)
		{
			return "";
		}

		string text = ReadReportFile(CrashReportPath);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = ReadReportFile(StateReportPath);
		}

		return text;
	}

	public static void CopyToClipboard(string text)
	{
		TryCopyToClipboard(text);
	}

	public static void ClearPreviousReports()
	{
		DeleteReportFile(CrashReportPath);
		DeleteReportFile(StateReportPath);
		DeleteReportFile(WatchPath);
	}

	public static string BuildCrashReport(string context, Exception? exception)
	{
		StringBuilder builder = new();
		builder.AppendLine("PT crash");
		builder.AppendLine("ctx=" + context);
		AppendDeviceHeader(builder);
		WriteBreadcrumbs(builder);
		builder.AppendLine();
		builder.AppendLine(exception?.ToString() ?? "No managed exception object was provided.");
		return builder.ToString();
	}

	private static void WriteStateSnapshot()
	{
		try
		{
			using FileAccess? file = FileAccess.Open(StateReportPath, FileAccess.ModeFlags.Write);
			if (file == null) return;

			file.StoreString(BuildStateSnapshot());
		}
		catch (Exception ex)
		{
			GD.PushWarning("Failed to write crash state: " + ex.Message);
		}
	}

	private static string BuildStateSnapshot()
	{
		StringBuilder builder = new();
		builder.AppendLine("PT state");
		AppendDeviceHeader(builder);
		WriteBreadcrumbs(builder);
		return builder.ToString();
	}

	private static void AppendDeviceHeader(StringBuilder builder)
	{
		builder.AppendLine("t=" + DateTime.UtcNow.ToString("HH:mm:ss.fff") + "Z");
		builder.AppendLine("dev=" + OS.GetModelName());
		builder.AppendLine("gpu=" + RenderingServer.GetVideoAdapterName());
	}

	private static string ReadReportFile(string path)
	{
		try
		{
			if (!FileAccess.FileExists(path)) return "";
			using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			return file?.GetAsText() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static void DeleteReportFile(string path)
	{
		try
		{
			if (!FileAccess.FileExists(path)) return;
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
		}
		catch (Exception ex)
		{
			GD.PushWarning("Failed to clear crash report: " + ex.Message);
		}
	}

	private static void TryCopyToClipboard(string text)
	{
		try
		{
			DisplayServer.ClipboardSet(text);
		}
		catch
		{
			// Android may refuse clipboard writes on some devices/builds.
		}
	}

	private static void WriteBreadcrumbs(StringBuilder builder)
	{
		if (Breadcrumbs.Count == 0) return;

		builder.AppendLine();
		foreach ((string key, string value) in Breadcrumbs)
		{
			builder.AppendLine(key + "=" + value);
		}
	}
}
