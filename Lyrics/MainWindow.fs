namespace Lyrics

open System.Collections.Generic
open System.Timers
open System.Windows
open System.Windows.Controls
open System.Windows.Shell

open Lyrics.Helpers
open Lyrics.Spotify


type internal ScanWindow(spotify : Spotify, canScanAgain : bool) as this =
    inherit Window()

    do
        // Setup UI
        let inline createBox descr =
            let box = TextBox()
            let grid = Grid.create()
                    |> Grid.defineColumn'
                    |> Grid.defineColumn'
                    |> Grid.child 0 0 (TextBlock(Text = descr))
                    |> Grid.child 0 1 (box)

            box, grid

        let currentTimeBox, currentTimeEl = createBox "Current time"
        let endTimeBox    , endTimeEl     = createBox "Song length"
        let titleBox      , titleEl       = createBox "Song title"
        let artistBox     , artistEl      = createBox "Song artist"

        let newScanButton = Button(Content = "New scan")
        let nextScanButton = Button(Content = "Next scan")

        match spotify.GuessArtistAndTitle() with
        | None -> ()
        | Some (artist, title) ->
            titleBox.Text <- title
            artistBox.Text <- artist

        newScanButton.Click.Add <| fun _ ->
            spotify.NewScan(currentTimeBox.Text, endTimeBox.Text, titleBox.Text, artistBox.Text)
            nextScanButton.IsEnabled <- true

        nextScanButton.Click.Add <| fun _ ->
            spotify.NextScan(currentTimeBox.Text, endTimeBox.Text, titleBox.Text, artistBox.Text)

        if not canScanAgain then
            nextScanButton.IsEnabled <- false

        let boxes = StackPanel()
                 |> Control.child currentTimeEl
                 |> Control.child endTimeEl
                 |> Control.child titleEl
                 |> Control.child artistEl

        let buttons = Grid.create()
                   |> Grid.defineColumn'
                   |> Grid.defineColumn'

                   |> Grid.child 0 0 newScanButton
                   |> Grid.child 0 1 nextScanButton

        let grid = Grid.create()
                |> Grid.defineRow'
                |> Grid.defineAutoRow

                |> Grid.child 0 0 boxes
                |> Grid.child 1 0 buttons

        this.Content <- grid


type internal MainWindow() as this =
    inherit Window()

    static let captionStyle =
        Style.forType<TextBlock>
        |> Style.setter <@ fun x -> x.TextAlignment @> TextAlignment.Center
        |> Style.setter <@ fun x -> x.Opacity @> 0.7
        |> Style.setter <@ fun x -> x.HorizontalAlignment @> HorizontalAlignment.Stretch
        |> Style.setter <@ fun x -> x.FontSize @> 18.0
        |> Style.build

    static let activeCaptionStyle =
        Style.forType<TextBlock>
        |> Style.basedOn captionStyle
        |> Style.setter <@ fun x -> x.Opacity @> 1.0
        |> Style.setter <@ fun x -> x.FontSize @> 22.0
        |> Style.build

    let spotify = new Spotify()
    let timer = new Timer()

    let openDialog canScanAgain =
        timer.Enabled <- false
        let scanWindow = ScanWindow(spotify, canScanAgain)

        ignore <| scanWindow.ShowDialog()
        timer.Enabled <- true

    let currentTimeText = TextBlock()
    let endTimeText     = TextBlock()
    let titleText       = TextBlock()
    let artistText      = TextBlock()
    let timeSlider      = Slider(IsEnabled = false)

    do
        currentTimeText.SimulatedClick.Add <| fun _ ->
            openDialog true
        endTimeText.SimulatedClick.Add <| fun _ ->
            openDialog true
        titleText.SimulatedClick.Add <| fun _ ->
            openDialog true
        artistText.SimulatedClick.Add <| fun _ ->
            openDialog true

    let captions = List<MusixMatch.Subtitle>()
    let captionsList = ItemsControl()
    let mutable activeCaption = null :> TextBlock

    do
        // Setup window chrome
        let chrome = WindowChrome(CaptionHeight = 0.0,
                                  ResizeBorderThickness = Thickness 5.0)

        WindowChrome.SetWindowChrome(this, chrome)

        // Setup UI
        let info = StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center)
                |> Control.child titleText
                |> Control.child (TextBlock(Text = " - "))
                |> Control.child artistText

        let time = Grid.create()
                |> Grid.defineAutoColumn
                |> Grid.defineColumn'
                |> Grid.defineAutoColumn

                |> Grid.child 0 0 currentTimeText
                |> Grid.child 0 1 timeSlider
                |> Grid.child 0 2 endTimeText

        let grid = Grid.create()
                |> Grid.defineAutoRow
                |> Grid.defineRow'
                |> Grid.defineAutoRow

                |> Grid.child 0 0 info
                |> Grid.child 1 0 (ScrollViewer(Content = captionsList))
                |> Grid.child 2 0 time

        this.Content <- grid


    do
        // Listen to events on Spotify
        spotify.SongChanged.Add <| fun song ->
            this.Dispatcher.InvokeSafe <| fun _ ->
                let min, sec = song.Length

                titleText.Text   <- song.Title
                artistText.Text  <- song.Artist

                endTimeText.Text <- sprintf "%d:%02d" min sec

                currentTimeText.Text <- "0:00"

                timeSlider.Minimum <- 0.0
                timeSlider.Maximum <- float (min * 60 + sec)

            captions.Clear()

            Async.Start <| async {
                let duration =
                    let min, sec = song.Length

                    min * 60 + sec

                let! foundLyrics = MusixMatch.getLyrics song.Artist song.Title duration captions

                do if foundLyrics then
                    this.Dispatcher.InvokeSafe <| fun _ ->
                        captionsList.Items.Clear()

                        for caption in captions do
                            let caption = TextBlock(Text = caption.Text, Style = captionStyle)

                            ignore <| captionsList.Items.Add(caption)
            }

        spotify.TimeChanged.Add <| fun (min, sec) ->
            this.Dispatcher.InvokeSafe <| fun _ ->
                let currentTime = float (min * 60 + sec)

                currentTimeText.Text <- sprintf "%d:%02d" min sec
                timeSlider.Value <- currentTime

                if captions.Count <> 0 then
                    match activeCaption with
                    | null -> ()
                    | caption -> caption.Style <- captionStyle

                    let captionIndex = captions.FindLastIndex(fun x -> x.Time <= currentTime)
                    let captionIndex = if captionIndex = -1 then captions.Count - 1 else captionIndex

                    match captionsList.Items.[captionIndex] with
                    | :? TextBlock as item ->
                        activeCaption <- item
                        item.Style <- activeCaptionStyle
                    | _ -> ()


        // Trigger a refresh of the currently playing song every 500ms
        spotify.NeedsScan.Add <| fun _ ->
            this.Dispatcher.InvokeSafe <| fun _ ->
                openDialog false

        timer.Interval <- 500.0
        timer.Elapsed.Add <| fun _ ->
            spotify.Update()

        timer.Start()

    do
        // Enter / quit fullscreen on double click
        this.MouseDoubleClick.Add <| fun _ ->
            this.WindowState <-
                match this.WindowState with
                | WindowState.Normal -> WindowState.Maximized
                | _                  -> WindowState.Normal
