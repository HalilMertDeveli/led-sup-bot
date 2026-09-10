namespace LedSupBot.Application.Workflow;

public static class NaturalResponseTimingCalculator
{
    public static TimeSpan ComputeDelay(string outboundText, NaturalResponseTimingOptions options)
    {
        if (!options.Enabled)
        {
            return TimeSpan.Zero;
        }

        var length = outboundText?.Trim().Length ?? 0;
        double minSeconds;
        double maxSeconds;

        if (length <= options.GreetingMaxChars)
        {
            minSeconds = options.GreetingMinSeconds;
            maxSeconds = options.GreetingMaxSeconds;
        }
        else if (length <= options.ShortMaxChars)
        {
            minSeconds = options.ShortMinSeconds;
            maxSeconds = options.ShortMaxSeconds;
        }
        else if (length <= options.MediumMaxChars)
        {
            minSeconds = options.MediumMinSeconds;
            maxSeconds = options.MediumMaxSeconds;
        }
        else
        {
            minSeconds = options.LongMinSeconds;
            maxSeconds = options.LongMaxSeconds;
        }

        var span = maxSeconds - minSeconds;
        var seconds = span <= 0
            ? minSeconds
            : minSeconds + (Random.Shared.NextDouble() * span);

        seconds = Math.Clamp(seconds, 0, options.MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
