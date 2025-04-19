using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

public static class DataTableTransformer
{
    public static DataTable TransformDataTable(DataTable dt, DateTime startDate, Dictionary<string, List<int>> weekMapping)
    {
        if (dt.Columns.Contains("Store"))
            dt.Columns.Remove("Store");

        List<Tuple<DataColumn, int>> salesColumns = new List<Tuple<DataColumn, int>>();

        Dictionary<string, int> salesColumnYears = new Dictionary<string, int>();
        foreach (DataColumn col in dt.Columns)
        {
            if (col.ColumnName.StartsWith("Sales"))
            {
                string remainder = col.ColumnName.Substring("Sales".Length);
                if (int.TryParse(remainder, out int year))
                {
                    salesColumns.Add(new Tuple<DataColumn, int>(col, year));
                    salesColumnYears[remainder] = year;
                    col.ColumnName = remainder;
                }
                else
                {
                    col.ColumnName = remainder;
                }
            }
        }
        if (salesColumns.Count > 0)
        {
            var latest = salesColumns.OrderBy(x => x.Item2).Last();
            latest.Item1.ColumnName = "Current";

            string keyToRemove = latest.Item2.ToString();
            if (salesColumnYears.ContainsKey(keyToRemove))
                salesColumnYears.Remove(keyToRemove);
            salesColumnYears["Current"] = latest.Item2;
        }

        if (dt.Columns.Contains("Week"))
        {
            dt.DefaultView.Sort = "Week ASC";
            dt = dt.DefaultView.ToTable();
        }

        if (dt.Columns.Contains("Week"))
        {
            dt.Columns["Week"].ColumnName = "WeekInt";
            dt.Columns.Add("Week", typeof(string));
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                dt.Rows[i]["Week"] = dt.Rows[i]["WeekInt"].ToString();
            }
            dt.Columns.Remove("WeekInt");
        }

        dt.Columns.Add("Date", typeof(DateTime));
        if (dt.Columns.Contains("Week"))
        {
            dt.Columns["Date"].SetOrdinal(dt.Columns["Week"].Ordinal + 1);
        }
        for (int i = 0; i < dt.Rows.Count; i++)
        {
            dt.Rows[i]["Date"] = startDate.AddDays(i * 7);
        }

        List<Tuple<int, DataRow>> summaryRows = new List<Tuple<int, DataRow>>();
        foreach (var kvp in weekMapping)
        {
            string month = kvp.Key;
            List<int> weeks = kvp.Value;
            List<int> indices = new List<int>();
            Dictionary<string, decimal> sums = new Dictionary<string, decimal>();

            foreach (DataColumn col in dt.Columns)
            {
                if (col.ColumnName != "Week" && col.ColumnName != "Date" &&
                   (col.DataType == typeof(int) || col.DataType == typeof(decimal) ||
                    col.DataType == typeof(double) || col.DataType == typeof(float)))
                {
                    sums[col.ColumnName] = 0;
                }
            }

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                string weekStr = dt.Rows[i]["Week"].ToString();
                if (int.TryParse(weekStr, out int weekNum))
                {
                    if (weeks.Contains(weekNum))
                    {
                        indices.Add(i);
                        foreach (var colName in sums.Keys.ToList())
                        {
                            decimal val = dt.Rows[i][colName] != DBNull.Value ? Convert.ToDecimal(dt.Rows[i][colName]) : 0;
                            sums[colName] += val;
                        }
                    }
                }
            }

            if (indices.Count > 0)
            {
                int insertIndex = indices.Max();
                DataRow summaryRow = dt.NewRow();
                summaryRow["Week"] = month;  // summary row label
                summaryRow["Date"] = DBNull.Value;
                foreach (var kv in sums)
                {
                    summaryRow[kv.Key] = kv.Value;
                }
                summaryRows.Add(new Tuple<int, DataRow>(insertIndex, summaryRow));
            }
        }
        foreach (var item in summaryRows.OrderByDescending(t => t.Item1))
        {
            dt.Rows.InsertAt(item.Item2, item.Item1 + 1);
        }

        dt = InsertCumulativeRows(dt);

        if (dt.Columns.Contains("Current") && dt.Columns.Contains("Target"))
        {
            dt.Columns.Add("Difference", typeof(decimal));
            foreach (DataRow row in dt.Rows)
            {
                if (row["Target"] != DBNull.Value && row["Current"] != DBNull.Value)
                {
                    row["Difference"] = Convert.ToDecimal(row["Current"]) - Convert.ToDecimal(row["Target"]);
                }
                else
                {
                    row["Difference"] = DBNull.Value;
                }
            }
        }

        List<string> salesColumnsOrder = new List<string>();
        if (dt.Columns.Contains("Current"))
            salesColumnsOrder.Add("Current");

        var remainingSales = dt.Columns.Cast<DataColumn>()
            .Where(c => int.TryParse(c.ColumnName, out _) && c.ColumnName != "Current" && c.ColumnName != "Difference")
            .Select(c => c.ColumnName)
            .OrderByDescending(name => int.Parse(name))
            .ToList();
        salesColumnsOrder.AddRange(remainingSales);

        for (int i = 1; i < salesColumnsOrder.Count; i++)
        {
            string leftCol = salesColumnsOrder[i - 1];
            string rightCol = salesColumnsOrder[i];
            int leftYear = leftCol == "Current" ? salesColumnYears["Current"] : int.Parse(leftCol);
            int rightYear = int.Parse(rightCol);
            string pctColName = $"{leftYear} vs {rightYear}";
            dt.Columns.Add(pctColName, typeof(decimal));
            foreach (DataRow row in dt.Rows)
            {
                if (row[leftCol] != DBNull.Value && row[rightCol] != DBNull.Value)
                {
                    decimal leftValue = Convert.ToDecimal(row[leftCol]);
                    decimal rightValue = Convert.ToDecimal(row[rightCol]);
                    if (rightValue != 0)
                    {
                        decimal pctDiff = Math.Round(((leftValue - rightValue) / rightValue) * 100, 2);
                        row[pctColName] = pctDiff;
                    }
                    else
                    {
                        row[pctColName] = DBNull.Value;
                    }
                }
                else
                {
                    row[pctColName] = DBNull.Value;
                }
            }
        }

        int nextOrdinal = 0;
        List<string> finalColumnOrder = new List<string>();

        // Fixed columns.
        finalColumnOrder.Add("Week");
        finalColumnOrder.Add("Date");
        finalColumnOrder.Add("Target");

        // "Current" and "Difference".
        if (dt.Columns.Contains("Current"))
            finalColumnOrder.Add("Current");
        if (dt.Columns.Contains("Difference"))
            finalColumnOrder.Add("Difference");

        // For each subsequent sales column, add its corresponding percentage column then the sales column.
        for (int i = 1; i < salesColumnsOrder.Count; i++)
        {
            int leftYear = salesColumnsOrder[i - 1] == "Current" ? salesColumnYears["Current"] : int.Parse(salesColumnsOrder[i - 1]);
            int rightYear = int.Parse(salesColumnsOrder[i]);
            string pctCol = $"{leftYear} vs {rightYear}";
            finalColumnOrder.Add(pctCol);
            finalColumnOrder.Add(salesColumnsOrder[i]);
        }

        foreach (string colName in finalColumnOrder)
        {
            if (dt.Columns.Contains(colName))
            {
                dt.Columns[colName].SetOrdinal(nextOrdinal++);
            }
        }

        // --- Set ExtendedProperties for Display Formatting ---
        // Non-percentage columns get "£0" (no decimals) while percentage columns get "0.00%" format.
        foreach (DataColumn col in dt.Columns)
        {
            if (col.DataType == typeof(int) ||
                col.DataType == typeof(decimal) ||
                col.DataType == typeof(double) ||
                col.DataType == typeof(float))
            {
                if (col.ColumnName.Contains("vs"))
                {
                    col.ExtendedProperties["Format"] = "0.00%";
                }
                else
                {
                    col.ExtendedProperties["Format"] = "£0";
                }
            }
        }

        return dt;
    }

    public static DataTable InsertCumulativeRows(DataTable dt)
    {
        var numericColumns = dt.Columns.Cast<DataColumn>()
            .Where(c => c.ColumnName != "Date" &&
                        (c.DataType == typeof(int) ||
                         c.DataType == typeof(decimal) ||
                         c.DataType == typeof(double) ||
                         c.DataType == typeof(float)))
            .Select(c => c.ColumnName)
            .ToList();

        Dictionary<string, decimal> cumulativeSums = new Dictionary<string, decimal>();
        foreach (string colName in numericColumns)
        {
            cumulativeSums[colName] = 0;
        }

        for (int i = 0; i < dt.Rows.Count; i++)
        {
            DataRow row = dt.Rows[i];

            if (int.TryParse(row["Week"].ToString(), out int weekNum))
            {
                foreach (string col in numericColumns)
                {
                    if (row[col] != DBNull.Value)
                        cumulativeSums[col] += Convert.ToDecimal(row[col]);
                }
            }
            else
            {
                DataRow cumulativeRow = dt.NewRow();
                cumulativeRow["Week"] = "Cumulative";
                cumulativeRow["Date"] = DBNull.Value;
                foreach (string col in numericColumns)
                {
                    cumulativeRow[col] = cumulativeSums[col];
                }
                dt.Rows.InsertAt(cumulativeRow, i + 1);
                i++;
            }
        }
        return dt;
    }
}
