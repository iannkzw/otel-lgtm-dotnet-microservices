using Confluent.Kafka;

namespace NotificationWorker.Metrics;

public sealed class NotificationLagRefresher(
    KafkaLagSnapshot snapshot,
    ILogger<NotificationLagRefresher> logger)
{
    public void Refresh(IConsumer<string, string> consumer)
    {
        try
        {
            var assignments = consumer.Assignment;

            if (assignments.Count == 0)
            {
                snapshot.Update(0);
                return;
            }

            long totalLag = 0;

            foreach (var topicPartition in assignments)
            {
                if (!string.Equals(topicPartition.Topic, snapshot.Topic, StringComparison.Ordinal))
                {
                    continue;
                }

                var position = consumer.Position(topicPartition);
                var watermarkOffsets = consumer.GetWatermarkOffsets(topicPartition);

                if (position.Value < 0 || watermarkOffsets.High.Value < 0)
                {
                    continue;
                }

                totalLag += Math.Max(watermarkOffsets.High.Value - position.Value, 0);
            }

            snapshot.Update(totalLag);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to refresh Kafka lag snapshot for topic {Topic}.", snapshot.Topic);
        }
    }
}