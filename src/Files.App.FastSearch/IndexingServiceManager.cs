// Copyright (c) Files Community
// Licensed under the MIT License.

using FindMyFiles.Engine;
using FindMyFiles.Services;

namespace Files.App.FastSearch
{
	public enum IndexingServiceStatus
	{
		/// <summary>The indexing service is not registered.</summary>
		NotInstalled,

		/// <summary>A registered service speaks a different protocol and must be set up again.</summary>
		NeedsUpdate,

		/// <summary>The service is registered and stopped; it starts on demand without elevation.</summary>
		Stopped,

		/// <summary>The service is running.</summary>
		Running,

		/// <summary>The service state could not be read.</summary>
		Unknown,
	}

	public enum IndexingServiceActionResult
	{
		Succeeded,
		Cancelled,
		Failed,

		/// <summary>This build carries no verified service binary, so it cannot cross the elevation boundary.</summary>
		NotAvailable,
	}

	/// <summary>
	/// Registers, updates and removes the Find My Files indexing service using its own <c>fmf-service</c> verbs. Each action
	/// needs one administrator approval; the bundled binary is checked against the image digest pinned into this assembly
	/// at release time, and its Authenticode signature, before it is started elevated.
	/// </summary>
	public static class IndexingServiceManager
	{
		/// <summary>
		/// Gets whether this build can register the service: a bundled <c>fmf-service.exe</c> and a pinned image digest.
		/// </summary>
		public static bool CanManageService
			=> ServiceExecutableTrust.IsPinnedDigest(ServiceExecutableTrust.ExpectedImageSha256)
				&& ServiceSetup.LocateServiceExe(AppContext.BaseDirectory) is not null;

		/// <summary>
		/// Reads the service state from the service control manager. Blocking; call off the UI thread.
		/// </summary>
		public static IndexingServiceStatus GetStatus()
			=> ServiceSetup.QueryState() switch
			{
				EngineServiceState.NotInstalled => IndexingServiceStatus.NotInstalled,
				EngineServiceState.Unknown => IndexingServiceStatus.Unknown,
				_ when !ServiceSetup.IsInstalledServiceCompatible() => IndexingServiceStatus.NeedsUpdate,
				EngineServiceState.Running => IndexingServiceStatus.Running,
				_ => IndexingServiceStatus.Stopped,
			};

		/// <summary>
		/// Installs or updates the service for the current user and starts it.
		/// </summary>
		public static Task<IndexingServiceActionResult> InstallOrUpdateAsync(IIndexedSearchEngine engine)
			=> Task.Run(() =>
			{
				if (!TryLocate(out var exe))
					return IndexingServiceActionResult.NotAvailable;

				// The SID binds the pipe to this user even when an administrator approves the prompt over the shoulder
				if (!ServiceSetup.TryCreateSetupArguments(out var arguments))
					return IndexingServiceActionResult.Failed;

				var result = Map(ServiceSetup.RunElevated(exe, arguments));
				if (result is IndexingServiceActionResult.Succeeded)
				{
					ServiceSetup.WaitForCompatibleStartedService(EngineContract.PipeNameShort);
					engine.EnsureStarted();
				}

				return result;
			});

		/// <summary>
		/// Removes the service; with <paramref name="deleteIndex"/>, also its saved index, settings and logs.
		/// </summary>
		public static Task<IndexingServiceActionResult> UninstallAsync(IIndexedSearchEngine engine, bool deleteIndex)
			=> Task.Run(() =>
			{
				if (!TryLocate(out var exe))
					return IndexingServiceActionResult.NotAvailable;

				// Release the pipe first so the service can stop promptly
				engine.Disconnect();
				return Map(ServiceSetup.RunElevated(exe, deleteIndex ? "uninstall --purge-data" : "uninstall"));
			});

		private static bool TryLocate(out string exe)
		{
			exe = string.Empty;
			if (!CanManageService)
				return false;

			exe = ServiceSetup.LocateServiceExe(AppContext.BaseDirectory)!;
			return true;
		}

		private static IndexingServiceActionResult Map(ServiceActionResult result)
			=> result.Outcome switch
			{
				ServiceActionOutcome.Ok => IndexingServiceActionResult.Succeeded,
				ServiceActionOutcome.Cancelled => IndexingServiceActionResult.Cancelled,
				_ => IndexingServiceActionResult.Failed,
			};
	}
}
