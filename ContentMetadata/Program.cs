using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace ContentMetadata
{
    internal class Metadata
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public int Year { get; set; }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            foreach (var zipPath in Directory.GetFiles(@"D:\zipdlc\", "*.zip"))
            {
                using (var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Read))
                {
                    var metadatas = new List<Metadata>();
                    foreach (var zipEntry in zipArchive.Entries)
                    {
                        var xml = new XmlDocument();
                        using (var stream = zipEntry.Open())
                        {
                            xml.Load(stream);
                        }
                        var songElement = (XmlElement?)xml.SelectSingleNode("/song");
                        var vocalsElement = (XmlElement?)xml.SelectSingleNode("/vocals");
                        if (songElement != null)
                        {
                            var title = Fix(songElement.SelectSingleNode("title")!.InnerText);
                            var artist = Fix(songElement.SelectSingleNode("artistName")!.InnerText);
                            var album = Fix(songElement.SelectSingleNode("albumName")!.InnerText);
                            var year = int.Parse(songElement.SelectSingleNode("albumYear")!.InnerText);
                            var metadata = new Metadata
                            {
                                Title = title,
                                Artist = artist,
                                Album = album,
                                Year = year,
                            };
                            if (metadatas.Count == 0)
                            {
                                metadatas.Add(metadata);
                            }
                            else
                            {
                                bool seen = false;
                                foreach (var check in metadatas)
                                {
                                    if (check.Title == metadata.Title
                                        && check.Artist == metadata.Artist
                                        && check.Album == metadata.Album
                                        && check.Year == metadata.Year)
                                    {
                                        seen = true;
                                        break;
                                    }
                                }
                                if (!seen)
                                {
                                    metadatas.Add(metadata);
                                }
                            }
                        }
                        else if (vocalsElement != null)
                        {
                            // No metadata to scrape here
                        }
                        else
                        {
                            //WTF!
                            Console.Error.WriteLine("Unexpected root element");
                        }
                    }

                    if (metadatas.Count == 0)
                    {
                        Console.WriteLine(Path.GetFileNameWithoutExtension(zipPath));
                    }
                    else
                    {
                        foreach (var metadata in metadatas)
                        {
                            Console.WriteLine($"{Path.GetFileNameWithoutExtension(zipPath)}\t{metadata?.Title}\t{metadata?.Artist}\t{metadata?.Album}\t{metadata?.Year}");
                        }
                    }
                }
            }
        }

        private readonly static Regex _whitespace = new Regex(@"\s+");

        private static string Fix(string value)
        {
            return _whitespace.Replace(value.Trim(), " ");
        }
    }
}