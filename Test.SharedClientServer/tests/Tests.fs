#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

let testsToRun =
   testList "Shared Client/Server Domain Tests" [
      AccountDomainTests.tests
      EmployeeDomainTests.tests
      AutomaticBalanceManagementTests.tests
   ]

(*
 * NOTE: 
 * Fable.Mocha and Expecto share the same API surface so we can test shared
 * domain logic in a browser environment via Mocha or in a dotnet environment.
 *
 * Just add the conditional compilation #if/#else/#endif statements in test
 * files to determine whether to load Fable.Mocha or Expecto based on the
 * execution environment.
*)

[<EntryPoint>]
let main argv =
#if FABLE_COMPILER
   // Run tests in browser environment via Fable.Mocha
   Mocha.runTests testsToRun
#else
   // Run tests in dotnet environment via Expecto
   Tests.runTestsWithCLIArgs [] argv testsToRun
#endif
