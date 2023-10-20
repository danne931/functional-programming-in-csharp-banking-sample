module Lib.ActivePatterns

let (|Contains|_|) (needle: string) (haystack: string) =
   if haystack.Contains needle then Some() else None
