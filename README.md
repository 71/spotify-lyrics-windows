Lyrics
======

Lyrics for Spotify, in real-time. Uses [Musixmatch](https://www.musixmatch.com) as a backend,
but it actually works.

It works, but the UI is **very** simple for now, so it's more of a POC.


## How does it work?
1. When a song is playing, the title of the main Spotify window is scanned,
   and the title / artist are extracted from it.
2. Several times per second, the memory of the Spotify process is scanned to find
   strings that match a timestamp (`0:00` or `00:00`), and all these strings are cached.
3. The program attempts to find the timestamp that is most likely to be the "current
   song position" timestamp, using the following criteria:
   - If a new song just started playing, we select the timestamp which is the lowest,
     but which was high before (since the text likely went from `3:42` to `0:00`).
   - If the same song is playing, we select the first timestamp that increased by 1
     since the last analysis (since a song will likely go from `3:41` to `3:42`).
   - If no song is selected this way, we take the last timestamp that matched either
     one of the previous conditions.
