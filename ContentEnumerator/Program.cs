using RocksmithToolkitLib.PSARC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ContentEnumerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var disliked = new HashSet<string>(Directory.GetFiles(@"Z:\Incoming\Rocksmith\0\", "*.psarc", SearchOption.AllDirectories)
                .Select(x => Path.GetFileName(x)));

            foreach (var path in Directory.GetFiles(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc\", "*.psarc", SearchOption.AllDirectories))
            {
                if (disliked.Contains(Path.GetFileName(path)))
                {
                    File.Delete(path);
                }
            }

            foreach (var dir in Directory.GetDirectories(@"Z:\Incoming\"))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("Song Pack "))
                {
                    TrimEmpty(dir);
                }
            }

            var targetBase = @"Z:\Incoming\Rocksmith\";

            //var path = @"Z:\Incoming\Song Pack I\-008 - Green Day Song Pack\americanidiot_p.psarc";
            foreach (var path in Directory.GetFiles(@"Z:\Incoming\", "*_p.psarc", SearchOption.AllDirectories))
            {
                if (path.StartsWith(targetBase))
                    continue;

                Metadataize(path, targetBase);
            }
        }

        private static bool TrimEmpty(string path)
        {
            if (Directory.GetFiles(path).Any())
                return false;
            var isEmpty = true;
            foreach (var sub in Directory.GetDirectories(path))
            {
                if (!TrimEmpty(sub))
                    isEmpty = false;
            }
            if (isEmpty)
            {
                Directory.Delete(path);
            }
            return isEmpty;
        }

        private static void Metadataize(string path, string targetBase)
        {
            var psarc = new PSARC();
            using (var stream = File.OpenRead(path))
            {
                psarc.Read(stream);
            }
            string albumName = null;
            string artistName = null;
            string songName = null;
            var regex = new Regex(@"""(?<key>AlbumName|ArtistName|SongName)""\s*:\s*""(?<value>[^""]+)"",");
            foreach (var entry in psarc.TOC)
            {
                //Console.WriteLine($"{entry.Name} [{entry.Length}]");
                if (Path.GetExtension(entry.Name) == ".json")
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var stream = entry.Data)
                        {
                            stream.CopyTo(memoryStream);
                        }
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                        foreach (var line in json.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var match = regex.Match(line);
                            if (match.Success)
                            {
                                void setName(ref string name)
                                {
                                    var value = match.Groups["value"].Value;
                                    if (name == null)
                                    {
                                        name = value;
                                    }
                                    else if (name != match.Groups["value"].Value)
                                    {
                                        name += "|" + value;
                                    }
                                }
                                switch (match.Groups["key"].Value)
                                {
                                    case "AlbumName":
                                        setName(ref albumName);
                                        break;
                                    case "ArtistName":
                                        setName(ref artistName);
                                        break;
                                    case "SongName":
                                        setName(ref songName);
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            //Console.WriteLine($"{artistName}\t{albumName}\t{songName}\t{Path.GetFileName(path)}");

            if (artistName == null || artistName.Contains('|'))
                return;
            if (albumName == null || albumName.Contains('|'))
                return;
            if (songName == null || songName.Contains('|'))
                return;

            Fix(ref artistName);
            Fix(ref albumName);
            Fix(ref songName);

            var targetDirectory = Path.Combine(targetBase, artistName, albumName, songName);

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Move(path, Path.Combine(targetDirectory, Path.GetFileName(path)));
        }

        private static void Fix(ref string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
        }
    }
}
