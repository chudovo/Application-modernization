using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OrderDesk.Domain;

namespace OrderDesk.Api.Infrastructure
{
    /// <summary>Checks the X-Api-Key header for every route except the health check.</summary>
    public class ApiKeyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.AbsolutePath.StartsWith("/api/health"))
                return base.SendAsync(request, cancellationToken);

            IEnumerable<string> values;
            var supplied = request.Headers.TryGetValues("X-Api-Key", out values) ? values.FirstOrDefault() : null;
            if (string.IsNullOrEmpty(supplied) || supplied != AppSettings.ApiKey)
            {
                var response = request.CreateResponse(HttpStatusCode.Unauthorized, new ErrorBody { Message = "API key is missing or invalid." });
                return Task.FromResult(response);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    public class ErrorBody
    {
        public string Message { get; set; }
    }
}
