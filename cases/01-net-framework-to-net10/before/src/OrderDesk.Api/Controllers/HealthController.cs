using System.Web.Http;

namespace OrderDesk.Api.Controllers
{
    public class HealthController : ApiController
    {
        [HttpGet, Route("api/health")]
        public IHttpActionResult Get()
        {
            return Ok(new { Status = "ok" });
        }
    }
}
