# SeekDownloader

A simple to use, commandline tool, for downloading from the SoulSeek network

# Description of arguments
| Longname Argument  | Shortname Argument | Description | Example |
| ------------- | ------------- | ------------- | ------------- |
| --download-file-path | -D | Download path to store the downloads. (Required) | -D "~/Downloads" |
| --soulseek-listen-port | -p | Soulseek listen port (used for portforwarding). (Required) | -p 12345 |
| --soulseek-username | -u | Soulseek username for login. (Required) | -u "John" |
| --soulseek-password | -P | Soulseek password for login. (Required) | -P "Doe" |
| --music-library | -m | Music Library path to use to check for existing local songs. (Required) | -m "~/Music" |
| --search-term | -s | Search term used to search for music use the order, Artist - Album - Track. | -s deadmau5 |
| --search-file-path | -S | Search term(s) used to search for music use from a file. | -S "./search-songs.txt" |
| --thread-count | -t | Download threads to use. (Default: 10) | -t 5 |
| --music-libraries | -M | Multiple Music Library path(s) to use to check for existing local songs. | -m ["\~/Music", "~/nfs_share_Music"] |
| --filter-out-file-names | -F | Filter out names to ignore for downloads. | -F ["jazz", "live", "concert", "classic"] |

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
