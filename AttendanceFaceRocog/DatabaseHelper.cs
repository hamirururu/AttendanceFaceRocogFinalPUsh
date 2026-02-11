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
        /// Get all employees with only ONE face image each (for profile display)
        /// </summary>
        public static DataTable GetAllEmployeesForDisplay()
        {
            using var conn = GetConnection();
            conn.Open();

            // Use ROW_NUMBER() to get only the latest image per employee
            string query = @"
                WITH RankedImages AS (
                    SELECT 
                        e.empID, 
                        e.empCode, 
                        e.FullName, 
                        e.profilePhotoPath,
                        f.imgPath,
                        ROW_NUMBER() OVER (PARTITION BY e.empID ORDER BY f.faceImageID DESC) AS rn
                    FROM dbo.Employees e
                    LEFT JOIN dbo.FaceImages f ON e.empID = f.empID
                )
                SELECT empID, empCode, FullName, 
                       COALESCE(profilePhotoPath, imgPath) AS imgPath
                FROM RankedImages
                WHERE rn = 1
                ORDER BY FullName";

            using var cmd = new SqlCommand(query, conn);
            using var adapter = new SqlDataAdapter(cmd);

            DataTable dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Get all employees with their face images (for training - returns ALL images)
        /// </summary>
        public static DataTable GetAllEmployeesWithFaces()
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT e.empID, e.empCode, e.FullName, f.imgPath 
                           FROM dbo.Employees e
                           INNER JOIN dbo.FaceImages f ON e.empID = f.empID
                           ORDER BY e.FullName";

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
        /// Log attendance action - Only allows one entry per action per day
        /// Returns: (success, message)
        /// </summary>
        public static (bool success, string message) LogAttendance(int empId, string timeIn, string timeOut, string startBr, string stopBr)
        {
            using var conn = GetConnection();
            conn.Open();

            // Check if a record exists for this employee today
            string checkQuery = @"SELECT attendanceID, TimeIn, TimeOut, StartBreak, StopBreak 
                                 FROM dbo.Attendance 
                                 WHERE empID = @empID AND CAST(LogTime AS DATE) = CAST(GETDATE() AS DATE)";

            using var checkCmd = new SqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("@empID", empId);

            using var reader = checkCmd.ExecuteReader();

            if (reader.Read())
            {
                int attendanceId = reader.GetInt32(0);
                string existingTimeIn = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string existingTimeOut = reader.IsDBNull(2) ? "" : reader.GetString(2);
                string existingStartBreak = reader.IsDBNull(3) ? "" : reader.GetString(3);
                string existingStopBreak = reader.IsDBNull(4) ? "" : reader.GetString(4);

                reader.Close();

                // Check if the action is already recorded
                if (!string.IsNullOrEmpty(timeIn) && !string.IsNullOrEmpty(existingTimeIn))
                    return (false, "Time In already recorded today.");

                if (!string.IsNullOrEmpty(timeOut) && !string.IsNullOrEmpty(existingTimeOut))
                    return (false, "Time Out already recorded today.");

                if (!string.IsNullOrEmpty(startBr) && !string.IsNullOrEmpty(existingStartBreak))
                    return (false, "Start Break already recorded today.");

                if (!string.IsNullOrEmpty(stopBr) && !string.IsNullOrEmpty(existingStopBreak))
                    return (false, "Stop Break already recorded today.");

                // Update only empty fields
                string updateQuery = @"UPDATE dbo.Attendance SET 
                                      TimeIn = CASE WHEN @TimeIn != '' AND TimeIn IS NULL THEN @TimeIn ELSE TimeIn END,
                                      TimeOut = CASE WHEN @TimeOut != '' AND TimeOut IS NULL THEN @TimeOut ELSE TimeOut END,
                                      StartBreak = CASE WHEN @StartBreak != '' AND StartBreak IS NULL THEN @StartBreak ELSE StartBreak END,
                                      StopBreak = CASE WHEN @StopBreak != '' AND StopBreak IS NULL THEN @StopBreak ELSE StopBreak END
                                      WHERE attendanceID = @attendanceID";

                using var updateCmd = new SqlCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("@TimeIn", timeIn ?? "");
                updateCmd.Parameters.AddWithValue("@TimeOut", timeOut ?? "");
                updateCmd.Parameters.AddWithValue("@StartBreak", startBr ?? "");
                updateCmd.Parameters.AddWithValue("@StopBreak", stopBr ?? "");
                updateCmd.Parameters.AddWithValue("@attendanceID", attendanceId);

                updateCmd.ExecuteNonQuery();
            }
            else
            {
                reader.Close();

                // Insert new record for today
                string insertQuery = @"INSERT INTO dbo.Attendance (empID, TimeIn, TimeOut, StartBreak, StopBreak, LogTime) 
                                      VALUES (@empID, @TimeIn, @TimeOut, @StartBreak, @StopBreak, GETDATE())";

                using var insertCmd = new SqlCommand(insertQuery, conn);
                insertCmd.Parameters.AddWithValue("@empID", empId);
                insertCmd.Parameters.AddWithValue("@TimeIn", timeIn ?? "");
                insertCmd.Parameters.AddWithValue("@TimeOut", timeOut ?? "");
                insertCmd.Parameters.AddWithValue("@StartBreak", startBr ?? "");
                insertCmd.Parameters.AddWithValue("@StopBreak", stopBr ?? "");

                insertCmd.ExecuteNonQuery();
            }

            return (true, "Attendance recorded successfully.");
        }

        /// <summary>
        /// Get the most recent face image for an employee
        /// </summary>
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
                           ORDER BY faceImageID DESC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@empID", empId);

                        object? result = cmd.ExecuteScalar();

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

        /// <summary>
        /// Get attendance history for an employee (last 7 days by default)
        /// </summary>
        public static DataTable GetEmployeeAttendanceHistory(int empId, int days = 7)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT 
                               CONVERT(VARCHAR(10), LogTime, 120) AS [Date],
                               TimeIn AS [Time In],
                               TimeOut AS [Time Out],
                               StartBreak AS [Start Break],
                               StopBreak AS [Stop Break]
                           FROM dbo.Attendance 
                           WHERE empID = @empID 
                             AND LogTime >= DATEADD(DAY, -@Days, GETDATE())
                           ORDER BY LogTime DESC";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);
            cmd.Parameters.AddWithValue("@Days", days);

            using var adapter = new SqlDataAdapter(cmd);
            DataTable dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Get today's attendance status for an employee
        /// Returns which actions have already been recorded
        /// </summary>
        public static (bool hasTimeIn, bool hasTimeOut, bool hasStartBreak, bool hasStopBreak) GetTodayAttendanceStatus(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT TimeIn, TimeOut, StartBreak, StopBreak 
                     FROM dbo.Attendance 
                     WHERE empID = @empID AND CAST(LogTime AS DATE) = CAST(GETDATE() AS DATE)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                bool hasTimeIn = !reader.IsDBNull(0) && !string.IsNullOrEmpty(reader.GetString(0));
                bool hasTimeOut = !reader.IsDBNull(1) && !string.IsNullOrEmpty(reader.GetString(1));
                bool hasStartBreak = !reader.IsDBNull(2) && !string.IsNullOrEmpty(reader.GetString(2));
                bool hasStopBreak = !reader.IsDBNull(3) && !string.IsNullOrEmpty(reader.GetString(3));

                return (hasTimeIn, hasTimeOut, hasStartBreak, hasStopBreak);
            }

            return (false, false, false, false);
        }

        /// <summary>
        /// Get all attendance records for today
        /// </summary>
        public static DataTable GetTodayAllAttendance()
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT 
                        a.attendanceID,
                        a.empID,
                        e.FullName,
                        e.empCode,
                        a.TimeIn,
                        a.TimeOut,
                        a.StartBreak,
                        a.StopBreak,
                        a.LogTime
                   FROM dbo.Attendance a
                   INNER JOIN dbo.Employees e ON a.empID = e.empID
                   WHERE CAST(a.LogTime AS DATE) = CAST(GETDATE() AS DATE)
                   ORDER BY a.LogTime DESC";

            using var cmd = new SqlCommand(query, conn);
            using var adapter = new SqlDataAdapter(cmd);
    
            DataTable dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Get attendance statistics for today
        /// </summary>
        public static (int timeInCount, int timeOutCount, int startBreakCount, int stopBreakCount) GetTodayAttendanceStats()
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT 
                        COUNT(CASE WHEN TimeIn IS NOT NULL AND TimeIn != '' THEN 1 END) AS TimeInCount,
                        COUNT(CASE WHEN TimeOut IS NOT NULL AND TimeOut != '' THEN 1 END) AS TimeOutCount,
                        COUNT(CASE WHEN StartBreak IS NOT NULL AND StartBreak != '' THEN 1 END) AS StartBreakCount,
                        COUNT(CASE WHEN StopBreak IS NOT NULL AND StopBreak != '' THEN 1 END) AS StopBreakCount
                   FROM dbo.Attendance
                   WHERE CAST(LogTime AS DATE) = CAST(GETDATE() AS DATE)";

            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
    
            if (reader.Read())
            {
                return (
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)
                );
            }

            return (0, 0, 0, 0);
        }

        /// <summary>
        /// Update employee name
        /// </summary>
        public static void UpdateEmployee(int empId, string fullName)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"UPDATE dbo.Employees 
                     SET FullName = @FullName 
                     WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.Parameters.AddWithValue("@empID", empId);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Delete employee and all related data (cascade handled by FK constraint)
        /// </summary>
        public static void DeleteEmployee(int empId)
        {
            using var conn = GetConnection();
            conn.Open();
            
            // Delete attendance records
            using (var cmd = new SqlCommand("DELETE FROM dbo.Attendance WHERE empID = @empId", conn))
            {
                cmd.Parameters.AddWithValue("@empId", empId);
                cmd.ExecuteNonQuery();
            }

            // Delete face images (FK constraint will handle cascade)
            using (var cmd = new SqlCommand("DELETE FROM dbo.FaceImages WHERE empID = @empId", conn))
            {
                cmd.Parameters.AddWithValue("@empId", empId);
                cmd.ExecuteNonQuery();
            }
            
            // Delete employee
            using (var cmd = new SqlCommand("DELETE FROM dbo.Employees WHERE empID = @empId", conn))
            {
                cmd.Parameters.AddWithValue("@empId", empId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Add/Update a full profile photo for an employee
        /// </summary>
        public static void AddProfilePhoto(int empId, string photoPath)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"UPDATE dbo.Employees 
                           SET profilePhotoPath = @profilePhotoPath 
                           WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);
            cmd.Parameters.AddWithValue("@profilePhotoPath", photoPath);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Get profile photo path for an employee
        /// </summary>
        public static string? GetProfilePhoto(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            string query = @"SELECT profilePhotoPath FROM dbo.Employees WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@empID", empId);

            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? result.ToString() : null;
        }
    }
}