module Lyrics.Program

open System
open System.Windows
open System.Windows.Media


/// Defines the application.
type App() = inherit Application()

/// Entry point of the application, which simply opens a `MainWindow` in an `App`.
[<EntryPoint; STAThread>]
let main _ =
    let app = App()
    let mainWindow = MainWindow()

    app.Run(mainWindow)
