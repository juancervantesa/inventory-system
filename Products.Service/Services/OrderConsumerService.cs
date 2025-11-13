using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Products.Service.Data;
using System.Text.Json;

namespace Products.Service.Services
{
    public class OrderConsumerService : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private Task _executingTask;
        private CancellationTokenSource _stoppingCts;

        public OrderConsumerService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = Task.Run(() => ConsumeOrders(_stoppingCts.Token), cancellationToken);
            return Task.CompletedTask;
        }

        private async Task ConsumeOrders(CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration["KAFKA_BOOTSTRAP_SERVERS"],
                GroupId = "products-service-group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe("orders_topic");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(cancellationToken);
                    var orderEvent = JsonSerializer.Deserialize<OrderEvent>(consumeResult.Message.Value);

                    if (orderEvent != null)
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
                            var product = await dbContext.Products.FindAsync(orderEvent.ProductId);

                            if (product != null)
                            {
                                product.Stock -= orderEvent.Quantity;
                                await dbContext.SaveChangesAsync(cancellationToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Allow the loop to be cancelled
                }
                catch (Exception ex)
                {
                    // Log the exception
                    Console.WriteLine($"Error consuming message: {ex.Message}");
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_executingTask == null)
            {
                return;
            }

            try
            {
                _stoppingCts.Cancel();
            }
            finally
            {
                await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
    }

    // A simple class to deserialize the event message
    public class OrderEvent
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
