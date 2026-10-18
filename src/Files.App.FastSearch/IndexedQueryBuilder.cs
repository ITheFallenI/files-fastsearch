// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;

namespace Files.App.FastSearch
{
	/// <summary>
	/// Translates a Files search box query into Find My Files query text.
	/// </summary>
	public static class IndexedQueryBuilder
	{
		private static readonly char[] OperatorChars = [' ', '\t', '|', '!', '"', '*', '?'];

		/// <summary>
		/// Returns true when the query uses Files-only syntax (AQS, tags) that the index cannot evaluate.
		/// </summary>
		public static bool IsSupported(string? query)
			=> !string.IsNullOrWhiteSpace(query)
				&& !query.StartsWith('$')
				&& !query.Contains(':', StringComparison.Ordinal);

		/// <summary>
		/// Returns the volume label ("C:") of a local drive path, or null for UNC, device and relative paths.
		/// </summary>
		public static string? GetVolumeLabel(string path)
		{
			if (path.Length < 2 || path[1] != ':' || !char.IsAsciiLetter(path[0]))
				return null;

			if (path.Length > 2 && path[2] is not ('\\' or '/'))
				return null;

			return char.ToUpperInvariant(path[0]) + ":";
		}

		/// <summary>
		/// Normalizes a scope folder to "C:\Folder\" so it only matches entries below it.
		/// </summary>
		public static string? NormalizeScope(string folder)
		{
			if (GetVolumeLabel(folder) is not { } label)
				return null;

			var rest = folder.Length > 2 ? folder[2..].Replace('/', '\\').TrimEnd('\\') : string.Empty;
			if (rest.Contains('"', StringComparison.Ordinal))
				return null;

			return label + rest + "\\";
		}

		/// <summary>
		/// Builds the engine query, scoping every OR group to <paramref name="scope"/> when one is given.
		/// </summary>
		public static string Build(string query, string? scope)
		{
			var text = ApplyFilesWildcardRule(query.Trim());
			if (scope is null)
				return text;

			var scopeTerm = $" path:\"{scope}\"";
			var builder = new StringBuilder(text.Length + scopeTerm.Length * 2);
			var inQuotes = false;
			foreach (var c in text)
			{
				if (c == '"')
					inQuotes = !inQuotes;
				else if (c == '|' && !inQuotes)
					TrimEnd(builder).Append(scopeTerm).Append(' ');

				builder.Append(c);
			}

			return builder.Append(scopeTerm).ToString();
		}

		private static StringBuilder TrimEnd(StringBuilder builder)
		{
			while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
				builder.Length--;

			return builder;
		}

		// Files' own search treats "report.pdf" as "report*.pdf*"; keep that for single plain terms
		private static string ApplyFilesWildcardRule(string query)
		{
			if (query.IndexOfAny(OperatorChars) >= 0 || !query.Contains('.', StringComparison.Ordinal) || query.Contains('\\', StringComparison.Ordinal))
				return query;

			var lastDot = query.LastIndexOf('.');
			return $"*{query[..lastDot]}*{query[lastDot..]}*";
		}
	}
}
