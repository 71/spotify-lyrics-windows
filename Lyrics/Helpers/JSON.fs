module Lyrics.Helpers.JSON

open System
open System.Collections.Generic
open System.Globalization
open System.Text


/// Represents a JSON value.
type Value = Null | Int of int | Float of float | String of string | True | False | Array of Value[] | Object of Dictionary<string, Value>

/// If the given value is a JSON object which contains the
/// given key, matches with the corresponding value.
let (|Key|_|) s = function
    | Object obj when obj.ContainsKey(s) -> Some obj.[s]
    | _ -> None

/// Skips white spaces in the given string, at the given position.
let private skipWhitespace (s : string) (i : int byref) =
    while Char.IsWhiteSpace(s.[i]) do
        i <- i + 1

/// Parses a string.
let private parseString (s: string) (i : int byref) =
    i <- i + 1

    let str = StringBuilder()

    while s.[i] <> '"' do
        match s.[i] with
        | '\\' ->
            let c = match s.[i + 1] with
                    | 'n' -> '\n'
                    | '"' -> '"'
                    | '/' -> '/'
                    | '\\' -> '\\'
                    | 'u' -> let hex = s.Substring(i + 2, 4)
                             i <- i + 4
                             char (Int32.Parse(hex, NumberStyles.HexNumber))
                    |  _  -> failwith "Invalid input."
            i <- i + 2
            str.Append(c)

        | c ->
            i <- i + 1
            str.Append(c)
        |> ignore

    i <- i + 1
    str.ToString()

/// Parses the given JSON input into a JSON `Value`.
///
/// This parser is not intended to validate its input. Instead, it parses
/// its input as efficiently as possible.
let rec parse (s : string) (i : int byref) =
    match s.[i] with
    | 'n' ->
        i <- i + 4
        Null

    | 't' ->
        i <- i + 4
        True

    | 'f' ->
        i <- i + 5
        False

    | '"' ->
        String (parseString s &i)

    | '0' | '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9' | '.' ->
        let start = i

        i <- i + 1

        while Char.IsDigit(s.[i]) || s.[i] = '.' do
            i <- i + 1

        let nbr = s.Substring(start, i - start)
        let isFloat = nbr.IndexOf('.') <> -1

        if isFloat then
            Float (Double.Parse nbr)
        else
            Int (Int32.Parse nbr)

    | '[' ->
        i <- i + 1
        skipWhitespace s &i

        let list = ResizeArray()
        let mutable skipComma = false

        while s.[i] <> ']' do
            if skipComma then
                i <- i + 1
                skipWhitespace s &i
            else
                skipComma <- true

            list.Add(parse s &i)
            skipWhitespace s &i

        i <- i + 1

        Array (list.ToArray())

    | '{' ->
        i <- i + 1
        skipWhitespace s &i

        let dic = Dictionary()
        let mutable skipComma = false

        while s.[i] <> '}' do
            if skipComma then
                i <- i + 1
                skipWhitespace s &i
            else
                skipComma <- true

            let key = parseString s &i

            skipWhitespace s &i
            i <- i + 1
            skipWhitespace s &i

            dic.Add(key, parse s &i)
            skipWhitespace s &i

        i <- i + 1

        Object dic

    |  _  ->
        failwith "Invalid JSON input."
