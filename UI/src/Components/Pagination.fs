module Pagination

open Feliz
open Fable.FontAwesome

type PaginatedQueryResults<'T> =
   Map<int, Deferred<Result<'T option, Lib.SharedTypes.Err>>>

let render
   (paginatedResults: PaginatedQueryResults<'T>)
   (page: int)
   (onPageChange: int -> unit)
   (onPageReset: unit -> unit)
   =
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
               onPageChange (page + 1))
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
               onPageReset ()
            elif page > 2 then
               onPageChange (page - 1))
      ]
   ]
