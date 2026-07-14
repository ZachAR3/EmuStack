using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SharpCompress.Archives;


public static class SafeArchive
{
	public const int MaximumFileCount = 25_000;
	public const long MaximumEntryBytes = 8L * 1024 * 1024 * 1024;
	public const long MaximumTotalBytes = 20L * 1024 * 1024 * 1024;
	public const long MaximumDownloadBytes = 20L * 1024 * 1024 * 1024;


	public static void Extract(string archivePath, string destinationRoot, CancellationToken cancellationToken = default)
	{
		FsHelpers.EnsureNoReparsePoints(archivePath);
		var root = FsHelpers.CanonicalPath(destinationRoot);
		FsHelpers.RejectDangerousRoot(root);
		FsHelpers.EnsureNoReparsePoints(root);
		Directory.CreateDirectory(root);
		FsHelpers.EnsureNoReparsePoints(root);

		using var archive = ArchiveFactory.OpenArchive(archivePath);
		var entries = new List<(IArchiveEntry Entry, string Destination)>();
		var paths = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal);
		long declaredTotal = 0;

		foreach (var entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entries.Count >= MaximumFileCount)
			{
				throw new InvalidDataException($"Archive exceeds the {MaximumFileCount:N0}-entry limit.");
			}

			var relativePath = ValidateEntryPath(entry.Key);
			if (relativePath.Length == 0)
			{
				continue;
			}

			if (!string.IsNullOrEmpty(entry.LinkTarget))
			{
				throw new InvalidDataException($"Archive links are not allowed: {entry.Key}");
			}

			var destination = FsHelpers.CanonicalPath(Path.Join(root, relativePath));
			if (!FsHelpers.IsPathContainedBy(root, destination))
			{
				throw new InvalidDataException($"Archive entry escapes the destination: {entry.Key}");
			}

			if (!paths.Add(destination))
			{
				throw new InvalidDataException($"Archive contains a duplicate path: {entry.Key}");
			}

			if (!entry.IsDirectory)
			{
				if (entry.Size < 0 || entry.Size > MaximumEntryBytes)
				{
					throw new InvalidDataException($"Archive entry is too large: {entry.Key}");
				}
				declaredTotal = checked(declaredTotal + entry.Size);
				if (declaredTotal > MaximumTotalBytes)
				{
					throw new InvalidDataException("Archive exceeds the uncompressed-size limit.");
				}
			}

			entries.Add((entry, destination));
		}

		long actualTotal = 0;
		foreach (var (entry, destination) in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.IsDirectory)
			{
				FsHelpers.EnsureNoReparsePoints(destination, root);
				Directory.CreateDirectory(destination);
				FsHelpers.EnsureNoReparsePoints(destination, root);
				continue;
			}

			var parent = Path.GetDirectoryName(destination) ?? root;
			FsHelpers.EnsureNoReparsePoints(parent, root);
			Directory.CreateDirectory(parent);
			FsHelpers.EnsureNoReparsePoints(parent, root);
			using var input = entry.OpenEntryStream();
			using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			actualTotal = checked(actualTotal + CopyWithLimit(input, output, entry.Key, cancellationToken));
			if (actualTotal > MaximumTotalBytes)
			{
				throw new InvalidDataException("Archive exceeded the total extraction limit.");
			}
		}
	}


	private static string ValidateEntryPath(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || key.IndexOf('\0') >= 0)
		{
			throw new InvalidDataException("Archive contains an empty or invalid path.");
		}

		var normalized = key.Replace('\\', '/').TrimEnd('/');
		if (normalized.StartsWith('/') || normalized.StartsWith("//") || Path.IsPathRooted(normalized) ||
		    (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
		{
			throw new InvalidDataException($"Archive contains an absolute path: {key}");
		}

		var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			if (part is "." or "..")
			{
				throw new InvalidDataException($"Archive contains traversal: {key}");
			}
			if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
				(OperatingSystem.IsWindows() && (part.EndsWith(' ') || part.EndsWith('.'))))
			{
				throw new InvalidDataException($"Archive contains an invalid filename: {key}");
			}
			if (OperatingSystem.IsWindows() && IsReservedDeviceName(part))
			{
				throw new InvalidDataException($"Archive contains a reserved filename: {key}");
			}
		}

		return Path.Join(parts);
	}


	private static bool IsReservedDeviceName(string part)
	{
		var name = part.TrimEnd(' ', '.');
		var extensionIndex = name.IndexOf('.');
		if (extensionIndex >= 0)
		{
			name = name[..extensionIndex];
		}

		return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
			(name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
				name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsDigit(name[3]));
	}


	private static long CopyWithLimit(Stream input, Stream output, string entryName, CancellationToken cancellationToken)
	{
		var buffer = new byte[128 * 1024];
		long written = 0;
		int read;
		while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			written = checked(written + read);
			if (written > MaximumEntryBytes)
			{
				throw new InvalidDataException($"Archive entry exceeded its extraction limit: {entryName}");
			}
			output.Write(buffer, 0, read);
		}
		return written;
	}
}
