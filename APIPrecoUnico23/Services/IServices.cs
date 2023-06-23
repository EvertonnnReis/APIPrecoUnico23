using APIPrecoUnico23.Models;

namespace APIPrecoUnico23.Services
{
    public interface IServices
    {
        Task<dynamic> ObterRegistros(string _interface);
        Task EnviarApiSDP(string caminhoArquivo, List<string> listaSku);
        Task SalvarRegistrosEmArquivo(List<RegistroModel> registros, string caminhoArquivo);
    }
}
