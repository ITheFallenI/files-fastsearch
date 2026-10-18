// Copyright (c) Files Community
// Licensed under the MIT License.

using FindMyFiles.Engine;
using FindMyFiles.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Files.App.FastSearch
{
	public interface IIndexedSearchEngine : IDisposable
	{
		/// <summary>
		/// Gets whether the indexing service is connected.
		/// </summary>
		bool IsConnected { get; }

		/// <summary>
		/// Raised from a background thread when indexed content changes on a volume ("C:").
		/// </summary>
		event EventHandler<string>? IndexChanged;

		/// <summary>
		/// Starts connecting to the indexing service in the background, starting the service if it is installed but stopped.
		/// </summary>
		void EnsureStarted();

		/// <summary>
		/// Closes the connection to the indexing service until <see cref="EnsureStarted"/> is called again.
		/// </summary>
		void Disconnect();

		/// <summary>
		/// Gets whether the index is connected and has a completed index for the drive of <paramref name="path"/>.
		/// </summary>
		bool CanSearch(string path);

		/// <summary>
		/// Streams result pages in engine order.
		/// </summary>
		/// <exception cref="IndexedSearchUnavailableException">The index could not answer; nothing more will be yielded.</exception>
		IAsyncEnumerable<IReadOnlyList<IndexedSearchItem>> SearchAsync(IndexedSearchRequest request, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Files' adapter over the Find My Files indexing service. The service runs elevated and indexes NTFS volumes from the
	/// MFT and USN journal; Files stays unelevated and talks to it over the service's authorized-user named pipe, through
	/// Find My Files' own pipe client (which verifies the pipe belongs to the SCM-registered service).
	/// </summary>
	public sealed class IndexedSearchEngine : IIndexedSearchEngine
	{
		private static readonly TimeSpan StartRetryInterval = TimeSpan.FromSeconds(30);

		private readonly Func<IEngineClient> clientFactory;
		private readonly Func<bool> tryStartService;
		private readonly ConcurrentDictionary<string, VolumeState> volumes = new(StringComparer.OrdinalIgnoreCase);
		private readonly Lock gate = new();
		private IEngineClient? client;
		private long lastStartAttemptTicks = long.MinValue;
		private bool disposed;

		public event EventHandler<string>? IndexChanged;

		public bool IsConnected => client?.Connection == EngineConnectionState.Connected;

		public IndexedSearchEngine(ILogger logger)
			: this(static () => new PipeEngineClient(), TryStartInstalledService)
		{
			FileLog.Attach(logger);
		}

		internal IndexedSearchEngine(Func<IEngineClient> clientFactory, Func<bool> tryStartService)
		{
			this.clientFactory = clientFactory;
			this.tryStartService = tryStartService;
		}

		public void EnsureStarted()
		{
			lock (gate)
			{
				if (disposed)
					return;

				if (client is null || client.Connection is EngineConnectionState.Faulted or EngineConnectionState.Unavailable)
				{
					DetachClient();
					client = clientFactory();
					client.VolumeUpdated += Client_VolumeUpdated;
					client.IndexChanged += Client_IndexChanged;
					client.ConnectionChanged += Client_ConnectionChanged;
				}

				if (client.Connection is EngineConnectionState.Connected)
					return;
			}

			RequestServiceStart();
		}

		public void Disconnect()
		{
			lock (gate)
				DetachClient();
		}

		public bool CanSearch(string path)
		{
			if (!IsConnected)
			{
				EnsureStarted();
				return false;
			}

			return IndexedQueryBuilder.GetVolumeLabel(path) is { } label
				&& volumes.TryGetValue(label, out var state)
				&& state is VolumeState.Ready;
		}

		public async IAsyncEnumerable<IReadOnlyList<IndexedSearchItem>> SearchAsync(
			IndexedSearchRequest request,
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(request);

			var engine = client;
			if (engine is null || !IsConnected)
				throw new IndexedSearchUnavailableException("The indexing service is not connected.");

			if (!IndexedQueryBuilder.IsSupported(request.Query))
				throw new IndexedSearchUnavailableException("The query uses syntax the index does not support.");

			string?[] scopes = request.Scopes.Count == 0 ? [null] : request.Scopes.Select(IndexedQueryBuilder.NormalizeScope).ToArray();
			if (scopes.Any(scope => scope is null && request.Scopes.Count > 0))
				throw new IndexedSearchUnavailableException("A search location is not on a local drive.");

			var options = new SearchOptions(
				request.Sort switch
				{
					IndexedSearchSort.Size => FmfSort.Size,
					IndexedSearchSort.DateModified => FmfSort.Mtime,
					_ => FmfSort.Name,
				},
				request.Descending,
				FmfCase.Insensitive,
				request.IncludeHiddenAndSystem);

			var remaining = request.MaxResults;
			foreach (var scope in scopes)
			{
				var query = IndexedQueryBuilder.Build(request.Query, scope);
				var offset = 0L;
				var restarted = false;
				ISearchResult? result = null;
				try
				{
					while (remaining > 0)
					{
						IReadOnlyList<RowData>? rows = null;
						try
						{
							result ??= (await RunQueryAsync(engine, query, options, cancellationToken).ConfigureAwait(false)).Result;
							if (offset >= result.Count)
								break;

							var count = (int)Math.Min(Math.Min(EngineContract.MaxPageRows, remaining), result.Count - offset);
							rows = await result.GetRangeAsync(offset, count, cancellationToken).ConfigureAwait(false);
						}
						catch (StaleResultException) when (!restarted && offset == 0)
						{
							// The index was rebuilt between the query and the first page; one fresh query recovers it
							restarted = true;
							result?.Dispose();
							result = null;
							continue;
						}
						catch (Exception ex) when (ex is StaleResultException or EngineUnavailableException or EngineException)
						{
							throw new IndexedSearchUnavailableException("The indexing service stopped answering.", ex);
						}

						if (rows.Count == 0)
							break;

						var page = new IndexedSearchItem[rows.Count];
						for (var i = 0; i < rows.Count; i++)
							page[i] = ToItem(rows[i]);

						offset += rows.Count;
						remaining -= rows.Count;
						yield return page;
					}
				}
				finally
				{
					result?.Dispose();
				}

				if (remaining <= 0)
					yield break;
			}
		}

		private static async Task<SearchOutcome> RunQueryAsync(IEngineClient engine, string query, SearchOptions options, CancellationToken cancellationToken)
		{
			try
			{
				return await engine.SearchAsync(query, options, cancellationToken).ConfigureAwait(false);
			}
			catch (QuerySyntaxException ex)
			{
				throw new IndexedSearchUnavailableException("The index rejected the query.", ex);
			}
			catch (Exception ex) when (ex is EngineUnavailableException or EngineException)
			{
				throw new IndexedSearchUnavailableException("The indexing service stopped answering.", ex);
			}
		}

		internal static IndexedSearchItem ToItem(RowData row)
			=> new(
				row.Name,
				row.FullPath,
				row.IsDirectory,
				(long)Math.Min(row.Size, long.MaxValue),
				row.Mtime > 0 && row.Mtime < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(row.Mtime) : null);

		private void RequestServiceStart()
		{
			var now = Environment.TickCount64;
			var last = Interlocked.Read(ref lastStartAttemptTicks);
			if (last != long.MinValue && now - last < StartRetryInterval.TotalMilliseconds)
				return;

			if (Interlocked.CompareExchange(ref lastStartAttemptTicks, now, last) != last)
				return;

			_ = Task.Run(() =>
			{
				try
				{
					tryStartService();
				}
				catch (Exception ex)
				{
					FileLog.Warn("fastsearch", "could not start the indexing service", ex);
				}
			});
		}

		private static bool TryStartInstalledService()
		{
			// Starting needs only the SERVICE_START right the installer grants the user, so no elevation prompt appears
			if (ServiceSetup.QueryState() is not EngineServiceState.Stopped)
				return false;

			return ServiceSetup.TryStartUnelevated();
		}

		private void Client_VolumeUpdated(VolumeStatus status)
			=> volumes[status.Label] = status.State;

		private void Client_IndexChanged(string volume)
			=> IndexChanged?.Invoke(this, volume);

		private void Client_ConnectionChanged(EngineConnectionState state)
		{
			if (state is EngineConnectionState.Connected)
				return;

			// Readiness is re-announced by the handshake after a reconnect
			volumes.Clear();
			if (state is EngineConnectionState.Reconnecting)
				RequestServiceStart();
		}

		private void DetachClient()
		{
			if (client is null)
				return;

			client.VolumeUpdated -= Client_VolumeUpdated;
			client.IndexChanged -= Client_IndexChanged;
			client.ConnectionChanged -= Client_ConnectionChanged;
			client.Dispose();
			client = null;
			volumes.Clear();
		}

		public void Dispose()
		{
			lock (gate)
			{
				disposed = true;
				DetachClient();
			}
		}
	}
}
