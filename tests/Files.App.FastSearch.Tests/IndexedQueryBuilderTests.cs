// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.FastSearch;

namespace Files.App.FastSearch.Tests
{
	[TestClass]
	public sealed class IndexedQueryBuilderTests
	{
		[DataTestMethod]
		[DataRow("report", true)]
		[DataRow("my report | notes", true)]
		[DataRow("", false)]
		[DataRow("   ", false)]
		[DataRow("$System.Size:>1MB", false)]
		[DataRow("kind:document", false)]
		[DataRow("tag:Red", false)]
		public void IsSupported_RoutesFilesOnlySyntaxToTheRegularSearch(string query, bool expected)
			=> Assert.AreEqual(expected, IndexedQueryBuilder.IsSupported(query));

		[DataTestMethod]
		[DataRow(@"C:\Users", "C:")]
		[DataRow(@"d:\", "D:")]
		[DataRow("e:", "E:")]
		[DataRow(@"\\server\share", null)]
		[DataRow(@"\\?\C:\Users", null)]
		[DataRow("Home", null)]
		[DataRow(@"C:relative", null)]
		public void GetVolumeLabel_AcceptsOnlyLocalDrivePaths(string path, string? expected)
			=> Assert.AreEqual(expected, IndexedQueryBuilder.GetVolumeLabel(path));

		[DataTestMethod]
		[DataRow(@"C:\Users\Me", @"C:\Users\Me\")]
		[DataRow(@"c:\Users\Me\\", @"C:\Users\Me\")]
		[DataRow(@"C:/Users/Me", @"C:\Users\Me\")]
		[DataRow(@"C:\", @"C:\")]
		[DataRow("C:", @"C:\")]
		[DataRow(@"\\server\share", null)]
		public void NormalizeScope_EndsWithASeparatorSoOnlyDescendantsMatch(string folder, string? expected)
			=> Assert.AreEqual(expected, IndexedQueryBuilder.NormalizeScope(folder));

		[TestMethod]
		public void Build_WithoutScope_PassesPlainTermsThrough()
			=> Assert.AreEqual("budget 2026", IndexedQueryBuilder.Build("  budget 2026 ", null));

		[TestMethod]
		public void Build_ScopesEveryOrGroup()
			=> Assert.AreEqual(
				@"a path:""C:\Work\"" | b path:""C:\Work\""",
				IndexedQueryBuilder.Build("a | b", @"C:\Work\"));

		[TestMethod]
		public void Build_DoesNotSplitOnQuotedPipes()
			=> Assert.AreEqual(
				@"""a | b"" path:""C:\Work\""",
				IndexedQueryBuilder.Build("\"a | b\"", @"C:\Work\"));

		[TestMethod]
		public void Build_KeepsFilesDottedTermBehaviour()
			=> Assert.AreEqual("*report*.pdf*", IndexedQueryBuilder.Build("report.pdf", null));

		[TestMethod]
		public void Build_LeavesExplicitWildcardsAlone()
			=> Assert.AreEqual("*.pdf", IndexedQueryBuilder.Build("*.pdf", null));
	}
}
