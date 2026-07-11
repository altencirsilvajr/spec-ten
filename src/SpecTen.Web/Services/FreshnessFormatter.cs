namespace SpecTen.Web.Services;

public static class FreshnessFormatter
{
    public static string UpdatedLabel(DateTimeOffset? timestamp)
    {
        return RelativeLabel(timestamp, "Atualizado");
    }

    public static string CollectedLabel(DateTimeOffset? timestamp)
    {
        return RelativeLabel(timestamp, "Coletado");
    }

    public static string RelativeLabel(DateTimeOffset? timestamp, string prefix)
    {
        if (timestamp is null)
        {
            return $"{prefix} sem data";
        }

        var localTimestamp = timestamp.Value.ToLocalTime();
        var currentLocal = DateTimeOffset.Now;
        var days = (currentLocal.Date - localTimestamp.Date).Days;

        return days switch
        {
            <= 0 => $"{prefix} hoje",
            1 => $"{prefix} ontem",
            < 30 => $"{prefix} ha {days} dias",
            _ => $"{prefix} em {localTimestamp:dd/MM/yyyy}",
        };
    }
}
