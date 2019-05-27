Lyrics
======

Lyrics for Spotify, in real-time. Uses [Musixmatch](https://www.musixmatch.com) as a backend,
but it actually works.

It works, but the UI is **very** simple for now, so it's more of a POC.

## How to use?
1. Start the software.
2. It will prompt for some information, like the title of the current song, or its artist.
3. With the given information, it will try to find matching lyrics, and it will watch
   Spotify's internal memory to know when the current song changes, or when its position
   changes, updating things automatically as much as possible.
4. If the updated information is invalid, you may click on the current time, song title,
   or artist to update it manually.
