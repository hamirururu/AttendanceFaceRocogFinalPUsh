using System;
using System.Data;
using System.Globalization;
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

            const string query = @"
INSERT INTO dbo.Employees (FullName)
OUTPUT INSERTED.empID
VALUES (@FullName)";

            using var cmd = new SqlCommand(query, conn);
            AddNVarChar(cmd, "@FullName", 150, fullName);

            object? result = cmd.ExecuteScalar();
            return result is int empId ? empId : Convert.ToInt32(result);
        }

        /// <summary>
        /// Add a face image path for an employee
        /// </summary>
        public static void AddFaceImage(int empId, string imagePath)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
INSERT INTO dbo.FaceImages (empID, imgPath)
VALUES (@empID, @imgPath)";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);
            AddNVarChar(cmd, "@imgPath", 255, imagePath);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Add or update profile photo for an employee
        /// </summary>
        public static void AddProfilePhoto(int empId, string photoPath)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
UPDATE dbo.Employees
SET profilePhotoPath = @profilePhotoPath
WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);
            AddNVarChar(cmd, "@profilePhotoPath", 255, photoPath);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Get employee profile photo path
        /// </summary>
        public static string? GetProfilePhoto(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
SELECT profilePhotoPath
FROM dbo.Employees
WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);

            object? result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToString(result);
        }

        /// <summary>
        /// Get all employees with only ONE face image each (for profile display)
        /// </summary>
        public static DataTable GetAllEmployeesForDisplay()
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
WITH RankedImages AS
(
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
SELECT
    empID,
    empCode,
    FullName,
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

            const string query = @"
SELECT
    e.empID,
    e.empCode,
    e.FullName,
    f.imgPath
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

            const string query = @"
SELECT empID, empCode, FullName
FROM dbo.Employees
WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2));
            }

            return null;
        }

        /// <summary>
        /// Get the latest face image path for an employee
        /// </summary>
        public static string? GetEmployeeFaceImage(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
SELECT TOP 1 imgPath
FROM dbo.FaceImages
WHERE empID = @empID
ORDER BY faceImageID DESC";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);

            object? result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToString(result);
        }

        /// <summary>
        /// Log attendance action - Only allows one entry per action per day
        /// Returns: (success, message)
        /// </summary>
        public static (bool success, string message) LogAttendance(int empId, string? timeIn, string? timeOut, string? startBr, string? stopBr)
        {
            TimeSpan? timeInValue     = ParseAttendanceTime(timeIn);
            TimeSpan? timeOutValue    = ParseAttendanceTime(timeOut);
            TimeSpan? startBreakValue = ParseAttendanceTime(startBr);
            TimeSpan? stopBreakValue  = ParseAttendanceTime(stopBr);

            using var conn = GetConnection();
            conn.Open();

            const string checkQuery = @"
SELECT TOP 1 attendanceID, TimeIn, TimeOut, StartBreak, StopBreak
FROM dbo.Attendance
WHERE empID = @empID
  AND CAST(LogTime AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY attendanceID DESC";

            using var checkCmd = new SqlCommand(checkQuery, conn);
            AddInt(checkCmd, "@empID", empId);

            using var reader = checkCmd.ExecuteReader();

            if (reader.Read())
            {
                int attendanceId  = reader.GetInt32(0);
                bool hasTimeIn    = !reader.IsDBNull(1);
                bool hasTimeOut   = !reader.IsDBNull(2);
                bool hasStartBreak = !reader.IsDBNull(3);
                bool hasStopBreak  = !reader.IsDBNull(4);

                reader.Close();

                if (timeInValue.HasValue && hasTimeIn)
                    return (false, "Time In already recorded today.");

                if (timeOutValue.HasValue && hasTimeOut)
                    return (false, "Time Out already recorded today.");

                if (startBreakValue.HasValue && hasStartBreak)
                    return (false, "Start Break already recorded today.");

                if (stopBreakValue.HasValue && hasStopBreak)
                    return (false, "Stop Break already recorded today.");

                const string updateQuery = @"
UPDATE dbo.Attendance
SET
    TimeIn     = CASE WHEN @TimeIn     IS NOT NULL AND TimeIn     IS NULL THEN @TimeIn     ELSE TimeIn     END,
    TimeOut    = CASE WHEN @TimeOut    IS NOT NULL AND TimeOut    IS NULL THEN @TimeOut    ELSE TimeOut    END,
    StartBreak = CASE WHEN @StartBreak IS NOT NULL AND StartBreak IS NULL THEN @StartBreak ELSE StartBreak END,
    StopBreak  = CASE WHEN @StopBreak  IS NOT NULL AND StopBreak  IS NULL THEN @StopBreak  ELSE StopBreak  END
WHERE attendanceID = @attendanceID";

                using var updateCmd = new SqlCommand(updateQuery, conn);
                AddNullableTime(updateCmd, "@TimeIn",      timeInValue);
                AddNullableTime(updateCmd, "@TimeOut",     timeOutValue);
                AddNullableTime(updateCmd, "@StartBreak",  startBreakValue);
                AddNullableTime(updateCmd, "@StopBreak",   stopBreakValue);
                AddInt(updateCmd, "@attendanceID", attendanceId);

                updateCmd.ExecuteNonQuery();
            }
            else
            {
                reader.Close();

                const string insertQuery = @"
INSERT INTO dbo.Attendance (empID, TimeIn, TimeOut, StartBreak, StopBreak, LogTime)
VALUES (@empID, @TimeIn, @TimeOut, @StartBreak, @StopBreak, @LogTime)";

                using var insertCmd = new SqlCommand(insertQuery, conn);
                AddInt(insertCmd, "@empID", empId);
                AddNullableTime(insertCmd, "@TimeIn",     timeInValue);
                AddNullableTime(insertCmd, "@TimeOut",    timeOutValue);
                AddNullableTime(insertCmd, "@StartBreak", startBreakValue);
                AddNullableTime(insertCmd, "@StopBreak",  stopBreakValue);
                AddDateTime(insertCmd, "@LogTime", DateTime.Now);

                insertCmd.ExecuteNonQuery();
            }

            return (true, "Attendance recorded successfully.");
        }

        /// <summary>
        /// Get employee attendance history
        /// </summary>
        public static DataTable GetEmployeeAttendanceHistory(int empId, int days = 7)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
SELECT
    CONVERT(VARCHAR(10), LogTime, 120) AS [Date],
    CONVERT(VARCHAR(8), TimeIn) AS [Time In],
    CONVERT(VARCHAR(8), TimeOut) AS [Time Out],
    CONVERT(VARCHAR(8), StartBreak) AS [Start Break],
    CONVERT(VARCHAR(8), StopBreak) AS [Stop Break]
FROM dbo.Attendance
WHERE empID = @empID
  AND LogTime >= DATEADD(DAY, -@Days, GETDATE())
ORDER BY LogTime DESC";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);
            AddInt(cmd, "@Days", days);

            using var adapter = new SqlDataAdapter(cmd);
            DataTable dt = new DataTable();
            adapter.Fill(dt);
            return dt;
        }

        /// <summary>
        /// Get today's attendance status for an employee
        /// Returns: (hasTimeIn, hasTimeOut, hasStartBreak, hasStopBreak)
        /// </summary>
        public static (bool hasTimeIn, bool hasTimeOut, bool hasStartBreak, bool hasStopBreak) GetTodayAttendanceStatus(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
SELECT TOP 1
    CAST(CASE WHEN TimeIn     IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasTimeIn,
    CAST(CASE WHEN TimeOut    IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasTimeOut,
    CAST(CASE WHEN StartBreak IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasStartBreak,
    CAST(CASE WHEN StopBreak  IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasStopBreak
FROM dbo.Attendance
WHERE empID = @empID
  AND CAST(LogTime AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY attendanceID DESC";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (
                    reader.GetBoolean(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3));
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

            const string query = @"
SELECT
    a.attendanceID,
    a.empID,
    e.FullName,
    e.empCode,
    CONVERT(VARCHAR(8), a.TimeIn) AS TimeIn,
    CONVERT(VARCHAR(8), a.TimeOut) AS TimeOut,
    CONVERT(VARCHAR(8), a.StartBreak) AS StartBreak,
    CONVERT(VARCHAR(8), a.StopBreak) AS StopBreak,
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
        /// Get today's attendance statistics
        /// </summary>
        public static (int timeInCount, int timeOutCount, int startBreakCount, int stopBreakCount) GetTodayAttendanceStats()
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
SELECT
    COUNT(CASE WHEN TimeIn IS NOT NULL THEN 1 END) AS TimeInCount,
    COUNT(CASE WHEN TimeOut IS NOT NULL THEN 1 END) AS TimeOutCount,
    COUNT(CASE WHEN StartBreak IS NOT NULL THEN 1 END) AS StartBreakCount,
    COUNT(CASE WHEN StopBreak IS NOT NULL THEN 1 END) AS StopBreakCount
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
                    reader.GetInt32(3));
            }

            return (0, 0, 0, 0);
        }

        /// <summary>
        /// Update an existing employee's details
        /// </summary>
        public static void UpdateEmployee(int empId, string fullName, string empCode)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
UPDATE dbo.Employees
SET FullName = @FullName,
    empCode  = @empCode
WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            AddNVarChar(cmd, "@FullName", 150, fullName);
            AddNVarChar(cmd, "@empCode",  20,  empCode);
            AddInt(cmd, "@empID", empId);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Delete an employee by ID
        /// </summary>
        public static void DeleteEmployee(int empId)
        {
            using var conn = GetConnection();
            conn.Open();

            const string query = @"
DELETE FROM dbo.Employees
WHERE empID = @empID";

            using var cmd = new SqlCommand(query, conn);
            AddInt(cmd, "@empID", empId);

            cmd.ExecuteNonQuery();
        }

        private static TimeSpan? ParseAttendanceTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string[] formats =
            {
                "hh:mm:ss tt",
                "h:mm:ss tt",
                "HH:mm:ss",
                "H:mm:ss",
                "hh:mm tt",
                "h:mm tt",
                "HH:mm",
                "H:mm"
            };

            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exact))
                return exact.TimeOfDay;

            if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return parsed.TimeOfDay;

            return null;
        }

        private static void AddInt(SqlCommand cmd, string name, int value)
        {
            cmd.Parameters.Add(name, SqlDbType.Int).Value = value;
        }

        private static void AddDateTime(SqlCommand cmd, string name, DateTime value)
        {
            cmd.Parameters.Add(name, SqlDbType.DateTime2).Value = value;
        }

        private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
        {
            cmd.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;
        }

        private static void AddNullableTime(SqlCommand cmd, string name, TimeSpan? value)
        {
            cmd.Parameters.Add(name, SqlDbType.Time).Value = value.HasValue ? value.Value : DBNull.Value;
        }
    }
}