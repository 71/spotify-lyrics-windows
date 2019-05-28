namespace Lyrics

open System.Collections.Generic
open System.Timers
open System.Windows
open System.Windows.Controls
open System.Windows.Shell

open Lyrics.Helpers
open Lyrics.Spotify


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
    let mutable lastPosition = 0
    let mutable guessedMilliseconds = 0

    let currentTimeText = TextBlock()
    let titleText       = TextBlock()
    let artistText      = TextBlock()

    let captions = List<Musixmatch.Subtitle>()
    let captionsList = ItemsControl()
    let mutable activeCaption = null :> TextBlock

    let positionChanged position ms =
        this.Dispatcher.InvokeSafe <| fun _ ->
            let min, sec = position / 60, position % 60

            currentTimeText.Text <- sprintf "%d:%02d" min sec

            if captionsList.Items.Count <> 0 then
                match activeCaption with
                | null -> ()
                | caption -> caption.Style <- captionStyle

                let position = float position + ms
                let captionIndex = captions.FindLastIndex(fun x -> x.Time <= position)
                let captionIndex = if captionIndex = -1 then captions.Count - 1 else captionIndex

                if captionIndex <> -1 && captionIndex < captionsList.Items.Count then
                    match captionsList.Items.[captionIndex] with
                    | :? TextBlock as item ->
                        activeCaption <- item
                        item.Style <- activeCaptionStyle
                    | _ -> ()
                else
                    activeCaption <- null

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

        let grid = Grid.create()
                |> Grid.defineAutoRow
                |> Grid.defineRow'
                |> Grid.defineAutoRow

                |> Grid.child 0 0 info
                |> Grid.child 1 0 (ScrollViewer(Content = captionsList))
                |> Grid.child 2 0 currentTimeText

        this.Content <- grid


    do
        // Listen to events on Spotify
        spotify.SongChanged.Add <| fun song ->
            this.Dispatcher.InvokeSafe <| fun _ ->
                titleText.Text  <- song.Title
                artistText.Text <- song.Artist

            captions.Clear()

            Async.Start <| async {
                let! _ = Musixmatch.getLyrics song.Artist song.Title captions

                do this.Dispatcher.InvokeSafe <| fun _ ->
                    activeCaption <- null
                    captionsList.Items.Clear()

                    for caption in captions do
                        let caption = TextBlock(Text = caption.Text, Style = captionStyle)

                        ignore <| captionsList.Items.Add(caption)
            }

        let mutable timeChanged = false

        spotify.TimeChanged.Add <| fun position ->
            lastPosition <- position
            timeChanged  <- true

            positionChanged position 0.

        // Trigger a refresh of the currently playing song every 500ms
        let timer = new Timer()

        timer.Interval <- 200.0
        timer.Elapsed.Add <| fun _ ->
            timeChanged <- false

            spotify.Update()

            if timeChanged then
                guessedMilliseconds <- 0
            else
                guessedMilliseconds <- min (guessedMilliseconds + 200) 900
                positionChanged lastPosition (float guessedMilliseconds / 1000.)

        timer.Start()

    do
        // Enter / quit fullscreen on double click
        this.MouseDoubleClick.Add <| fun _ ->
            this.WindowState <-
                match this.WindowState with
                | WindowState.Normal -> WindowState.Maximized
                | _                  -> WindowState.Normal
