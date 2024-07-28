module Pagination

open Feliz
open Fable.FontAwesome

type PaginatedQueryResults<'T> =
   Map<int, Deferred<Result<'T option, Lib.SharedTypes.Err>>>

let render
   (props:
      {|
         PaginatedResults: PaginatedQueryResults<'T>
         Page: int
         OnPageChange: int -> unit
         OnPageReset: unit -> unit
      |})
   =
   let page = props.Page
   let paginatedResults = props.PaginatedResults
   let currPageData = paginatedResults.TryFind page
   let nextPageData = paginatedResults.TryFind(page + 1)

   let endOfPagination =
      match currPageData, nextPageData with
      | _, (Some(Deferred.Resolved(Ok None))) -> true
      | (Some(Deferred.Resolved(Ok None))), _ -> true
      | _, _ -> false

   React.fragment [
      Html.a [
         attr.children [ Fa.i [ Fa.Solid.CaretRight ] [] ]

         attr.href ""

         attr.classes [
            "pagination"
            if endOfPagination then
               "secondary"
         ]

         if endOfPagination then
            attr.ariaDisabled true

         attr.onClick (fun e ->
            e.preventDefault ()

            if not endOfPagination then
               props.OnPageChange(page + 1))
      ]

      Html.a [
         attr.children [ Fa.i [ Fa.Solid.CaretLeft ] [] ]

         attr.href ""

         attr.classes [
            "pagination"
            if page = 1 then
               "secondary"
         ]

         if page = 1 then
            attr.ariaDisabled true

         attr.onClick (fun e ->
            e.preventDefault ()

            if page = 2 then
               props.OnPageReset()
            elif page > 2 then
               props.OnPageChange(page - 1))
      ]
   ]
