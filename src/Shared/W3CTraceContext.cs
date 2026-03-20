using System.Diagnostics;

namespace Shared;

public static class W3CTraceContext
{
    public static ActivityContext? Extract(string? traceParent, string? traceState)
    {
        if (string.IsNullOrWhiteSpace(traceParent))
        {
            return null;
        }

        return ActivityContext.TryParse(traceParent, traceState, out var activityContext)
            ? activityContext
            : null;
    }

    public static void Inject(Activity? activity, Action<string, string> setHeader)
    {
        if (activity is null)
        {
            return;
        }

        var traceParent = activity.Id;

        if (string.IsNullOrWhiteSpace(traceParent))
        {
            return;
        }

        setHeader("traceparent", traceParent);

        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
        {
            setHeader("tracestate", activity.TraceStateString);
        }
    }
}