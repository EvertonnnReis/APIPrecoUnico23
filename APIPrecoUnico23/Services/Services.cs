using APIPrecoUnico23.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace APIPrecoUnico23.Services
{
    public class Services : IServices
    {
        private readonly string _connectionString;
        private readonly string _urlDestino;
        private readonly string _authorization;

        public Services(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _urlDestino = configuration["API:UrlDestino"];
            _authorization = configuration["API:Authorization"];
        }
        public async Task<dynamic> ObterRegistros(string _interface)
        {
            List<RegistroModel> registros = new List<RegistroModel>();
            List<string> listaSku = new List<string>();

            // Conecta ao banco de dados
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Cria o comando SQL
                using (var cmd = new System.Data.SqlClient.SqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"
                        SELECT TOP 75 PRODUTOCODIGOEXTERNO AS SKU, 
                                   PRODUTOPRECOTABELANOVO AS Preco, 
                                   PRODUTOPRECOPROMOCIONALNOVO AS PrecoPromocao,
                                   DATACONFIRMACAO AS InicioPromocao, 
                                   TERMINOPROMOCAO = '2050-12-31 00:00:00.000', 
                                   INTERFACE = @Interface,
                                   LISTAPRECO = 'LISTA DE PRECO B2W SP'
                        FROM CONNECTPARTS.DBO.PRECIFICACOES
                        WHERE DataEnvioAtualizacao IS NULL 
                              AND DATACONFIRMACAO >= '2023-06-21'
                              AND PRODUTOLISTAPRECOCODIGO = 23
                              AND APROVADO = 1
                        ORDER BY DATACONFIRMACAO ASC";

                    cmd.Parameters.AddWithValue("@Interface", _interface);

                    // Executa o comando e obtém os resultados
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var registro = new RegistroModel
                            {
                                SKU = reader.GetString(reader.GetOrdinal("SKU")),
                                Preco = reader.GetDecimal(reader.GetOrdinal("Preco")).ToString(),
                                PrecoPromocao = reader.GetDecimal(reader.GetOrdinal("PrecoPromocao")).ToString(),
                                InicioPromocao = reader.GetDateTime(reader.GetOrdinal("InicioPromocao")).ToString("yyyy-MM-dd HH:mm:ss"),
                                TerminoPromocao = "2050-12-31 00:00:00.000",
                                INTERFACE = _interface,
                                ListaPreco = "LISTA DE PRECO B2W SP"
                            };
                            // Trocar vírgula por . para o JSON enviar a SDP
                            registro.Preco = registro.Preco.Replace(",", ".");
                            registro.PrecoPromocao = registro.PrecoPromocao.Replace(",", ".");
                            if (registro.PrecoPromocao == "0")
                            {
                                registro.PrecoPromocao = registro.Preco;
                            }
                            registros.Add(registro);
                            listaSku.Add(registro.SKU);
                        }
                    }
                }

                if (registros.Count > 0)
                {
                    string pastaDestino = Path.Combine("C:/APIPreco/2", DateTime.Now.ToString("dd-MM-yyyy"));

                    if (!Directory.Exists(pastaDestino))
                    {
                        // Cria a pasta de destino se não existir
                        Directory.CreateDirectory(pastaDestino);
                    }

                    // Gera o nome do arquivo único para essa iteração
                    string dataAtual = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string nomeArquivo = $"{_interface}_{dataAtual}.json";
                    string caminhoArquivo = Path.Combine(pastaDestino, nomeArquivo);

                    // Salva os registros em um arquivo JSON
                    await SalvarRegistrosEmArquivo(registros, caminhoArquivo);

                    // Envia os registros para a API da SDP
                    await EnviarApiSDP(caminhoArquivo, listaSku);
                }
            }

            // Retorna os registros como resposta JSON
            return registros;
        }
        public async Task SalvarRegistrosEmArquivo(List<RegistroModel> registros, string caminhoArquivo)
        {
            using (StreamWriter file = System.IO.File.CreateText(caminhoArquivo))
            {
                var serializer = new Newtonsoft.Json.JsonSerializer();
                serializer.Serialize(file, registros);
            }
            Console.WriteLine("1");
        }

        [HttpGet("obter")]

        public async Task EnviarApiSDP(string caminhoArquivo, List<string> listaSku)
        {
            string conteudo = await System.IO.File.ReadAllTextAsync(caminhoArquivo);
            dynamic retornoApi = null;


            using (var httpClient = new HttpClient())
            {
                var jsonContent = new StringContent(conteudo);
                jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "Y29ubmVjdC0tMzU2QFNIT1BQSU5HOkdZWnVPdEVWOU1YUQ==");

                var response = await httpClient.PostAsync(_urlDestino, jsonContent);

                if (response.IsSuccessStatusCode) // Se a resposta for 200
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var responseData = JsonConvert.DeserializeObject(responseContent);
                        if (responseData is JObject jsonObject && jsonObject.ContainsKey("status") && jsonObject.ContainsKey("protocolo"))
                        {
                            var corpoResposta = conteudo;
                            Console.WriteLine("Dados enviados para a API com sucesso!");
                            retornoApi = responseContent;

                        }
                        else
                        {
                            var corpoResposta = conteudo;
                            retornoApi = null;
                        }
                    }
                    catch (JsonReaderException)
                    {
                        var corpoResposta = conteudo;
                    }
                }
                else // Se for diferente de 200
                {
                    var corpoResposta = conteudo;
                    Console.WriteLine("Erro ao enviar dados para a API: " + await response.Content.ReadAsStringAsync());
                }
            }
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Insere os dados de resposta na tabela CONTROLE_API_PRECO
                using (var cmd = new System.Data.SqlClient.SqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"
                        INSERT INTO CONTROLE_API_PRECO (JSON, LISTA_ENV, STATUS_RETORNO_API)
                        VALUES (@json, @listaEnv, @statusRetornoApi)";

                    string jsonSemBarra = conteudo.Replace("\\", "");
                    string statusRetornoApi = retornoApi != null ? JsonConvert.SerializeObject(retornoApi) : null;
                    statusRetornoApi = statusRetornoApi.Replace("\\", "");

                    cmd.Parameters.AddWithValue("@json", jsonSemBarra);
                    cmd.Parameters.AddWithValue("@listaEnv", 23);
                    cmd.Parameters.AddWithValue("@statusRetornoApi", statusRetornoApi != null ? (object)statusRetornoApi : DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Transforma SKU no padrão ('SKU', 'sku')
                string listaSkuFormatada = "'" + string.Join("','", listaSku.Select(sku => sku).ToList()) + "'";

                // Atualiza os registros no banco de dados
                using (var cmd = new System.Data.SqlClient.SqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = $"UPDATE Precificacoes " +
                        $"SET DataEnvioAtualizacao = GETDATE() " +
                        $"WHERE PRODUTOCODIGOEXTERNO IN ({listaSkuFormatada}) AND ProdutoListaPrecoCodigo = 23 " +
                        $"AND DataConfirmacao >= '2023-06-21' ";

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
