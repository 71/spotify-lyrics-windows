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
        |> Style.setter <@ fun x -> x.Opacity             @> 0.8
        |> Style.setter <@ fun x -> x.HorizontalAlignment @> HorizontalAlignment.Stretch
        |> Style.setter <@ fun x -> x.FontSize            @> 32.0
        |> Style.setter <@ fun x -> x.Foreground          @> (Brushes.White :> _)
        |> Style.setter <@ fun x -> x.Margin              @> (Thickness(0., 5., 0., 5.))
        |> Style.setter <@ fun x -> x.TextWrapping        @> TextWrapping.Wrap
        |> Style.build

    static let activeCaptionStyle =
        Style.forType<TextBlock>
        |> Style.basedOn captionStyle
        |> Style.setter <@ fun x -> x.Opacity    @> 1.0
        |> Style.setter <@ fun x -> x.FontSize   @> 46.0
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
    let btn icon tooltip =
        Button(Style = iconButtonStyle, Content = icon, ToolTip = tooltip)

    let togglebtn onicon officon on ontooltip offtooltip =
        let content = if !on then onicon else officon
        let tooltip = if !on then ontooltip else offtooltip

        let btn = Button(Style = iconButtonStyle, Content = content, ToolTip = tooltip)

        btn.Click.Add <| fun _ ->
            if !on then
                on := false
                btn.Content <- officon
                btn.ToolTip <- offtooltip
            else
                on := true
                btn.Content <- onicon
                btn.ToolTip <- ontooltip

        btn

    let mutable autoscroll = false
    let mutable autoscrolling = 0

    let ontop      = ref false
    let fullscreen = ref false

    let onTopBtn      = togglebtn "\uE944" "\uE8A7" ontop       "Do not stay on top"  "Stay on top"
    let minimizeBtn   =       btn "\uE921"                      "Minimize"
    let fullscreenBtn = togglebtn "\uE923" "\uE922" fullscreen  "Restore" "Maximize"
    let quitBtn       =       btn "\uE8BB"                      "Close"

    do
        let m = minimizeBtn.Margin

        minimizeBtn.Margin <- Thickness(m.Left + 14., m.Top, m.Right, m.Bottom)

        minimizeBtn.FontSize   <- 12.
        fullscreenBtn.FontSize <- 12.
        quitBtn.FontSize       <- 12.

    let captionsList = ItemsControl(Margin = Thickness(0., 14., 0., 12.))
    let captionsViewer = ScrollViewer(Content = captionsList,
                                      VerticalScrollBarVisibility = ScrollBarVisibility.Hidden)

    do captionsViewer.ScrollChanged.Add <| fun _ ->
        if autoscrolling > 0 then
            autoscrolling <- autoscrolling - 1
        else
            let visibleHeight = captionsViewer.ActualHeight
            let viewMid = visibleHeight / 2.

            let absoluteMid = captionsViewer.VerticalOffset + viewMid

            autoscroll <-
                match activeCaption with
                | null -> true
                | caption ->
                    let activeCaptionOffset = caption.TranslatePoint(Point(), captionsList).Y

                    absoluteMid - 50. < activeCaptionOffset &&
                    activeCaptionOffset < absoluteMid + 50. + caption.ActualHeight

    let positionChanged position ms =
        this.Dispatcher.InvokeSafe <| fun _ ->
            if captionsList.Items.Count <> 0 then
                let position = float position + ms
                let captionIndex = captions.FindLastIndex(fun x -> x.Time <= position)

                if captionIndex <> -1 && captionIndex < captionsList.Items.Count then
                    match captionsList.Items.[captionIndex] with
                    | :? TextBlock as item when item <> activeCaption ->
                        if (not << isNull) activeCaption then
                            activeCaption.Style <- captionStyle

                        activeCaption <- item
                        activeCaption.Style <- activeCaptionStyle

                        if autoscroll then
                            let itemOffset = item.TranslatePoint(Point(), captionsList).Y
                            let offset = itemOffset + item.ActualHeight / 2. - this.ActualHeight / 2.
                            let offset = max 0. offset

                            autoscrolling <- autoscrolling + 1

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
            autoscrolling <- 0

            Async.Start <| async {
                let! found = Musixmatch.getLyrics song.Artist song.Title captions

                do this.Dispatcher.InvokeSafe <| fun _ ->
                    captionsViewer.ScrollToTop()
                    autoscroll <- true

                    activeCaption <- null
                    captionsList.Items.Clear()

                    if captions.Count = 0 then
                        let text = if not found then "Lyrics not found" else "Instrumental song"
                        let captionElement = TextBlock(Text = text, Style = captionStyle)

                        captionElement.Opacity <- 0.6

                        ignore <| captionsList.Items.Add(captionElement)
                    else
                        for caption in captions do
                            let meta = System.String.IsNullOrWhiteSpace caption.Text
                            let captionElement = TextBlock(Text = caption.Text, Style = captionStyle)

                            if meta then
                                captionElement.Opacity <- 0.5
                                captionElement.Text <-
                                    if caption = captions.[captions.Count - 1] then
                                        "(end)"
                                    else
                                        "(pause)"

                            ignore <| captionsList.Items.Add(captionElement)
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
                fullscreenBtn.ToolTip <- "Restore"
            else
                fullscreen := false
                fullscreenBtn.Content <- "\uE922"
                fullscreenBtn.ToolTip <- "Maximize"
