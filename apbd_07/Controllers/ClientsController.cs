using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using TravelAgencyAPI.Models;

namespace TravelAgencyAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClientsController : ControllerBase
    {
        private readonly DatabaseHelper _dbHelper;
        
        public ClientsController(DatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }
        
        /// <summary>
        /// Retrieves all trips associated with a specific client
        /// </summary>
        /// <param name="id">Client ID</param>
        [HttpGet("{id}/trips")]
        public async Task<IActionResult> GetClientTrips(int id)
        {
            try
            {
                using (var connection = _dbHelper.GetConnection())
                {
                    await connection.OpenAsync();
                    
                    // First check if client exists
                    var clientCheckQuery = "SELECT 1 FROM Client WHERE IdClient = @IdClient";
                    using (var clientCheckCmd = new SqlCommand(clientCheckQuery, connection))
                    {
                        clientCheckCmd.Parameters.AddWithValue("@IdClient", id);
                        var clientExists = await clientCheckCmd.ExecuteScalarAsync();
                        
                        if (clientExists == null)
                        {
                            return NotFound($"Client with ID {id} not found");
                        }
                    }
                    
                    var query = @"
                        SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                               ct.RegisteredAt, ct.PaymentDate
                        FROM Client_Trip ct
                        JOIN Trip t ON ct.IdTrip = t.IdTrip
                        WHERE ct.IdClient = @IdClient
                        ORDER BY t.DateFrom";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@IdClient", id);
                        
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
                                    RegisteredAt = reader.GetInt32(6),
                                    PaymentDate = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7)
                                });
                            }
                            
                            if (trips.Count == 0)
                            {
                                return Ok($"Client with ID {id} has no registered trips");
                            }
                            
                            return Ok(trips);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new client record
        /// </summary>
        /// <param name="client">Client data to create</param>
        [HttpPost]
        public async Task<IActionResult> CreateClient([FromBody] ClientDto client)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            if (string.IsNullOrWhiteSpace(client.FirstName)) 
            {
                return BadRequest("FirstName is required");
            }
            
            if (string.IsNullOrWhiteSpace(client.LastName))
            {
                return BadRequest("LastName is required");
            }
            
            if (string.IsNullOrWhiteSpace(client.Email))
            {
                return BadRequest("Email is required");
            }
            
            try
            {
                using (var connection = _dbHelper.GetConnection())
                {
                    await connection.OpenAsync();
                    
                    var query = @"
                        INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                        OUTPUT INSERTED.IdClient
                        VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@FirstName", client.FirstName);
                        command.Parameters.AddWithValue("@LastName", client.LastName);
                        command.Parameters.AddWithValue("@Email", client.Email);
                        command.Parameters.AddWithValue("@Telephone", (object)client.Telephone ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pesel", (object)client.Pesel ?? DBNull.Value);
                        
                        var newId = await command.ExecuteScalarAsync();
                        
                        return CreatedAtAction(nameof(GetClientTrips), new { id = newId }, new { IdClient = newId });
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 2627)
            {
                return Conflict("A client with this email already exists");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a client for a specific trip
        /// </summary>
        /// <param name="id">Client ID</param>
        /// <param name="tripid">Trip ID</param>
        [HttpPut("{id}/trips/{tripid}")]
        public async Task<IActionResult> RegisterForTrip(int id, int tripid)
        {
            try
            {
                using (var connection = _dbHelper.GetConnection())
                {
                    await connection.OpenAsync();
                    
                    // Check if client exists
                    var clientCheckQuery = "SELECT 1 FROM Client WHERE IdClient = @IdClient";
                    using (var clientCheckCmd = new SqlCommand(clientCheckQuery, connection))
                    {
                        clientCheckCmd.Parameters.AddWithValue("@IdClient", id);
                        var clientExists = await clientCheckCmd.ExecuteScalarAsync();
                        
                        if (clientExists == null)
                        {
                            return NotFound($"Client with ID {id} not found");
                        }
                    }
                    
                    // Check if trip exists
                    var tripCheckQuery = "SELECT MaxPeople FROM Trip WHERE IdTrip = @IdTrip";
                    int maxPeople;
                    using (var tripCheckCmd = new SqlCommand(tripCheckQuery, connection))
                    {
                        tripCheckCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        var tripExists = await tripCheckCmd.ExecuteScalarAsync();
                        
                        if (tripExists == null)
                        {
                            return NotFound($"Trip with ID {tripid} not found");
                        }
                        
                        maxPeople = Convert.ToInt32(tripExists);
                    }
                    
                    // Check if registration already exists
                    var registrationCheckQuery = "SELECT 1 FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip";
                    using (var registrationCheckCmd = new SqlCommand(registrationCheckQuery, connection))
                    {
                        registrationCheckCmd.Parameters.AddWithValue("@IdClient", id);
                        registrationCheckCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        var registrationExists = await registrationCheckCmd.ExecuteScalarAsync();
                        
                        if (registrationExists != null)
                        {
                            return Conflict($"Client is already registered for this trip");
                        }
                    }
                    
                    // Check current number of participants
                    var participantsQuery = "SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @IdTrip";
                    int currentParticipants;
                    using (var participantsCmd = new SqlCommand(participantsQuery, connection))
                    {
                        participantsCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        currentParticipants = Convert.ToInt32(await participantsCmd.ExecuteScalarAsync());
                    }
                    
                    if (currentParticipants >= maxPeople)
                    {
                        return BadRequest("This trip has reached its maximum number of participants");
                    }
                    
                    // Register the client
                    var registerQuery = @"
                        INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt)
                        VALUES (@IdClient, @IdTrip, @RegisteredAt)";
                    
                    using (var registerCmd = new SqlCommand(registerQuery, connection))
                    {
                        registerCmd.Parameters.AddWithValue("@IdClient", id);
                        registerCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        registerCmd.Parameters.AddWithValue("@RegisteredAt", DateTime.Now.ToString("yyyyMMdd"));
                        
                        await registerCmd.ExecuteNonQueryAsync();
                        
                        return Ok($"Client {id} successfully registered for trip {tripid}");
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a client's registration from a trip
        /// </summary>
        /// <param name="id">Client ID</param>
        /// <param name="tripid">Trip ID</param>
        [HttpDelete("{id}/trips/{tripid}")]
        public async Task<IActionResult> CancelTripRegistration(int id, int tripid)
        {
            try
            {
                using (var connection = _dbHelper.GetConnection())
                {
                    await connection.OpenAsync();
                    
                    // Check if registration exists
                    var checkQuery = "SELECT 1 FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip";
                    using (var checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@IdClient", id);
                        checkCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        var registrationExists = await checkCmd.ExecuteScalarAsync();
                        
                        if (registrationExists == null)
                        {
                            return NotFound($"Registration not found for client {id} and trip {tripid}");
                        }
                    }
                    
                    // Delete the registration
                    var deleteQuery = "DELETE FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip";
                    using (var deleteCmd = new SqlCommand(deleteQuery, connection))
                    {
                        deleteCmd.Parameters.AddWithValue("@IdClient", id);
                        deleteCmd.Parameters.AddWithValue("@IdTrip", tripid);
                        
                        int rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Ok($"Registration for client {id} on trip {tripid} has been canceled");
                        }
                        else
                        {
                            return StatusCode(500, "Failed to cancel registration");
                        }
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