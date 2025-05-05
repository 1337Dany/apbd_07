using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using TravelAgencyAPI.Models;

namespace TravelAgencyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TripsController : ControllerBase
    {
        private readonly DatabaseHelper _dbHelper;
        
        public TripsController(DatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }
        
        /// <summary>
        /// Retrieves all available trips with their basic information and associated countries
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllTrips()
        {
            try
            {
                using (var connection = _dbHelper.GetConnection())
                {
                    await connection.OpenAsync();
                    
                    var query = @"
                        SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                               STRING_AGG(c.Name, ', ') AS Countries
                        FROM Trip t
                        JOIN Country_Trip ct ON t.IdTrip = ct.IdTrip
                        JOIN Country c ON ct.IdCountry = c.IdCountry
                        GROUP BY t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople
                        ORDER BY t.DateFrom";
                    
                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var trips = new List<object>();
                        
                        while (await reader.ReadAsync())
                        {
                            trips.Add(new
                            {
                                IdTrip = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                                DateFrom = reader.GetDateTime(3),
                                DateTo = reader.GetDateTime(4),
                                MaxPeople = reader.GetInt32(5),
                                Countries = reader.GetString(6)
                            });
                        }
                        
                        return Ok(trips);
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}