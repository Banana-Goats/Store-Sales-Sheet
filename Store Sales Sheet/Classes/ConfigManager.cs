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
        List<string> storeMappings = new List<string>();
        string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
        string machineName = Environment.MachineName;
        try
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT Mapping FROM Sales.SalesSheetMapping WHERE Machine = @Machine";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Machine", machineName);
                    object result = cmd.ExecuteScalar();
                    string mappingStr = (result != null) ? result.ToString() : string.Empty;
                    if (!string.IsNullOrWhiteSpace(mappingStr))
                    {
                        // Assume comma-separated list.
                        storeMappings = new List<string>(
                            mappingStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim())
                        );
                    }
                }
            }
        }
        catch (Exception)
        {
            // Log error or handle as needed.
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
