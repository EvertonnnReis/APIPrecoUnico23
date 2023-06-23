using APIPrecoUnico23.Services;
using Microsoft.AspNetCore.Mvc;

namespace APIPrecoUnico23.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegistroController : ControllerBase
    {
        private readonly IServices _services;

        public RegistroController(IConfiguration configuration,
            IServices services)
        {
            _services = services;
        }



        [HttpGet]
        public async Task<dynamic> ObterRegistros()
        {
            var registro = await _services.ObterRegistros("E48FF01D-1C9F-4505-87E1-953E7046E65C");

            // Retorna os registros como resposta JSON
            return new { registro };
        }
    }
}
