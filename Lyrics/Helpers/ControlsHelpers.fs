namespace Lyrics.Helpers

open System.Windows
open System.Windows.Controls
open System.Windows.Data
open System.Windows.Threading

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns


[<AutoOpen>]
module ControlsHelpers =
    type Dispatcher with
        member this.InvokeSafe(f: unit -> unit) =
            this.BeginInvoke(System.Action<unit> f :> System.Delegate, ())
            |> ignore // Operation may have failed, but we don't really care

    type UIElement with
        member x.SimulatedClick =
            let event = Event<_>()
            let mutable handle = false

            x.MouseLeftButtonDown.Add <| fun _ ->
                handle <- true

            x.MouseLeftButtonUp.Add <| fun e ->
                if handle then
                    event.Trigger(e)

            event.Publish


module Binding =
    let empty = Binding()
    let ofName name = Binding(name)

    let update f (binding: Binding) = f binding ; binding

    let withRelativeSource relativeSource =
        update (fun x -> x.RelativeSource <- relativeSource)

    let withSelfRelativeSource binding =
        withRelativeSource (RelativeSource RelativeSourceMode.Self) binding
    let withTemplatedParentRelativeSource binding =
        withRelativeSource (RelativeSource RelativeSourceMode.TemplatedParent) binding

    let set (element: 'a) (propGetter: Expr<'a -> 'b>) (binding: Binding) =
        let prop = DependencyObjectHelpers.getPropertyFromGetter propGetter

        BindingOperations.SetBinding(element, prop, binding)
        |> ignore


type StyleBuilder<'T>() =
    member val Style = Style(typeof<'T>)
type TriggerBuilder<'T>(trigger: Trigger) =
    member val Trigger = trigger

module Style =
    let inline empty<'a> = StyleBuilder<'a>()
    let inline forType<'a> = StyleBuilder<'a>()

    let inline apply f (style: StyleBuilder<'a>) =
        f style.Style
        style

    let inline build (style: StyleBuilder<'a>) =
        style.Style

    let inline basedOn typ =
        apply(fun s -> s.BasedOn <- typ)

    let inline setter (propGetter: Expr<'a -> 'b>) (value: 'b) (style: StyleBuilder<'a>) =
        let prop = match propGetter with
                   | Lambda (_, PropertyGet (_, prop, _)) -> prop
                   | _ -> invalidArg "propGetter" "Not a property getter."

        let prop = DependencyObjectHelpers.getPropertyFromName(prop.DeclaringType, prop.Name)

        apply(fun s -> s.Setters.Add(Setter(prop, value))) style

    let inline trigger (propGetter: Expr<'a -> 'b>) (value: 'b) (f: TriggerBuilder<'a> -> TriggerBuilder<'a>) (style: StyleBuilder<'a>) =
        let prop = match propGetter with
                   | Lambda (_, PropertyGet (_, prop, _)) -> prop
                   | _ -> invalidArg "propGetter" "Not a property getter."

        let prop = DependencyObjectHelpers.getPropertyFromName(prop.DeclaringType, prop.Name)
        let builder = TriggerBuilder(Trigger(Property = prop, Value = value))

        apply(fun s -> s.Triggers.Add(f(builder).Trigger)) style

module Trigger =
    let inline empty<'a> = TriggerBuilder<'a>(Trigger())
    let inline forType<'a> = TriggerBuilder<'a>(Trigger())

    let inline prop (propGetter: Expr<'a -> 'b>) (value: 'b) =
        let prop = match propGetter with
                   | Lambda (_, PropertyGet (_, prop, _)) -> prop
                   | _ -> invalidArg "propGetter" "Not a property getter."

        let prop = DependencyObjectHelpers.getPropertyFromName(prop.DeclaringType, prop.Name)

        TriggerBuilder(Trigger(Property = prop, Value = value))

    let inline setter (propGetter: Expr<'a -> 'b>) (value: 'b) (trigger: TriggerBuilder<'a>) =
        let prop = match propGetter with
                   | Lambda (_, PropertyGet (_, prop, _)) -> prop
                   | _ -> invalidArg "propGetter" "Not a property getter."

        let prop = DependencyObjectHelpers.getPropertyFromName(prop.DeclaringType, prop.Name)

        trigger.Trigger.Setters.Add(Setter(prop, value))
        trigger

module Control =
    let inline child child (control : Panel) =
        control.Children.Add(child) |> ignore
        control


module Grid =
    let inline create() = Grid()

    let inline defineRow height (grid : Grid) =
        grid.RowDefinitions.Add(RowDefinition(Height = GridLength(height, GridUnitType.Star)))
        grid
    let inline defineRow' (grid : Grid) =
        grid.RowDefinitions.Add(RowDefinition(Height = GridLength(1., GridUnitType.Star)))
        grid
    let inline defineAutoRow (grid : Grid) =
        grid.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))
        grid

    let inline defineColumn width (grid : Grid) =
        grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(width, GridUnitType.Star)))
        grid
    let inline defineColumn' (grid : Grid) =
        grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1., GridUnitType.Star)))
        grid
    let inline defineAutoColumn (grid : Grid) =
        grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength.Auto))
        grid

    let inline child row col child (grid : Grid) =
        grid.Children.Add(child) |> ignore

        Grid.SetRow(child, row)
        Grid.SetColumn(child, col)

        grid

    let inline child' row rowSpan col colSpan child (grid : Grid) =
        grid.Children.Add(child) |> ignore

        Grid.SetRow(child, row)
        Grid.SetRowSpan(child, rowSpan)
        Grid.SetColumn(child, col)
        Grid.SetColumnSpan(child, colSpan)

        grid

    let inline build (grid : Grid) = grid
