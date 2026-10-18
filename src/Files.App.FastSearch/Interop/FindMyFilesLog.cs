// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FindMyFiles.Services
{
	/// <summary>
	/// Stands in for the Find My Files app's Serilog-backed <c>FileLog</c> so its pipe client and
	/// service-control sources compile unchanged; lines are routed to the host's <see cref="ILogger"/>.
	/// </summary>
	internal static class FileLog
	{
		private static ILogger logger = NullLogger.Instance;

		internal static void Attach(ILogger hostLogger)
			=> logger = hostLogger;

		public static void Info(string area, string message)
			=> logger.LogInformation("[{Area}] {Message}", area, message);

		public static void Debug(string area, string message)
			=> logger.LogDebug("[{Area}] {Message}", area, message);

		public static void Warn(string area, string message, Exception? ex = null)
			=> logger.LogWarning(ex, "[{Area}] {Message}", area, message);

		public static void Error(string area, string message, Exception? ex = null)
			=> logger.LogError(ex, "[{Area}] {Message}", area, message);

		public static void Event(string area, string message, params (string Key, object Value)[] fields)
			=> Write(LogLevel.Information, area, message, null, fields);

		internal static void WarnEvent(string area, string message, Exception? ex = null, params (string Key, object Value)[] fields)
			=> Write(LogLevel.Warning, area, message, ex, fields);

		internal static void ErrorEvent(string area, string message, Exception? ex = null, params (string Key, object Value)[] fields)
			=> Write(LogLevel.Error, area, message, ex, fields);

		private static void Write(LogLevel level, string area, string message, Exception? ex, (string Key, object Value)[] fields)
		{
			if (!logger.IsEnabled(level))
				return;

			var details = string.Join(' ', fields.Select(f => $"{f.Key}={f.Value}"));
			logger.Log(level, ex, "[{Area}] {Message} {Details}", area, message, details);
		}
	}

	/// <summary>
	/// Stands in for the Find My Files app's fire-and-forget helper, which also posts to its own notification bar.
	/// </summary>
	internal static class TaskExtensions
	{
		public static async void Forget(this Task task, string area)
		{
			try
			{
				await task.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				FileLog.Error(area, "background task failed", ex);
			}
		}
	}
}
