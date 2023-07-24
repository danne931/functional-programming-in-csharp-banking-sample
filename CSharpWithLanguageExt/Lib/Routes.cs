using static Microsoft.AspNetCore.Http.Results;
using LanguageExt;
using static LanguageExt.Prelude;
using Lib.Types;

namespace Lib.Route;

public static class Response {
   public static Task<IResult> Unwrap<T>(this Task<T> WrappedResult) =>
      TryAsync(WrappedResult).Match(
         Fail: ExceptionResponse,
         Succ: Ok
      );

   public static Task<IResult> Unwrap<T>(
      this Task<Validation<Err, T>> WrappedResult,
      Func<T, Object>? ShapeResponse = null
   )
   =>
   TryAsync(WrappedResult).Match(
      Fail: ExceptionResponse,
      Succ: val => val.Match(
         Fail: e => BadRequest(new { validationError = e.Head.Message }),
         Succ: state => Ok(ShapeResponse != null ? ShapeResponse(state) : state)
      )
   );

   public static Task<IResult> Unwrap<T>(
      this Task<Option<T>> WrappedResult,
      Func<T, Object>? ShapeResponse = null
   ) {
      return WrappedResult.ToTryOptionAsync().Match(
         Fail: ExceptionResponse,
         None: () => NotFound(),
         Some: state => Ok(ShapeResponse != null ? ShapeResponse(state) : state)
      );
   }

   private static IResult ExceptionResponse(Exception ex) {
      Console.WriteLine("EXCEPTION: " + ex.Message);
      return StatusCode(StatusCodes.Status500InternalServerError);
   }
}
