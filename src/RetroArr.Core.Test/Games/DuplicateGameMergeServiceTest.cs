using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download.History;
using RetroArr.Core.Games;
using RetroArr.Core.Wishlist;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class DuplicateGameMergeServiceTest
    {
        private const int Snes = 41;
        private const int Gba = 52;

        private DbContextOptions<RetroArrDbContext> _dbOptions = null!;
        // SQLite, so the unique indexes and cascades hold, and a library on disk
        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _sqlite = null!;
        private string _root = null!;
        private string _lib = null!;
        private ConfigurationService _config = null!;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        [SetUp]
        public void Setup()
        {
            _dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _sqlite = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_sqlite);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = 20, Name = "PlayStation 1", Slug = "ps1", FolderName = "psx" },
                new Platform { Id = Snes, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" },
                new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_merge_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            _config = new ConfigurationService(_root);
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib });
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private Game Row(int id)
        {
            using var ctx = new RetroArrDbContext(_sqlite);
            return ctx.Games.AsNoTracking().Single(g => g.Id == id);
        }

        private static string Put(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, "rom");
            return path;
        }

        // One row of every kind that points at a game
        private static void AddDependents(RetroArrDbContext ctx, int gameId, string file = "game.bin")
        {
            ctx.Collections.Add(new Collection { Id = 1, Name = "Favourites" });
            ctx.CollectionGames.Add(new CollectionGame { CollectionId = 1, GameId = gameId });
            ctx.Tags.Add(new Tag { Id = 1, Name = "played" });
            ctx.GameTags.Add(new GameTag { TagId = 1, GameId = gameId });
            ctx.GameFiles.Add(new GameFile { GameId = gameId, RelativePath = file, Size = 3, FileType = "Main" });
            ctx.GameReviews.Add(new GameReview { GameId = gameId, Notes = "copy" });
            ctx.WishlistPriceWatches.Add(new WishlistPriceWatch { GameId = gameId, Provider = "steam", ExternalId = "1" });
            ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "abc", Title = "Game", GameId = gameId });
        }

        private static int[] DependentGameIds(RetroArrDbContext ctx) =>
            ctx.CollectionGames.Select(r => r.GameId)
                .Concat(ctx.GameTags.Select(r => r.GameId))
                .Concat(ctx.GameFiles.Select(r => r.GameId))
                .Concat(ctx.GameReviews.Select(r => r.GameId))
                .Concat(ctx.WishlistPriceWatches.Select(r => r.GameId))
                .Concat(ctx.DownloadHistory.Select(r => r.GameId!.Value))
                .ToArray();

        [Test]
        public async Task MergeAsync_CueBinPair_KeepsCueRowDropsBinRow()
        {
            // Reproduce the user's case: SLES_***.bin and SLES_***.cue both
            // sit in /media/psx and end up as two rows. The cue carries the
            // IGDB id; merging should keep that one and drop the bin row.
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game
                    {
                        Id = 1,
                        Title = "SLES 040 93 Beyblade",
                        PlatformId = 20,
                        Path = Path.Combine("/media", "psx", "SLES_040.93.Beyblade (EU).bin"),
                        IgdbId = null
                    },
                    new Game
                    {
                        Id = 2,
                        Title = "Beyblade",
                        PlatformId = 20,
                        Path = Path.Combine("/media", "psx", "SLES_040.93.Beyblade (EU).cue"),
                        IgdbId = 9999
                    }
                );
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);

                Assert.That(result.RowsMerged, Is.EqualTo(1));
                Assert.That(result.ClustersFound, Is.GreaterThan(0));
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var games = await ctx.Games.ToListAsync();
                Assert.That(games.Count, Is.EqualTo(1));
                Assert.That(games[0].Id, Is.EqualTo(2));
                Assert.That(games[0].Title, Is.EqualTo("Beyblade"));
            }
        }

        [Test]
        public async Task MergeAsync_GameFilesReattachedToWinner()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game { Id = 10, Title = "A", PlatformId = 20, Path = "/x/Game.bin" },
                    new Game { Id = 11, Title = "A", PlatformId = 20, Path = "/x/Game.cue", IgdbId = 1 }
                );
                ctx.GameFiles.AddRange(
                    new GameFile { Id = 100, GameId = 10, RelativePath = "/x/Game.bin", Size = 1, FileType = "Main" },
                    new GameFile { Id = 101, GameId = 10, RelativePath = "/x/Game.lrg", Size = 2, FileType = "Main" }
                );
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                await DuplicateGameMergeService.MergeAsync(ctx);
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var files = await ctx.GameFiles.ToListAsync();
                Assert.That(files.Count, Is.EqualTo(2));
                Assert.That(files.All(f => f.GameId == 11), Is.True);
            }
        }

        // x takes over y (the same disc), then y and z are one title: z goes to the row that holds y by now.
        // SQLite, so a second review on one row would break its unique index.
        [TestCase(9, null, 1)]
        [TestCase(null, 5, 3)]
        public async Task MergeAsync_ChainOfClusters_EverythingEndsUpOnTheSurvivor(int? xIgdbId, int? zIgdbId, int survivor)
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Database.EnsureCreated();
                ctx.Platforms.Add(new Platform { Id = 20, Name = "PlayStation 1", Slug = "ps1", FolderName = "psx" });
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Alpha", PlatformId = 20, Path = "/psx/Game.cue", IgdbId = xIgdbId },
                    new Game { Id = 2, Title = "Beta", PlatformId = 20, Path = "/psx/Game.bin" },
                    new Game { Id = 3, Title = "Beta", PlatformId = 20, Path = "/other/Beta.iso", IgdbId = zIgdbId });
                ctx.GameFiles.AddRange(
                    new GameFile { Id = 100, GameId = 2, RelativePath = "Game.bin", Size = 1, FileType = "Main" },
                    new GameFile { Id = 101, GameId = 3, RelativePath = "Beta.iso", Size = 1, FileType = "Main" });
                ctx.GameReviews.AddRange(new GameReview { GameId = 2, Notes = "y" }, new GameReview { GameId = 3, Notes = "z" });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(2));
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(ctx.Games.Select(g => g.Id), Is.EqualTo(new[] { survivor }));
                Assert.That(ctx.GameFiles.AsEnumerable().Select(f => (f.Id, f.GameId)), Is.EquivalentTo(new[] { (100, survivor), (101, survivor) }));
                Assert.That(ctx.GameReviews.Select(r => r.GameId), Is.EqualTo(new[] { survivor }));
            }
        }

        // Every row pointing at the merged entry would go with it (or point at nothing): all of them move, and where
        // the survivor has one of its own already, the survivor's stays. SQLite, so the unique indexes and cascades hold.
        [Test]
        public async Task MergeAsync_EveryRowOfTheMergedEntry_MovesToTheSurvivor()
        {
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Alpha", PlatformId = 20, Path = "/psx/Game.cue", IgdbId = 9 },
                    new Game { Id = 2, Title = "Beta", PlatformId = 20, Path = "/psx/Game.bin" });
                ctx.Collections.AddRange(new Collection { Id = 1, Name = "Both" }, new Collection { Id = 2, Name = "Merged" });
                ctx.CollectionGames.AddRange(
                    new CollectionGame { CollectionId = 1, GameId = 1 },
                    new CollectionGame { CollectionId = 1, GameId = 2 },
                    new CollectionGame { CollectionId = 2, GameId = 2 });
                ctx.Tags.AddRange(new Tag { Id = 1, Name = "both" }, new Tag { Id = 2, Name = "merged" });
                ctx.GameTags.AddRange(
                    new GameTag { TagId = 1, GameId = 1 },
                    new GameTag { TagId = 1, GameId = 2 },
                    new GameTag { TagId = 2, GameId = 2 });
                ctx.GameFiles.Add(new GameFile { Id = 100, GameId = 2, RelativePath = "Game.bin", Size = 1, FileType = "Main" });
                ctx.GameReviews.Add(new GameReview { GameId = 2, Notes = "merged" });
                ctx.WishlistPriceWatches.AddRange(
                    new WishlistPriceWatch { GameId = 1, Provider = "gog", ExternalId = "g1" },
                    new WishlistPriceWatch { GameId = 2, Provider = "gog", ExternalId = "g2" },
                    new WishlistPriceWatch { GameId = 2, Provider = "steam", ExternalId = "s2" });
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "abc", Title = "Beta", GameId = 2 });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(1));
            }

            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                Assert.That(ctx.Games.Select(g => g.Id), Is.EqualTo(new[] { 1 }));
                Assert.That(ctx.GameFiles.Select(f => f.GameId), Is.EqualTo(new[] { 1 }));
                Assert.That(ctx.CollectionGames.AsEnumerable().Select(c => (c.CollectionId, c.GameId)), Is.EquivalentTo(new[] { (1, 1), (2, 1) }));
                Assert.That(ctx.GameTags.AsEnumerable().Select(t => (t.TagId, t.GameId)), Is.EquivalentTo(new[] { (1, 1), (2, 1) }));
                Assert.That(ctx.GameReviews.AsEnumerable().Select(r => (r.GameId, r.Notes)), Is.EqualTo(new[] { (1, "merged") }));
                Assert.That(ctx.WishlistPriceWatches.AsEnumerable().Select(w => (w.GameId, w.Provider, w.ExternalId)), Is.EquivalentTo(new[] { (1, "gog", "g1"), (1, "steam", "s2") }));
                Assert.That(ctx.DownloadHistory.Select(h => h.GameId), Is.EqualTo(new int?[] { 1 }));
            }
        }

        // The entry with the IGDB id wins and is monitored when either was. Like the scanner, it keeps a path whose
        // content may still be there: present, on a library root that is not mounted, or gone with most of its folder
        // (the breaker takes a mass loss for an outage). Only a lost one gives way: its folder stayed empty since it
        // was added, and the game is the other entry's file.
        [TestCase("empty", true)]
        [TestCase("present", false)]
        [TestCase("offline", false)]
        [TestCase("held", false)]
        public async Task MergeAsync_TheSurvivorKeepsAPathItsContentMayBeAt_OrTakesTheCopyOnDisk(string winner, bool takes)
        {
            var snes = Path.Combine(_lib, "snes");
            var copy = Put(snes, "Chrono Trigger (USA).sfc");
            var offline = Path.Combine(_root, "offline");
            if (winner == "offline") _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = offline });
            var own = winner switch
            {
                "empty" => Directory.CreateDirectory(Path.Combine(snes, "Chrono Trigger")).FullName,
                "present" => Put(snes, "Chrono Trigger.sfc"),
                "offline" => Path.Combine(offline, "snes", "Chrono Trigger.sfc"),
                _ => Path.Combine(snes, "Chrono Trigger.sfc")
            };
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, IgdbId = 5, Path = own,
                        Status = takes ? GameStatus.Missing : winner == "present" ? GameStatus.Released : GameStatus.Downloaded, MissingSince = takes ? new DateTime(2026, 9, 1) : null },
                    // a status that lags behind the content: whoever ends up with content there is Downloaded
                    new Game { Id = 2, Title = "Chrono Trigger", PlatformId = Snes, Path = copy, ExecutablePath = copy, Status = GameStatus.Released, Monitored = true });
                if (winner == "held")
                    ctx.Games.AddRange(Enumerable.Range(3, 5).Select(i =>
                        new Game { Id = i, Title = $"Game {i}", PlatformId = Snes, Path = Path.Combine(snes, $"Game {i}.sfc"), Status = GameStatus.Downloaded }));
                await ctx.SaveChangesAsync();
            }

            var result = await new DuplicateGameMergeService(new DbFactory(_sqlite), _config).MergeAsync();

            Assert.That(result.RowsMerged, Is.EqualTo(1));
            var survivor = Row(1);
            Assert.That(survivor.Monitored, Is.True);
            Assert.That((survivor.Path, survivor.ExecutablePath, survivor.Status, survivor.MissingSince),
                Is.EqualTo(takes ? (copy, copy, GameStatus.Downloaded, (DateTime?)null) : (own, (string?)null, GameStatus.Downloaded, (DateTime?)null)));
        }

        // The rows of both entries find their files by the survivor's path: the survivor's own stay relative to its
        // folder, the merged single file's (relative to the platform folder) keep their full path
        [Test]
        public async Task MergeAsync_FileRowsOfTheMergedEntry_StillFindTheirFiles()
        {
            var snes = Path.Combine(_lib, "snes");
            var folder = Path.Combine(snes, "Chrono Trigger");
            Put(folder, "Chrono Trigger.sfc");
            var copy = Put(snes, "Chrono Trigger (USA).sfc");
            var patch = Put(snes, "Chrono Trigger (USA) [Patch].ips");
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, IgdbId = 5, Path = folder, Status = GameStatus.Downloaded },
                    new Game { Id = 2, Title = "Chrono Trigger", PlatformId = Snes, Path = copy, ExecutablePath = copy, Status = GameStatus.Downloaded });
                ctx.GameFiles.AddRange(
                    new GameFile { GameId = 1, RelativePath = "Chrono Trigger.sfc", FileType = "Main" },
                    new GameFile { GameId = 2, RelativePath = "Chrono Trigger (USA).sfc", FileType = "Main" },
                    new GameFile { GameId = 2, RelativePath = "Chrono Trigger (USA) [Patch].ips", FileType = "Patch" });
                await ctx.SaveChangesAsync();
            }

            await new DuplicateGameMergeService(new DbFactory(_sqlite), _config).MergeAsync();

            using var check = new RetroArrDbContext(_sqlite);
            Assert.That(check.GameFiles.AsEnumerable().Select(f => (f.GameId, f.RelativePath)),
                Is.EquivalentTo(new[] { (1, "Chrono Trigger.sfc"), (1, copy), (1, patch) }));
        }

        // A cue and its bin as two entries: the bin is a row of both, and one row is left for it, the survivor's
        [Test]
        public async Task MergeAsync_AFileBothEntriesHadARowOf_KeepsOneRow()
        {
            var psx = Path.Combine(_lib, "psx");
            var cue = Put(psx, "Game.cue");
            var bin = Put(psx, "Game.bin");
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Game", PlatformId = 20, Path = cue, IgdbId = 9 },
                    new Game { Id = 2, Title = "Game (Track 1)", PlatformId = 20, Path = bin });
                ctx.GameFiles.AddRange(
                    new GameFile { Id = 100, GameId = 1, RelativePath = "Game.cue", FileType = "Main" },
                    new GameFile { Id = 101, GameId = 1, RelativePath = "Game.bin", FileType = "Main" },
                    new GameFile { Id = 102, GameId = 2, RelativePath = "Game.bin", FileType = "Main" });
                await ctx.SaveChangesAsync();
            }

            await new DuplicateGameMergeService(new DbFactory(_sqlite), _config).MergeAsync();

            using var check = new RetroArrDbContext(_sqlite);
            Assert.That(check.GameFiles.AsEnumerable().Select(f => (f.Id, f.GameId, f.RelativePath)),
                Is.EquivalentTo(new[] { (100, 1, "Game.cue"), (101, 1, "Game.bin") }));
        }

        // The main use is a target without a path yet (a wanted entry): the source row goes first, so the target can
        // take its path. What hung off the source moves over (its file rows still find their files), and a target
        // with its content keeps it.
        [TestCase(false)]
        [TestCase(true)]
        public async Task MergeInto_TheTargetTakesTheSourcesRows_AndItsPathWhenItHasNoContent(bool targetHasContent)
        {
            var gba = Path.Combine(_lib, "gba");
            var copy = Put(gba, "Advance Wars (USA) (Rev 1).gba");
            var folder = Path.Combine(gba, "Advance Wars");
            var rom = targetHasContent ? Put(folder, "Advance Wars (USA).gba") : null;
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom == null ? null : folder, ExecutablePath = rom, Monitored = true,
                        Status = rom == null ? GameStatus.Missing : GameStatus.Released },
                    new Game { Id = 2, Title = "Advance Wars (Rev 1)", PlatformId = Gba, Path = copy, ExecutablePath = copy, Status = GameStatus.Released,
                        Region = "USA", Languages = "En" });
                AddDependents(ctx, 2, Path.GetFileName(copy));
                await ctx.SaveChangesAsync();
            }
            var controller = new GameController(new SqliteGameRepository(new DbFactory(_sqlite)), null!, null!, null!, _config, null!, null!, null!, null!, null!, null!, null!,
                mergeService: new DuplicateGameMergeService(new DbFactory(_sqlite), _config));

            var result = await controller.MergeInto(2, 1);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_sqlite);
            var target = check.Games.AsNoTracking().Single();
            Assert.That((target.Id, target.Path, target.ExecutablePath, target.Status, target.Monitored, target.Region, target.Languages),
                Is.EqualTo((1, rom == null ? copy : folder, rom ?? copy, GameStatus.Downloaded, true, "USA", "En")));
            Assert.That(DependentGameIds(check), Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1 }));
            // relative to the folder the target has now, or the full path outside it
            var row = check.GameFiles.Single().RelativePath;
            Assert.That(row, Is.EqualTo(rom == null ? Path.GetFileName(copy) : copy));
            Assert.That(File.Exists(GameFilePaths.Resolve(target.Path, row)), Is.True);
        }

        // The repair merges with the library roots: an entry on a root that is not mounted keeps its path
        [Test]
        public async Task Repair_MergeDuplicates_KeepsAPathOnARootThatIsOffline()
        {
            var offline = Path.Combine(_root, "offline");
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = offline });
            var own = Path.Combine(offline, "snes", "Chrono Trigger (USA).sfc");
            var copy = Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                // One release to the duplicate check, two rows to the unique index (case)
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = "USA", IgdbId = 5, Path = own, Status = GameStatus.Downloaded },
                    new Game { Id = 2, Title = "CHRONO TRIGGER", PlatformId = Snes, Region = "USA", Path = copy, ExecutablePath = copy, Status = GameStatus.Downloaded });
                await ctx.SaveChangesAsync();
            }
            var controller = new DatabaseController(_config, new DbFactory(_sqlite), null!, new DuplicateGameMergeService(new DbFactory(_sqlite), _config));

            var result = await controller.Repair(new DatabaseRepairRequest { MergeDuplicates = true });

            Assert.That(((DatabaseRepairResult)((OkObjectResult)result.Result!).Value!).DuplicatesMerged, Is.EqualTo(1));
            Assert.That(Row(1).Path, Is.EqualTo(own));
        }

        // Two untagged rows of a title on a platform: '' for both would collide in the unique index, which failed the
        // whole repair. They stay untagged, and the merge takes them together; what is left is tagged ''.
        [TestCase(true)]
        [TestCase(false)]
        public async Task Repair_UntaggedDuplicates_AreMergedInsteadOfFailingTheRepair(bool merge)
        {
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, IgdbId = 5 },
                    new Game { Id = 2, Title = "Chrono Trigger", PlatformId = Snes });
                await ctx.SaveChangesAsync();
            }
            var controller = new DatabaseController(_config, new DbFactory(_sqlite), null!, new DuplicateGameMergeService(new DbFactory(_sqlite), _config));

            var result = await controller.Repair(new DatabaseRepairRequest { MergeDuplicates = merge });

            Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_sqlite);
            Assert.That(check.Games.AsNoTracking().OrderBy(g => g.Id).AsEnumerable().Select(g => (g.Id, g.Region)),
                Is.EqualTo(merge ? new[] { (1, (string?)"") } : new[] { (1, (string?)null), (2, (string?)null) }));
        }

        // An orphan platform id goes to Unknown, a row in a platform's folder to that platform. An entry of the title
        // there already broke the unique index and the whole repair: the row now stays, and the merge takes the two as
        // one of that platform. The one with the IGDB id stays, and goes there if it is the moving one.
        [TestCase("orphan", false)]
        [TestCase("orphan", true)]
        [TestCase("heal", false)]
        [TestCase("heal", true)]
        public async Task Repair_PlatformChangeOntoAnEntryOfTheTitle_IsMergedInsteadOfFailingTheRepair(string change, bool merge)
        {
            const int Pc = 1, Gone = 999;
            var heal = change == "heal";
            var target = heal ? Snes : PlatformDefinitions.UnknownPlatformId;
            var snes = Path.Combine(_lib, "snes");
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Platforms.AddRange(
                    new Platform { Id = Pc, Name = "PC", Slug = "pc", FolderName = "windows" },
                    new Platform { Id = PlatformDefinitions.UnknownPlatformId, Name = "Unknown", Slug = "unknown", FolderName = "unknown" },
                    new Platform { Id = Gone, Name = "Gone", Slug = "gone", FolderName = "gone" });
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = target, Region = "USA", IgdbId = heal ? null : 5,
                        Path = heal ? Put(snes, "Chrono Trigger (USA).sfc") : null },
                    new Game { Id = 2, Title = "Chrono Trigger", PlatformId = heal ? Pc : Gone, Region = "USA", IgdbId = heal ? 5 : null,
                        Path = heal ? Put(snes, "Chrono Trigger (USA) (Rev 1).sfc") : null });
                await ctx.SaveChangesAsync();
            }
            var controller = new DatabaseController(_config, new DbFactory(_sqlite), null!, new DuplicateGameMergeService(new DbFactory(_sqlite), _config));

            var result = await controller.Repair(new DatabaseRepairRequest { MergeDuplicates = merge });

            Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_sqlite);
            Assert.That(check.Games.AsNoTracking().OrderBy(g => g.Id).AsEnumerable().Select(g => (g.Id, g.PlatformId)),
                Is.EqualTo(merge ? new[] { (heal ? 2 : 1, target) } : new[] { (1, target), (2, heal ? Pc : Gone) }));
        }

        // Only the part of a path below the library root counts: a folder above it named like a container (a backup.ps4
        // mount) is not one, a row outside every root stays, and a row inside a .ps4 folder in the library goes
        [Test]
        public async Task Repair_ContainerOrphans_CountOnlyBelowTheLibraryRoot()
        {
            var lib = Directory.CreateDirectory(Path.Combine(_root, "backup.ps4", "library")).FullName;
            _config.SaveMediaSettings(new MediaSettings { FolderPath = lib, DestinationPath = lib });
            using (var ctx = new RetroArrDbContext(_sqlite))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Path = Path.Combine(lib, "snes", "Chrono Trigger (USA).sfc") },
                    new Game { Id = 2, Title = "Elsewhere", PlatformId = Snes, Path = Path.Combine(_root, "old.ps4", "Elsewhere.sfc") },
                    new Game { Id = 3, Title = "Icon", PlatformId = Snes, Path = Path.Combine(lib, "ps4", "Game.ps4", "sce_sys", "icon0.png") });
                await ctx.SaveChangesAsync();
            }
            var controller = new DatabaseController(_config, new DbFactory(_sqlite), null!, new DuplicateGameMergeService(new DbFactory(_sqlite), _config));

            var result = await controller.Repair(new DatabaseRepairRequest { HealPlatformFromPath = false });

            Assert.That(((DatabaseRepairResult)((OkObjectResult)result.Result!).Value!).ContainerOrphansRemoved, Is.EqualTo(1));
            using var check = new RetroArrDbContext(_sqlite);
            Assert.That(check.Games.Select(g => g.Id).OrderBy(id => id), Is.EqualTo(new[] { 1, 2 }));
        }

        // A database made before the unique IGDB index can hold both
        [Test]
        public async Task MergeAsync_TwoRegionsSharingAnIgdbId_BothStay()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Region = "USA", Path = "/gba/Advance Wars", IgdbId = 7 },
                    new Game { Id = 2, Title = "Advance Wars", PlatformId = 52, Region = "Europe", Path = "/gba/Advance Wars (Europe)", IgdbId = 7 });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(0));
                Assert.That(await ctx.Games.CountAsync(), Is.EqualTo(2));
            }
        }

        [Test]
        public async Task MergeAsync_NoDuplicates_NoChange()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Mario", PlatformId = 41, Path = "/snes/Mario.sfc" });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(0));
            }
        }
    }
}
