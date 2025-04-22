using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Configuration;

namespace Store_Sales_Sheet
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            UpdateRunInfo();
            LoadTabsFromDatabase();
        }

        private void UpdateRunInfo()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["SQL"].ConnectionString;
            string machineName = Environment.MachineName;
            string query = "UPDATE Sales.SalesSheetMapping SET LastRan = @LastRan, TimesRan = TimesRan + 1 WHERE Machine = @Machine";

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@LastRan", DateTime.Now);
                        cmd.Parameters.AddWithValue("@Machine", machineName);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating run info: " + ex.Message);
            }
        }

        private void LoadTabsFromDatabase()
        {
            // Get the global mapping (start) date from SQL.
            DateTime globalStartDate = ConfigManager.GetMappingDateFromDatabase();

            // Get the store mapping list from SQL.
            List<string> storeMappings = ConfigManager.GetStoreMappingsFromDatabase();

            // Get week mappings from SQL.
            Dictionary<string, List<int>> weekMapping = ConfigManager.GetWeekMappingDictionaryFromSQL();

            // Clear any existing tabs.
            tabControl1.TabPages.Clear();

            // Create a tab for each store.
            foreach (string storeName in storeMappings)
            {
                TabPage tabPage = new TabPage(storeName);

                // Create a DataGridView for this tab.
                DataGridView dgv = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AutoGenerateColumns = true,
                    AllowUserToOrderColumns = true
                };

                // Enable double buffering using reflection.
                typeof(DataGridView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(dgv, true, null);

                tabPage.Controls.Add(dgv);

                // Load store data using the global start date and the week mapping dictionary.
                LoadDataForStore(dgv, storeName, globalStartDate, weekMapping);

                tabControl1.TabPages.Add(tabPage);
            }
        }

        private void LoadDataForStore(DataGridView dgv, string storeName, DateTime startDate, Dictionary<string, List<int>> weekMapping)
        {
            try
            {
                // Get raw data from SQL.
                DataTable dt = ConfigManager.GetSalesData(storeName);

                // Transform the DataTable using the provided start date and week mapping.
                DataTable transformedTable = DataTableTransformer.TransformDataTable(dt, startDate, weekMapping);

                // Bind to the DataGridView.
                dgv.DataSource = transformedTable;
                dgv.RowHeadersVisible = false;
                dgv.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgv.ReadOnly = true;
                dgv.AllowUserToAddRows = false;
                dgv.AllowUserToDeleteRows = false;

                dgv.DataBindingComplete += (s, e) =>
                {
                    foreach (DataGridViewColumn col in dgv.Columns)
                        col.SortMode = DataGridViewColumnSortMode.NotSortable;
                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        if (row.Cells["Week"].Value != null && !int.TryParse(row.Cells["Week"].Value.ToString(), out _))
                        {
                            row.DefaultCellStyle.Font = new Font(dgv.Font, FontStyle.Bold);
                            row.DefaultCellStyle.BackColor = System.Drawing.Color.LightGray;
                        }
                    }
                };

                dgv.CellFormatting += (s, e) =>
                {
                    if (dgv.Columns[e.ColumnIndex].Name == "Difference" && e.Value != null && e.Value != DBNull.Value)
                    {
                        if (decimal.TryParse(e.Value.ToString(), out decimal diff))
                        {
                            if (diff < 0)
                            {
                                e.CellStyle.ForeColor = System.Drawing.Color.Red;
                                e.Value = diff.ToString("0.00");
                            }
                            else
                            {
                                e.CellStyle.ForeColor = System.Drawing.Color.Black;
                                e.Value = diff.ToString("0.00");
                            }
                            e.FormattingApplied = true;
                        }
                    }
                };

                dgv.CellFormatting += (s, e) =>
                {
                    var weekObj = dgv.Rows[e.RowIndex].Cells["Week"].Value;
                    bool isNumericWeek = (weekObj != null && int.TryParse(weekObj.ToString(), out _));
                    if (isNumericWeek && dgv.Columns[e.ColumnIndex].Name == "Current")
                    {
                        e.CellStyle.BackColor = System.Drawing.Color.LightYellow;
                    }
                    if (isNumericWeek && dgv.Columns[e.ColumnIndex].Name == "Difference" &&
                        e.Value != null && e.Value != DBNull.Value)
                    {
                        if (decimal.TryParse(e.Value.ToString(), out decimal diff))
                        {
                            if (diff < 0)
                            {
                                e.CellStyle.BackColor = System.Drawing.Color.MistyRose;
                                e.CellStyle.ForeColor = System.Drawing.Color.Red;
                                e.Value = diff.ToString("0.00");
                            }
                            else
                            {
                                e.CellStyle.BackColor = System.Drawing.Color.LightGreen;
                                e.CellStyle.ForeColor = System.Drawing.Color.Black;
                                e.Value = diff.ToString("0.00");
                            }
                            e.FormattingApplied = true;
                        }
                    }
                };

                dgv.CellFormatting += (s, e) =>
                {
                    string colName = dgv.Columns[e.ColumnIndex].Name;
                    var dtSource = dgv.DataSource as DataTable;
                    if (dtSource != null && dtSource.Columns.Contains(colName))
                    {
                        DataColumn dc = dtSource.Columns[colName];
                        if (dc.ExtendedProperties.ContainsKey("Format"))
                        {
                            string format = dc.ExtendedProperties["Format"].ToString();
                            if (e.Value != null && e.Value != DBNull.Value && decimal.TryParse(e.Value.ToString(), out decimal num))
                            {
                                if (format == "0.00%")
                                {
                                    e.Value = num.ToString("0.00") + "%";
                                }
                                else if (format == "£0")
                                {
                                    e.Value = "£" + num.ToString("0");
                                }
                                e.FormattingApplied = true;
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data for store {storeName}: {ex.Message}");
            }
        }        

    }
}
