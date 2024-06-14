using ICSharpCode.SharpZipLib.Zip;
using RocksmithToolkitLib;
using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.Ogg;
using RocksmithToolkitLib.PSARC;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib.Sng2014HSL;
using RocksmithToolkitLib.XML;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ContentExtractor
{
    class Program
    {
        readonly static Platform _platform = new Platform(GamePlatform.Pc, GameVersion.RS2014);
        readonly static string _ppsarcSuffix = "_p.psarc";

        static void Main(string[] args)
        {
            //var psarcSource = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Desktop\dlc\");
            var psarcSource = @"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc\";
            var zipDestination = @".\zipdlc\";

            foreach (var psarcPath in Directory.GetFiles(psarcSource, "*" + _ppsarcSuffix, SearchOption.AllDirectories))
            {
                

                //Scan(psarcPath);

                Extract(psarcPath, zipDestination, overwrite: true);
            }
        }

        private static void Scan(string psarcPath)
        {
            var psarc = new PSARC();
            using (var stream = File.OpenRead(psarcPath))
            {
                psarc.Read(stream);
            }
            Console.WriteLine("PSARC: " + Path.GetFileName(psarcPath));
            foreach (var psarcEntry in psarc.TOC)
            {
                Console.WriteLine("Name: " + psarcEntry.Name);
                Console.WriteLine("Length: " + psarcEntry.Length);
                Console.WriteLine("------");
            }
        }

        private static Manifest2014<Attributes2014> LoadManifest(string manifestPath)
        {
            return Manifest2014<Attributes2014>.LoadFromFile(manifestPath);
        }

        private static Sng2014File LoadSong(string songPath)
        {
            return Sng2014File.LoadFromFile(songPath, _platform);
        }

        private static void Extract(string psarcPath, string zipDestination, bool overwrite)
        {
            var fileName = Path.GetFileName(psarcPath);
            var code = fileName.Substring(0, fileName.Length - _ppsarcSuffix.Length);
            if (code == "rs1compatibilitydlc")
                return;
            var zipPath = Path.Combine(zipDestination, code + ".zip");
            if (File.Exists(zipPath) && !overwrite)
            {
                Console.WriteLine("File already extracted: " + zipPath);
                return;
            }
            //if (new FileInfo(zipPath).Length > 500_000)
            //{
            //    return;
            //}
            try
            {
                var psarc = new PSARC();
                using (var stream = File.OpenRead(psarcPath))
                {
                    psarc.Read(stream);
                }
                var psarcEntriesByExtension = new Dictionary<string, List<Entry>>();
                foreach (var psarcEntry in psarc.TOC)
                {
                    var extension = Path.GetExtension(psarcEntry.Name);
                    if (!psarcEntriesByExtension.TryGetValue(extension, out var psarcEntries))
                    {
                        psarcEntries = new List<Entry>();
                        psarcEntriesByExtension.Add(extension, psarcEntries);
                    }
                    psarcEntries.Add(psarcEntry);

                }
                if (!psarcEntriesByExtension.TryGetValue(".wem", out var audioEntries))
                {
                    audioEntries = new List<Entry>();
                }
                if (!psarcEntriesByExtension.TryGetValue(".sng", out var songEntries))
                {
                    songEntries = new List<Entry>();
                }
                if (!psarcEntriesByExtension.TryGetValue(".json", out var manifestEntries))
                {
                    manifestEntries = new List<Entry>();
                }
                var songEntryByName = songEntries.ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name));
                var manifestEntryByName = manifestEntries.ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name));

                Debug.Assert(songEntryByName.Keys.OrderBy(x => x).SequenceEqual(manifestEntryByName.Keys.OrderBy(x => x)));

                using (var memoryStream = new MemoryStream())
                using (var zipOutputStream = new ZipOutputStream(memoryStream))
                {
                    foreach (var name in songEntryByName.Keys)
                    {
                        var songEntry = songEntryByName[name];
                        var tempSongPath = Path.Combine(Path.GetTempPath(), name + ".sng");
                        WriteTempFile(songEntry, tempSongPath);
                        Sng2014File song;
                        try
                        {
                            song = LoadSong(tempSongPath);
                        }
                        finally
                        {
                            File.Delete(tempSongPath);
                        }

                        var manifestEntry = manifestEntryByName[name];
                        var tempManifestPath = Path.Combine(Path.GetTempPath(), name + ".json");
                        WriteTempFile(manifestEntry, tempManifestPath);
                        Manifest2014<Attributes2014> manifest;
                        try
                        {
                            manifest = LoadManifest(tempManifestPath);
                        }
                        finally
                        {
                            File.Delete(tempManifestPath);
                        }

                        var attributes = manifest.Entries.First().Value.First().Value;

                        ArrangementType getArrangementType()
                        {
                            switch (attributes.ArrangementType)
                            {
                                case null:
                                    return ArrangementType.Vocal;
                                case 0: // Lead
                                    return ArrangementType.Guitar;
                                case 1: // Rhythm
                                    return ArrangementType.Guitar;
                                case 2: // Combo
                                    return ArrangementType.Guitar;
                                case 3:
                                    return ArrangementType.Bass;
                                default:
                                    return ArrangementType.Unknown;
                            }
                        }

                        dynamic xml = null;

                        if (getArrangementType() == ArrangementType.Vocal)
                        {
                            xml = new Vocals(song);
                        }
                        else
                        {
                            xml = new Song2014(song, attributes);
                        }

                        Console.WriteLine(name);
                        var zipEntry = new ZipEntry(name + ".xml");
                        zipOutputStream.PutNextEntry(zipEntry);
                        using (var xmlStream = new MemoryStream())
                        {
                            xml.Serialize(xmlStream, true);
                            xmlStream.CopyTo(zipOutputStream);
                        }
                        zipOutputStream.CloseEntry();
                    }

                    var audioEntry = audioEntries.OrderByDescending(x => x.Length)
                        .First();
                    var tempAudioPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(audioEntry.Name));
                    var tempOggPath = Path.Combine(Path.GetTempPath(), code + "_audio.ogg");
                    WriteTempFile(audioEntry, tempAudioPath);
                    try
                    {
                        var wwiseVersion = GetWwiseVersion(audioEntry.Name);
                        var tmp = ExternalApps.TOOLKIT_ROOT;
                        ExternalApps.TOOLKIT_ROOT = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents\GitHub\rocksmith-custom-song-toolkit\RocksmithTookitGUI\bin\Debug");
                        try
                        {
                            OggFile.Revorb(tempAudioPath, tempOggPath, wwiseVersion);
                        }
                        finally
                        {
                            ExternalApps.TOOLKIT_ROOT = tmp;
                        }

                        Console.WriteLine("Audio");
                        var zipEntry = new ZipEntry(Path.GetFileName(tempOggPath));
                        zipOutputStream.PutNextEntry(zipEntry);
                        using (var stream = File.OpenRead(tempOggPath))
                        {
                            stream.CopyTo(zipOutputStream);
                        }
                        zipOutputStream.CloseEntry();
                    }
                    finally
                    {
                        File.Delete(tempAudioPath);
                    }
                    

                    zipOutputStream.Finish();
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    using (var fileStream = File.Create(zipPath))
                    {
                        memoryStream.CopyTo(fileStream);
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private static OggFile.WwiseVersion GetWwiseVersion(string extension)
        {
            switch (Path.GetExtension(extension))
            {
                case ".ogg":
                    return OggFile.WwiseVersion.Wwise2010;
                case ".wem":
                    return OggFile.WwiseVersion.Wwise2013;
                default:
                    throw new InvalidOperationException("Audio file not supported.");
            }
        }

        private static void WriteTempFile(Entry psarcEntry, string tempFileName)
        {
            using (var fileStream = File.Create(tempFileName))
            using (var psarcEntryStream = psarcEntry.Data)
            {
                psarcEntryStream.CopyTo(fileStream);
            }
        }
    }
}
