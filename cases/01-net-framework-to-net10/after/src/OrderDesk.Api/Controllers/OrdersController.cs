using Microsoft.AspNetCore.Mvc;
using OrderDesk.Data;
using OrderDesk.Domain;

namespace OrderDesk.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders) : ControllerBase
{
    [HttpGet("{id:int}", Name = "GetOrder")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        return order is null ? NotFound() : Ok(OrderDto.From(order));
    }

    [HttpGet("")]
    public async Task<IActionResult> Find([FromQuery] int? customerId, [FromQuery] OrderStatus? status, CancellationToken ct) =>
        Ok((await orders.FindAsync(customerId, status, ct)).Select(OrderDto.From).ToList());

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest? request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid)
            return BadRequest(new ErrorBody("Request body is missing or malformed."));

        try
        {
            var lines = (request.Lines ?? []).Select(l => new NewOrderLine(l.Sku, l.Quantity, l.UnitPrice)).ToList();
            var order = await orders.CreateAsync(request.CustomerId, lines, ct);
            return CreatedAtRoute("GetOrder", new { id = order.Id }, OrderDto.From(order));
        }
        catch (OrderValidationException ex)
        {
            return BadRequest(new ErrorBody(ex.Message));
        }
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        try
        {
            var order = await orders.CancelAsync(id, ct);
            return order is null ? NotFound() : Ok(OrderDto.From(order));
        }
        catch (OrderConflictException ex)
        {
            return Conflict(new ErrorBody(ex.Message));
        }
    }
}
