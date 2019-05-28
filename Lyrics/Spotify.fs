module Lyrics.Spotify

open System
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices


[<AutoOpen>]
module private Native =
    [<Literal>]
    let PROCESS_QUERY_INFORMATION = 0x0400
    [<Literal>]
    let PROCESS_WM_READ           = 0x0010

    [<Struct>]
    type AllocationProtection =
       | PAGE_EXECUTE           = 0x00000010
       | PAGE_EXECUTE_READ      = 0x00000020
       | PAGE_EXECUTE_READWRITE = 0x00000040
       | PAGE_EXECUTE_WRITECOPY = 0x00000080
       | PAGE_NOACCESS          = 0x00000001
       | PAGE_READONLY          = 0x00000002
       | PAGE_READWRITE         = 0x00000004
       | PAGE_WRITECOPY         = 0x00000008
       | PAGE_GUARD             = 0x00000100
       | PAGE_NOCACHE           = 0x00000200
       | PAGE_WRITECOMBINE      = 0x00000400

    [<Struct>]
    type State =
        | MEM_COMMIT  = 0x1000
        | MEM_FREE    = 0x10000
        | MEM_RESERVE = 0x2000

    [<Struct>]
    type MEMORY_BASIC_INFORMATION =
        val BaseAddress      : nativeint
        val AllocationBase   : nativeint
        val AllocationProtect: AllocationProtection
        val RegionSize       : nativeint
        val State            : State
        val Protect          : AllocationProtection
        val Type             : uint32

    [<Struct>]
    type SYSTEM_INFO =
        val processorArchitecture    : uint16
        val reserved                 : uint16
        val pageSize                 : uint32
        val minimumApplicationAddress: nativeint
        val maximumApplicationAddress: nativeint
        val activeProcessorMask      : nativeint
        val numberOfProcessors       : uint32
        val processorType            : uint32
        val allocationGranularity    : uint32
        val processorLevel           : uint16
        val processorRevision        : uint16

    [<DllImport("kernel32.dll")>]
    extern nativeint OpenProcess(int, bool, int)

    [<DllImport("kernel32.dll")>]
    extern bool CloseHandle(nativeint)

    [<DllImport("kernel32.dll")>]
    extern bool ReadProcessMemory(nativeint, nativeint, byte[], int, nativeint&)

    [<DllImport("kernel32.dll")>]
    extern void GetSystemInfo(SYSTEM_INFO&)

    [<DllImport("kernel32.dll")>]
    extern int VirtualQueryEx(nativeint, nativeint, MEMORY_BASIC_INFORMATION&, uint32)


    let mutable private sysInfo = Unchecked.defaultof<SYSTEM_INFO>

    do
        GetSystemInfo(&sysInfo)

    let private procMinAddress = sysInfo.minimumApplicationAddress
    let private procMaxAddress = sysInfo.maximumApplicationAddress


    /// Returns the process handle of the given `Process`.
    let getProcessHandle (p : Process) =
        OpenProcess(PROCESS_QUERY_INFORMATION ||| PROCESS_WM_READ, false, p.Id)

    /// Friendly F# wrapper around `VirtualQueryEx`.
    let private getMemoryInformation (hProcess : nativeint) (minAddr : nativeint) =
        let mutable memoryBasicInformation = Unchecked.defaultof<MEMORY_BASIC_INFORMATION>

        if VirtualQueryEx(hProcess, minAddr, &memoryBasicInformation, uint32 sizeof<MEMORY_BASIC_INFORMATION>) = 0 then
            failwith "Could not query process."

        memoryBasicInformation

    /// Cached buffer used by `memscan`.
    let private memscanbuf = Array.zeroCreate (4_096 * 4_096)

    /// Scans a process's memory, invoking the given function for each region of memory
    /// the given process can read and write to.
    let inline private memscan hProcess f =
        let buf = memscanbuf
        let mutable addr = procMinAddress

        while addr < procMaxAddress do
            let mbi = getMemoryInformation hProcess addr

            if mbi.Protect = AllocationProtection.PAGE_READWRITE && mbi.State = State.MEM_COMMIT then
                let mutable totalRead = 0n

                while totalRead < mbi.RegionSize do
                    let remaining = int (mbi.RegionSize - totalRead)
                    let mutable read = 0n

                    if not <| ReadProcessMemory(hProcess, mbi.BaseAddress + totalRead, buf, min buf.Length remaining, &read) then
                        failwith "Could not read process memory."

                    totalRead <- totalRead + read
                    f mbi.BaseAddress (int read) buf

            addr <- mbi.BaseAddress + mbi.RegionSize

    /// Returns whether the given byte matches an ASCII digit (from '0' to '9').
    let inline isDigit b = b >= '0'B && b <= '9'B

    /// Parses the given timestamp into a (minutes, seconds) pair.
    let inline parseTimestamp (buf : byte[]) (i : int) =
        let mutable minutes = 0
        let mutable seconds = 0

        let mutable i = i
        let mutable parsingMinutes = true

        while parsingMinutes do
            if buf.[i] = ':'B then
                parsingMinutes <- false
            else
                minutes <- minutes * 10 + int (buf.[i] - '0'B)

            i <- i + 1

        while buf.[i] <> 0uy do
            seconds <- seconds * 10 + int (buf.[i] - '0'B)
            i <- i + 1

        minutes * 60 + seconds

    /// Defines a timestamp in memory.
    type MemoryTimeStamp =
        struct
            val Address: nativeint
            val Position: int
            val PreviousPosition: int

            new(addr : nativeint, position : int) =
                { Address = addr; Position = position; PreviousPosition = position; }
            new(addr : nativeint, position : int, previousPosition : int) =
                { Address = addr; Position = position; PreviousPosition = previousPosition; }

            member x.Difference = x.Position - x.PreviousPosition

            member x.Update(newPosition : int) =
                if newPosition <> x.Position then
                    MemoryTimeStamp(x.Address, newPosition, x.Position)
                else
                    x
        end

    /// Scans a process's memory for patterns that match a '00:00' or '0:00' timestamp.
    let findTimestamps (hProcess : nativeint) (results : List<MemoryTimeStamp>) =
        memscan hProcess <| fun offset len buf ->
            let mutable i = Array.IndexOf(buf, ':'B, 1, len - 3)

            while i <> -1 do
                if isDigit buf.[i - 1] && isDigit buf.[i + 1] && isDigit buf.[i + 2] && buf.[i + 3] = 0uy then
                    let address = offset + nativeint (i - 1)
                    let position = parseTimestamp buf (i - 1)

                    results.Add <| MemoryTimeStamp(address, position)

                    if i > 1 && isDigit buf.[i - 2] then
                        // We may have a longer timestamp, so we'll add it as well just in case
                        results.Add <| MemoryTimeStamp(offset + nativeint (i - 2), parseTimestamp buf (i - 2))

                    i <- Array.IndexOf(buf, ':'B, i + 5, len - i - 8)
                else
                    i <- Array.IndexOf(buf, ':'B, i + 1, len - i - 4)

    /// Filters a `List` of timestamps found via `findTimestamps`, dropping pointers
    /// that are no longer valid.
    let filterTimestamps (hProcess : nativeint) (results : List<MemoryTimeStamp>) =
        let mutable i = 0
        let mutable read = 0n

        let buf = Array.zeroCreate 6

        while i < results.Count do
            let result = results.[i]

            if not <| ReadProcessMemory(hProcess, result.Address, buf, buf.Length, &read) then
                results.RemoveAt(i)
            else
                if (read > 4n && buf.[4] = 0uy && buf.[1] = ':'B && isDigit buf.[0] && isDigit buf.[2] && isDigit buf.[3]) ||
                   (read > 5n && buf.[5] = 0uy && buf.[2] = ':'B && isDigit buf.[0] && isDigit buf.[1] && isDigit buf.[3] && isDigit buf.[4]) then
                    // It is still valid, cool.
                    results.[i] <- result.Update(parseTimestamp buf 0)

                    i <- i + 1
                else
                    results.RemoveAt(i)

/// Represents a Spotify process.
type private SpotifyProcess(p : Process) =
    /// Gets the Spotify `Process` object.
    member __.Process = p

    /// Gets the native handle of the Spotify process.
    member val Handle = getProcessHandle p

    /// Gets a list of addresses that contains all the addresses
    /// to potential timestamps that represent the current time
    /// in the song we're listening to.
    member val Timestamps = List<MemoryTimeStamp>()

    /// Attempts to find a Spotify process with an open window.
    static member Find() =
        Process.GetProcessesByName("Spotify")
        |> Array.tryFind (fun x -> x.MainWindowHandle <> IntPtr.Zero)
        |> Option.map (fun x -> new SpotifyProcess(x))

    /// Attempts to guess the current artist and title from the title of the main Spotify window,
    /// and returns an (artist, title) pair on success.
    member __.FindArtistAndTitle() =
        p.Refresh()

        let windowTitle = p.MainWindowTitle
        let hyphenIndex = windowTitle.IndexOf(" - ")

        if hyphenIndex = -1 then
            None
        else
            Some (windowTitle.Substring(0, hyphenIndex), windowTitle.Substring(hyphenIndex + 3))

    interface IDisposable with
        member x.Dispose() = CloseHandle(x.Handle) |> ignore


/// Defines a song.
[<Struct>]
type Song = { Title : string ; Artist : string }

/// Represents the computer's Spotify status.
type Spotify() =
    let mutable p = SpotifyProcess.Find()
    let mutable filterCounter = 0
    let mutable updating = false

    let timeChangeEvent = Event<int>()
    let songChangeEvent = Event<Song>()

    let mutable previousArtist, previousTitle, previousPosition = "", "", 0

    let updateProcess() =
        match p with
        | None ->
            p <- SpotifyProcess.Find()
            p
        | Some pp when pp.Process.HasExited ->
            (pp :> IDisposable).Dispose()
            p <- SpotifyProcess.Find()
            p
        | Some _ ->
            p

    /// Event triggered when the current time in a song changes.
    [<CLIEvent>]
    member __.TimeChanged = timeChangeEvent.Publish

    /// Event triggered when the current song changes.
    [<CLIEvent>]
    member __.SongChanged = songChangeEvent.Publish

    /// Updates the status of the app.
    member __.Update() =
        match updateProcess() with
        | None -> ()
        | Some p ->
            match p.FindArtistAndTitle() with
            | None -> ()
            | Some _ when updating -> ()
            | Some (artist, title) ->
                updating <- true

                // We're playing, so we try to update our timestamp.
                let songChanged = previousTitle <> title || previousArtist <> artist

                if songChanged then
                    previousTitle <- title
                    previousArtist <- artist

                    songChangeEvent.Trigger { Title = title; Artist = artist; }

                if p.Timestamps.Count = 0 || filterCounter = 200 then
                    p.Timestamps.Clear()

                    findTimestamps p.Handle p.Timestamps
                    filterCounter <- 0
                else
                    filterTimestamps p.Handle p.Timestamps
                    filterCounter <- filterCounter + 1

                assert (p.Timestamps.Count > 0)

                // Try to find what is most likely to be the timestamp
                let bestGuess =
                    if songChanged then
                        // The song changed, so our timestamp PROBABLY
                        // went back to 0, or close.
                        p.Timestamps.Sort { new IComparer<MemoryTimeStamp> with
                            member __.Compare(x, y) = x.Position.CompareTo(y.Position)
                        }

                        // Ideally, our song had a great timestamp before, but it just changed
                        match p.Timestamps.FindIndex(fun x -> x.PreviousPosition > x.Position) with
                        | -1 ->
                            // No match, so we just take the first song; that is, the song
                            // whose timestamp is the lowest
                            p.Timestamps.[0]

                        | 0 ->
                            // The song with the lowest timestamp is also the one which changed,
                            // so we're pretty sure it's the right one
                            p.Timestamps.[0]

                        | n ->
                            // A new match has been found, so we use it
                            let newBest = p.Timestamps.[n]

                            p.Timestamps.RemoveAt(n)
                            p.Timestamps.Insert(0, newBest)

                            newBest
                    else
                        // Otherwise, we try to find a song that advances linearly; that is,
                        // its position increases second per second.
                        match p.Timestamps.FindIndex(fun x -> x.Position <> x.PreviousPosition && x.Difference < 5) with
                        | -1 ->
                            // No song matches our condition, so instead we take the first one
                            p.Timestamps.[0]

                        | 0 ->
                            // Our first song matches our condition, so we take it
                            p.Timestamps.[0]

                        | n ->
                            // A new best match has been found, so we increase its priority and take it
                            let newBest = p.Timestamps.[n]

                            p.Timestamps.RemoveAt(n)
                            p.Timestamps.Insert(0, newBest)

                            newBest

                if bestGuess.Position <> previousPosition then
                    previousPosition <- bestGuess.Position
                    timeChangeEvent.Trigger bestGuess.Position

                updating <- false

    interface IDisposable with
        member __.Dispose() =
            match p with
            | Some p -> (p :> IDisposable).Dispose()
            | None -> ()
