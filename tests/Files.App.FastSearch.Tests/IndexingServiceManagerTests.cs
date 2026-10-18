// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.FastSearch;

namespace Files.App.FastSearch.Tests
{
	[TestClass]
	public sealed class IndexingServiceManagerTests
	{
		[TestMethod]
		public async Task UnpinnedBuild_RefusesToElevateTheBundledService()
		{
			using var engine = new IndexedSearchEngine(() => throw new InvalidOperationException("must not connect"), () => false);

			Assert.IsFalse(IndexingServiceManager.CanManageService);
			Assert.AreEqual(IndexingServiceActionResult.NotAvailable, await IndexingServiceManager.InstallOrUpdateAsync(engine));
			Assert.AreEqual(IndexingServiceActionResult.NotAvailable, await IndexingServiceManager.UninstallAsync(engine, deleteIndex: true));
		}
	}
}
