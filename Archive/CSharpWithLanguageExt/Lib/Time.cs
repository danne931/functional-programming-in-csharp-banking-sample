namespace Lib;

public static class Time {
   public static bool IsToday(DateTime debitDate) {
      var today = DateTime.UtcNow;
      return ($"{today.Day}-{today.Month}-{today.Year}" ==
              $"{debitDate.Day}-{debitDate.Month}-{debitDate.Year}");
   }
}
