using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using OrderDesk.Api.Infrastructure;
using OrderDesk.Data;
using OrderDesk.Domain;

namespace OrderDesk.Api.Controllers
{
    [RoutePrefix("api/orders")]
    public class OrdersController : ApiController
    {
        private readonly OrderService _orders;

        // Web API 2 instantiates controllers through the parameterless constructor.
        public OrdersController() : this(ServiceLocator.Resolve<OrderService>()) { }

        public OrdersController(OrderService orders)
        {
            _orders = orders;
        }

        [HttpGet, Route("{id:int}", Name = "GetOrder")]
        public IHttpActionResult Get(int id)
        {
            var order = _orders.Get(id);
            if (order == null) return NotFound();
            return Ok(OrderDto.From(order));
        }

        [HttpGet, Route("")]
        public IHttpActionResult Find(int? customerId = null, OrderStatus? status = null)
        {
            return Ok(_orders.Find(customerId, status).Select(OrderDto.From).ToList());
        }

        [HttpPost, Route("")]
        public IHttpActionResult Create(CreateOrderRequest request)
        {
            if (request == null || !ModelState.IsValid)
                return BadRequest("Request body is missing or malformed.");

            try
            {
                var lines = (request.Lines ?? new System.Collections.Generic.List<CreateOrderLine>())
                    .Select(l => new NewOrderLine { Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList();
                var order = _orders.Create(request.CustomerId, lines);
                return CreatedAtRoute("GetOrder", new { id = order.Id }, OrderDto.From(order));
            }
            catch (OrderValidationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost, Route("{id:int}/cancel")]
        public IHttpActionResult Cancel(int id)
        {
            try
            {
                var order = _orders.Cancel(id);
                if (order == null) return NotFound();
                return Ok(OrderDto.From(order));
            }
            catch (OrderConflictException ex)
            {
                return Content(HttpStatusCode.Conflict, new ErrorBody { Message = ex.Message });
            }
        }
    }
}
