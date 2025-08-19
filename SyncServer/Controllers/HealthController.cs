using Microsoft.AspNetCore.Mvc;

namespace SyncServer.Controllers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>
        /// Restituisce lo stato di salute del server.
        /// </summary>

        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "Healthy",
                timestamp = DateTime.Now
            });
        }

    }
}
