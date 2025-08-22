# SeekDownloader

A simple to use, commandline tool, for downloading from the SoulSeek network

When selecting your music library(ies) by using the parameters -m/-M it will only try to download what music you're missing from your library, avoiding duplicate music/downloads, this is the main power of the entire tool, skipping music you already own and only download what you're missing out on.

Take in mind that the downloads will not move into your music library, this is a very complex process.

If want to move the music from Downloads into your Music Library, take a look at my other project, https://github.com/MusicMoveArr/MusicMover

Loving the work I do? buy me a coffee https://buymeacoffee.com/musicmovearr

# Usage example
```
dotnet SeekDownloader.dll \
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
| --search-term | -s | Search term used to search for music use the order, Artist - Album - Track. | -s "deadmau5" |
| --search-file-path | -S | Search term(s) used to search for music use from a file. | -S "./search-songs.txt" |
| --thread-count | -t | Download threads to use. (Default: 10) | -t 5 |
| --music-library | -m | Music Library path to use to check for existing local songs. (Required) | -m "~/Music" |
| --music-libraries | -M | Multiple Music Library path(s) to use to check for existing local songs. | -m ["\~/Music", "~/nfs_share_Music"] |
| --filter-out-file-names | -F | Filter out names to ignore for downloads, seperated by space in commandline, seperated by ':' in docker/environment variable. | -F jazz live concert classic |
| --grouped-downloads | -G | Put each search into his own download thread. | -G |
| --download-singles | -DS | When combined with Grouped Downloads, it will quit downloading the entire group after 1 song finished downloading. | -DS |
| --search-delimeter | -SD | Search term(s) delimeter is used to take the correct Artist, Album, Track names from your Search Term(s). | -SD |
| --update-album-name | -UA | Update the Album name's tag by your search term, only updates if Trackname matches as well for +90%. | -UA |
| --allow-non-tagged-files |  | Allow non-tagged files, original music-files do not contain tags either. | true |
| --check-tags |  | Check the tags if we downloaded the correct track. | true |
| --check-tags-delete |  | If the tags do not match the search, delete after download. | true |
| --output-status |  | Output the overall status and of each thread. | true |
| --search-file-extensions |  | Search for specific file extensions, seperated by space in commandline, seperated by ':' in docker/environment variable. | wav flac opus |
| --music-library-match |  | Set the hitrate percentage against your own music library, if it hits it will skip the download. | 50 |
| --music-library-quick-match |  | Quickly try to find only the missing tracks from the search. | true |
| --max-file-size |  | Set the max file size to download in Megabytes (MB). | 50 |
| --in-memory-downloads |  | Store the downloads temporarily in memory, only successful downloads are written to disk. | true |
| --in-memory-downloads-max-size |  | Store the downloads temporarily in memory only if smaller then X MB else the disk is used as normal. | 100 |

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
| Artist - Album - Track.ext | ^(.+?)\s-\s(.+?)\s-\s(\d{2}(?:-\d{2})?)\s-\s(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber-DiscNumber Artist - TrackName.ext (dot is optional) | ^(\d{1,3})-(\d{1,3})[\.]{0,1}(.+?)[\s]{0,}-[\s]{0,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber-DiscNumber TrackName.ext (dot is optional) | ^(\d{1,3})-(\d{1,3})[\.]{0,1}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber-Artist-TrackName.ext | ^(\d{1,3})[\s]{0,}-[\s]{0,}(.+?)[\s]{0,}-[\s]{0,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber. Artist - TrackName.ext ('.' or '-') | ^(\d{1,3})[\s]{0,}(.+?)[-\.]{1}[\s]{0,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber. TrackName.ext ('.' or '-') | ^(\d{1,3})[\s]{0,}[-\.]{1}[\s]{0,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackNumber TrackName.ext (without '.' or '-') | ^(\d{1,3})[\s]{1,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| Artist - Track.ext | ^(.+?)[\s]{0,}-[\s]{0,}(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |
| TrackName.ext | ^(?\<track>(.+?))\.(mp3\|flac\|m4a\|opus\|wav\|aiff)$ |

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

# Docker Compose example
Keep note of the multi-value seperator ":"

This process will run every 6 hours based on the quartz cronjob format which includes seconds

Environment variables with a missing "=" are a boolean, no value is needed, you set the "true" without "=true"

```
services:
  seekdownloader:
    image: musicmovearr/seekdownloader:latest
    container_name: SeekDownloader
    restart: unless-stopped
    user: "1000:1000"
    environment:
      - PUID=1000
      - PGID=1000
      - CRON=0 0 */6 ? * *
      - SEEK_DOWNLOADFILEPATH=/Downloads
      - SEEK_SOULSEEKLISTENPORT=12345
      - SEEK_SOULSEEKUSERNAME=username_here
      - SEEK_SOULSEEKPASSWORD=password_here
      - SEEK_SEARCHDELIMETER=-
      - SEEK_MUSICLIBRARY=/music
      - SEEK_SEARCHTERM=
      - SEEK_SEARCHFILEPATH=/Downloads/seek.txt
      - SEEK_THREADCOUNT=10
      - SEEK_GROUPEDDOWNLOADS=true
      - SEEK_DOWNLOADSINGLES=true
      - SEEK_UPDATEALBUMNAME=true
      - SEEK_MUSICLIBRARIES=/music:/nfs_share/
      - SEEK_FILTEROUTFILENAMES=live:concert:acoustic
      - SEEK_CHECKTAGS=true
      - SEEK_CHECKTAGSDELETE=true
      - SEEK_OUTPUTSTATUS=false
      - SEEK_ALLOWNONTAGGEDFILES=false
      - SEEK_FILEEXTENSIONS=opus:flac:wav
      - SEEK_MUSICLIBRARY_MATCH=50
      - SEEK_MUSICLIBRARY_QUICK_MATCH=true
      - SEEK_MAX_FILE_SIZE=50
      - SEEK_IN_MEMORY_DOWNLOADS=true
      - SEEK_IN_MEMORY_DOWNLOADS_MAX_SIZE=100 
    volumes:
      - ~/Music:/Music
      - ~/nfs_share:/nfs_share
      - ~/Downloads:/Downloads
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
