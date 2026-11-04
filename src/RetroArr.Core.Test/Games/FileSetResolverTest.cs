using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class FileSetResolverTest
    {
        private string _tempDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void Resolve_SingleFile_ReturnsSingleType()
        {
            var romPath = Path.Combine(_tempDir, "game.z64");
            File.WriteAllText(romPath, "dummy");

            var set = FileSetResolver.Resolve(romPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
            Assert.That(set.PrimaryFile, Is.EqualTo(romPath));
            Assert.That(set.CompanionFiles, Is.Empty);
        }

        [Test]
        public void Resolve_CueBin_FindsReferencedBinFiles()
        {
            var binPath = Path.Combine(_tempDir, "Track 01.bin");
            File.WriteAllText(binPath, "dummy bin data");

            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath, "FILE \"Track 01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.CueBin));
            Assert.That(set.PrimaryFile, Is.EqualTo(cuePath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Track 01.bin"));
        }

        [Test]
        public void Resolve_CueBin_MultipleBinFiles()
        {
            var bin1 = Path.Combine(_tempDir, "Game (Track 1).bin");
            var bin2 = Path.Combine(_tempDir, "Game (Track 2).bin");
            File.WriteAllText(bin1, "data1");
            File.WriteAllText(bin2, "data2");

            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath,
                "FILE \"Game (Track 1).bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n" +
                "FILE \"Game (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 01 00:00:00\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.CueBin));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_M3U_FindsReferencedDiscFiles()
        {
            var cue1 = Path.Combine(_tempDir, "Disc1.cue");
            var cue2 = Path.Combine(_tempDir, "Disc2.cue");
            File.WriteAllText(cue1, "FILE \"Disc1.bin\" BINARY\n");
            File.WriteAllText(cue2, "FILE \"Disc2.bin\" BINARY\n");

            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "Disc1.cue\nDisc2.cue\n");

            var set = FileSetResolver.Resolve(m3uPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.M3U));
            Assert.That(set.PrimaryFile, Is.EqualTo(m3uPath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_M3U_SkipsComments()
        {
            var disc = Path.Combine(_tempDir, "Disc1.cue");
            File.WriteAllText(disc, "FILE \"Disc1.bin\" BINARY\n");

            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "# This is a comment\nDisc1.cue\n\n");

            var set = FileSetResolver.Resolve(m3uPath);

            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_GDI_FindsTrackFiles()
        {
            var track1 = Path.Combine(_tempDir, "track01.bin");
            var track2 = Path.Combine(_tempDir, "track02.raw");
            File.WriteAllText(track1, "data");
            File.WriteAllText(track2, "data");

            var gdiPath = Path.Combine(_tempDir, "disc.gdi");
            File.WriteAllText(gdiPath,
                "2\n" +
                "1 0 4 2352 track01.bin 0\n" +
                "2 600 0 2352 track02.raw 0\n");

            var set = FileSetResolver.Resolve(gdiPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.GDI));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_SingleWithCompanion_FindsSbiFile()
        {
            var binPath = Path.Combine(_tempDir, "Game.bin");
            var sbiPath = Path.Combine(_tempDir, "Game.sbi");
            File.WriteAllText(binPath, "data");
            File.WriteAllText(sbiPath, "sbi data");

            var set = FileSetResolver.Resolve(binPath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Game.sbi"));
        }

        [Test]
        public void ResolveDirectory_GroupsFileSets()
        {
            var bin1 = Path.Combine(_tempDir, "Track 01.bin");
            File.WriteAllText(bin1, "data");
            var cue = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cue, "FILE \"Track 01.bin\" BINARY\n");
            var standalone = Path.Combine(_tempDir, "Other.iso");
            File.WriteAllText(standalone, "data");

            var sets = FileSetResolver.ResolveDirectory(_tempDir);

            Assert.That(sets.Count, Is.EqualTo(2));
            var cueSet = sets.First(s => s.Type == FileSetType.CueBin);
            Assert.That(cueSet.CompanionFiles.Count, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_EmptyPath_ReturnsSingle()
        {
            var set = FileSetResolver.Resolve(string.Empty);
            Assert.That(set.Type, Is.EqualTo(FileSetType.Single));
        }

        [Test]
        public void Resolve_Bin_FindsCueAsCompanion()
        {
            // Symmetric: Resolve(.bin) must pull in its sibling .cue, otherwise
            // existing rows with Path=.bin never get the cue registered.
            var binPath = Path.Combine(_tempDir, "Game.bin");
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(binPath, "data");
            File.WriteAllText(cuePath, "FILE \"Game.bin\" BINARY\n");

            var set = FileSetResolver.Resolve(binPath);

            Assert.That(set.PrimaryFile, Is.EqualTo(binPath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Game.cue"));
        }

        [Test]
        public void Resolve_SingleFileRom_DoesNotPickUpReadme()
        {
            // .smc and friends never share their disc. Stem-matching must NOT
            // pull a Readme.txt or cover.png next to a Mario.smc.
            var smcPath = Path.Combine(_tempDir, "Mario.smc");
            var noisePath = Path.Combine(_tempDir, "Mario.txt");
            File.WriteAllText(smcPath, "rom");
            File.WriteAllText(noisePath, "readme");

            var set = FileSetResolver.Resolve(smcPath);

            Assert.That(set.CompanionFiles, Is.Empty);
        }

        [Test]
        public void Resolve_Iso_PullsMdsCompanion()
        {
            var isoPath = Path.Combine(_tempDir, "Game.iso");
            var mdsPath = Path.Combine(_tempDir, "Game.mds");
            File.WriteAllText(isoPath, "data");
            File.WriteAllText(mdsPath, "metadata");

            var set = FileSetResolver.Resolve(isoPath);

            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Game.mds"));
        }

        [Test]
        public void AllFiles_EnumeratesPrimaryThenCompanions()
        {
            // The scanner's SyncGameFilesFromDisk path iterates AllFiles to
            // turn a cue/bin set into two GameFile rows. Pin that ordering.
            var binPath = Path.Combine(_tempDir, "Game.bin");
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(binPath, "data");
            File.WriteAllText(cuePath, "FILE \"Game.bin\" BINARY\n");

            var set = FileSetResolver.Resolve(cuePath);
            var all = set.AllFiles.ToList();

            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all[0], Is.EqualTo(cuePath));
            Assert.That(Path.GetFileName(all[1]), Is.EqualTo("Game.bin"));
        }

        [Test]
        public void Resolve_CueWithIdenticalStemBin_ClaimsBinViaStemFallback()
        {
            // Real-world PSX rip where the cue lists a track that doesn't resolve
            // (renamed bin, copied folder, missing track entries). The bin sitting
            // next to the cue with the exact same stem is still part of the same
            // disc and must not become a second game.
            var binPath = Path.Combine(_tempDir, "SLES_039.00.Digimon Digital Card Battle (EU).bin");
            var cuePath = Path.Combine(_tempDir, "SLES_039.00.Digimon Digital Card Battle (EU).cue");
            File.WriteAllText(binPath, "binary track data");
            File.WriteAllText(cuePath, "FILE \"missing-track.bin\" BINARY\n  TRACK 01 MODE1/2352\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.Type, Is.EqualTo(FileSetType.CueBin));
            Assert.That(set.PrimaryFile, Is.EqualTo(cuePath));
            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]),
                Is.EqualTo("SLES_039.00.Digimon Digital Card Battle (EU).bin"));
        }

        [Test]
        public void Resolve_CueWithEmptyContent_ClaimsSiblingBinByStem()
        {
            var binPath = Path.Combine(_tempDir, "Game.bin");
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(binPath, "data");
            File.WriteAllText(cuePath, string.Empty);

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.CompanionFiles.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(set.CompanionFiles[0]), Is.EqualTo("Game.bin"));
        }

        [Test]
        public void AllFiles_DottedPsxStem_ListsCueAndBin()
        {
            // detail view used to show only the cue, bin was missing from the size total
            var binPath = Path.Combine(_tempDir, "SCES_002.55.Tekken 2 (EU).bin");
            var cuePath = Path.Combine(_tempDir, "SCES_002.55.Tekken 2 (EU).cue");
            File.WriteAllBytes(binPath, new byte[1024]);
            File.WriteAllText(cuePath, "FILE \"SCES_002.55.Tekken 2 (EU).bin\" BINARY\n  TRACK 01 MODE2/2352\n");

            var set = FileSetResolver.Resolve(cuePath);
            var all = set.AllFiles.ToList();

            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all[0], Is.EqualTo(cuePath));
            Assert.That(Path.GetFileName(all[1]), Is.EqualTo("SCES_002.55.Tekken 2 (EU).bin"));
        }

        [Test]
        public void Resolve_GDI_QuotedTrackNames()
        {
            // Redump quotes track names with spaces
            File.WriteAllText(Path.Combine(_tempDir, "Crazy Taxi (USA) (Track 1).bin"), "data");
            File.WriteAllText(Path.Combine(_tempDir, "Crazy Taxi (USA) (Track 2).raw"), "data");
            var gdiPath = Path.Combine(_tempDir, "Crazy Taxi (USA).gdi");
            File.WriteAllText(gdiPath,
                "2\n" +
                "1 0 4 2352 \"Crazy Taxi (USA) (Track 1).bin\" 0\n" +
                "2 756 0 2352 \"Crazy Taxi (USA) (Track 2).raw\" 0\n");

            var set = FileSetResolver.Resolve(gdiPath);

            Assert.That(set.CompanionFiles.Select(Path.GetFileName),
                Is.EqualTo(new[] { "Crazy Taxi (USA) (Track 1).bin", "Crazy Taxi (USA) (Track 2).raw" }));
        }

        [Test]
        public void Resolve_CueFileInAnotherCase_FindsTheBin()
        {
            File.WriteAllText(Path.Combine(_tempDir, "Track 01.bin"), "data");
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath, "FILE \"TRACK 01.BIN\" BINARY\n");

            var set = FileSetResolver.Resolve(cuePath);

            Assert.That(set.CompanionFiles.Select(Path.GetFileName), Is.EqualTo(new[] { "Track 01.bin" }).IgnoreCase);
        }

        [Test]
        public void Resolve_SameStemInAnotherCase_IsClaimed()
        {
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            var binPath = Path.Combine(_tempDir, "GAME.bin");
            File.WriteAllText(cuePath, string.Empty);
            File.WriteAllText(binPath, "data");

            Assert.That(FileSetResolver.Resolve(cuePath).CompanionFiles, Is.EqualTo(new[] { binPath }), "cue without usable FILE lines");
            Assert.That(FileSetResolver.Resolve(binPath).CompanionFiles, Is.EqualTo(new[] { cuePath }), "bin pulls its cue");
        }

        [Test]
        public void Resolve_M3UWrittenOnWindows_FindsItsDiscs()
        {
            var disc1 = Path.Combine(_tempDir, "Disc1", "Game (Disc 1).chd");
            var disc2 = Path.Combine(_tempDir, "Game (Disc 2).chd");
            Directory.CreateDirectory(Path.GetDirectoryName(disc1)!);
            File.WriteAllText(disc1, "data");
            File.WriteAllText(disc2, "data");
            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "Disc1\\Game (Disc 1).chd\r\nGAME (DISC 2).CHD\r\n");

            var set = FileSetResolver.Resolve(m3uPath);

            Assert.That(set.CompanionFiles, Is.EqualTo(new[] { disc1, disc2 }).IgnoreCase);
        }

        [Test]
        public void Resolve_M3UFolderInAnotherCase_FindsItsDisc()
        {
            var disc = Path.Combine(_tempDir, "CD1", "Game (Disc 1).chd");
            Directory.CreateDirectory(Path.GetDirectoryName(disc)!);
            File.WriteAllText(disc, "data");
            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "cd1/game (disc 1).chd\n");

            Assert.That(FileSetResolver.Resolve(m3uPath).CompanionFiles, Is.EqualTo(new[] { disc }).IgnoreCase);
        }

        // Another case is only looked for below the playlist's folder
        [Test, Platform(Exclude = "Win,MacOsX")]
        public void Resolve_M3UEntryOutsideItsFolder_OnlyAsWritten()
        {
            var shared = Directory.CreateDirectory(Path.Combine(_tempDir, "Shared")).FullName;
            File.WriteAllText(Path.Combine(shared, "Game (Disc 1).chd"), "data");
            var m3uPath = Path.Combine(Directory.CreateDirectory(Path.Combine(_tempDir, "Game")).FullName, "Game.m3u");
            File.WriteAllText(m3uPath, "../Shared/GAME (DISC 1).CHD\n");

            Assert.That(FileSetResolver.Resolve(m3uPath).CompanionFiles, Is.Empty);
        }

        // Many files of one folder resolved with one read of it find what each finds reading the folder itself
        [Test]
        public void Resolve_WithTheFolderReadOnce_FindsTheSameFiles()
        {
            foreach (var name in new[] { "Game.iso", "GAME.SUB", "Game.img", "Game.", "Game", "Game.part.bin", ".Game.bin", "Game [!].iso", "Game [!].bin",
                "Other.bin", "Other.sbi", "Broken.cue", "Broken.bin", "Tracks.cue", "Tracks (Track 1).bin", "Set.m3u", "Set (Disc 1).iso", "Set (Disc 1).ccd" })
            {
                var text = name switch { "Tracks.cue" => "FILE \"Tracks (Track 1).bin\" BINARY", "Set.m3u" => "Set (Disc 1).iso", _ => "data" };
                File.WriteAllText(Path.Combine(_tempDir, name), text);
            }
            Directory.CreateDirectory(Path.Combine(_tempDir, "Game.bin"));
            if (!OperatingSystem.IsWindows()) File.WriteAllText(Path.Combine(_tempDir, "Gam?.iso"), "data");

            var listings = new Dictionary<string, ILookup<string, string>>();
            foreach (var file in Directory.GetFiles(_tempDir).OrderBy(f => f, StringComparer.Ordinal))
            {
                Assert.That(FileSetResolver.Resolve(file, listings).AllFiles, Is.EqualTo(FileSetResolver.Resolve(file).AllFiles), Path.GetFileName(file));
            }
            Assert.That(FileSetResolver.Resolve(Path.Combine(_tempDir, "Game.iso"), listings).CompanionFiles, Is.Not.Empty);
        }

        // Descriptors written on Windows name their tracks in another case: with the folder read once, each finds
        // what it finds reading the folder itself
        [Test]
        public void Resolve_ReferencesInAnotherCase_WithTheFolderReadOnce_FindTheSameFiles()
        {
            var files = new Dictionary<string, string>
            {
                ["Tracks.cue"] = "FILE \"TRACKS (TRACK 1).BIN\" BINARY\nFILE \"tracks (track 2).bin\" BINARY\nFILE \"Tracks (Track 3).bin\" BINARY\n",
                ["Tracks (Track 1).bin"] = "data", ["Tracks (Track 2).bin"] = "data", ["Tracks (Track 3).bin"] = "data",
                ["Taxi.gdi"] = "2\n1 0 4 2352 \"TAXI (TRACK 1).BIN\" 0\n2 756 0 2352 taxi(track2).raw 0\n",
                ["Taxi (Track 1).bin"] = "data", ["Taxi(Track2).RAW"] = "data",
                ["Set.m3u"] = "SET (DISC 1).CUE\nDisc2\\SET (DISC 2).CHD\n",
                ["Set (Disc 1).cue"] = "FILE \"set (disc 1).BIN\" BINARY\n", ["Set (Disc 1).bin"] = "data", ["disc2/Set (Disc 2).chd"] = "data",
                ["Dead.cue"] = "FILE \"elsewhere.bin\" BINARY\n", ["DEAD.bin"] = "data", ["Dead.sub"] = "data",
                ["Two.cue"] = "FILE \"two.bin\" BINARY\n", ["TWO.BIN"] = "data", ["Two.Bin"] = "data",
                ["Nested.cue"] = "FILE \"DISC2\\SET (DISC 2).CHD\" BINARY\n",
            };
            foreach (var (name, text) in files)
            {
                var path = Path.Combine(_tempDir, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }

            var listings = new Dictionary<string, ILookup<string, string>>();
            foreach (var file in Directory.GetFiles(_tempDir).OrderBy(f => f, StringComparer.Ordinal))
                Assert.That(FileSetResolver.Resolve(file, listings).AllFiles, Is.EqualTo(FileSetResolver.Resolve(file).AllFiles), Path.GetFileName(file));
            Assert.That(FileSetResolver.Resolve(Path.Combine(_tempDir, "Tracks.cue"), listings).CompanionFiles, Has.Count.EqualTo(3));
        }

        // A track named in another case is looked up in the folder as read, not by reading the folder again
        [Test]
        public void Resolve_ReferenceInAnotherCase_IsFoundInTheFolderAsRead()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) Assert.Ignore("needs a file system that tells case apart");
            var cuePath = Path.Combine(_tempDir, "Game.cue");
            File.WriteAllText(cuePath, "FILE \"GAME (TRACK 1).BIN\" BINARY\n");
            var track = Path.Combine(_tempDir, "Game (Track 1).bin");
            var listings = new Dictionary<string, ILookup<string, string>>
            {
                [_tempDir] = new[] { cuePath, track }.ToLookup(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            };

            Assert.That(FileSetResolver.Resolve(cuePath, listings).CompanionFiles, Is.EqualTo(new[] { track }));
        }

        [Test]
        public void Resolve_M3UListingItself_IsNotFollowed()
        {
            // "ls > Game.m3u" lists the playlist too; following it overflowed the stack
            var disc = Path.Combine(_tempDir, "Game (Disc 1).chd");
            File.WriteAllText(disc, "data");
            var m3uPath = Path.Combine(_tempDir, "Game.m3u");
            File.WriteAllText(m3uPath, "Game (Disc 1).chd\nGame.m3u\n");

            Assert.That(FileSetResolver.Resolve(m3uPath).CompanionFiles, Is.EqualTo(new[] { disc }));
        }

        [Test]
        public void IsContent_DescriptorsOnlyTheResolverCanRead_Count()
        {
            // The missing-content check reads descriptors through the resolver. None of these data
            // files is named after its descriptor, so without the resolver the game looked empty.
            var m3uDir = Directory.CreateDirectory(Path.Combine(_tempDir, "m3u")).FullName;
            Directory.CreateDirectory(Path.Combine(m3uDir, "Disc1"));
            File.WriteAllText(Path.Combine(m3uDir, "Disc1", "Game (Disc 1).chd"), "data");
            var m3uPath = Path.Combine(m3uDir, "Game.m3u");
            File.WriteAllText(m3uPath, "Disc1\\Game (Disc 1).chd\n");

            var cueDir = Directory.CreateDirectory(Path.Combine(_tempDir, "cue")).FullName;
            File.WriteAllText(Path.Combine(cueDir, "Track 01.bin"), "data");
            var cuePath = Path.Combine(cueDir, "Game.cue");
            File.WriteAllText(cuePath, "FILE \"TRACK 01.BIN\" BINARY\n");

            var gdiDir = Directory.CreateDirectory(Path.Combine(_tempDir, "gdi")).FullName;
            File.WriteAllText(Path.Combine(gdiDir, "Crazy Taxi (USA) (Track 1).bin"), "data");
            var gdiPath = Path.Combine(gdiDir, "disc.gdi");
            File.WriteAllText(gdiPath, "1\n1 0 4 2352 \"Crazy Taxi (USA) (Track 1).bin\" 0\n");

            Assert.That(MediaScannerService.IsContent(m3uPath), Is.True, "m3u with backslashes");
            Assert.That(MediaScannerService.IsContent(cuePath), Is.True, "cue FILE in another case");
            Assert.That(MediaScannerService.IsContent(gdiPath), Is.True, "quoted gdi track names");
        }
    }
}
