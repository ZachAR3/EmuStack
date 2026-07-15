using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;


var tests = new (string Name, Action Run)[]
{
	("path containment rejects sibling prefixes and equality", PathContainment),
	("owned deletion refuses unmanaged directories", OwnedDeletion),
	("owned deletion refuses filesystem roots", OwnedDeletionRoot),
	("owned deletion refuses symlink traversal", SymlinkDeletion),
	("archive extraction rejects traversal and absolute paths", ArchiveTraversal),
	("archive extraction commits valid nested files", ArchiveValid),
	("atomic text writes retain the previous value", AtomicWrite),
	("atomic writes reject backup links", AtomicBackupLink),
	("nested links are rejected before moves", NestedLinkMove),
	("archive source links are rejected", ArchiveSourceLink),
	("transactional copy rejects overlapping roots", CopyOverlap),
	("backup snapshot and restore merge are recoverable", BackupRoundTrip),
	("copy transactions recover an interrupted swap", CopyRecovery),
	("copy recovery rolls back an uncommitted destination", CopyRecoveryRollback),
	("copy recovery preserves a pre-existing staging destination", CopyRecoveryPreservesStagingDestination),
	("copy recovery removes a fresh destination before its phase write", CopyRecoveryRemovesFreshStagingDestination),
	("copy recovery preserves an unowned destination", CopyRecoveryRefusesUnownedDestination),
	("copy cancellation leaves the existing destination unchanged", CopyCancellation),
	("swap recovery removes only fresh uncommitted targets", SwapRecoveryDecision),
	("mod paths use stable identities and ownership markers", ModIdentity),
	("provider registry preserves legacy aliases", ProviderAliases),
	("Eden discovery selects the matching official asset", EdenReleaseDiscovery),
	("Ryubing discovery accepts a direct update-server release", RyubingReleaseDiscovery),
	("Ryubing discovery falls back on malformed update JSON", RyubingMalformedUpdateResponse),
	("title parsing skips malformed rows and decodes names", TitleParsing),
	("scene signal methods remain bound to existing C# methods", SceneSignalContracts),
	("unsupported operating systems fail explicitly", UnsupportedOs),
	("custom provider versions reject path syntax", CustomVersionValidation),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
	try
	{
		run();
	}
	catch (Exception error)
	{
		failures.Add($"{name}: {error.Message}");
	}
}

if (failures.Count > 0)
{
	Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
	return 1;
}

Console.WriteLine($"Passed {tests.Length} safety tests.");
return 0;


static void PathContainment()
{
	using var fixture = new Fixture();
	var root = Path.Join(fixture.Root, "mods");
	Assert(FsHelpers.IsPathContainedBy(root, Path.Join(root, "game", "mod")), "nested path should be contained");
	Assert(!FsHelpers.IsPathContainedBy(root, root), "root equality should be rejected by default");
	Assert(!FsHelpers.IsPathContainedBy(root, root + "-other", true), "sibling prefix must not be contained");
	Assert(!FsHelpers.IsPathContainedBy(root, Path.Join(root, "..", "outside")), "traversal must not be contained");
}


static void OwnedDeletion()
{
	using var fixture = new Fixture();
	var parent = Path.Join(fixture.Root, "parent");
	var target = Path.Join(parent, "target");
	Directory.CreateDirectory(target);
	File.WriteAllText(Path.Join(target, "user.txt"), "keep");
	AssertThrows<InvalidOperationException>(() => FsHelpers.DeleteOwnedDirectory(target, parent, "owner"));
	Assert(File.Exists(Path.Join(target, "user.txt")), "unowned file was deleted");
	FsHelpers.MarkOwnedDirectory(target, "owner");
	FsHelpers.DeleteOwnedDirectory(target, parent, "owner");
	Assert(!Directory.Exists(target), "owned directory was not deleted");
}


static void SymlinkDeletion()
{
	if (OperatingSystem.IsWindows())
	{
		return;
	}

	using var fixture = new Fixture();
	var parent = Path.Join(fixture.Root, "parent");
	var target = Path.Join(parent, "target");
	var external = Path.Join(fixture.Root, "external");
	Directory.CreateDirectory(target);
	Directory.CreateDirectory(external);
	File.WriteAllText(Path.Join(external, "keep.txt"), "keep");
	FsHelpers.MarkOwnedDirectory(target, "owner");
	File.WriteAllText(Path.Join(target, "before-link.txt"), "keep");
	Directory.CreateSymbolicLink(Path.Join(target, "link"), external);
	AssertThrows<IOException>(() => FsHelpers.DeleteOwnedDirectory(target, parent, "owner"));
	Assert(File.Exists(Path.Join(external, "keep.txt")), "symlink target was modified");
	Assert(File.Exists(Path.Join(target, "before-link.txt")), "preflight did not prevent partial deletion");
	Directory.Delete(Path.Join(target, "link"));
}


static void OwnedDeletionRoot()
{
	var root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
	AssertThrows<InvalidOperationException>(() => FsHelpers.DeleteOwnedDirectoryContents(root!, "owner"));
}


static void ArchiveTraversal()
{
	using var fixture = new Fixture();
	foreach (var maliciousName in new[] { "../escape.txt", "..\\escape.txt", "/absolute.txt", "C:/drive.txt", "//server/share.txt", "folder/../../escape.txt" })
	{
		var archive = Path.Join(fixture.Root, Guid.NewGuid() + ".zip");
		CreateZip(archive, (maliciousName, "bad"));
		var destination = Path.Join(fixture.Root, Guid.NewGuid().ToString("N"));
		AssertThrows<InvalidDataException>(() => SafeArchive.Extract(archive, destination));
		Assert(!File.Exists(Path.Join(fixture.Root, "escape.txt")), "archive escaped the destination");
	}
}


static void ArchiveValid()
{
	using var fixture = new Fixture();
	var archive = Path.Join(fixture.Root, "valid.zip");
	CreateZip(archive, ("nested/file.txt", "content"), ("empty.txt", ""));
	var destination = Path.Join(fixture.Root, "out");
	SafeArchive.Extract(archive, destination);
	Assert(File.ReadAllText(Path.Join(destination, "nested", "file.txt")) == "content", "valid content mismatch");
	Assert(File.Exists(Path.Join(destination, "empty.txt")), "extensionless/empty files must be extracted");
}


static void AtomicWrite()
{
	using var fixture = new Fixture();
	var path = Path.Join(fixture.Root, "state.json");
	FsHelpers.AtomicWriteText(path, "one");
	FsHelpers.AtomicWriteText(path, "two");
	Assert(File.ReadAllText(path) == "two", "new value was not committed");
	Assert(File.ReadAllText(path + ".bak") == "one", "backup did not retain previous value");
}


static void AtomicBackupLink()
{
	if (OperatingSystem.IsWindows())
	{
		return;
	}

	using var fixture = new Fixture();
	var path = Path.Join(fixture.Root, "state.json");
	var external = Path.Join(fixture.Root, "external.txt");
	FsHelpers.AtomicWriteText(path, "one");
	File.WriteAllText(external, "outside");
	File.CreateSymbolicLink(path + ".bak", external);
	AssertThrows<IOException>(() => FsHelpers.AtomicWriteText(path, "two"));
	Assert(File.ReadAllText(path) == "one", "failed atomic write changed the destination");
	Assert(File.ReadAllText(external) == "outside", "backup link target was modified");
	var brokenDestination = Path.Join(fixture.Root, "broken.json");
	File.CreateSymbolicLink(brokenDestination, Path.Join(fixture.Root, "missing.json"));
	AssertThrows<IOException>(() => FsHelpers.AtomicWriteText(brokenDestination, "blocked"));
}


static void NestedLinkMove()
{
	if (OperatingSystem.IsWindows())
	{
		return;
	}

	using var fixture = new Fixture();
	var source = Path.Join(fixture.Root, "source");
	var nested = Path.Join(source, "nested");
	var external = Path.Join(fixture.Root, "external");
	var target = Path.Join(fixture.Root, "target");
	Directory.CreateDirectory(nested);
	Directory.CreateDirectory(external);
	File.WriteAllText(Path.Join(external, "keep.txt"), "keep");
	Directory.CreateSymbolicLink(Path.Join(nested, "link"), external);
	AssertThrows<IOException>(() => FsHelpers.MoveFilesAndDirs(source, target));
	Assert(File.Exists(Path.Join(external, "keep.txt")), "nested link target was modified");
}


static void ArchiveSourceLink()
{
	if (OperatingSystem.IsWindows())
	{
		return;
	}

	using var fixture = new Fixture();
	var archive = Path.Join(fixture.Root, "archive.zip");
	CreateZip(archive, ("file.txt", "content"));
	var linkedArchive = Path.Join(fixture.Root, "linked.zip");
	File.CreateSymbolicLink(linkedArchive, archive);
	AssertThrows<IOException>(() => SafeArchive.Extract(linkedArchive, Path.Join(fixture.Root, "out")));
}


static void CopyOverlap()
{
	using var fixture = new Fixture();
	var source = Path.Join(fixture.Root, "source");
	Directory.CreateDirectory(source);
	AssertThrows<InvalidOperationException>(() =>
		FsHelpers.TransactionalCopyDirectory(source, Path.Join(source, "child"), false));
}


static void BackupRoundTrip()
{
	using var fixture = new Fixture();
	var saves = Path.Join(fixture.Root, "saves");
	var backup = Path.Join(fixture.Root, "backup");
	Directory.CreateDirectory(saves);
	File.WriteAllText(Path.Join(saves, "slot.dat"), "v1");
	FsHelpers.TransactionalCopyDirectory(saves, backup, false, requiredDestinationOwner: "backup:test");
	File.WriteAllText(Path.Join(saves, "slot.dat"), "v2");
	File.WriteAllText(Path.Join(saves, "new.dat"), "preserve");
	FsHelpers.TransactionalCopyDirectory(backup, saves, true, requiredSourceOwner: "backup:test");
	Assert(File.ReadAllText(Path.Join(saves, "slot.dat")) == "v1", "backup did not restore the saved file");
	Assert(File.ReadAllText(Path.Join(saves, "new.dat")) == "preserve", "merge restore removed an unrelated file");
}


static void CopyRecovery()
{
	using var fixture = new Fixture();
	var destination = Path.Join(fixture.Root, "destination");
	var operationRoot = Path.Join(fixture.Root, ".destination-copy-crashed");
	var previous = Path.Join(operationRoot, "previous");
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(destination, "old.txt"), "old");
	Directory.CreateDirectory(previous);
	File.Move(Path.Join(destination, "old.txt"), Path.Join(previous, "old.txt"));
	Directory.Delete(destination);
	FsHelpers.MarkOwnedDirectory(previous, "copy-previous:test");
	FsHelpers.MarkOwnedDirectory(operationRoot, "copy-operation:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "swapping");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "previous.marker"), "");

	FsHelpers.RecoverTransactionalCopy(destination);
	Assert(File.ReadAllText(Path.Join(destination, "old.txt")) == "old", "interrupted copy did not restore the previous tree");
	Assert(!File.Exists(Path.Join(destination, FsHelpers.OwnerMarkerFileName)), "recovery left a temporary ownership marker");
	Assert(!Directory.Exists(operationRoot), "recovery left an operation directory");
}


static void CopyRecoveryRollback()
{
	using var fixture = new Fixture();
	var destination = Path.Join(fixture.Root, "destination");
	var operationRoot = Path.Join(fixture.Root, ".destination-copy-crashed");
	var previous = Path.Join(operationRoot, "previous");
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(destination, "new.txt"), "new");
	FsHelpers.MarkOwnedDirectory(destination, "copy-destination:test");
	Directory.CreateDirectory(previous);
	File.WriteAllText(Path.Join(previous, "old.txt"), "old");
	FsHelpers.MarkOwnedDirectory(previous, "copy-previous:test");
	FsHelpers.MarkOwnedDirectory(operationRoot, "copy-operation:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.owner"), "copy-destination:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "filesystem-committed");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "previous.marker"), "");

	FsHelpers.RecoverTransactionalCopy(destination);
	Assert(File.ReadAllText(Path.Join(destination, "old.txt")) == "old", "uncommitted copy did not restore the previous tree");
	Assert(!File.Exists(Path.Join(destination, "new.txt")), "uncommitted destination was retained");
	Assert(!Directory.Exists(operationRoot), "rollback recovery left an operation directory");
}


static void CopyRecoveryRefusesUnownedDestination()
{
	using var fixture = new Fixture();
	var destination = Path.Join(fixture.Root, "destination");
	var operationRoot = Path.Join(fixture.Root, ".destination-copy-crashed");
	var previous = Path.Join(operationRoot, "previous");
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(destination, "user.txt"), "keep");
	Directory.CreateDirectory(previous);
	File.WriteAllText(Path.Join(previous, "old.txt"), "old");
	FsHelpers.MarkOwnedDirectory(previous, "copy-previous:test");
	FsHelpers.MarkOwnedDirectory(operationRoot, "copy-operation:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.owner"), "copy-destination:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "filesystem-committed");

	AssertThrows<IOException>(() => FsHelpers.RecoverTransactionalCopy(destination));
	Assert(File.Exists(Path.Join(destination, "user.txt")), "unowned destination was modified");
	Assert(File.Exists(Path.Join(previous, "old.txt")), "previous snapshot was deleted after ownership loss");
	Assert(Directory.Exists(operationRoot), "unsafe recovery journal was discarded");
}


static void CopyRecoveryPreservesStagingDestination()
{
	using var fixture = new Fixture();
	var destination = Path.Join(fixture.Root, "destination");
	var operationRoot = Path.Join(fixture.Root, ".destination-copy-crashed");
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(destination, "existing.txt"), "keep");
	FsHelpers.MarkOwnedDirectory(destination, "backup:test");
	FsHelpers.MarkOwnedDirectory(operationRoot, "copy-operation:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.owner"), "backup:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "staging");

	FsHelpers.RecoverTransactionalCopy(destination);
	Assert(File.ReadAllText(Path.Join(destination, "existing.txt")) == "keep", "staging recovery discarded the existing destination");
	Assert(FsHelpers.IsOwnedDirectory(destination, "backup:test"), "staging recovery changed destination ownership");
	Assert(!Directory.Exists(operationRoot), "staging recovery left an operation directory");
}


static void CopyRecoveryRemovesFreshStagingDestination()
{
	using var fixture = new Fixture();
	var destination = Path.Join(fixture.Root, "destination");
	var operationRoot = Path.Join(fixture.Root, ".destination-copy-crashed");
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(destination, "new.txt"), "new");
	FsHelpers.MarkOwnedDirectory(destination, "copy-destination:test");
	FsHelpers.MarkOwnedDirectory(operationRoot, "copy-operation:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.owner"), "copy-destination:test");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "destination.marker-temporary"), "true");
	FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "staging");

	FsHelpers.RecoverTransactionalCopy(destination);
	Assert(!Directory.Exists(destination), "fresh uncommitted destination was retained");
	Assert(!Directory.Exists(operationRoot), "fresh-destination recovery left an operation directory");
}


static void CopyCancellation()
{
	using var fixture = new Fixture();
	var source = Path.Join(fixture.Root, "source");
	var destination = Path.Join(fixture.Root, "destination");
	Directory.CreateDirectory(source);
	Directory.CreateDirectory(destination);
	File.WriteAllText(Path.Join(source, "new.txt"), "new");
	File.WriteAllText(Path.Join(destination, "existing.txt"), "keep");
	using var cancellation = new CancellationTokenSource();
	cancellation.Cancel();

	AssertThrows<OperationCanceledException>(() => FsHelpers.TransactionalCopyDirectory(
		source,
		destination,
		preserveDestinationFiles: true,
		cancellationToken: cancellation.Token));
	Assert(File.ReadAllText(Path.Join(destination, "existing.txt")) == "keep", "canceled copy changed the destination");
	Assert(!File.Exists(Path.Join(destination, "new.txt")), "canceled copy committed source data");
}


static void SwapRecoveryDecision()
{
	foreach (var phase in new[] { "swapping", "filesystem-committed", "rollback" })
	{
		Assert(FsHelpers.ShouldRemoveUncommittedTarget(phase, false, false),
			$"fresh target should be removed during {phase}");
		Assert(!FsHelpers.ShouldRemoveUncommittedTarget(phase, true, false),
			$"previous target should be preserved during {phase}");
		Assert(!FsHelpers.ShouldRemoveUncommittedTarget(phase, false, true),
			$"target with a previous snapshot should be recovered from the snapshot during {phase}");
	}
	Assert(!FsHelpers.ShouldRemoveUncommittedTarget("staging", false, false),
		"staging should not remove a target before a swap begins");
	Assert(!FsHelpers.ShouldRemoveUncommittedTarget("state-committed", false, false),
		"committed target should not be removed");
}


static void ModIdentity()
{
	using var fixture = new Fixture();
	var first = new Mod { ModId = "100", ModName = "Same Name" };
	var second = new Mod { ModId = "200", ModName = "Same Name" };
	var firstPath = ModPaths.ManagedFolder(fixture.Root, "game", first);
	var secondPath = ModPaths.ManagedFolder(fixture.Root, "game", second);
	Assert(firstPath != secondPath, "distinct IDs collided on disk");
	Directory.CreateDirectory(firstPath);
	Assert(!ModPaths.IsManaged(firstPath), "a prefix alone must not establish ownership");
	FsHelpers.MarkOwnedDirectory(firstPath, "owner");
	Assert(ModPaths.IsManaged(firstPath), "ownership marker was not recognized");
	var linkedLocal = new Mod { StableId = "local-keep", ModId = "remote-id", ModName = "Local" };
	Assert(ModPaths.StableId(linkedLocal) == "local-keep", "linking a local mod changed its on-disk identity");
}


static void ProviderAliases()
{
	var registry = new ProviderRegistry();
	Assert(registry.NormalizeProviderId("Yuzu") == "eden", "Yuzu alias did not migrate to Eden");
	Assert(registry.NormalizeProviderId("Ryujinx") == "ryubing", "Ryujinx alias did not migrate to Ryubing");
	Assert(registry.Get("unknown").Id == registry.DefaultProvider.Id, "unknown provider did not use the default");
}


static void EdenReleaseDiscovery()
{
	const string releaseHtml = """
		<html><body>
		<a href="https://stable.eden-emu.dev/eden-v0.2.0-windows-amd64-msvc-standard.zip">Windows</a>
		<a href="https://stable.eden-emu.dev/eden-v0.2.0-linux-amd64-clang-pgo.AppImage">Linux</a>
		</body></html>
		""";
	using var client = new HttpClient(new StubHttpMessageHandler(request =>
		new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(request.RequestUri?.Query.Contains("page=", StringComparison.Ordinal) == true
				? "<html></html>"
				: releaseHtml),
		}));
	var releases = new ModeEden().GetAvailableReleases(client, OsKind.Windows, "stable")
		.GetAwaiter().GetResult();

	Assert(releases.Count == 1, "Eden discovery did not group assets by version");
	Assert(releases[0].Version == "0.2.0", "Eden discovery returned the wrong version");
	Assert(releases[0].FileName.EndsWith("windows-amd64-msvc-standard.zip", StringComparison.Ordinal),
		"Eden discovery selected the wrong platform asset");
}


static void RyubingReleaseDiscovery()
{
	using var client = new HttpClient(new StubHttpMessageHandler(_ =>
		new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(
				"{\"tag\":\"v1.2.3\",\"download_url\":\"https://downloads.example/Ryubing.AppImage\"}"),
		}));
	var releases = new ModeRyubing().GetAvailableReleases(client, OsKind.Linux, "stable")
		.GetAwaiter().GetResult();

	Assert(releases.Count == 1, "Ryubing update-server release was not returned");
	Assert(releases[0].Version == "1.2.3", "Ryubing release version was not normalized");
	Assert(releases[0].FileName == "Ryubing.AppImage", "Ryubing download filename was not parsed");
}


static void RyubingMalformedUpdateResponse()
{
	using var client = new HttpClient(new StubHttpMessageHandler(request =>
		new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(request.RequestUri?.Host == "update.ryujinx.app" ? "{invalid" : "[]"),
		}));
	var releases = new ModeRyubing().GetAvailableReleases(client, OsKind.Linux, "stable")
		.GetAwaiter().GetResult();

	Assert(releases.Count == 0, "malformed update JSON did not fall back to the empty Forgejo response");
}


static void TitleParsing()
{
	const string html = """
		<table>
		<tr><th>Title ID</th><th>Name</th></tr>
		<tr><td>0100F2C0115B6000</td><td><a>Zelda &amp; Friends™</a></td></tr>
		<tr><td>not-an-id</td><td>Ignore</td></tr>
		<tr><td>010012101468C000</td></tr>
		</table>
		""";
	var titles = ModManager.ParseTitles(html);

	Assert(titles.Count == 1, "malformed title rows were not skipped");
	Assert(titles["0100F2C0115B6000"] == "Zelda & Friends", "title name was not decoded and normalized");
}


static void SceneSignalContracts()
{
	var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/Home.tscn", ".", typeof(Home));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/Home.tscn", "ToolsService", typeof(Tools));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/InstallerPage.tscn", ".", typeof(Installer));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/ModManagerPage.tscn", ".", typeof(ModManager));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/SettingsPage.tscn", ".", typeof(SettingsPage));
	AssertSceneTargetMethods(repositoryRoot, "Scenes/ToolsPage.tscn", ".", typeof(ToolsPage));
}


static void AssertSceneTargetMethods(string repositoryRoot, string relativeScenePath, string target, Type scriptType)
{
	var scene = File.ReadAllText(Path.Join(repositoryRoot, relativeScenePath));
	var matches = Regex.Matches(
		scene,
		$@"\[connection[^\]]+to=""{Regex.Escape(target)}"" method=""([^""]+)""");
	Assert(matches.Count > 0, $"no signal connections found for {relativeScenePath} target {target}");
	foreach (Match match in matches)
	{
		var methodName = match.Groups[1].Value;
		var methodExists = scriptType
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Any(method => method.Name == methodName);
		Assert(methodExists, $"{relativeScenePath} targets missing method {scriptType.Name}.{methodName}");
	}
}


static void UnsupportedOs()
{
	Assert(Os.Parse("Linux") == OsKind.Linux, "Linux parse failed");
	Assert(Os.Parse("macOS") == OsKind.MacOS, "macOS parse failed");
	AssertThrows<PlatformNotSupportedException>(() => Os.Parse("FreeBSD"));
}


static void CustomVersionValidation()
{
	var provider = new ModeRyubing();
	Assert(provider.IsValidCustomVersion("v1.2.3-rc1"), "valid version was rejected");
	foreach (var invalid in new[] { "../1.2.3", "1/2", "1\\2", "/absolute", new string('a', 65) })
	{
		Assert(!provider.IsValidCustomVersion(invalid), $"unsafe version was accepted: {invalid}");
	}
}


static void CreateZip(string path, params (string Name, string Content)[] entries)
{
	using var stream = File.Create(path);
	using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
	foreach (var (name, content) in entries)
	{
		var entry = archive.CreateEntry(name);
		using var writer = new StreamWriter(entry.Open());
		writer.Write(content);
	}
}


static void Assert(bool condition, string message)
{
	if (!condition)
	{
		throw new InvalidOperationException(message);
	}
}


static void AssertThrows<T>(Action action) where T : Exception
{
	try
	{
		action();
	}
	catch (T)
	{
		return;
	}
	throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}


sealed class Fixture : IDisposable
{
	public string Root { get; } = Path.Join(Path.GetTempPath(), "emustack-tests", Guid.NewGuid().ToString("N"));

	public Fixture() => Directory.CreateDirectory(Root);

	public void Dispose()
	{
		if (Directory.Exists(Root))
		{
			Directory.Delete(Root, true);
		}
	}
}


sealed class StubHttpMessageHandler : HttpMessageHandler
{
	private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

	public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
	{
		_responseFactory = responseFactory;
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(_responseFactory(request));
	}
}
