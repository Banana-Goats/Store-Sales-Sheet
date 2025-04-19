using Microsoft.Data.SqlClient;
using System.Configuration;
using System.Data;
using System.Xml.Linq;

public static class ConfigManager
{   

    public static Dictionary<string, List<int>> GetWeekMappingDictionaryFromSQL()
    {
        Dictionary<string, List<int>> mapping = new Dictionary<string, List<int>>();
        string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
        List<(string MonthName, string WeekNumbers)> dbWeekMappings = new List<(string, string)>();

        try
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT MonthName, WeekNumbers
                    FROM Config.MonthWeeks
                    ORDER BY MonthName;";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string monthName = reader["MonthName"].ToString();
                        string weekNumbers = reader["WeekNumbers"].ToString();
                        dbWeekMappings.Add((monthName, weekNumbers));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Handle exceptions (logging, fallback, etc.) as needed.
        }

        // Optional: Specify a custom month order.
        var monthOrder = new List<string>
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        // Reorder the mappings by the position of MonthName in the monthOrder list.
        dbWeekMappings = dbWeekMappings
            .OrderBy(x => monthOrder.IndexOf(x.MonthName))
            .ThenBy(x => x.MonthName)
            .ToList();

        foreach (var row in dbWeekMappings)
        {
            // Convert the comma-separated week numbers into a list of integers.
            List<int> weeks = row.WeekNumbers
                                .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => int.Parse(s.Trim()))
                                .ToList();
            mapping[row.MonthName] = weeks;
        }
        return mapping;
    }

    public static DateTime GetMappingDateFromDatabase()
    {
        string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
        // Default fallback value.
        DateTime mappingDate = new DateTime(2024, 9, 1);
        try
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT [Value] FROM [Config].[AppConfigs] WHERE [Application] = @app AND [Config] = @config";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@app", "BG Menu");
                    cmd.Parameters.AddWithValue("@config", "Start Date");
                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        string dateString = result.ToString();
                        if (DateTime.TryParse(dateString, out DateTime parsedDate))
                        {
                            mappingDate = parsedDate;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Log or handle error as needed.
        }
        return mappingDate;
    }

    public static List<string> GetStoreMappingsFromDatabase()
    {
        var storeMappings = new List<string>();
        string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
        string machineName = Environment.MachineName;

        try
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // 1) Check if a row for this machine already exists
                const string existsSql = @"
                SELECT COUNT(*) 
                  FROM Sales.SalesSheetMapping 
                 WHERE Machine = @Machine";
                using (var existsCmd = new SqlCommand(existsSql, conn))
                {
                    existsCmd.Parameters.AddWithValue("@Machine", machineName);
                    int rowCount = (int)existsCmd.ExecuteScalar();

                    if (rowCount == 0)
                    {
                        // 2) No row found → insert a placeholder with Company='Unknown'
                        const string insertSql = @"
                        INSERT INTO Sales.SalesSheetMapping (Machine, Company, Mapping)
                        VALUES (@Machine, @Company, @Mapping)";
                        using (var insertCmd = new SqlCommand(insertSql, conn))
                        {
                            insertCmd.Parameters.AddWithValue("@Machine", machineName);
                            insertCmd.Parameters.AddWithValue("@Company", "Unknown");
                            insertCmd.Parameters.AddWithValue("@Mapping", "Unknown");
                            insertCmd.ExecuteNonQuery();
                        }

                        // 3) Return empty list (no Mapping yet)
                        return storeMappings;
                    }
                }

                // 4) Row exists: fetch its Mapping column
                const string selectSql = @"
                SELECT Mapping
                  FROM Sales.SalesSheetMapping
                 WHERE Machine = @Machine";
                using (var selectCmd = new SqlCommand(selectSql, conn))
                {
                    selectCmd.Parameters.AddWithValue("@Machine", machineName);
                    object result = selectCmd.ExecuteScalar();

                    // Handle both “no rows” (shouldn’t happen now) and DBNull
                    if (result != null && result != DBNull.Value)
                    {
                        string mappingStr = result.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(mappingStr))
                        {
                            // 5) Split comma‑separated Mapping into individual stores
                            storeMappings = mappingStr
                                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .ToList();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // TODO: log ex so you can diagnose any failures
        }

        return storeMappings;
    }

    public static DataTable GetSalesData(string storeName)
    {
        string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
        DataTable dt = new DataTable();
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            string query = "SELECT * FROM Sales.SalesData WHERE Store = @Store";
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Store", storeName);
                SqlDataAdapter adapter = new SqlDataAdapter(cmd);
                adapter.Fill(dt);
            }
        }
        return dt;
    }
}
