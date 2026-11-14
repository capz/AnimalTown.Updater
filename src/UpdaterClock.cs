using System.IO;
using Dolphin.Timing;

namespace AnimalTown.Updater;

internal static class UpdaterClock
{
    // The SDK verifies certificates across the entire possible UTC window.
    // This value is the console's local calendar, not a guessed UTC offset.
    internal static long LocalUnixSeconds(CalendarTime clock)
    {
        if(clock.Year<2024||clock.Year>2038)
            throw new IOException("Set the GameCube clock to the current local date and time before updating.");
        int days=0;
        for(int year=2000;year<clock.Year;year++)days+=year%4==0?366:365;
        for(int month=1;month<clock.Month;month++)days+=CalendarTime.DaysInMonth(clock.Year,month);
        long value=946684800L+(days+clock.Day-1)*86400L+clock.Hour*3600+clock.Minute*60+clock.Second;
        if(value<1704067200L+50400||value>2147483647L-50400)
            throw new IOException("The GameCube clock is outside the supported HTTPS date range.");
        return value;
    }
}
