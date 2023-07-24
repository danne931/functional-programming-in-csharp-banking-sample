using static LanguageExt.Prelude;
using Lib.Types;

namespace Lib;

public static class Validators {
   public static Validator<T> Pass<T>() => Success<Err, T>;
}
