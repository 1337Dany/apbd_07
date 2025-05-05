using System.Data.SqlClient;

namespace TravelAgencyAPI.Models
{
    public class DatabaseHelper
    {
        private readonly string _connectionString;
        
        public DatabaseHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TravelAgencyDB");
        }
        
        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}