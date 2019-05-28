namespace Lyrics

open System.Collections.Generic
open System.Timers
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Shell

open Lyrics.Helpers
open Lyrics.Spotify


type internal MainWindow() as this =
    inherit Window()

    // Styles
    static let segoeUiSymbol = FontFamily("Segoe MDL2 Assets")

    static let captionStyle =
        Style.forType<TextBlock>
        |> Style.setter <@ fun x -> x.TextAlignment       @> TextAlignment.Center
        |> Style.setter <@ fun x -> x.Opacity             @> 0.7
        |> Style.setter <@ fun x -> x.HorizontalAlignment @> HorizontalAlignment.Stretch
        |> Style.setter <@ fun x -> x.FontSize            @> 24.0
        |> Style.setter <@ fun x -> x.Foreground          @> (Brushes.White :> _)
        |> Style.setter <@ fun x -> x.Margin              @> (Thickness(0., 5., 0., 5.))
        |> Style.setter <@ fun x -> x.TextWrapping        @> TextWrapping.Wrap
        |> Style.build

    static let activeCaptionStyle =
        Style.forType<TextBlock>
        |> Style.basedOn captionStyle
        |> Style.setter <@ fun x -> x.Opacity    @> 1.0
        |> Style.setter <@ fun x -> x.FontSize   @> 32.0
        |> Style.setter <@ fun x -> x.FontWeight @> FontWeights.Bold
        |> Style.build

    static let iconButtonStyle =
        Style.forType<Button>
        |> Style.setter <@ fun x -> x.Background      @> (Brushes.Transparent :> _)
        |> Style.setter <@ fun x -> x.Foreground      @> (Brushes.White :> _)
        |> Style.setter <@ fun x -> x.FontFamily      @> segoeUiSymbol
        |> Style.setter <@ fun x -> x.FontSize        @> 16.
        |> Style.setter <@ fun x -> x.Padding         @> (Thickness 0.)
        |> Style.setter <@ fun x -> x.BorderThickness @> (Thickness 0.)
        |> Style.setter <@ fun x -> x.Margin          @> (Thickness 0.)
        |> Style.setter <@ fun x -> x.Width           @> 46.
        |> Style.setter <@ fun x -> x.Height          @> 30.
        |> Style.setter <@ fun x -> x.Opacity         @> 0.9
        |> Style.build


    // State
    let spotify = new Spotify()
    let mutable lastPosition = 0
    let mutable guessedMilliseconds = 0

    let captions = List<Musixmatch.Subtitle>()
    let mutable activeCaption = null :> TextBlock


    // UI
    let btn icon =
        let btn = Button(Style = iconButtonStyle, Content = icon)

        Panel.SetZIndex(btn, 10)

        btn

    let togglebtn onicon officon on =
        let content = if !on then onicon else officon
        let btn = Button(Style = iconButtonStyle, Content = content)

        Panel.SetZIndex(btn, 10)

        btn.Click.Add <| fun _ ->
            if !on then
                on := false
                btn.Content <- officon
            else
                on := true
                btn.Content <- onicon

        btn

    let autoscroll = ref true
    let ontop      = ref false
    let fullscreen = ref false

    let autoScrollBtn = togglebtn "\uE8D0" "\uE8CB" autoscroll
    let onTopBtn      = togglebtn "\uE944" "\uE8A7" ontop
    let minimizeBtn   =       btn "\uE921"
    let fullscreenBtn = togglebtn "\uE923" "\uE922" fullscreen
    let quitBtn       =       btn "\uE8BB"

    do
        let m = minimizeBtn.Margin

        minimizeBtn.Margin <- Thickness(m.Left + 14., m.Top, m.Right, m.Bottom)

        minimizeBtn.FontSize   <- 12.
        fullscreenBtn.FontSize <- 12.
        quitBtn.FontSize       <- 12.

    let captionsList = ItemsControl()
    let captionsViewer = ScrollViewer(Content = captionsList,
                                      VerticalScrollBarVisibility = ScrollBarVisibility.Hidden)


    let positionChanged position ms =
        this.Dispatcher.InvokeSafe <| fun _ ->
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

                        if !autoscroll then
                            let itemOffset = item.TranslatePoint(Point(), captionsList)
                            let offset = itemOffset.Y - captionsViewer.ViewportHeight / 2.
                            let offset = max 0. (min offset captionsViewer.ViewportHeight)

                            captionsViewer.ScrollToVerticalOffset(offset)
                    | _ -> ()
                else
                    activeCaption <- null

    do
        // Setup window frame
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        this.WindowStyle           <- WindowStyle.None

        let chrome = WindowChrome(CaptionHeight = 0.0,
                                  ResizeBorderThickness = Thickness 5.0)

        WindowChrome.SetWindowChrome(this, chrome)

        // Setup UI
        let controls = StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right)
                    |> Control.child autoScrollBtn
                    |> Control.child onTopBtn
                    |> Control.child minimizeBtn
                    |> Control.child fullscreenBtn
                    |> Control.child quitBtn

        let grid = Grid.create()
                |> Grid.defineAutoRow
                |> Grid.defineRow'

                |> Grid.child' 0 2 0 1 captionsViewer
                |> Grid.child 0 0 controls

        this.Content <- grid

        this.Background <- [
            GradientStop(Color.FromRgb(0x0fuy, 0x20uy, 0x27uy), 0.0)
            GradientStop(Color.FromRgb(0x20uy, 0x3auy, 0x43uy), 0.5)
            GradientStop(Color.FromRgb(0x2cuy, 0x53uy, 0x64uy), 1.0)
        ] |> GradientStopCollection
          |> fun x -> RadialGradientBrush(x, GradientOrigin = Point(0., 0.), RadiusX = 2., RadiusY = 2.)


    do
        // Listen to events on Spotify
        spotify.SongChanged.Add <| fun song ->
            this.Dispatcher.InvokeSafe <| fun _ ->
                this.Title <- sprintf "Lyrics: %s - %s" song.Artist song.Title

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
        // Move window when mouse down
        this.MouseDown.Add <| fun e ->
            if e.LeftButton = MouseButtonState.Pressed then
                this.DragMove()

        captionsList.MouseDown.Add <| fun e ->
            if e.LeftButton = MouseButtonState.Pressed then
                this.DragMove()

        // Enter / quit fullscreen on double click
        this.MouseDoubleClick.Add <| fun _ ->
            this.WindowState <-
                match this.WindowState with
                | WindowState.Normal -> WindowState.Maximized
                | _                  -> WindowState.Normal

        // Handle top buttons
        minimizeBtn.Click.Add <| fun _ -> this.WindowState <- WindowState.Minimized
        quitBtn.Click.Add     <| fun _ -> this.Close()

        fullscreenBtn.Click.Add <| fun _ ->
            this.WindowState <-
                if !fullscreen then
                    WindowState.Maximized
                else
                    WindowState.Normal

        onTopBtn.Click.Add <| fun _ ->
            this.Topmost <- !ontop

        this.StateChanged.Add <| fun _ ->
            if this.WindowState = WindowState.Maximized then
                fullscreen := true
                fullscreenBtn.Content <- "\uE923"
            else
                fullscreen := false
                fullscreenBtn.Content <- "\uE922"
