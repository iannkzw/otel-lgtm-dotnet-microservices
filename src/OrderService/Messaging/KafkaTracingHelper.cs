using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Shared;

namespace OrderService.Messaging;

public static class KafkaTracingHelper
{
    public static void Inject(Activity? activity, Headers headers)
    {
        W3CTraceContext.Inject(activity, (key, value) => SetHeader(headers, key, value));
    }

    public static ActivityContext? Extract(Headers? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        var traceParent = GetHeader(headers, "traceparent");
        var traceState = GetHeader(headers, "tracestate");

        return W3CTraceContext.Extract(traceParent, traceState);
    }

    private static string? GetHeader(Headers headers, string key)
    {
        foreach (var header in headers)
        {
            if (!string.Equals(header.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Encoding.ASCII.GetString(header.GetValueBytes());
        }

        return null;
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.ASCII.GetBytes(value));
    }
}