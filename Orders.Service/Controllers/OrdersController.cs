using Microsoft.AspNetCore.Mvc;
using Orders.Service.Data;
using Orders.Service.Models;
using Orders.Service.Services;

namespace Orders.Service.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly ProductsApiClient _productsApiClient;
    private readonly KafkaProducerService _kafkaProducerService;

    public OrdersController(OrderDbContext context, ProductsApiClient productsApiClient, KafkaProducerService kafkaProducerService)
    {
        _context = context;
        _productsApiClient = productsApiClient;
        _kafkaProducerService = kafkaProducerService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] Order order)
    {
        var product = await _productsApiClient.GetProductAsync(order.ProductId);

        if (product == null)
        {
            return BadRequest("Product not found.");
        }

        if (product.Stock < order.Quantity)
        {
            return BadRequest("Insufficient stock.");
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // Produce Kafka event
        await _kafkaProducerService.ProduceOrderPlacedEventAsync(order);
        //------------------------


        return CreatedAtAction(nameof(CreateOrder), new { id = order.Id }, order);
    }
}
}
