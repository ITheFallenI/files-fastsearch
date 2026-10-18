// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.FastSearch
{
	public enum IndexedSearchSort
	{
		Name,
		Size,
		DateModified,
	}

	/// <summary>
	/// A filename search against the native index.
	/// </summary>
	/// <param name="Query">The user's query, in Files' plain filename syntax (Find My Files operators also work).</param>
	/// <param name="Scopes">Folders to search under; empty searches every indexed drive.</param>
	/// <param name="Sort">The engine-side order, which decides which rows survive <paramref name="MaxResults"/>.</param>
	/// <param name="Descending">Whether <paramref name="Sort"/> is descending.</param>
	/// <param name="IncludeHiddenAndSystem">Whether hidden/system entries and everything beneath them are returned.</param>
	/// <param name="MaxResults">The maximum number of rows to return.</param>
	public sealed record IndexedSearchRequest(
		string Query,
		IReadOnlyList<string> Scopes,
		IndexedSearchSort Sort = IndexedSearchSort.Name,
		bool Descending = false,
		bool IncludeHiddenAndSystem = false,
		int MaxResults = int.MaxValue);

	public sealed record IndexedSearchItem(
		string Name,
		string Path,
		bool IsDirectory,
		long Size,
		DateTime? DateModifiedUtc);

	/// <summary>
	/// The index cannot answer this search; the caller should use the regular search instead.
	/// </summary>
	public sealed class IndexedSearchUnavailableException(string message, Exception? innerException = null)
		: Exception(message, innerException);
}
