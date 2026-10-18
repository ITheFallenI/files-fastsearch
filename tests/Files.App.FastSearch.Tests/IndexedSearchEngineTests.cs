// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.FastSearch;
using FindMyFiles.Engine;
using FindMyFiles.Tests.TestDoubles;

namespace Files.App.FastSearch.Tests
{
	[TestClass]
	public sealed class IndexedSearchEngineTests
	{
		private static readonly long Mtime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc();

		public TestContext TestContext { get; set; } = null!;

		[TestMethod]
		public async Task CanSearch_FollowsVolumeReadiness()
		{
			using var server = new FakePipeServer { Statuses = [new("C:", VolumeState.Ready, 10), new("D:", VolumeState.Scanning, 5)] };
			using var engine = await ConnectAsync(server);

			Assert.IsTrue(engine.CanSearch(@"C:\Users"));
			Assert.IsFalse(engine.CanSearch(@"D:\Data"), "a volume still scanning must use the regular search");
			Assert.IsFalse(engine.CanSearch(@"E:\"), "an unindexed volume must use the regular search");
			Assert.IsFalse(engine.CanSearch(@"\\server\share"));

			await server.SendEventAsync((uint)EventKind.VolumeReady, 5, "D:");
			await WaitUntilAsync(() => engine.CanSearch(@"D:\Data"));
		}

		[TestMethod]
		public async Task Search_StreamsEveryPageAndMapsRows()
		{
			using var server = new FakePipeServer { Rows = MakeRows(150) };
			using var engine = await ConnectAsync(server);

			var pages = await CollectAsync(engine.SearchAsync(new("file", []), TestContext.CancellationToken));

			Assert.AreEqual(150, pages.Sum(p => p.Count));
			Assert.IsTrue(pages.All(p => p.Count <= EngineContract.MaxPageRows));
			var first = pages[0][0];
			Assert.AreEqual("file0.txt", first.Name);
			Assert.AreEqual(@"C:\Work\file0.txt", first.Path);
			Assert.IsFalse(first.IsDirectory);
			Assert.AreEqual(100, first.Size);
			Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), first.DateModifiedUtc);
			Assert.IsTrue(pages[0][1].IsDirectory);
			Assert.AreEqual(server.Received.Count(r => r.Opcode == EngineContract.Op.Query), server.Received.Count(r => r.Opcode == EngineContract.Op.ResultFree), "every result handle is released");
		}

		[TestMethod]
		public async Task Search_StopsAtMaxResults()
		{
			using var server = new FakePipeServer { Rows = MakeRows(500) };
			using var engine = await ConnectAsync(server);

			var pages = await CollectAsync(engine.SearchAsync(new("file", [], MaxResults: 10), TestContext.CancellationToken));

			Assert.AreEqual(10, pages.Sum(p => p.Count));
			Assert.AreEqual(1, server.Received.Count(r => r.Opcode == EngineContract.Op.ResultPage));
		}

		[TestMethod]
		public async Task Search_LeavesOutRowsInFoldersTheUserCannotList()
		{
			var rows = MakeRows(200);
			for (var i = 0; i < rows.Count; i += 4)
				rows[i] = rows[i] with { ParentPath = @"C:\Users\Other\" };

			using var server = new FakePipeServer { Rows = rows };
			var checkedFolders = new List<string>();
			using var engine = await ConnectAsync(server, canListDirectory: path =>
			{
				checkedFolders.Add(path);
				return !path.StartsWith(@"C:\Users\Other", StringComparison.OrdinalIgnoreCase);
			});

			var pages = await CollectAsync(engine.SearchAsync(new("file", [], MaxResults: 120), TestContext.CancellationToken));

			Assert.AreEqual(120, pages.Sum(p => p.Count), "hidden rows must not count toward the result limit");
			Assert.IsFalse(pages.SelectMany(p => p).Any(item => item.Path.StartsWith(@"C:\Users\Other", StringComparison.OrdinalIgnoreCase)));
			Assert.AreEqual(2, checkedFolders.Count, "each folder is checked once per search");
		}

		[TestMethod]
		public async Task Search_SendsScopedCaseInsensitiveQueryPerFolder()
		{
			using var server = new FakePipeServer { Rows = MakeRows(1) };
			using var engine = await ConnectAsync(server);

			await CollectAsync(engine.SearchAsync(
				new("budget", [@"C:\Users\Me\Documents", @"D:\Archive"], IndexedSearchSort.DateModified, Descending: true),
				TestContext.CancellationToken));

			var queries = server.Received
				.Where(r => r.Opcode == EngineContract.Op.Query)
				.Select(r => PipeProtocol.DecodeQueryReq(r.Payload))
				.ToList();
			CollectionAssert.AreEqual(
				new[] { @"budget path:""C:\Users\Me\Documents\""", @"budget path:""D:\Archive\""" },
				queries.Select(q => q.Text).ToArray());
			Assert.IsTrue(queries.All(q => q.Options.Case == FmfCase.Insensitive && q.Options.Sort == FmfSort.Mtime && q.Options.Descending));
		}

		[TestMethod]
		public async Task Search_ReportsRejectedQueriesAsUnavailable()
		{
			using var server = new FakePipeServer
			{
				Handler = (op, _) => op == EngineContract.Op.Query
					? Task.FromResult((EngineContract.Status.QuerySyntax, System.Text.Encoding.UTF8.GetBytes("unclosed quote")))
					: null,
			};
			using var engine = await ConnectAsync(server);

			await Assert.ThrowsExactlyAsync<IndexedSearchUnavailableException>(
				() => CollectAsync(engine.SearchAsync(new("\"open", []), TestContext.CancellationToken)));
		}

		[TestMethod]
		public async Task Search_RejectsAqsAndNonLocalScopesWithoutTouchingTheService()
		{
			using var server = new FakePipeServer { Rows = MakeRows(1) };
			using var engine = await ConnectAsync(server);

			await Assert.ThrowsExactlyAsync<IndexedSearchUnavailableException>(
				() => CollectAsync(engine.SearchAsync(new("kind:music", []), TestContext.CancellationToken)));
			await Assert.ThrowsExactlyAsync<IndexedSearchUnavailableException>(
				() => CollectAsync(engine.SearchAsync(new("song", [@"\\nas\music"]), TestContext.CancellationToken)));
			Assert.IsFalse(server.Received.Any(r => r.Opcode == EngineContract.Op.Query));
		}

		[TestMethod]
		public async Task Search_HonoursCancellation()
		{
			using var server = new FakePipeServer { Rows = MakeRows(1000) };
			using var engine = await ConnectAsync(server);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

			var received = 0;
			await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			{
				await foreach (var page in engine.SearchAsync(new("file", []), cts.Token))
				{
					received += page.Count;
					cts.Cancel();
				}
			});

			Assert.AreEqual(EngineContract.MaxPageRows, received);
		}

		[TestMethod]
		public async Task Disconnect_FallsBackUntilTheServiceIsBack()
		{
			using var server = new FakePipeServer();
			var startRequests = 0;
			using var engine = await ConnectAsync(server, () => Interlocked.Increment(ref startRequests));

			server.HoldAccept();
			server.DisconnectAll();
			await WaitUntilAsync(() => !engine.CanSearch(@"C:\"));
			await WaitUntilAsync(() => Volatile.Read(ref startRequests) > 0);

			server.ReleaseAccept();
			await WaitUntilAsync(() => engine.CanSearch(@"C:\"));
		}

		private static List<RowData> MakeRows(int count)
			=> [.. Enumerable.Range(0, count).Select(i => new RowData(
				(ulong)i,
				(ulong)i,
				i % 2 == 0 ? 100UL : 0UL,
				Mtime,
				i % 2 == 0 ? 0U : EngineContract.RowFlags.Directory,
				i % 2 == 0 ? $"file{i}.txt" : $"folder{i}",
				@"C:\Work\"))];

		private static async Task<IndexedSearchEngine> ConnectAsync(FakePipeServer server, Func<int>? onStart = null, Func<string, bool>? canListDirectory = null)
		{
			var engine = new IndexedSearchEngine(() => new PipeEngineClient(server.PipeName), () => { onStart?.Invoke(); return false; }, canListDirectory);
			engine.EnsureStarted();
			await WaitUntilAsync(() => engine.IsConnected && server.Statuses.Any(s => s.State == VolumeState.Ready && engine.CanSearch(s.Label + "\\")));
			return engine;
		}

		private static async Task<List<IReadOnlyList<IndexedSearchItem>>> CollectAsync(IAsyncEnumerable<IReadOnlyList<IndexedSearchItem>> pages)
		{
			var list = new List<IReadOnlyList<IndexedSearchItem>>();
			await foreach (var page in pages)
				list.Add(page);

			return list;
		}

		private static async Task WaitUntilAsync(Func<bool> condition)
		{
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
					Assert.Fail("condition was not reached in time");

				await Task.Delay(20);
			}
		}
	}
}
