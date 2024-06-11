module Pagination

open Feliz
open Fable.FontAwesome

let render
   (page: int)
   (endOfPagination: bool)
   (onPageChange: int -> unit)
   (onPageReset: unit -> unit)
   =
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
