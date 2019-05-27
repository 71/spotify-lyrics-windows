namespace Lyrics.Helpers

open System
open System.Collections.Generic
open System.ComponentModel
open System.Reflection
open System.Runtime.CompilerServices
open System.Windows

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns


[<EditorBrowsable(EditorBrowsableState.Never)>]
module DependencyObjectHelpersCache =
    let cache = Dictionary<int, DependencyProperty>()
    let settingsProperties = List<DependencyProperty>()
    let mutable settingsType = typeof<obj>


[<AutoOpen>]
module DependencyObjectHelpers =

    /// Registers a dependency property.
    let inline registerDependencyProperty<'a, 'b when 'a :> DependencyObject>(propGetter: Expr<'a -> 'b>) (defaultValue: 'b) (valueChanged: 'a -> 'b -> unit) =
        match propGetter with
        | Lambda (_, PropertyGet (_, prop, [])) ->
            let metadata = PropertyMetadata(defaultValue,
                                            fun o e -> if e.OldValue <> e.NewValue then valueChanged (o :?> 'a) (e.NewValue :?> 'b))

            let dp = DependencyProperty.Register(prop.Name, prop.PropertyType, prop.DeclaringType, metadata)

            if typeof<'a> = DependencyObjectHelpersCache.settingsType then
                DependencyObjectHelpersCache.settingsProperties.Add(dp)

            dp

        | _ ->
            invalidArg "prop" "Not a simple property getter."

    /// Registers a dependency property.
    let inline registerDependencyProperty'<'a, 'b when 'a :> DependencyObject> (propGetter: Expr<'a -> 'b>) defaultValue =
        registerDependencyProperty propGetter defaultValue (fun _ _ -> ())

    /// Registers a dependency property.
    let inline registerDependencyProperty''<'a, 'b when 'a :> DependencyObject> (propGetter: Expr<'a -> 'b>)  =
        registerDependencyProperty propGetter Unchecked.defaultof<'b> (fun _ _ -> ())

    let getPropertyFromName(objType: System.Type, propName: string) =
        let cache = DependencyObjectHelpersCache.cache
        let hash = objType.GetHashCode() * propName.GetHashCode()

        match cache.TryGetValue(hash) with
        | true, value -> value
        | false, _    ->
            let flags = BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.FlattenHierarchy
            let propName = propName + "Property"

            let value = match objType.GetProperty(propName, flags) with
                        | null -> match objType.GetField(propName, flags) with
                                  | null -> invalidArg "propName" "Cannot find matching dependency property."
                                  | field -> field.GetValue(null)
                        | prop -> prop.GetValue(null)

            match value with
            | :? DependencyProperty as prop -> cache.[hash] <- prop ; prop
            | _ -> invalidArg "propName" "Invalid dependency property."

    let inline getPropertyFromGetter(propGetter: Expr<'a -> 'b>) =
        match propGetter with
        | Lambda (_, PropertyGet (Some target, prop, [])) ->
            getPropertyFromName(target.Type, prop.Name)
        | expr ->
            failwithf "Invalid property getter: %A." expr

    type DependencyObject with
        member inline this.GetPropertyValue([<CallerMemberName>] ?propName: string) =
            let propName = match propName with
                           | Some propName -> propName
                           | None -> nullArg "propName"
            let prop = getPropertyFromName(this.GetType(), propName)

            this.GetValue(prop) :?> _

        member inline this.SetPropertyValue(value, [<CallerMemberName>] ?propName: string) =
            let propName = match propName with
                           | Some propName -> propName
                           | None -> nullArg "propName"
            let prop = getPropertyFromName(this.GetType(), propName)

            this.SetValue(prop, value :> obj)

[<Extension>]
type DependencyObjectExtensions =
    [<Extension>]
    static member inline ValueChanged(this: 'a, propGetter: Expr<'a -> 'b>): ('b -> unit) -> IDisposable when 'a :> DependencyObject =
        let dp = getPropertyFromGetter(propGetter)
        let dpd = DependencyPropertyDescriptor.FromProperty(dp, this.GetType())

        fun listener ->
            let handler = EventHandler(fun _ _ -> listener(this.GetValue(dp) :?> 'b))

            dpd.AddValueChanged(this, handler)

            { new IDisposable with
                member __.Dispose() = dpd.RemoveValueChanged(this, handler)
            }
