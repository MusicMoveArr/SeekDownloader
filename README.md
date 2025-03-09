# SeekDownloader

A simple to use, commandline tool, for downloading from the SoulSeek network

When selecting your music library(ies) by using the parameters -m/-M it will only try to download what music you're missing from your library, avoiding duplicate music/downloads, this is the main power of the entire tool, skipping music you already own and only download what you're missing out on.

Take in mind that the downloads will not move into your music library, this is a very complex process.

If want to move the music from Downloads into your Music Library, take a look at my other project, https://github.com/MusicMoveArr/MusicMover

Loving the work I do? buy me a coffee https://buymeacoffee.com/musicmovearr

# Usage example
```
dotnet SeekDownloader \
--soulseek-username "John" \
--soulseek-password "Doe" \
--soulseek-listen-port 12345 \
--download-file-path "~/Downloads" \
--music-library "~/Music" \
--search-term "deadmau5"
```

# Description of arguments
| Longname Argument  | Shortname Argument | Description | Example |
| ------------- | ------------- | ------------- | ------------- |
| --download-file-path | -D | Download path to store the downloads. (Required) | -D "~/Downloads" |
| --soulseek-listen-port | -p | Soulseek listen port (used for portforwarding). (Required) | -p 12345 |
| --soulseek-username | -u | Soulseek username for login. (Required) | -u "John" |
| --soulseek-password | -P | Soulseek password for login. (Required) | -P "Doe" |
| --music-library | -m | Music Library path to use to check for existing local songs. (Required) | -m "~/Music" |
| --search-term | -s | Search term used to search for music use the order, Artist - Album - Track. | -s "deadmau5" |
| --search-file-path | -S | Search term(s) used to search for music use from a file. | -S "./search-songs.txt" |
| --thread-count | -t | Download threads to use. (Default: 10) | -t 5 |
| --music-libraries | -M | Multiple Music Library path(s) to use to check for existing local songs. | -m ["\~/Music", "~/nfs_share_Music"] |
| --filter-out-file-names | -F | Filter out names to ignore for downloads. | -F ["jazz", "live", "concert", "classic"] |

# How MusicLibrary Filtering works
When selecting your music library(ies) by -m/-M to filter out downloads/music we already own, we will use the following regex'es on soulseek files

Besides the regex patterns, it will filter out for example [123ABC] at the end of the filenames which (I think/believe) are youtube video id's

These video id's will make the detection fail on what we already own for music

Next we will try to match what we already own against the file list we receive from Soulseek, here we're using Fuzzy filename matching, at 50% and above it's a match

For now (maybe change in the future) it will become an option to change the ratio, but I found 50% to work the best because of all the weird filenames everyone uses

For the matching to work it will expect the following filesystem structure, MusicLibrary/ArtistName/

Only the ArtistName folder is critical to prevent reading thousands of files/folders

## Filename Patterns
| Description  | Pattern |
| ------------- | ------------- |
| Artist Album Number Track | ^([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}(\d{1,3}) (?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Number Artist Track | ^(\d{1,3})[ ]{0,1}-[ ]{0,1}([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}(?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Number Artist Track | ^(\d{1,3})\. ([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}(?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Collection Year Artist Track | ^([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}\d{4}[ ]{0,1}-[ ]{0,1}(\d{1,3}) ([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}(?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Artist Track | ^([\w\s\d\p{P}]+)[ ]{0,1}-[ ]{0,1}(?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Disc Number Track | ^(\d{1,3})\. (?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |
| Number Track | ^(\d{1,3}) (?\<track>([\w\s\d\p{P}]+))\.(mp3\|flac\|m4a\|opus)$ |

# Textfile Search
When you want to search for a lot of songs use the following format for your goal

For each line in the text file you can use all formats,

And of course specify the file location by using --search-file-path or -S

```
Artist
```
```
Artist - Trackname
```
```
Artist - Album - Trackname
```

# Build
## ArchLinux
```
sudo pacman -Syy dotnet-sdk-8.0 git
git clone https://github.com/MusicMoveArr/SeekDownloader.git
cd SeekDownloader
dotnet restore
dotnet build
cd SeekDownloader/bin/Debug/net8.0
dotnet SeekDownloader.dll --help
```
