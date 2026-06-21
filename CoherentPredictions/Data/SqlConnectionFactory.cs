using Microsoft.Data.SqlClient;

namespace CoherentPredictions.Data;

public class SqlConnectionFactory
{
    private readonly IConfiguration _configuration;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public SqlConnection Create()
    {
        return new SqlConnection(_configuration.GetConnectionString("Default"));
    }
}