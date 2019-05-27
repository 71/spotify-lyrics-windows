module Lyrics.Spotify

open System
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open System.Text
open System.Globalization


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


    let mutable sysInfo = Unchecked.defaultof<SYSTEM_INFO>

    do
        GetSystemInfo(&sysInfo)

    let procMinAddress = sysInfo.minimumApplicationAddress
    let procMaxAddress = sysInfo.maximumApplicationAddress


    let getProcessHandle (p : Process) =
        OpenProcess(PROCESS_QUERY_INFORMATION ||| PROCESS_WM_READ, false, p.Id)

    let getMemoryInformation (hProcess : nativeint) (minAddr : nativeint) =
        let mutable memoryBasicInformation = Unchecked.defaultof<MEMORY_BASIC_INFORMATION>

        if VirtualQueryEx(hProcess, minAddr, &memoryBasicInformation, uint32 sizeof<MEMORY_BASIC_INFORMATION>) = 0 then
            failwith "Could not query process."

        memoryBasicInformation

    let inline memscan hProcess f =
        let mutable addr = procMinAddress

        while addr < procMaxAddress do
            let mbi = getMemoryInformation hProcess addr

            if mbi.Protect = AllocationProtection.PAGE_READWRITE && mbi.State = State.MEM_COMMIT then
                let buf = Array.zeroCreate (int mbi.RegionSize)
                let mutable read = nativeint 0

                if not <| ReadProcessMemory(hProcess, mbi.BaseAddress, buf, buf.Length, &read) then
                    failwith "Could not read process memory."

                f addr buf

            addr <- addr + mbi.RegionSize

    let findMatchingAddressesInBuffer (results : List<nativeint>) (haystack : byte[]) (needle : string) (offset : nativeint) =
        let needle = Encoding.Unicode.GetBytes(needle)
        let maxLength = haystack.Length - needle.Length

        let mutable i = 0

        while i <= maxLength do
            let mutable j = 0

            while j < needle.Length do
                if haystack.[i + j] = needle.[j] then
                    j <- j + 1
                else
                    j <- needle.Length + 1

            if j = needle.Length && haystack.[i + j] = 0uy then
                results.Add (offset + nativeint i)
                i <- i + j
            else
                i <- i + 1

    let findMatchingAddresses (hProcess : nativeint) (results : List<nativeint>) (needle : string) =
        memscan hProcess <| fun offset buf ->
            findMatchingAddressesInBuffer results buf needle offset

    let filterMatchingAddresses (hProcess : nativeint) (results : List<nativeint>) (needle : string) =
        let needle = Encoding.Unicode.GetBytes(needle)
        let mutable i = 0

        while i < results.Count do
            let mutable read = IntPtr.Zero
            let mutable j = 0

            let buf = Array.zeroCreate (needle.Length + 1)

            if not <| ReadProcessMemory(hProcess, results.[i], buf, buf.Length, &read) then
                results.RemoveAt(i)
            else
                while j < needle.Length do
                    if buf.[j] = needle.[j] then
                        j <- j + 1
                    else
                        j <- needle.Length + 1

                if j = needle.Length && buf.[j] = 0uy then
                    i <- i + 1
                else
                    results.RemoveAt(i)


let private stringBuf = Array.zeroCreate<byte> 1024
let private isValidChar ch =
    match Char.GetUnicodeCategory ch with
    | UnicodeCategory.ClosePunctuation
    | UnicodeCategory.ConnectorPunctuation
    | UnicodeCategory.CurrencySymbol
    | UnicodeCategory.DashPunctuation
    | UnicodeCategory.DecimalDigitNumber
    | UnicodeCategory.FinalQuotePunctuation
    | UnicodeCategory.InitialQuotePunctuation
    | UnicodeCategory.LetterNumber
    | UnicodeCategory.LowercaseLetter
    | UnicodeCategory.MathSymbol
    | UnicodeCategory.OpenPunctuation
    | UnicodeCategory.OtherLetter
    | UnicodeCategory.OtherNumber
    | UnicodeCategory.OtherPunctuation
    | UnicodeCategory.OtherSymbol
    | UnicodeCategory.SpaceSeparator
    | UnicodeCategory.TitlecaseLetter
    | UnicodeCategory.UppercaseLetter -> true
    | _ -> false

let private isValidTitle = String.forall isValidChar
let private isValidTimestamp (str : string) =
    let mutable i = 0
    let mutable sepIndex = -1

    while sepIndex = -1 && i < str.Length do
        if str.[i] = ':' then
            sepIndex <- i
        else if (not << Char.IsDigit) str.[i] then
            i <- str.Length
        else
            i <- i + 1

    i <- i + 1

    while i < str.Length do
        if (not << Char.IsDigit) str.[i] then
            i <- str.Length + 1
        else
            i <- i + 1

    sepIndex > 0 && sepIndex < str.Length - 1 && i = str.Length

let private readString (hProcess : nativeint) (addresses : List<nativeint>) (isValid : string -> bool) =
    let mutable read = IntPtr.Zero
    let mutable result = None

    while addresses.Count > 0 && result = None do
        if ReadProcessMemory(hProcess, addresses.[0], stringBuf, 128, &read) then
            // Find end of string
            let mutable i = 0

            while i < stringBuf.Length && (stringBuf.[i] <> 0uy || stringBuf.[i + 1] <> 0uy) do
                i <- i + 1

            if i = stringBuf.Length then
                addresses.RemoveAt(0)
            else
                // Make sure the string is valid
                let str = Encoding.Unicode.GetString(stringBuf, 0, i + 1)

                if isValid str then
                    result <- Some str
                else
                    addresses.RemoveAt(0)
        else
            addresses.RemoveAt(0)

    result

let parseTime (time : string) =
    let mutable minutes = 0
    let mutable seconds = 0

    let mutable i = 0
    let mutable parsingMinutes = true

    while parsingMinutes do
        if time.[i] = ':' then
            parsingMinutes <- false
        else
            minutes <- minutes * 10 + (int time.[i] - int '0')

        i <- i + 1

    while i < time.Length do
        seconds <- seconds * 10 + (int time.[i] - int '0')
        i <- i + 1

    minutes, seconds

/// Represents a Spotify process.
type SpotifyProcess(p : Process) =
    member __.Process = p

    member val Handle = getProcessHandle p

    member val StartTimeAddresses = List<nativeint>()
    member val EndTimeAddresses   = List<nativeint>()
    member val TitleAddresses     = List<nativeint>()
    member val ArtistAddresses    = List<nativeint>()

    static member Find() =
        Process.GetProcessesByName("Spotify")
        |> Array.tryFind (fun x -> x.MainWindowHandle <> IntPtr.Zero)
        |> Option.map (fun x -> new SpotifyProcess(x))

    interface IDisposable with
        member x.Dispose() = CloseHandle(x.Handle) |> ignore


/// Defines the time in a song.
type SongTime = int * int

/// Defines a song.
type Song = { Title : string ; Artist : string ; Length : SongTime }


/// Represents the computer's Spotify status.
type Spotify() =
    let mutable p = SpotifyProcess.Find()

    let mutable currentTime, endTime, title, artist = "", "", "", ""

    let timeChangeEvent = Event<SongTime>()
    let songChangeEvent = Event<Song>()
    let scanNeededEvent = Event<unit>()

    let notifyIfScanNeeded(p : SpotifyProcess) =
        if p.StartTimeAddresses.Count = 0 ||
           p.EndTimeAddresses.Count = 0 ||
           p.TitleAddresses.Count = 0 ||
           p.ArtistAddresses.Count = 0
        then
            scanNeededEvent.Trigger()
            true
        else
            false

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

    /// Event triggered when a new scan is needed to continue working properly.
    [<CLIEvent>]
    member __.NeedsScan = scanNeededEvent.Publish

    /// Updates the status of the app.
    member __.Update() =
        match updateProcess() with
        | None -> ()
        | Some p when notifyIfScanNeeded p -> ()
        | Some p ->
            let mutable songChanged = false

            match readString p.Handle p.EndTimeAddresses isValidTimestamp with
            | None -> scanNeededEvent.Trigger()
            | Some sameEndTime when endTime = sameEndTime -> ()
            | Some newEndTime ->
                endTime <- newEndTime
                songChanged <- true

            match readString p.Handle p.TitleAddresses isValidTitle with
            | None -> scanNeededEvent.Trigger()
            | Some sameTitle when title = sameTitle -> ()
            | Some newTitle ->
                title <- newTitle
                songChanged <- true

            match readString p.Handle p.ArtistAddresses isValidTitle with
            | None -> scanNeededEvent.Trigger()
            | Some sameArtist when artist = sameArtist -> ()
            | Some newArtist ->
                artist <- newArtist
                songChanged <- true

            if songChanged then
                songChangeEvent.Trigger({ Title = title; Artist = artist; Length = parseTime endTime })
                currentTime <- "0:00"

            match readString p.Handle p.StartTimeAddresses isValidTimestamp with
            | None -> scanNeededEvent.Trigger()
            | Some sameCurrentTime when currentTime = sameCurrentTime -> ()
            | Some newCurrentTime ->
                currentTime <- newCurrentTime
                timeChangeEvent.Trigger(parseTime currentTime)

    /// Performs a new scan.
    member __.NewScan(currentTime : string, endTime : string, title : string, artist : string) =
        match updateProcess() with
        | None -> ()
        | Some p ->
            p.StartTimeAddresses.Clear()
            p.EndTimeAddresses.Clear()
            p.TitleAddresses.Clear()
            p.ArtistAddresses.Clear()

            memscan p.Handle <| fun offset buf ->
                findMatchingAddressesInBuffer p.StartTimeAddresses buf currentTime offset
                findMatchingAddressesInBuffer p.EndTimeAddresses   buf endTime     offset
                findMatchingAddressesInBuffer p.TitleAddresses     buf title       offset
                findMatchingAddressesInBuffer p.ArtistAddresses    buf artist      offset

            notifyIfScanNeeded p |> ignore

    /// Performs a scan update.
    member __.NextScan(currentTime : string, endTime : string, title : string, artist : string) =
        match updateProcess() with
        | None -> ()
        | Some p ->
            let handle = p.Handle

            filterMatchingAddresses handle p.StartTimeAddresses currentTime
            filterMatchingAddresses handle p.EndTimeAddresses   endTime
            filterMatchingAddresses handle p.TitleAddresses     title
            filterMatchingAddresses handle p.ArtistAddresses    artist

            notifyIfScanNeeded p |> ignore

    /// Attempts to guess the current artist and title from the title of the main Spotify window.
    member __.GuessArtistAndTitle() =
        match updateProcess() with
        | None -> None
        | Some p ->
            let windowTitle = p.Process.MainWindowTitle
            let hyphenIndex = windowTitle.LastIndexOf(" - ")

            if hyphenIndex = -1 then
                None
            else
                Some (windowTitle.Substring(0, hyphenIndex), windowTitle.Substring(hyphenIndex + 3))

    interface IDisposable with
        member __.Dispose() =
            match p with
            | Some p -> (p :> IDisposable).Dispose()
            | None -> ()
