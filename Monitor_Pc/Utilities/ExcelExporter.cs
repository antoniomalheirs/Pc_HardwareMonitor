using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using OfficeOpenXml.ConditionalFormatting;

namespace Monitor_Pc.Utilities
{
    public static class ExcelExporter
    {
        static ExcelExporter()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private static readonly (string SheetName, string Prefix, string SensorType, Color HeaderColor, Color AccentColor, string NumberFormat)[] Categories =
        {
            ("CPU Frequências",  "CPU",  "Clock",       ColorTranslator.FromHtml("#7B1FA2"), ColorTranslator.FromHtml("#CE93D8"), "#,##0.0 \"MHz\""),
            ("CPU Tensões",      "CPU",  "Voltage",     ColorTranslator.FromHtml("#E65100"), ColorTranslator.FromHtml("#FFAB91"), "0.000 \"V\""),
            ("CPU Temperaturas", "CPU",  "Temperature", ColorTranslator.FromHtml("#C62828"), ColorTranslator.FromHtml("#EF9A9A"), "0.0 \"°C\""),
            ("CPU Potência",     "CPU",  "Power",       ColorTranslator.FromHtml("#00695C"), ColorTranslator.FromHtml("#80CBC4"), "0.0 \"W\""),
            ("GPU Frequências",  "GPU",  "Clock",       ColorTranslator.FromHtml("#1565C0"), ColorTranslator.FromHtml("#90CAF9"), "#,##0.0 \"MHz\""),
            ("GPU Tensões",      "GPU",  "Voltage",     ColorTranslator.FromHtml("#F57F17"), ColorTranslator.FromHtml("#FFF176"), "0.000 \"V\""),
            ("GPU Temperaturas", "GPU",  "Temperature", ColorTranslator.FromHtml("#B71C1C"), ColorTranslator.FromHtml("#EF9A9A"), "0.0 \"°C\""),
            ("GPU Potência",     "GPU",  "Power",       ColorTranslator.FromHtml("#004D40"), ColorTranslator.FromHtml("#80CBC4"), "0.0 \"W\""),
            ("MB Tensões",       "MB",   "Voltage",     ColorTranslator.FromHtml("#FF6F00"), ColorTranslator.FromHtml("#FFE082"), "0.000 \"V\""),
            ("MB Temperaturas",  "MB",   "Temperature", ColorTranslator.FromHtml("#D84315"), ColorTranslator.FromHtml("#FFAB91"), "0.0 \"°C\""),
        };

        private static readonly Color[] ChartColors = new[]
        {
            Color.FromArgb(33, 150, 243),
            Color.FromArgb(244, 67, 54),
            Color.FromArgb(76, 175, 80),
            Color.FromArgb(255, 152, 0),
            Color.FromArgb(156, 39, 176),
            Color.FromArgb(0, 188, 212),
            Color.FromArgb(255, 193, 7),
            Color.FromArgb(233, 30, 99),
            Color.FromArgb(0, 150, 136),
            Color.FromArgb(139, 195, 74)
        };

        public static string ExportCsvToExcel(string csvPath)
        {
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("CSV não encontrado", csvPath);

            // Phase 1: Parse CSV using CurrentCulture (pt-BR uses commas as decimal separator)
            var (headers, rows) = ParseCsv(csvPath);
            string xlsxPath = Path.ChangeExtension(csvPath, ".xlsx");

            if (File.Exists(xlsxPath))
            {
                try { File.Delete(xlsxPath); } catch { /* ignore */ }
            }

            // Phase 2: Generate Excel using InvariantCulture to prevent XML serialization issues
            // EPPlus serializes chart axis values, CF thresholds, etc. using CurrentCulture,
            // which on pt-BR systems writes commas instead of dots, corrupting the xlsx.
            var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            var originalUICulture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

                using var package = new ExcelPackage();
                var wb = package.Workbook;

                CreateOverviewSheet(wb, headers, rows, csvPath);

                foreach (var cat in Categories)
                {
                    var colIndices = FindColumns(headers, cat.Prefix, cat.SensorType);
                    if (colIndices.Count == 0) continue;

                    CreateCategorySheet(wb, cat.SheetName, headers, rows,
                        colIndices, cat.HeaderColor, cat.AccentColor, cat.NumberFormat, cat.SensorType);
                }

                CreateRawSheet(wb, headers, rows);

                package.SaveAs(new FileInfo(xlsxPath));
                return xlsxPath;
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = originalUICulture;
            }
        }

        private static void CreateOverviewSheet(ExcelWorkbook wb, string[] headers, List<string[]> rows, string csvPath)
        {
            var ws = wb.Worksheets.Add("Resumo");
            ws.View.ShowGridLines = false;

            ws.Cells["A1"].Value = "MONITOR PC — RELATÓRIO DE TELEMETRIA";
            var titleRange = ws.Cells["A1:E1"];
            titleRange.Merge = true;
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.Size = 18;
            titleRange.Style.Font.Color.SetColor(Color.White);
            titleRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            titleRange.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#1A1A2E"));
            titleRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            titleRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            ws.Row(1).Height = 40;

            int r = 3;
            void AddStat(string label, string value)
            {
                ws.Cells[r, 1].Value = label;
                ws.Cells[r, 1].Style.Font.Bold = true;
                ws.Cells[r, 1].Style.Font.Color.SetColor(ColorTranslator.FromHtml("#666666"));
                ws.Cells[r, 2].Value = value;
                ws.Cells[r, 2].Style.Font.Bold = true;
                r++;
            }

            AddStat("Arquivo CSV:", Path.GetFileName(csvPath));
            AddStat("Total de Leituras:", rows.Count.ToString("N0"));

            if (rows.Count > 0)
            {
                AddStat("Primeira Leitura:", rows[0][0]);
                AddStat("Última Leitura:", rows[^1][0]);
            }

            AddStat("Sensores Monitorados:", (headers.Length - 1).ToString());

            r += 1;
            ws.Cells[r, 1].Value = "ABAS DISPONÍVEIS";
            ws.Cells[r, 1].Style.Font.Bold = true;
            ws.Cells[r, 1].Style.Font.Size = 13;
            ws.Cells[r, 1].Style.Font.Color.SetColor(ColorTranslator.FromHtml("#1A1A2E"));
            r++;

            foreach (var cat in Categories)
            {
                var colCount = FindColumns(headers, cat.Prefix, cat.SensorType).Count;
                if (colCount == 0) continue;

                ws.Cells[r, 1].Value = $"📊 {cat.SheetName}";
                ws.Cells[r, 1].Style.Font.Color.SetColor(cat.HeaderColor);
                ws.Cells[r, 1].Style.Font.Bold = true;
                ws.Cells[r, 2].Value = $"{colCount} sensores";
                ws.Cells[r, 2].Style.Font.Color.SetColor(ColorTranslator.FromHtml("#999999"));
                r++;
            }

            ws.Column(1).Width = 35;
            ws.Column(2).Width = 45;
        }

        private static void CreateCategorySheet(ExcelWorkbook wb, string sheetName, string[] headers, List<string[]> rows,
            List<int> colIndices, Color headerColor, Color accentColor, string numberFormat, string sensorType)
        {
            var ws = wb.Worksheets.Add(sheetName);

            ws.Cells[1, 1].Value = "Timestamp";
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Color.SetColor(Color.White);
            ws.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#333333"));

            for (int c = 0; c < colIndices.Count; c++)
            {
                string cleanName = CleanColumnName(headers[colIndices[c]]);
                var cell = ws.Cells[1, c + 2];
                cell.Value = cleanName;
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(headerColor);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            int statsRow = 2;
            ws.Cells[statsRow, 1].Value = "📈 MIN / MÁX / MÉD";
            ws.Cells[statsRow, 1].Style.Font.Bold = true;
            ws.Cells[statsRow, 1].Style.Font.Color.SetColor(headerColor);
            ws.Cells[statsRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[statsRow, 1].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#F5F5F5"));

            for (int c = 0; c < colIndices.Count; c++)
            {
                var values = GetNumericValues(rows, colIndices[c]);
                var cell = ws.Cells[statsRow, c + 2];
                if (values.Count > 0)
                {
                    cell.Value = $"{values.Min():F2} / {values.Max():F2} / {values.Average():F2}";
                }
                else
                {
                    cell.Value = "--";
                }
                cell.Style.Font.Size = 9;
                cell.Style.Font.Color.SetColor(ColorTranslator.FromHtml("#555555"));
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#F5F5F5"));
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            int dataStart = 3;
            for (int i = 0; i < rows.Count; i++)
            {
                int row = dataStart + i;
                
                if (DateTime.TryParseExact(rows[i][0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                {
                    ws.Cells[row, 1].Value = dt;
                    ws.Cells[row, 1].Style.Numberformat.Format = "HH:mm:ss";
                }
                else
                {
                    ws.Cells[row, 1].Value = rows[i][0];
                }

                for (int c = 0; c < colIndices.Count; c++)
                {
                    int colIdx = colIndices[c];
                    if (colIdx < rows[i].Length &&
                        double.TryParse(rows[i][colIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        var cell = ws.Cells[row, c + 2];
                        cell.Value = val;
                        cell.Style.Numberformat.Format = numberFormat;
                    }
                }

                if (i % 2 == 1)
                {
                    ws.Cells[row, 1, row, colIndices.Count + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1, row, colIndices.Count + 1].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#FAFAFA"));
                }
            }

            ws.Row(1).Height = 28;
            ws.View.FreezePanes(3, 2);
            ws.Column(1).Width = 24;
            for (int c = 0; c < colIndices.Count; c++)
                ws.Column(c + 2).Width = 18;

            if (rows.Count > 0)
            {
                var dataRange = ws.Cells[dataStart, 1, dataStart + rows.Count - 1, colIndices.Count + 1];
                dataRange.AutoFilter = true;

                ApplyConditionalFormatting(ws, dataStart, rows.Count, colIndices, headers, sensorType);

                GenerateCharts(ws, sheetName, rows.Count, colIndices.Count, dataStart, headers, colIndices, rows);
            }
        }

        private static void GenerateCharts(ExcelWorksheet ws, string sheetName, int rowCount, int colCount, int dataStart, string[] headers, List<int> colIndices, List<string[]> rows)
        {
            List<List<int>> chartGroups = new List<List<int>>();
            foreach (int colIdx in colIndices)
            {
                var vals = GetNumericValues(rows, colIdx);
                if (vals.Count == 0) continue;
                
                double avg = vals.Average();

                var group = chartGroups.FirstOrDefault(g => 
                {
                    double firstAvg = GetNumericValues(rows, g[0]).Average();
                    double maxAvg = Math.Max(Math.Abs(firstAvg), Math.Abs(avg));
                    if (maxAvg == 0) return true; // ambos 0
                    double diff = Math.Abs(firstAvg - avg) / maxAvg;
                    return diff <= 0.25 && g.Count < 5;
                });

                if (group != null)
                {
                    group.Add(colIdx);
                }
                else
                {
                    chartGroups.Add(new List<int> { colIdx });
                }
            }

            int chartStartRow = 1;
            int chartStartCol = colCount + 3;

            for (int chartIdx = 0; chartIdx < chartGroups.Count; chartIdx++)
            {
                var groupColIndices = chartGroups[chartIdx];

                string chartTitle = chartGroups.Count > 1 ? $"{sheetName} (Grupo {chartIdx + 1})" : sheetName;
                
                // 1. Line Chart
                var chart = ws.Drawings.AddLineChart($"LineChart_{sheetName.Replace(" ", "_")}_{chartIdx}", eLineChartType.Line);
                chart.Title.Text = chartTitle + " - Histórico";
                
                int dynamicWidth = Math.Min(4000, 1100 + (rowCount * 3));
                chart.SetPosition(chartStartRow, 0, chartStartCol, 0);
                chart.SetSize(dynamicWidth, 500); 
                chart.Legend.Position = eLegendPosition.Bottom;
                chart.XAxis.Format = "HH:mm:ss";

                // 2. Area Chart
                var areaChart = ws.Drawings.AddAreaChart($"AreaChart_{sheetName.Replace(" ", "_")}_{chartIdx}", eAreaChartType.Area);
                areaChart.Title.Text = chartTitle + " - Volume Acumulado";
                areaChart.SetPosition(chartStartRow + 26, 0, chartStartCol, 0);
                areaChart.SetSize(dynamicWidth, 400);
                areaChart.Legend.Position = eLegendPosition.Bottom;
                areaChart.XAxis.Format = "HH:mm:ss";

                // 3. Column Chart
                var colChart = ws.Drawings.AddBarChart($"ColChart_{sheetName.Replace(" ", "_")}_{chartIdx}", eBarChartType.ColumnClustered);
                colChart.Title.Text = chartTitle + " - Mínimo, Médio e Máximo";
                colChart.SetPosition(chartStartRow + 48, 0, chartStartCol, 0);
                colChart.SetSize(dynamicWidth, 350);
                colChart.Legend.Position = eLegendPosition.Bottom;

                double globalMin = double.MaxValue;
                double globalMax = double.MinValue;
                
                int statsStartRow = rowCount + dataStart + 5;
                ws.Cells[statsStartRow, chartStartCol].Value = "Mínimo";
                ws.Cells[statsStartRow + 1, chartStartCol].Value = "Médio";
                ws.Cells[statsStartRow + 2, chartStartCol].Value = "Máximo";

                for (int i = 0; i < groupColIndices.Count; i++)
                {
                    int colIdx = groupColIndices[i];
                    int localColOffset = colIndices.IndexOf(colIdx) + 2; 

                    var yRange = ws.Cells[dataStart, localColOffset, dataStart + rowCount - 1, localColOffset];
                    var xRange = ws.Cells[dataStart, 1, dataStart + rowCount - 1, 1];
                    string header = CleanColumnName(headers[colIdx]);

                    // Add to Line Chart
                    var series = chart.Series.Add(yRange, xRange);
                    series.Header = header;
                    var lineChartSeries = (ExcelLineChartSerie)series;
                    lineChartSeries.Smooth = true;
                    if (lineChartSeries.Marker != null)
                        lineChartSeries.Marker.Style = eMarkerStyle.None;
                    
                    if (i < ChartColors.Length)
                        lineChartSeries.Border.Fill.Color = ChartColors[i];

                    // Add to Area Chart
                    var areaSeries = areaChart.Series.Add(yRange, xRange);
                    areaSeries.Header = header;
                    if (i < ChartColors.Length)
                        areaSeries.Fill.Color = ChartColors[i];

                    // Prepare Column Chart
                    var numericVals = GetNumericValues(rows, colIdx);
                    if (numericVals.Count > 0)
                    {
                        double sMin = numericVals.Min();
                        double sMax = numericVals.Max();
                        double sAvg = numericVals.Average();

                        int statCol = chartStartCol + i + 1;
                        ws.Cells[statsStartRow - 1, statCol].Value = header;
                        ws.Cells[statsStartRow, statCol].Value = sMin;
                        ws.Cells[statsStartRow + 1, statCol].Value = sAvg;
                        ws.Cells[statsStartRow + 2, statCol].Value = sMax;

                        var colRange = ws.Cells[statsStartRow, statCol, statsStartRow + 2, statCol];
                        var xNameRange = ws.Cells[statsStartRow, chartStartCol, statsStartRow + 2, chartStartCol];
                        
                        var barSeries = colChart.Series.Add(colRange, xNameRange);
                        barSeries.Header = header;
                        if (i < ChartColors.Length)
                            barSeries.Fill.Color = ChartColors[i];

                        globalMin = Math.Min(globalMin, sMin);
                        globalMax = Math.Max(globalMax, sMax);
                    }
                }

                if (globalMin != double.MaxValue && globalMax != double.MinValue)
                {
                    double padding = (globalMax - globalMin) * 0.15;
                    if (padding == 0) padding = globalMax * 0.05;
                    if (padding == 0) padding = 1;
                    
                    double finalMin = Math.Max(0, globalMin - padding);
                    double finalMax = globalMax + padding;

                    chart.YAxis.MinValue = finalMin; 
                    chart.YAxis.MaxValue = finalMax;

                    areaChart.YAxis.MinValue = finalMin;
                    areaChart.YAxis.MaxValue = finalMax;
                    
                    colChart.YAxis.MinValue = finalMin;
                    colChart.YAxis.MaxValue = finalMax;
                }

                chartStartRow += 68;
            }
        }

        private static void CreateRawSheet(ExcelWorkbook wb, string[] headers, List<string[]> rows)
        {
            var ws = wb.Worksheets.Add("Dados Brutos");

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#263238"));
                cell.Style.Font.Color.SetColor(Color.White);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                for (int c = 0; c < rows[i].Length; c++)
                {
                    var cell = ws.Cells[i + 2, c + 1];
                    if (c == 0)
                    {
                        cell.Value = rows[i][c];
                    }
                    else if (double.TryParse(rows[i][c], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        cell.Value = val;
                    }
                    else
                    {
                        cell.Value = rows[i][c];
                    }
                }
            }

            ws.View.FreezePanes(2, 1);
            ws.Cells[1, 1, rows.Count + 1, headers.Length].AutoFilter = true;
        }

        private static void ApplyConditionalFormatting(ExcelWorksheet ws, int dataStart, int rowCount, List<int> colIndices, string[] headers, string sensorType)
        {
            for (int c = 0; c < colIndices.Count; c++)
            {
                string colName = headers[colIndices[c]];
                var address = new ExcelAddress(dataStart, c + 2, dataStart + rowCount - 1, c + 2);
                var cf = ws.ConditionalFormatting.AddThreeColorScale(address);

                if (sensorType == "Temperature")
                {
                    cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    cf.LowValue.Value = 40;
                    cf.LowValue.Color = ColorTranslator.FromHtml("#C8E6C9");
                    
                    cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    cf.MiddleValue.Value = 75;
                    cf.MiddleValue.Color = ColorTranslator.FromHtml("#FFF9C4");
                    
                    cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                    cf.HighValue.Value = 90;
                    cf.HighValue.Color = ColorTranslator.FromHtml("#FFCDD2");
                }
                else if (sensorType == "Clock")
                {
                    cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.LowValue.Value = 10;
                    cf.LowValue.Color = ColorTranslator.FromHtml("#FFF9C4");

                    cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.MiddleValue.Value = 50;
                    cf.MiddleValue.Color = ColorTranslator.FromHtml("#E8F5E9");

                    cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.HighValue.Value = 90;
                    cf.HighValue.Color = ColorTranslator.FromHtml("#A5D6A7");
                }
                else if (sensorType == "Power")
                {
                    cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.LowValue.Value = 10;
                    cf.LowValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                    cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.MiddleValue.Value = 50;
                    cf.MiddleValue.Color = ColorTranslator.FromHtml("#FFF9C4");

                    cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.HighValue.Value = 90;
                    cf.HighValue.Color = ColorTranslator.FromHtml("#FFCC80");
                }
                else if (sensorType == "Voltage")
                {
                    if (colName.Contains("+12V"))
                    {
                        cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.LowValue.Value = 11.4;
                        cf.LowValue.Color = ColorTranslator.FromHtml("#FFCDD2");

                        cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.MiddleValue.Value = 12.0;
                        cf.MiddleValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                        cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.HighValue.Value = 12.6;
                        cf.HighValue.Color = ColorTranslator.FromHtml("#FFCDD2");
                    }
                    else if (colName.Contains("+5V"))
                    {
                        cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.LowValue.Value = 4.75;
                        cf.LowValue.Color = ColorTranslator.FromHtml("#FFCDD2");

                        cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.MiddleValue.Value = 5.0;
                        cf.MiddleValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                        cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.HighValue.Value = 5.25;
                        cf.HighValue.Color = ColorTranslator.FromHtml("#FFCDD2");
                    }
                    else if (colName.Contains("3VCC") || colName.Contains("3VSB") || colName.Contains("VBAT") || colName.Contains("AVSB") || colName.Contains("3V"))
                    {
                        cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.LowValue.Value = 3.135;
                        cf.LowValue.Color = ColorTranslator.FromHtml("#FFCDD2");

                        cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.MiddleValue.Value = 3.3;
                        cf.MiddleValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                        cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                        cf.HighValue.Value = 3.465;
                        cf.HighValue.Color = ColorTranslator.FromHtml("#FFCDD2");
                    }
                    else
                    {
                        cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                        cf.LowValue.Value = 10;
                        cf.LowValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                        cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                        cf.MiddleValue.Value = 50;
                        cf.MiddleValue.Color = ColorTranslator.FromHtml("#FFF9C4");

                        cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                        cf.HighValue.Value = 90;
                        cf.HighValue.Color = ColorTranslator.FromHtml("#FFCC80");
                    }
                }
                else
                {
                    cf.LowValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.LowValue.Value = 10;
                    cf.LowValue.Color = ColorTranslator.FromHtml("#C8E6C9");

                    cf.MiddleValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.MiddleValue.Value = 50;
                    cf.MiddleValue.Color = ColorTranslator.FromHtml("#FFF9C4");

                    cf.HighValue.Type = eExcelConditionalFormattingValueObjectType.Percentile;
                    cf.HighValue.Value = 90;
                    cf.HighValue.Color = ColorTranslator.FromHtml("#FFCDD2");
                }
            }
        }

        private static (string[] Headers, List<string[]> Rows) ParseCsv(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return (Array.Empty<string>(), new List<string[]>());

            var headers = lines[0].Split(',');
            var rows = new List<string[]>(lines.Length - 1);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                rows.Add(lines[i].Split(','));
            }

            return (headers, rows);
        }

        private static List<int> FindColumns(string[] headers, string prefix, string sensorType)
        {
            var result = new List<int>();
            string pattern = $"{prefix}_{sensorType}_";

            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    result.Add(i);
            }
            return result;
        }

        private static string CleanColumnName(string colName)
        {
            var parts = colName.Split('_');
            if (parts.Length > 2)
            {
                return string.Join(" ", parts.Skip(2)).Replace("_", " ");
            }
            return colName;
        }

        private static List<double> GetNumericValues(List<string[]> rows, int colIdx)
        {
            var values = new List<double>();
            foreach (var row in rows)
            {
                if (colIdx < row.Length &&
                    double.TryParse(row[colIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                {
                    values.Add(val);
                }
            }
            return values;
        }
    }
}
