using Confluent.Kafka;
using Orders.Service.Models;
using System.Text.Json;

namespace Orders.Service.Services
{
    public class KafkaProducerService : IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private readonly string _topicName = "orders_topic";

        public KafkaProducerService(IConfiguration configuration)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = configuration["KAFKA_BOOTSTRAP_SERVERS"],
               // Acks = Acks.All
            };
            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        public async Task ProduceOrderPlacedEventAsync(Order order)
        {
            var message = new
            {
                order.ProductId,
                order.Quantity
            };

            var serializedMessage = JsonSerializer.Serialize(message);

            await _producer.ProduceAsync(_topicName, new Message<string, string> { Key = order.Id.ToString(), Value = serializedMessage });
            _producer.Flush(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            _producer.Dispose();
        }
    }
}
