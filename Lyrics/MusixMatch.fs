module Lyrics.MusixMatch

open System.Collections.Generic
open System.Net
open System.IO
open System.Text

open Lyrics.Helpers


type Subtitle(json : JSON.Value) =
    let text =
        match json with
        | JSON.Key "text" (JSON.String s) -> s
        | _ -> failwith ""

    let time =
        match json with
        | JSON.Key "time" (JSON.Key "total" (JSON.Float t)) -> t
        | JSON.Key "time" (JSON.Key "total" (JSON.Int t)) -> float t
        | _ -> failwith ""

    member __.Text = text

    member __.Time = time


let getLyrics artist song (subtitles : List<Subtitle>) =
    let track  = WebUtility.UrlEncode song
    let artist = WebUtility.UrlEncode artist

    let url = "https://apic-desktop.musixmatch.com/ws/1.1/macro.subtitles.get"
            + "?format=json&user_language=en&namespace=lyrics_synched"
            + "&f_subtitle_length_max_deviation=1&subtitle_format=mxm"
            + "&app_id=web-desktop-app-v1.0&usertoken=190511307254ae92ff84462c794732b84754b64a2f051121eff330"
            + sprintf "&q_track=%s&q_artist=%s" track artist

    async {
        let req = WebRequest.Create(url)

        req.Headers.Add("Cookie", "AWSELB=55578B011601B1EF8BC274C33F9043CA947F99DCFF0A80541772015CA2B39C35C0F9E1C932D31725A7310BCAEB0C37431E024E2B45320B7F2C84490C2C97351FDE34690157");

        let! res = Async.AwaitTask <| req.GetResponseAsync()

        use stream = res.GetResponseStream()
        use reader = new StreamReader(stream, Encoding.UTF8)

        let! json = Async.AwaitTask <| reader.ReadToEndAsync()

        let mutable index = 0
        let data = JSON.parse json &index

        let subtitleWrapper =
            match data with
            | JSON.Key "message"
                (JSON.Key "body"
                    (JSON.Key "macro_calls"
                        (JSON.Key "track.subtitles.get"
                            (JSON.Key "message"
                                (JSON.Key "body"
                                    (JSON.Key "subtitle_list"
                                        (JSON.Array arr))))))) when arr.Length > 0 -> Some arr.[0]
            | _ -> None

        return
            match subtitleWrapper with
            | Some (JSON.Key "subtitle" (JSON.Key "subtitle_body" (JSON.String body))) ->
                index <- 0

                match JSON.parse body &index with
                | JSON.Array arr ->
                    for item in arr do
                        subtitles.Add(Subtitle item)
                    true
                | _ -> false

            | _ -> false
    }
