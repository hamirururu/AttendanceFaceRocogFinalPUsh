using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AttendanceFaceRocog
{
    public static class DatabaseHelper
    {
        // Update this connection string for your database
        private static readonly string ConnectionString = 
            "Server=localhost;Database=AttendanceFaceRecogDB;Trusted_Connection=True;TrustServerCertificate=True;";

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(ConnectionString);
        }

        /// <summary>
        /// Add a new employee to the database
        /// </summary>
        public static int AddEmployee(string fullName)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"INSERT INTO dbo.Employees (FullName) 
                           OUTPUT INSERTED.empID 
                           VALUES (@FullName)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@FullName", fullName);

            return (int)cmd.ExecuteScalar();
        }

        /// <summary>
        /// Add a face image path for an employee
        /// </summary>
        public static void AddFaceImage(int empId, string imagePath)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"INSERT INTO dbo.FaceImages (empID, imgPath) 
                           VALUES (@empID, @imgPath)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);
            cmd.Parameters.AddWithValue("@imgPath", imagePath);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Get all employees with their face images
        /// </summary>
        public static DataTable GetAllEmployeesWithFaces()
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT e.empID, e.empCode, e.FullName, f.imgPath 
                           FROM dbo.Employees e
                           INNER JOIN dbo.FaceImages f ON e.empID = f.empID";

            using var cmd = new SqlCommand(query, conn);
            using var adapter = new SqlDataAdapter(cmd);

            DataTable dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Get employee by ID
        /// </summary>
        public static (int empId, string empCode, string fullName)? GetEmployeeById(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = "SELECT empID, empCode, FullName FROM dbo.Employees WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            }
            return null;
        }

        /// <summary>
        /// Log attendance action
        /// </summary>
        public static void LogAttendance(int empId, string timeIn, string timeOut, string startBr, string stopBr)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"INSERT INTO dbo.Attendance (empID, TimeIn, TimeOut, StartBreak, StopBreak, LogTime) 
                           VALUES (@empID, @TimeIn, @TimeOut, @StartBreak, @StopBreak, GETDATE())";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);
            cmd.Parameters.AddWithValue("@TimeIn", timeIn);
            cmd.Parameters.AddWithValue("@TimeOut", timeOut);
            cmd.Parameters.AddWithValue("@StartBreak", startBr);
            cmd.Parameters.AddWithValue("@StopBreak", stopBr);

            cmd.ExecuteNonQuery();
        }

        public static string? GetEmployeeFaceImage(int empId)
        {
            try
            {
                using (SqlConnection con = new SqlConnection(ConnectionString))
                {
                    con.Open();

                    string query = @"SELECT TOP 1 imgPath 
                           FROM dbo.FaceImages 
                           WHERE empID = @empID 
                           ORDER BY createdDate DESC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@empID", empId);

                        object result = cmd.ExecuteScalar();

                        if (result != null && result != DBNull.Value)
                        {
                            return result.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting employee face image: {ex.Message}");
            }

            return null;
        }
    }
}