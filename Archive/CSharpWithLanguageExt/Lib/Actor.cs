using static LanguageExt.Prelude;
using Echo;
using static Echo.Process;

namespace Lib;

public static class ActorUtil {
   public static bool IsAlive (ProcessId pid) =>
      Try(() => exists(pid)).IfFail(err => {
         Console.WriteLine($"Echo.Process.exists exception: {pid} {err.Message}");
         return false;
      });
}